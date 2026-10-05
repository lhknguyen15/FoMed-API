using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Real HTTP/JWT audit. Never seed or mutate the configured application database.
internal static class HttpWorkflowAudit
{
    public static async Task RunAsync(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var focusDispensing = args.Contains("--dispensing-only");
        // Never let an occupied audit port redirect writes to a user's running API.
        var probe = new TcpListener(IPAddress.Loopback, 5181);
        try { probe.Start(); } finally { probe.Stop(); }
        var config = JsonDocument.Parse(await File.ReadAllTextAsync("FoMed-API/FoMed.Api/appsettings.Development.json"));
        var builder = new SqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString());
        var name = "FoMed_Audit_" + Guid.NewGuid().ToString("N");
        var attachmentRoot = Path.GetFullPath(Path.Combine(root, "tests", "ClinicWorkflow", "bin", name));
        builder.InitialCatalog = "master"; builder.ConnectTimeout = 5;
        await using var admin = new SqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await new SqlCommand($"CREATE DATABASE [{name}]", admin).ExecuteNonQueryAsync();
        builder.InitialCatalog = name;
        var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(builder.ConnectionString).Options;
        var observations = new List<object>();
        var passed = 0; var failed = 0;
        void Check(bool ok, string label, object? evidence = null)
        {
            observations.Add(new { label, passed = ok, evidence });
            if (ok) passed++; else failed++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}" + (evidence is null ? "" : $" ({evidence})"));
        }
        Process? api = null;
        try
        {
            await using (var db = new FoMedDbContext(options))
            {
                var schema = await File.ReadAllTextAsync("database/fomed-create-database.sql");
                schema = schema[schema.IndexOf("IF NOT EXISTS (SELECT 1 FROM sys.schemas", StringComparison.Ordinal)..];
                async Task Sql(string sql)
                {
                    // Migrations hard-code FoMedDb: strip USE before executing ONLY on our database.
                    sql = Regex.Replace(sql, @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (Regex.IsMatch(sql, @"\bUSE\s+|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase))
                        throw new InvalidOperationException("Cross-database SQL is prohibited in audit fixtures.");
                    foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                        if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
                }
                await Sql(schema);
                foreach (var file in Directory.GetFiles("database/migrations", "*.sql").Order())
                    await Sql(await File.ReadAllTextAsync(file));
            }
            const string password = "Audit-Only!2026";
            int doctorId, medicineId, serviceId, cancelServiceId;
            await using (var db = new FoMedDbContext(options))
            {
                var roles = await db.Roles.ToDictionaryAsync(r => r.Name);
                User Seed(string role, string username)
                {
                    var user = new User { Username = username, FullName = "Audit " + username, Email = username + "@test.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), IsActive = true, CreatedAt = DateTime.UtcNow };
                    user.UserRoles.Add(new UserRole { Role = roles[role] }); db.Users.Add(user); return user;
                }
                var p = Seed("Patient", "patient"); var other = Seed("Patient", "other-patient");
                p.Patient = new Patient { PatientCode = "AUDIT1", FullName = p.FullName!, Phone = "0912345678", DateOfBirth = new(1995, 1, 1), Allergies = "Audit Penicillin", IsActive = true };
                other.Patient = new Patient { PatientCode = "AUDIT2", FullName = other.FullName!, IsActive = true };
                var doctor = Seed("Doctor", "doctor"); var otherDoctor = Seed("Doctor", "other-doctor");
                var specialty = new Specialty { Name = "Audit specialty", IsActive = true };
                foreach (var user in new[] { doctor, otherDoctor })
                {
                    user.Doctor = new Doctor { FullName = user.FullName!, Specialty = specialty, ConsultationFee = 300, Room = "AUDIT", IsActive = true };
                    for (byte day = 0; day < 7; day++) user.Doctor.DoctorSchedules.Add(new DoctorSchedule { DayOfWeek = day, StartTime = new(0, 0), EndTime = new(23, 59), SlotMinutes = 15, IsActive = true });
                }
                Seed("Receptionist", "receptionist"); Seed("Technician", "technician"); Seed("Technician", "other-technician"); Seed("Pharmacist", "pharmacist"); Seed("Admin", "admin");
                var med = new Medicine { Name = "Audit medicine", Unit = "tablet", Price = 10, IsActive = true };
                var svc = new Service { Name = "Audit lab", Price = 100, IsActive = true };
                var cancelSvc = new Service { Name = "Audit canceled lab", Price = 200, IsActive = true };
                db.AddRange(med, svc, cancelSvc); await db.SaveChangesAsync();
                doctorId = doctor.Doctor!.Id; medicineId = med.Id; serviceId = svc.Id; cancelServiceId = cancelSvc.Id;
            }
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "FoMed-API/FoMed.Api"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(root, "FoMed-API/FoMed.Api/bin/WorkflowAudit/FoMed.Api.dll"));
            foreach (var arg in new[] { "--urls", "http://127.0.0.1:5181", "--Logging:EventLog:LogLevel:Default=None", "--Logging:Console:LogLevel:Default=None" }) start.ArgumentList.Add(arg);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__DefaultConnection"] = builder.ConnectionString;
            start.Environment["ClinicalAttachments__StoragePath"] = attachmentRoot;
            api = Process.Start(start) ?? throw new Exception("Cannot start isolated API");
            // Drain logs, but never persist configuration/secrets to audit artifacts.
            api.BeginOutputReadLine(); api.BeginErrorReadLine();
            using var client = new HttpClient { BaseAddress = new("http://127.0.0.1:5181"), Timeout = TimeSpan.FromSeconds(25) };
            var ready = false;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (api.HasExited) throw new Exception("Isolated API exited during startup");
                try { using var response = await client.GetAsync("/api/specialties"); ready = response.IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { }
                await Task.Delay(200);
            }
            if (!ready) throw new Exception("Isolated API did not become ready");
            var tokens = new Dictionary<string, string>();
            async Task<(int Status, JsonElement Body)> Call(string? role, string method, string path, object? data = null, bool spoofContext = false)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (role is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[role]);
                if (spoofContext)
                {
                    request.Headers.Add("X-Forwarded-For", "203.0.113.99");
                    request.Headers.Add("X-User-Id", "999999");
                    request.Headers.Add("X-Request-Id", "caller-controlled-request-id");
                }
                if (data is HttpContent content) request.Content = content;
                else if (data is not null) request.Content = JsonContent.Create(data);
                using var response = await client.SendAsync(request);
                var raw = await response.Content.ReadAsStringAsync();
                JsonElement body;
                try { body = JsonDocument.Parse(raw).RootElement.Clone(); } catch (JsonException) { body = JsonSerializer.SerializeToElement(new { nonJson = true }); }
                return ((int)response.StatusCode, body);
            }
            JsonElement Data((int Status, JsonElement Body) response) => response.Body.GetProperty("dataResponse");
            async Task<JsonElement> Need(string? role, string method, string path, object? data, int expected, string label)
            {
                var response = await Call(role, method, path, data); Check(response.Status == expected, label, response.Status);
                if (response.Status != expected) throw new Exception($"Workflow stopped at {label}: HTTP {response.Status}");
                return Data(response);
            }
            async Task Reject(string role, string method, string path, object? data, int expected, string label)
            { var response = await Call(role, method, path, data); Check(response.Status == expected, label, response.Status); }
            async Task<(int Status, JsonElement Body)> Upload(string role, int record, int? order, string filename, byte[] bytes)
            {
                var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "file", filename);
                return await Call(role, "POST", $"/api/clinical/records/{record}/attachments" + (order.HasValue ? $"?orderId={order}" : ""), form);
            }
            async Task WithAuditFailure(string source, Func<Task> action)
            {
                if (!Regex.IsMatch(source, "^[A-Za-z]+$")) throw new Exception("Invalid audit trigger source");
                await using var db = new FoMedDbContext(options);
                // Fault injection only on this random disposable DB; never on application data.
                // DDL cannot capture a command parameter inside a persisted trigger. Source is an internal
                // ASCII identifier validated above; no request/configuration data enters this SQL.
                var triggerSql = $"""
                    CREATE TRIGGER audit.TR_IsolatedAuditFailure ON audit.audit_logs AFTER INSERT AS
                    BEGIN
                        IF EXISTS (SELECT 1 FROM inserted WHERE entity = 'MedicalRecord' AND
                            JSON_VALUE(CASE WHEN ISJSON(new_value) = 1 THEN new_value ELSE N'[]' END, '$.context.source') = N'{source}')
                            THROW 51002, 'Intentional isolated audit failure', 1;
                    END
                    """;
                await db.Database.ExecuteSqlRawAsync(triggerSql);
                try { await action(); }
                finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER audit.TR_IsolatedAuditFailure"); }
            }
            async Task<int> RecordAuditCount()
            {
                await using var db = new FoMedDbContext(options);
                return await db.AuditLogs.CountAsync(a => a.Entity == "MedicalRecord");
            }
            foreach (var role in new[] { "patient", "other-patient", "doctor", "other-doctor", "receptionist", "technician", "other-technician", "pharmacist", "admin" })
            {
                var login = await Need(null, "POST", "/api/auth/login", new { username = role, password }, 200, "Login " + role);
                tokens[role] = login.GetProperty("accessToken").GetString()!;
            }
            var today = DateOnly.FromDateTime(ClinicTime.Now);
            var slots = await Need(null, "GET", $"/api/appointments/available-slots?doctorId={doctorId}&date={today:yyyy-MM-dd}", null, 200, "VC-03 available slots");
            var available = slots.EnumerateArray().Where(s => s.GetProperty("isAvailable").GetBoolean()).ToArray();
            if (available.Length < 2) throw new Exception("Audit requires two future slots today; run before the final slot.");
            var slot = available[0];
            var appointment = await Need("patient", "POST", "/api/appointments/book", new { doctorId, startTime = slot.GetProperty("startTime").GetString() }, 201, "VC-03 patient booking");
            int appointmentId = appointment.GetProperty("id").GetInt32();
            Check(appointment.GetProperty("status").GetInt32() == 0, "New appointment is Pending");
            await Reject("patient", "PUT", $"/api/appointments/{appointmentId}/confirm", new { }, 403, "Patient cannot confirm");
            await Need("receptionist", "PUT", $"/api/appointments/{appointmentId}/confirm", new { }, 200, "VC-07 receptionist confirm");
            await Need("receptionist", "PUT", $"/api/appointments/{appointmentId}/check-in", new { }, 200, "VC-07 check-in");
            await Reject("receptionist", "PUT", $"/api/appointments/{appointmentId}/check-in", new { }, 409, "Repeated check-in blocked");
            var queue = await Need("doctor", "GET", "/api/appointments/doctor-queue", null, 200, "VC-12 doctor queue");
            Check(queue.EnumerateArray().Any(a => a.GetProperty("appointment").GetProperty("id").GetInt32() == appointmentId), "Checked-in patient reaches doctor queue");
            await WithAuditFailure("CreateRecord", () => Reject("doctor", "POST", $"/api/clinical/appointments/{appointmentId}/record", new { diagnosis = "Must rollback" }, 500, "Audit insert failure rejects record creation"));
            await using (var db = new FoMedDbContext(options))
                Check(!await db.MedicalRecords.AnyAsync(r => r.AppointmentId == appointmentId) && await db.Appointments.Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync() == 1 &&
                    !await db.AppointmentStatusHistories.AnyAsync(h => h.AppointmentId == appointmentId && h.ToStatus == 2) && !await db.AuditLogs.AnyAsync(a => a.Entity == "MedicalRecord"),
                    "Failed create audit rolls back record, status, history and audit together");
            var record = await Need("doctor", "POST", $"/api/clinical/appointments/{appointmentId}/record", new { symptoms = "Audit symptom", diagnosis = "Audit diagnosis", vitalSigns = new { systolic = 120, diastolic = 80, heartRate = 82, temperature = 37, weightKg = 52 } }, 201, "VC-13 start consultation");
            var recordId = record.GetProperty("id").GetInt32();
            var beforeMissingRead = await RecordAuditCount();
            await Reject("doctor", "GET", $"/api/clinical/records/{recordId}/prescription", null, 404, "Missing prescription is not a successful access");
            await Reject("other-doctor", "GET", $"/api/clinical/records/{recordId}", null, 403, "Other doctor cannot read record");
            Check(await RecordAuditCount() == beforeMissingRead, "404/403 reads do not emit successful medical-record audits");
            await WithAuditFailure("UpdateRecord", () => Reject("doctor", "PUT", $"/api/clinical/records/{recordId}", new { symptoms = "Must rollback", diagnosis = "Must rollback" }, 500, "Audit insert failure rejects record update"));
            await using (var db = new FoMedDbContext(options))
            {
                var preserved = await db.MedicalRecords.SingleAsync(r => r.Id == recordId);
                Check(preserved.Symptoms == "Audit symptom" && preserved.Diagnosis == "Audit diagnosis" && preserved.UpdatedAt is null && await RecordAuditCount() == beforeMissingRead,
                    "Failed update audit preserves previous clinical content and timestamps");
            }
            await Need("doctor", "PUT", $"/api/clinical/records/{recordId}", new { symptoms = "Audit symptom", diagnosis = "Audit diagnosis", treatmentPlan = "Audit treatment", vitalSigns = new { systolic = 120, diastolic = 80, heartRate = 82 } }, 200, "VC-13 save updated consultation");
            var pdfBytes = "%PDF-1.4\n% isolated test fixture\n%%EOF"u8.ToArray();
            var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jJ54AAAAASUVORK5CYII=");
            Check((await Upload("doctor", recordId, null, "not-a-pdf.pdf", "not a pdf"u8.ToArray())).Status == 400, "Attachment extension spoof is rejected by signature");
            Check((await Upload("doctor", recordId, null, "script.svg", "<svg/>"u8.ToArray())).Status == 400, "Unsupported attachment format rejected");
            Check((await Upload("doctor", recordId, null, "../escape.pdf", pdfBytes)).Status == 400, "Attachment path-like file name rejected");
            Check((await Upload("doctor", recordId, null, "empty.pdf", [])).Status == 400, "Empty attachment rejected");
            Check((await Upload("doctor", recordId, null, "oversize.pdf", new byte[10 * 1024 * 1024 + 1])).Status == 400, "Attachment over 10 MB rejected");
            Check((await Upload("other-doctor", recordId, null, "other.pdf", pdfBytes)).Status == 403, "Other doctor cannot upload attachment");
            Check((await Upload("patient", recordId, null, "patient.pdf", pdfBytes)).Status == 403, "Patient cannot upload clinical attachment");
            await WithAuditFailure("UploadAttachment", async () => Check((await Upload("doctor", recordId, null, "rollback.pdf", pdfBytes)).Status == 500, "Audit failure rolls back attachment upload"));
            await using (var db = new FoMedDbContext(options))
                Check(!await db.Attachments.AnyAsync() && (!Directory.Exists(attachmentRoot) || !Directory.EnumerateFiles(attachmentRoot).Any()), "Failed upload leaves no attachment metadata or private file");
            var uploadedRecordFile = await Upload("doctor", recordId, null, "clinical-demo.pdf", pdfBytes);
            Check(uploadedRecordFile.Status == 201, "Doctor uploads private clinical PDF");
            var recordAttachmentId = Data(uploadedRecordFile).GetProperty("id").GetInt32();
            await Reject("patient", "GET", $"/api/clinical/attachments/{recordAttachmentId}/download", null, 403, "Patient cannot download draft attachment");
            await Reject("patient", "GET", $"/api/clinical/records/{recordId}", null, 403, "Patient cannot read draft");
            await Reject("other-doctor", "PUT", $"/api/clinical/records/{recordId}", new { diagnosis = "Unauthorized" }, 403, "Other doctor cannot edit");
            await Reject("receptionist", "POST", "/api/invoices", new { medicalRecordId = recordId }, 409, "Draft record cannot be invoiced");
            await Reject("doctor", "POST", $"/api/clinical/records/{recordId}/prescription", new { items = new[] { new { medicineId, quantity = 5, dosage = "1 daily" } } }, 409, "VC-15 allergy acknowledgment required");
            await Reject("doctor", "POST", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 5, dosage = "Audit" } } }, 409, "VC-15 blocks zero-stock prescription");
            await using (var db = new FoMedDbContext(options))
            {
                Check(!await db.Prescriptions.AnyAsync(p => p.MedicalRecordId == recordId), "Rejected create leaves no prescription");
                db.Add(new MedicineBatch { MedicineId = medicineId, LotNumber = "EXPIRED", Quantity = 100, ExpiryDate = today.AddDays(-1), CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await Reject("doctor", "POST", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 1, dosage = "Audit" } } }, 409, "VC-15 expired batches do not enable prescription");
            foreach (var lot in new[] { new { lotNumber = "EARLY", quantity = 3, expiryDate = today.AddDays(30) }, new { lotNumber = "LATE", quantity = 10, expiryDate = today.AddDays(60) } })
                await Need("pharmacist", "POST", "/api/pharmacy/inventory/receipts", new { medicineId, lot.lotNumber, lot.quantity, lot.expiryDate }, 201, "VC-17 receive " + lot.lotNumber);
            var context = await Need("doctor", "GET", $"/api/clinical/records/{recordId}/prescribing-context", null, 200, "Doctor gets authoritative prescribing context");
            Check(context.GetProperty("allergies").GetString() == "Audit Penicillin", "Patient allergy is sourced from database");
            var searchPath = $"/api/clinical/medicines/search?recordId={recordId}&keyword=Audit";
            var search = await Need("doctor", "GET", searchPath, null, 200, "Search medicine with valid batch stock");
            Check(search.GetProperty("items")[0].GetProperty("availableQuantity").GetInt64() == 13, "Search excludes expired quantity (13 not 113)");
            await Reject("other-doctor", "GET", searchPath, null, 403, "Other doctor cannot search in another doctor's record");
            await Reject("other-doctor", "GET", $"/api/clinical/records/{recordId}/prescribing-context", null, 403, "Other doctor cannot read allergy context");
            await Reject("patient", "GET", searchPath, null, 403, "Patient cannot use doctor search endpoint");
            await Reject("patient", "GET", $"/api/clinical/records/{recordId}/prescribing-context", null, 403, "Patient cannot use doctor allergy context");
            await Reject("doctor", "GET", searchPath + "&page=0", null, 400, "Invalid medicine search page rejected");
            await Reject("doctor", "GET", searchPath + new string('a', 101), null, 400, "Oversized medicine keyword rejected");
            await Reject("doctor", "POST", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 14, dosage = "Audit" } } }, 409, "Cannot prescribe above valid stock");
            var rxResponse = await Call("doctor", "POST", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 5, dosage = "1 daily", instruction = "After food" } } });
            Check(rxResponse.Status == 201, "VC-15 save stocked prescription", rxResponse.Status);
            if (rxResponse.Status != 201) throw new Exception("Stocked prescription failed.");
            var prescription = Data(rxResponse);
            var prescriptionId = prescription.GetProperty("id").GetInt32();
            await using (var db = new FoMedDbContext(options))
            {
                Check(await db.MedicineBatches.Where(b => b.MedicineId == medicineId && b.ExpiryDate >= today).SumAsync(b => b.Quantity) == 13, "Prescribing does not deduct or reserve stock");
                var medicine = await db.Medicines.SingleAsync(m => m.Id == medicineId); medicine.Price = 99; await db.SaveChangesAsync();
            }
            await Reject("doctor", "PUT", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, note = "Must rollback", items = new[] { new { medicineId, quantity = 14, dosage = "Must rollback" } } }, 409, "Updating above stock rejected");
            var unchangedRx = await Need("doctor", "GET", $"/api/clinical/records/{recordId}/prescription", null, 200, "Read prescription after rejected update");
            Check(unchangedRx.GetProperty("items")[0].GetProperty("quantity").GetInt32() == 5 && unchangedRx.GetProperty("items")[0].GetProperty("unitPriceSnapshot").GetDecimal() == 10 && unchangedRx.GetProperty("note").ValueKind == JsonValueKind.Null, "Rejected update preserves lines, original price snapshot and note");
            var order = await Need("doctor", "POST", $"/api/clinical/records/{recordId}/services", new { serviceId, quantity = 2 }, 201, "VC-14 order lab");
            var orderId = order.GetProperty("id").GetInt32();
            var cancelled = await Need("doctor", "POST", $"/api/clinical/records/{recordId}/services", new { serviceId = cancelServiceId, quantity = 3 }, 201, "VC-14 second lab order");
            var cancelledId = cancelled.GetProperty("id").GetInt32();
            await Need("doctor", "PUT", $"/api/clinical/orders/{cancelledId}/cancel", null, 200, "VC-14 cancel Ordered");
            await Reject("technician", "POST", $"/api/clinical/lab-orders/{cancelledId}/result", new { resultSummary = "Invalid" }, 409, "Canceled order cannot receive results");
            await using (var db = new FoMedDbContext(options))
            { var svc = await db.Services.SingleAsync(s => s.Id == serviceId); svc.Price = 999; await db.SaveChangesAsync(); }
            await Reject("doctor", "PUT", $"/api/appointments/{appointmentId}/complete", new { }, 409, "Pending lab prevents finalization");
            await Reject("doctor", "POST", $"/api/clinical/lab-orders/{orderId}/result", new { resultSummary = "Invalid role" }, 403, "Only technician enters results");
            await Need("technician", "POST", $"/api/clinical/lab-orders/{orderId}/result", new { resultSummary = "HGB 13.2 g/dl", referenceRange = "12-16", conclusion = "Audit normal" }, 201, "VC-14 technician completes lab");
            var uploadedLabFile = await Upload("technician", recordId, orderId, "lab-demo.png", pngBytes);
            Check(uploadedLabFile.Status == 201, "Technician uploads file for own completed result");
            var labAttachmentId = Data(uploadedLabFile).GetProperty("id").GetInt32();
            await Reject("technician", "GET", $"/api/clinical/records/{recordId}/attachments", null, 403, "Technician cannot list the full record attachments");
            await Need("technician", "GET", $"/api/clinical/records/{recordId}/attachments?orderId={orderId}", null, 200, "Technician reads scoped lab attachments");
            await Reject("doctor", "PUT", $"/api/clinical/orders/{orderId}/cancel", null, 409, "Completed lab cannot be canceled");
            var beforeFinalAudit = await RecordAuditCount();
            await WithAuditFailure("FinalizeRecord", () => Reject("doctor", "PUT", $"/api/appointments/{appointmentId}/complete", new { }, 500, "Audit insert failure rejects record finalization"));
            await using (var db = new FoMedDbContext(options))
            {
                var unfinalized = await db.MedicalRecords.Include(r => r.Appointment).SingleAsync(r => r.Id == recordId);
                Check(!unfinalized.IsFinalized && unfinalized.FinalizedAt is null && unfinalized.Appointment.Status == 2 &&
                    !await db.AppointmentStatusHistories.AnyAsync(h => h.AppointmentId == appointmentId && h.ToStatus == 3) && await RecordAuditCount() == beforeFinalAudit,
                    "Failed finalization audit rolls back record, appointment and completion history");
            }
            await Need("doctor", "PUT", $"/api/appointments/{appointmentId}/complete", new { }, 200, "VC-13 finalize consultation");
            await Reject("doctor", "PUT", $"/api/appointments/{appointmentId}/complete", new { }, 409, "Repeat finalization blocked");
            var final = await Need("patient", "GET", $"/api/clinical/records/{recordId}", null, 200, "VC-05 patient reads finalized record");
            Check(final.GetProperty("isFinalized").GetBoolean(), "Record is finalized");
            await Reject("doctor", "PUT", $"/api/clinical/records/{recordId}", new { diagnosis = "Changed" }, 409, "Finalized record immutable");
            await Reject("other-patient", "GET", $"/api/clinical/records/{recordId}", null, 403, "Patient data isolation");
            var invoice = await Need("receptionist", "POST", "/api/invoices", new { medicalRecordId = recordId }, 201, "VC-11 issue invoice");
            var invoiceId = invoice.GetProperty("id").GetInt32();
            Check(invoice.GetProperty("totalAmount").GetDecimal() == 550, "Invoice = fee 300 + Completed lab 2x100 + medicine 5x10; canceled excluded; snapshot retained", invoice.GetProperty("totalAmount"));
            await Reject("receptionist", "POST", "/api/invoices", new { medicalRecordId = recordId }, 409, "Duplicate invoice blocked");
            await Reject("other-patient", "GET", $"/api/invoices/{invoiceId}", null, 403, "Invoice ownership");
            await Reject("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", new { amount = 551, method = 0 }, 400, "Overpayment blocked");
            await Need("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", new { amount = 200, method = 0 }, 200, "VC-11 partial payment");
            var range = $"from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";
            var report = await Call("admin", "GET", "/api/reports/summary?" + range);
            Check(report.Status == 200 && report.Body.GetProperty("collectedAmount").GetDecimal() == 200 && report.Body.GetProperty("outstandingAmount").GetDecimal() == 350, "VC-23 payments-based revenue and remaining debt");
            await Need("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", new { amount = 350, method = 1 }, 200, "VC-11 settle balance");
            await Reject("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", new { amount = 1, method = 0 }, 409, "Paid invoice cannot be charged again");
            report = await Call("admin", "GET", "/api/reports/summary?" + range);
            Check(report.Status == 200 && report.Body.GetProperty("collectedAmount").GetDecimal() == 550 && report.Body.GetProperty("outstandingAmount").GetDecimal() == 0, "VC-23 settled report reconciles");
            await Reject("patient", "GET", "/api/pharmacy/inventory", null, 403, "Patient cannot access inventory");
            // Stock can change after prescription: dispensing still checks it independently.
            await using (var db = new FoMedDbContext(options))
                await db.MedicineBatches.Where(b => b.MedicineId == medicineId && b.ExpiryDate >= today).ExecuteUpdateAsync(s => s.SetProperty(b => b.Quantity, 0));
            var shortagePreview = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{prescriptionId}", null, 200, "Shortage preview");
            Check(!shortagePreview.GetProperty("canDispense").GetBoolean() && shortagePreview.GetProperty("items")[0].GetProperty("shortageQuantity").GetInt32() == 5,
                "Insufficient stock disables confirmation and exposes exact shortage");
            await Reject("pharmacist", "POST", $"/api/pharmacy/prescriptions/{prescriptionId}/dispense", null, 409, "VC-16 rechecks stock after prescription");
            await using (var db = new FoMedDbContext(options))
            {
                foreach (var batch in await db.MedicineBatches.Where(b => b.MedicineId == medicineId && b.ExpiryDate >= today).ToListAsync()) batch.Quantity = batch.LotNumber == "EARLY" ? 3 : 10;
                await db.SaveChangesAsync();
            }
            var splitPreview = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{prescriptionId}", null, 200, "Split-lot FEFO preview");
            var proposedLots = splitPreview.GetProperty("items")[0].GetProperty("proposedBatches");
            Check(proposedLots.GetArrayLength() == 2 && proposedLots[0].GetProperty("lotNumber").GetString() == "EARLY" && proposedLots[0].GetProperty("proposedQuantity").GetInt32() == 3 && proposedLots[1].GetProperty("proposedQuantity").GetInt32() == 2,
                "Preview splits exact quantities across unexpired FEFO lots");
            var dispense = await Need("pharmacist", "POST", $"/api/pharmacy/prescriptions/{prescriptionId}/dispense", null, 200, "VC-16 dispense after payment");
            Check(dispense.GetProperty("lines")[0].GetProperty("lotNumber").GetString() == "EARLY" && dispense.GetProperty("lines")[0].GetProperty("quantity").GetInt32() == 3 && dispense.GetProperty("lines")[1].GetProperty("quantity").GetInt32() == 2, "FEFO splits across two batches");
            var again = await Need("pharmacist", "POST", $"/api/pharmacy/prescriptions/{prescriptionId}/dispense", null, 200, "VC-16 repeat dispensing is idempotent");
            Check(again.GetProperty("alreadyDispensed").GetBoolean(), "No second stock deduction");
            var audit = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&pageSize=10");
            Check(audit.Status == 200 && audit.Body.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("action").GetString() == "Read"), "VC-24 record access logged");
            var writeAudit = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&action=Update&pageSize=200");
            Check(writeAudit.Status == 200 && writeAudit.Body.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("source").GetString() == "UpdateRecord" && i.GetProperty("entityId").GetInt32() == recordId), "VC-24 record update is logged with exact record ID");
            await Reject("patient", "GET", "/api/audit-logs", null, 403, "Patient cannot read audit logs");
            // Probe an unsafe alternate ordering on a separate draft consultation.
            var second = await Need("patient", "POST", "/api/appointments/book", new { doctorId, startTime = available[1].GetProperty("startTime").GetString() }, 201, "Negative-flow second appointment");
            var secondId = second.GetProperty("id").GetInt32();
            await Need("receptionist", "PUT", $"/api/appointments/{secondId}/confirm", new { }, 200, "Second confirm");
            await Need("receptionist", "PUT", $"/api/appointments/{secondId}/check-in", new { }, 200, "Second check-in");
            var draft = await Need("doctor", "POST", $"/api/clinical/appointments/{secondId}/record", new { symptoms = "Draft" }, 201, "Second draft record");
            var draftId = draft.GetProperty("id").GetInt32();
            var draftRx = await Need("doctor", "POST", $"/api/clinical/records/{draftId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 1, dosage = "Audit" } } }, 201, "Draft prescription");
            var draftRxId = draftRx.GetProperty("id").GetInt32();
            async Task<string> StockState()
            {
                await using var db = new FoMedDbContext(options);
                return JsonSerializer.Serialize(new {
                    batches = await db.MedicineBatches.OrderBy(b => b.Id).Select(b => new { b.Id, b.Quantity }).ToListAsync(),
                    allocations = await db.Set<PrescriptionDispense>().OrderBy(d => d.Id).Select(d => new { d.Id, d.PrescriptionItemId, d.BatchId, d.Quantity }).ToListAsync(),
                    movements = await db.Set<StockTransaction>().OrderBy(t => t.Id).Select(t => new { t.Id, t.BatchId, t.Quantity, t.RefId }).ToListAsync()
                });
            }
            var stockBefore = await StockState();
            await Reject("pharmacist", "POST", $"/api/pharmacy/prescriptions/{draftRxId}/dispense", null, 409, "Safety: cannot dispense an unfinalized prescription");
            Check(await StockState() == stockBefore, "Blocked dispense preserves batches, allocations and stock movements");
            var draftDetail = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{draftRxId}", null, 200, "Pharmacy reads draft readiness");
            Check(!draftDetail.GetProperty("canDispense").GetBoolean() && !draftDetail.GetProperty("isDispensed").GetBoolean() && !string.IsNullOrWhiteSpace(draftDetail.GetProperty("blockedReason").GetString()), "Draft preview is blocked with a business reason");
            await Reject("patient", "GET", $"/api/pharmacy/prescriptions/{draftRxId}", null, 403, "Patient cannot query pharmacy prescription");
            await Reject("doctor", "GET", $"/api/pharmacy/prescriptions/{draftRxId}", null, 403, "Doctor cannot use pharmacy-only read endpoint");
            await Reject("pharmacist", "GET", "/api/pharmacy/prescriptions/2147483647", null, 404, "Unknown prescription returns 404");
            await Need("doctor", "PUT", $"/api/clinical/records/{draftId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 2, dosage = "Changed" } } }, 200, "Undispensed draft prescription remains editable");
            foreach (var finalizedFlagOnly in new[] { true, false })
            {
                await using (var db = new FoMedDbContext(options))
                {
                    var inconsistent = await db.MedicalRecords.Include(r => r.Appointment).SingleAsync(r => r.Id == draftId);
                    inconsistent.IsFinalized = finalizedFlagOnly;
                    inconsistent.Appointment.Status = finalizedFlagOnly ? (byte)2 : (byte)3;
                    await db.SaveChangesAsync();
                }
                await Reject("pharmacist", "POST", $"/api/pharmacy/prescriptions/{draftRxId}/dispense", null, 409,
                    finalizedFlagOnly ? "Finalized flag without Completed appointment is blocked" : "Completed appointment without finalized record is blocked");
                Check(await StockState() == stockBefore, "Inconsistent clinical state cannot mutate stock");
            }
            await using (var db = new FoMedDbContext(options))
            {
                var restored = await db.MedicalRecords.Include(r => r.Appointment).SingleAsync(r => r.Id == draftId);
                restored.IsFinalized = false; restored.Appointment.Status = 2;
                await db.SaveChangesAsync();
            }
            // Simulate existing bad data from before the fix: partial dispensing on a draft.
            await using (var db = new FoMedDbContext(options))
            {
                var item = await db.PrescriptionItems.SingleAsync(i => i.PrescriptionId == draftRxId);
                var batch = await db.MedicineBatches.SingleAsync(b => b.LotNumber == "LATE");
                var pharmacist = await db.Users.SingleAsync(u => u.Username == "pharmacist");
                batch.Quantity -= 1;
                db.Add(new PrescriptionDispense { PrescriptionItemId = item.Id, BatchId = batch.Id, Quantity = 1, DispensedBy = pharmacist.Id, DispensedAt = DateTime.UtcNow });
                db.Add(new StockTransaction { BatchId = batch.Id, Quantity = -1, Type = 1, RefId = draftRxId, RefType = "Prescription", CreatedBy = pharmacist.Id, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var locked = await Need("doctor", "GET", $"/api/clinical/records/{draftId}/prescription", null, 200, "Doctor reads dispensing metadata");
            Check(locked.GetProperty("isDispensed").GetBoolean(), "Partial dispense is exposed as editing lock");
            stockBefore = await StockState();
            await Reject("doctor", "PUT", $"/api/clinical/records/{draftId}/prescription", new { allergyAcknowledged = true, note = "Must not save", items = new[] { new { medicineId, quantity = 3, dosage = "Must not change" } } }, 409, "Safety: partially dispensed draft prescription cannot be edited");
            var lockedAfter = await Need("doctor", "GET", $"/api/clinical/records/{draftId}/prescription", null, 200, "Read original prescription after rejected edit");
            Check(locked.GetRawText() == lockedAfter.GetRawText() && await StockState() == stockBefore, "Rejected edit preserves prescription lines, note, stock and allocations");
            // Verify older batch/OUT-only data is also protected, without allocations.
            await using (var db = new FoMedDbContext(options))
            {
                await db.Set<PrescriptionDispense>().Where(d => d.PrescriptionItem.PrescriptionId == draftRxId).ExecuteDeleteAsync();
                var item = await db.PrescriptionItems.SingleAsync(i => i.PrescriptionId == draftRxId);
                item.BatchId = await db.MedicineBatches.Where(b => b.LotNumber == "LATE").Select(b => b.Id).SingleAsync();
                await db.SaveChangesAsync();
            }
            stockBefore = await StockState();
            await Reject("doctor", "PUT", $"/api/clinical/records/{draftId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 3, dosage = "Must not change" } } }, 409, "Legacy batch/OUT prescription cannot be edited");
            Check(await StockState() == stockBefore, "Rejected legacy edit preserves stock history");
            // Restore the allocation fixture for browser checks of partial dispensing.
            await using (var db = new FoMedDbContext(options))
            {
                var item = await db.PrescriptionItems.SingleAsync(i => i.PrescriptionId == draftRxId);
                db.Add(new PrescriptionDispense { PrescriptionItemId = item.Id, BatchId = item.BatchId!.Value, Quantity = 1, DispensedBy = await db.Users.Where(u => u.Username == "pharmacist").Select(u => u.Id).SingleAsync(), DispensedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var completeDetail = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{prescriptionId}", null, 200, "Pharmacy reads fully dispensed prescription");
            Check(completeDetail.GetProperty("isFullyDispensed").GetBoolean() && !completeDetail.GetProperty("canDispense").GetBoolean(), "Already dispensed preview cannot dispense again");
            await Reject("doctor", "PUT", $"/api/clinical/records/{recordId}/prescription", new { allergyAcknowledged = true, items = new[] { new { medicineId, quantity = 9, dosage = "Must not change" } } }, 409, "Finalized dispensed prescription remains immutable");
            int concurrentRxId, uiRxId, emptyMedicineId, prescribingRecordId, browserMedicineId;
            await using (var db = new FoMedDbContext(options))
            {
                var patient = await db.Patients.SingleAsync(p => p.PatientCode == "AUDIT1");
                var emptyMedicine = new Medicine { Name = "Audit no-stock", Unit = "tablet", Price = 10, IsActive = true };
                db.Add(emptyMedicine); await db.SaveChangesAsync(); emptyMedicineId = emptyMedicine.Id;
                for (var i = 1; i <= 22; i++) db.Add(new Medicine { Name = $"Catalog medicine {i:D2}", Unit = "tablet", Price = 10, IsActive = true });
                var browserMedicine = new Medicine { Name = "ZZZ Search beyond first page", Unit = "tablet", Price = 12, IsActive = true };
                browserMedicine.MedicineBatches.Add(new MedicineBatch { LotNumber = "BROWSER-VALID", ExpiryDate = today.AddDays(30), Quantity = 10, CreatedAt = DateTime.UtcNow });
                browserMedicine.MedicineBatches.Add(new MedicineBatch { LotNumber = "BROWSER-EXPIRED", ExpiryDate = today.AddDays(-1), Quantity = 100, CreatedAt = DateTime.UtcNow });
                db.Add(browserMedicine);
                Prescription SeedFinalized(string code, JsonElement futureSlot, bool addShortageLine)
                {
                    var appointment = new FoMed.Infrastructure.Models.Appointment {
                        AppointmentCode = code, PatientId = patient.Id, DoctorId = doctorId,
                        StartTime = futureSlot.GetProperty("startTime").GetDateTime(), EndTime = futureSlot.GetProperty("endTime").GetDateTime(),
                        Status = 3, FeeSnapshot = 300, CreatedAt = DateTime.UtcNow
                    };
                    var rx = new Prescription { MedicalRecord = new MedicalRecord {
                        Appointment = appointment, PatientId = patient.Id, DoctorId = doctorId,
                        IsFinalized = true, FinalizedAt = DateTime.UtcNow, Diagnosis = "Dispensing fixture", CreatedAt = DateTime.UtcNow
                    }, CreatedAt = DateTime.UtcNow };
                    rx.PrescriptionItems.Add(new PrescriptionItem { MedicineId = medicineId, Quantity = 2, UnitPriceSnapshot = 99, Dosage = "Audit" });
                    if (addShortageLine) rx.PrescriptionItems.Add(new PrescriptionItem { MedicineId = emptyMedicineId, Quantity = 1, UnitPriceSnapshot = 10, Dosage = "Audit" });
                    db.Add(rx); return rx;
                }
                if (available.Length < 5) throw new Exception("Run audit while at least five future slots remain today.");
                var concurrentRx = SeedFinalized("AUDIT-CONCURRENT", available[^1], true);
                var uiRx = SeedFinalized("AUDIT-UI", available[^2], false);
                var editingRecord = new MedicalRecord { PatientId = patient.Id, DoctorId = doctorId, Symptoms = "Prescribing UI fixture", CreatedAt = DateTime.UtcNow,
                    Appointment = new FoMed.Infrastructure.Models.Appointment { AppointmentCode = "AUDIT-PRESCRIBING", PatientId = patient.Id, DoctorId = doctorId,
                        StartTime = available[^3].GetProperty("startTime").GetDateTime(), EndTime = available[^3].GetProperty("endTime").GetDateTime(),
                        CheckedInAt = DateTime.UtcNow, Status = 2, FeeSnapshot = 300, CreatedAt = DateTime.UtcNow } };
                db.Add(editingRecord);
                await db.SaveChangesAsync(); concurrentRxId = concurrentRx.Id; uiRxId = uiRx.Id;
                prescribingRecordId = editingRecord.Id; browserMedicineId = browserMedicine.Id;
            }
            var pagedSearch = await Need("doctor", "GET", $"/api/clinical/medicines/search?recordId={prescribingRecordId}&page=2", null, 200, "Medicine search page beyond first 20");
            Check(pagedSearch.GetProperty("totalCount").GetInt32() == 25 && pagedSearch.GetProperty("items").GetArrayLength() == 5 && pagedSearch.GetProperty("items").EnumerateArray().Any(m => m.GetProperty("id").GetInt32() == browserMedicineId), "Search pagination includes medicine beyond first page");
            var targetedSearch = await Need("doctor", "GET", $"/api/clinical/medicines/search?recordId={prescribingRecordId}&keyword=ZZZ", null, 200, "Search by keyword finds later medicine");
            Check(targetedSearch.GetProperty("totalCount").GetInt32() == 1 && targetedSearch.GetProperty("items")[0].GetProperty("availableQuantity").GetInt32() == 10, "Targeted search excludes expired batch and narrows total");
            var unsavedContext = await Need("doctor", "GET", $"/api/clinical/records/{prescribingRecordId}/prescribing-context?medicineIds={browserMedicineId}", null, 200, "Refresh availability for unsaved medicine selection");
            Check(unsavedContext.GetProperty("medicines")[0].GetProperty("id").GetInt32() == browserMedicineId && unsavedContext.GetProperty("medicines")[0].GetProperty("availableQuantity").GetInt32() == 10, "Context refresh includes selected medicine without a saved prescription");
            await Reject("doctor", "GET", $"/api/clinical/records/{prescribingRecordId}/prescribing-context?medicineIds=0", null, 400, "Invalid selected medicine ID rejected");
            var noMatches = await Need("doctor", "GET", $"/api/clinical/medicines/search?recordId={prescribingRecordId}&keyword=does-not-exist", null, 200, "Empty medicine search");
            Check(noMatches.GetProperty("totalCount").GetInt32() == 0 && noMatches.GetProperty("items").GetArrayLength() == 0, "Empty search returns empty page");
            stockBefore = await StockState();
            await Reject("pharmacist", "POST", $"/api/pharmacy/prescriptions/{concurrentRxId}/dispense", null, 409, "Multi-line shortage returns 409");
            Check(await StockState() == stockBefore, "Multi-line failure rolls back all batches and dispensing movements");
            await Need("pharmacist", "POST", "/api/pharmacy/inventory/receipts", new { medicineId = emptyMedicineId, lotNumber = "RESTOCK", quantity = 10, expiryDate = today.AddDays(60) }, 201, "Receive missing medicine");
            var readyDetail = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{uiRxId}", null, 200, "Pharmacy reads eligible finalized prescription");
            Check(readyDetail.GetProperty("canDispense").GetBoolean() && !readyDetail.GetProperty("isDispensed").GetBoolean(), "Finalized prescription is eligible (no new payment requirement)");
            var concurrentDispense = await Task.WhenAll(Call("pharmacist", "POST", $"/api/pharmacy/prescriptions/{concurrentRxId}/dispense"), Call("pharmacist", "POST", $"/api/pharmacy/prescriptions/{concurrentRxId}/dispense"));
            Check(concurrentDispense.All(r => r.Status == 200) && concurrentDispense.Count(r => !Data(r).GetProperty("alreadyDispensed").GetBoolean()) == 1, "Concurrent dispensing executes once and replays once");
            await using (var db = new FoMedDbContext(options))
            {
                Check(await db.Set<PrescriptionDispense>().Where(d => d.PrescriptionItem.PrescriptionId == concurrentRxId).SumAsync(d => d.Quantity) == 3 &&
                    await db.Set<StockTransaction>().Where(t => t.RefType == "Prescription" && t.RefId == concurrentRxId).SumAsync(t => t.Quantity) == -3,
                    "Concurrent dispense deducts exactly the prescribed quantities");
            }
            // Item 4: server history, selection list/FEFO preview, and authenticated private files.
            var stockBeforePreview = await StockState();
            var preview = await Need("pharmacist", "GET", $"/api/pharmacy/prescriptions/{uiRxId}", null, 200, "FEFO preview before dispensing");
            var previewLine = preview.GetProperty("items")[0];
            Check(previewLine.GetProperty("remainingQuantity").GetInt32() == 2 && previewLine.GetProperty("shortageQuantity").GetInt32() == 0 && previewLine.GetProperty("proposedBatches").EnumerateArray().Sum(b => b.GetProperty("proposedQuantity").GetInt32()) == 2 && await StockState() == stockBeforePreview, "Preview proposes exact quantities without reserving/deducting stock");
            var history = await Need("technician", "GET", "/api/clinical/lab-results", null, 200, "Server-backed technician result history");
            Check(history.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("order").GetProperty("id").GetInt32() == orderId), "Completed result survives outside browser session storage");
            await Reject("doctor", "GET", "/api/clinical/lab-results", null, 403, "Doctor cannot use technician history endpoint");
            await Reject("technician", "GET", "/api/clinical/lab-results?page=0", null, 400, "Invalid lab history page rejected");
            await Reject("patient", "GET", "/api/pharmacy/prescriptions", null, 403, "Patient cannot list pharmacy prescriptions");
            await Reject("pharmacist", "GET", "/api/pharmacy/prescriptions?status=bogus", null, 400, "Invalid pharmacy list state rejected");
            var pendingRx = await Need("pharmacist", "GET", "/api/pharmacy/prescriptions", null, 200, "Server pharmacy selection list");
            Check(pendingRx.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("prescriptionId").GetInt32() == uiRxId) &&
                !pendingRx.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("prescriptionId").GetInt32() == draftRxId), "Selection list includes finalized pending prescription, excludes draft");
            var issuedRx = await Need("pharmacist", "GET", "/api/pharmacy/prescriptions?status=dispensed", null, 200, "Dispensed prescription list");
            Check(issuedRx.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("prescriptionId").GetInt32() == prescriptionId), "Fully dispensed prescription moves to issued list");
            var patientFiles = await Need("patient", "GET", $"/api/clinical/records/{recordId}/attachments", null, 200, "Patient lists finalized record and lab files");
            Check(patientFiles.GetProperty("total").GetInt32() == 2 && !patientFiles.GetRawText().Contains("fileUrl") && !patientFiles.GetRawText().Contains(attachmentRoot), "File list exposes safe metadata, not private paths");
            await Reject("other-patient", "GET", $"/api/clinical/attachments/{labAttachmentId}/download", null, 403, "Other patient cannot download lab file");
            await Reject("admin", "GET", $"/api/clinical/attachments/{recordAttachmentId}/download", null, 403, "Admin audit permission does not grant clinical file access");
            Check((await Upload("doctor", recordId, null, "after-final.pdf", pdfBytes)).Status == 409, "Doctor cannot modify attachments of finalized record");
            using (var downloadRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/clinical/attachments/{recordAttachmentId}/download"))
            {
                downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["patient"]);
                using var download = await client.SendAsync(downloadRequest);
                Check(download.StatusCode == HttpStatusCode.OK && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(pdfBytes) &&
                    download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.CacheControl?.NoStore == true &&
                    download.Headers.GetValues("X-Content-Type-Options").Contains("nosniff"), "Authenticated download returns exact file bytes with safe attachment/no-store headers");
            }
            var dicomBytes = new byte[133]; "DICM"u8.CopyTo(dicomBytes.AsSpan(128));
            Check((await Upload("technician", recordId, orderId, "result.dicom", dicomBytes)).Status == 201, "Technician can append DICOM Part 10 to own result after finalization");
            Check((await Upload("technician", recordId, cancelledId, "cancelled.png", pngBytes)).Status == 409, "Canceled order cannot receive an attachment");
            await using (var db = new FoMedDbContext(options))
            {
                var technicianId = await db.Users.Where(u => u.Username == "technician").Select(u => u.Id).SingleAsync();
                var patient = await db.Patients.SingleAsync(p => p.PatientCode == "AUDIT1");
                for (var index = 0; index < 12; index++)
                {
                    var historicalRecord = new MedicalRecord { PatientId = patient.Id, DoctorId = doctorId, IsFinalized = true, FinalizedAt = DateTime.UtcNow.AddYears(-1), CreatedAt = DateTime.UtcNow.AddYears(-1),
                        Appointment = new FoMed.Infrastructure.Models.Appointment { AppointmentCode = $"AUDIT-LIST-{index}", PatientId = patient.Id, DoctorId = doctorId,
                            StartTime = today.AddYears(-1).ToDateTime(new TimeOnly(8, 0)).AddMinutes(index * 15), EndTime = today.AddYears(-1).ToDateTime(new TimeOnly(8, 15)).AddMinutes(index * 15), Status = 3, FeeSnapshot = 300, CreatedAt = DateTime.UtcNow.AddYears(-1) } };
                    var listRx = new Prescription { MedicalRecord = historicalRecord, CreatedAt = DateTime.UtcNow.AddYears(-1) };
                    listRx.PrescriptionItems.Add(new PrescriptionItem { MedicineId = medicineId, Quantity = 1, UnitPriceSnapshot = 10, Dosage = "Audit" });
                    db.Add(listRx);
                    db.Add(new MedicalRecordService { MedicalRecordId = recordId, ServiceId = serviceId, Quantity = 1, UnitPriceSnapshot = 100, Status = 1, OrderedBy = doctorId, OrderedAt = DateTime.UtcNow.AddDays(-1),
                        LabResult = new LabResult { ResultSummary = $"Historical result {index}", TechnicianId = technicianId, ResultAt = DateTime.UtcNow.AddDays(-1).AddMinutes(index) } });
                }
                await db.SaveChangesAsync();
            }
            var rxPage1 = await Need("pharmacist", "GET", "/api/pharmacy/prescriptions?page=1", null, 200, "Prescription list page 1");
            var rxPage2 = await Need("pharmacist", "GET", "/api/pharmacy/prescriptions?page=2", null, 200, "Prescription list page 2");
            Check(rxPage1.GetProperty("items").GetArrayLength() == 10 && rxPage2.GetProperty("items").GetArrayLength() > 0 &&
                !rxPage1.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("prescriptionId").GetInt32()).Intersect(rxPage2.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("prescriptionId").GetInt32())).Any(), "Prescription server pagination is stable, 10 per page");
            var historyPage1 = await Need("technician", "GET", "/api/clinical/lab-results?page=1", null, 200, "Result history page 1");
            var historyPage2 = await Need("technician", "GET", "/api/clinical/lab-results?page=2", null, 200, "Result history page 2");
            Check(historyPage1.GetProperty("items").GetArrayLength() == 10 && historyPage2.GetProperty("items").GetArrayLength() == 3, "Lab result server history paginates 10 per page");
            int differentOrderId, differentResultId;
            await using (var db = new FoMedDbContext(options))
            {
                var differentResult = await db.LabResults.SingleAsync(r => r.ResultSummary == "Historical result 0");
                differentOrderId = differentResult.MedicalRecordServiceId; differentResultId = differentResult.Id;
            }
            Check(differentOrderId != differentResultId, "Attachment fixture has distinct order/result IDs");
            var distinctFile = await Upload("technician", recordId, differentOrderId, "distinct-result.png", pngBytes);
            Check(distinctFile.Status == 201 && Data(distinctFile).GetProperty("orderId").GetInt32() == differentOrderId, "Attachment API keeps orderId distinct from LabResult owner ID");
            var distinctFileId = Data(distinctFile).GetProperty("id").GetInt32();
            await using (var db = new FoMedDbContext(options))
                Check((await db.Attachments.SingleAsync(a => a.Id == distinctFileId)).OwnerId == differentResultId, "Lab attachment OwnerId references actual LabResult ID");
            var distinctFiles = await Need("technician", "GET", $"/api/clinical/records/{recordId}/attachments?orderId={differentOrderId}", null, 200, "Scoped file list with distinct result/order IDs");
            Check(distinctFiles.GetProperty("items").GetArrayLength() == 1 && distinctFiles.GetProperty("items")[0].GetProperty("id").GetInt32() == distinctFileId && distinctFiles.GetProperty("items")[0].GetProperty("orderId").GetInt32() == differentOrderId,
                "Scoped attachment lookup maps correct result to requested order");
            var otherHistory = await Need("other-technician", "GET", "/api/clinical/lab-results", null, 200, "Other technician history");
            Check(otherHistory.GetProperty("total").GetInt32() == 0, "Technician history is limited to own completed results");
            await Reject("other-technician", "GET", $"/api/clinical/records/{recordId}/attachments?orderId={orderId}", null, 403, "Other technician cannot list another technician's result files");
            await Reject("other-technician", "GET", $"/api/clinical/attachments/{labAttachmentId}/download", null, 403, "Other technician cannot download another technician's result file");
            Check((await Upload("technician", recordId, orderId, "bad.dicom", pdfBytes)).Status == 400, "DICOM extension requires Part 10 signature");
            await Reject("patient", "GET", $"/api/clinical/records/{recordId}/attachments?page=0", null, 400, "Invalid attachment page rejected");
            await Reject("patient", "GET", "/api/clinical/attachments/2147483647/download", null, 404, "Unknown attachment returns 404");
            int legacyFileId;
            await using (var db = new FoMedDbContext(options))
            {
                var legacyFiles = Enumerable.Range(0, 11).Select(index => new Attachment { OwnerType = "MedicalRecord", OwnerId = prescribingRecordId,
                    FileUrl = $"https://example.invalid/legacy-{index}.pdf", UploadedAt = DateTime.UtcNow.AddYears(-1) }).ToList();
                db.AddRange(legacyFiles); await db.SaveChangesAsync(); legacyFileId = legacyFiles[0].Id;
            }
            var filesPage1 = await Need("doctor", "GET", $"/api/clinical/records/{prescribingRecordId}/attachments?page=1", null, 200, "Attachment list first page");
            var filesPage2 = await Need("doctor", "GET", $"/api/clinical/records/{prescribingRecordId}/attachments?page=2", null, 200, "Attachment list second page");
            Check(filesPage1.GetProperty("total").GetInt32() == 11 && filesPage1.GetProperty("items").GetArrayLength() == 10 && filesPage2.GetProperty("items").GetArrayLength() == 1 &&
                filesPage1.GetProperty("items").EnumerateArray().All(a => !a.GetProperty("downloadable").GetBoolean()), "Attachments paginate 10 per page and mark unmigrated legacy files unavailable");
            await Reject("doctor", "GET", $"/api/clinical/attachments/{legacyFileId}/download", null, 404, "Legacy external URL is not fetched as clinical file");
            // VC-24: medical read/write audit contracts, historic identity and clinic-day boundaries.
            int doctorUserId, patientId;
            long legacyAuditId;
            await using (var db = new FoMedDbContext(options))
            {
                doctorUserId = await db.Users.Where(u => u.Username == "doctor").Select(u => u.Id).SingleAsync();
                patientId = await db.Patients.Where(p => p.PatientCode == "AUDIT1").Select(p => p.Id).SingleAsync();
            }
            var spoofedRead = await Call("doctor", "GET", $"/api/clinical/records/{recordId}", spoofContext: true);
            Check(spoofedRead.Status == 200, "Authorized record read succeeds with untrusted headers");
            await using (var db = new FoMedDbContext(options))
            {
                var latest = await db.AuditLogs.Where(a => a.Entity == "MedicalRecord" && a.EntityId == recordId && a.Action == "Read").OrderByDescending(a => a.Id).FirstAsync();
                var metadata = FoMed.Application.Services.Clinical.MedicalRecordAudit.ReadMetadata(latest)!;
                Check(latest.UserId == doctorUserId && metadata.Actor.Username == "doctor" && metadata.Actor.Roles.SequenceEqual(new[] { "Doctor" }), "Audit actor comes from authenticated user and database roles");
                Check(metadata.Context.IpAddress is "127.0.0.1" or "::1" && !string.IsNullOrWhiteSpace(metadata.Context.RequestId) && metadata.Context.RequestId != "caller-controlled-request-id", "Audit uses server connection IP and trace ID, not caller headers");
                var create = await db.AuditLogs.Where(a => a.Entity == "MedicalRecord" && a.EntityId == recordId && a.Action == "Create").SingleAsync();
                Check(create.NewValue is not null && !create.NewValue.Contains("Audit diagnosis") && !create.NewValue.Contains("Audit Penicillin"), "Audit storage contains metadata only, not clinical values");
                Check(await db.AuditLogs.CountAsync(a => a.Entity == "MedicalRecord" && a.EntityId == recordId && a.Action == "Finalize") == 1, "Finalization retry creates no duplicate audit event");
            }
            // All three summary endpoints return clinical data; each returned record must be auditable.
            DateTime historySlot;
            await using (var db = new FoMedDbContext(options))
            {
                var usedStarts = (await db.Appointments.Where(a => a.DoctorId == doctorId).Select(a => a.StartTime).ToListAsync()).ToHashSet();
                historySlot = available.Select(slot => slot.GetProperty("startTime").GetDateTime()).First(time => !usedStarts.Contains(time));
            }
            var historyBooking = await Need("patient", "POST", "/api/appointments/book", new { doctorId, startTime = historySlot }, 201, "Book consultation with prior medical history");
            var historyAppointmentId = historyBooking.GetProperty("id").GetInt32();
            await Need("receptionist", "PUT", $"/api/appointments/{historyAppointmentId}/confirm", new { }, 200, "Confirm history queue fixture");
            await Need("receptionist", "PUT", $"/api/appointments/{historyAppointmentId}/check-in", new { }, 200, "Check in history queue fixture");
            var historyQueue = await Need("doctor", "GET", $"/api/appointments/doctor-queue?date={today:yyyy-MM-dd}", null, 200, "Doctor queue history read");
            Check(historyQueue.EnumerateArray().Any(item => item.GetProperty("recentHistory").EnumerateArray().Any(record => record.GetProperty("medicalRecordId").GetInt32() == recordId)), "Doctor queue actually returns the audited previous medical record");
            await Need("receptionist", "GET", $"/api/patients/staff/{patientId}/history", null, 200, "Reception history summary read");
            var auditOrder = await Need("doctor", "POST", $"/api/clinical/records/{prescribingRecordId}/services", new { serviceId, quantity = 1 }, 201, "Create pending order for lab audit");
            await Need("technician", "GET", "/api/clinical/lab-orders", null, 200, "Technician queue read");
            await Need("doctor", "PUT", $"/api/clinical/orders/{auditOrder.GetProperty("id").GetInt32()}/cancel", null, 200, "Cancel disposable lab audit order");
            var browserOrder = await Need("doctor", "POST", $"/api/clinical/records/{prescribingRecordId}/services", new { serviceId, quantity = 1 }, 201, "Create pending order for browser result submission");
            var pendingUiOrderId = browserOrder.GetProperty("id").GetInt32();
            await Need("patient", "GET", "/api/clinical/records", null, 200, "Patient record list read");
            await Need("patient", "GET", $"/api/clinical/records/{recordId}/services", null, 200, "Patient reads finalized lab results");
            // Change the live account: existing events must keep the identity/roles captured at the time.
            await using (var db = new FoMedDbContext(options))
            {
                var actor = await db.Users.SingleAsync(u => u.Id == doctorUserId);
                actor.Username = "renamed-doctor"; actor.FullName = "Renamed actor";
                db.UserRoles.Add(new UserRole { UserId = doctorUserId, RoleId = await db.Roles.Where(r => r.Name == "Technician").Select(r => r.Id).SingleAsync() });
                await db.SaveChangesAsync();
            }
            try
            {
                var historic = await Call("admin", "GET", $"/api/audit-logs?entity=MedicalRecord&action=Create&userId={doctorUserId}&pageSize=200");
                var row = historic.Body.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("entityId").GetInt32() == recordId);
                Check(historic.Status == 200 && row.GetProperty("username").GetString() == "doctor" && row.GetProperty("fullName").GetString() == "Audit doctor" && row.GetProperty("actorSnapshot").GetBoolean() && row.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).SequenceEqual(new[] { "Doctor" }), "Historic audit identity/roles survive live account edits");
                await Need("doctor", "GET", $"/api/clinical/records/{recordId}", null, 200, "Existing authenticated actor reads after account edit");
                var current = await Call("admin", "GET", $"/api/audit-logs?entity=MedicalRecord&action=Read&userId={doctorUserId}&pageSize=1");
                var newRow = current.Body.GetProperty("items")[0];
                Check(newRow.GetProperty("username").GetString() == "renamed-doctor" && newRow.GetProperty("roles").GetArrayLength() == 2, "New event captures updated live actor, without rewriting history");
            }
            finally
            {
                await using var db = new FoMedDbContext(options);
                var actor = await db.Users.SingleAsync(u => u.Id == doctorUserId);
                actor.Username = "doctor"; actor.FullName = "Audit doctor";
                db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == doctorUserId && r.Role.Name == "Technician").ToListAsync());
                await db.SaveChangesAsync();
            }
            // Legacy strings/malformed JSON must remain readable without leaking stored clinical snapshots.
            var boundary = new DateTime(2001, 1, 1, 17, 0, 0, DateTimeKind.Utc); // 02/01 00:00 clinic time.
            await using (var db = new FoMedDbContext(options))
            {
                var legacy = new AuditLog { UserId = doctorUserId, Entity = "MedicalRecord", EntityId = recordId, Action = "Read", CreatedAt = boundary,
                    OldValue = "PRIVATE_CLINICAL_SENTINEL", NewValue = "{invalid PRIVATE_CLINICAL_SENTINEL" };
                db.AuditLogs.AddRange(legacy,
                    new AuditLog { UserId = doctorUserId, Entity = "MedicalRecord", EntityId = recordId, Action = "Read", CreatedAt = boundary.AddTicks(-10000000), NewValue = "old legacy text" },
                    new AuditLog { UserId = doctorUserId, Entity = "MedicalRecord", EntityId = recordId, Action = "Read", CreatedAt = boundary.AddDays(1), NewValue = "old legacy text" });
                await db.SaveChangesAsync(); legacyAuditId = legacy.Id;
            }
            var dateAudit = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&action=Read&from=2001-01-02T00%3A00%3A00%2B07%3A00&to=2001-01-03T00%3A00%3A00%2B07%3A00");
            Check(dateAudit.Status == 200 && dateAudit.Body.GetProperty("total").GetInt32() == 1 && dateAudit.Body.GetProperty("items")[0].GetProperty("auditLogId").GetInt64() == legacyAuditId, "Clinic date filter is inclusive from and exclusive to at UTC+07 boundary");
            var oldRow = dateAudit.Body.GetProperty("items")[0];
            Check(!oldRow.GetProperty("actorSnapshot").GetBoolean() && oldRow.GetProperty("ipAddress").ValueKind == JsonValueKind.Null && oldRow.GetProperty("oldValue").ValueKind == JsonValueKind.Null && oldRow.GetProperty("newValue").ValueKind == JsonValueKind.Null && !dateAudit.Body.GetRawText().Contains("PRIVATE_CLINICAL_SENTINEL"), "Legacy malformed metadata falls back safely and never exposes raw snapshots");
            Check(oldRow.GetProperty("createdAt").GetString()!.EndsWith('Z'), "Audit timestamps serialize with explicit UTC offset");
            var unspecifiedDate = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&from=2001-01-02&to=2001-01-03");
            Check(unspecifiedDate.Status == 200 && unspecifiedDate.Body.GetProperty("total").GetInt32() == 1, "Offset-free dates default to clinic timezone, not server timezone");
            var page1 = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&action=Read&pageSize=10&page=1");
            var page2 = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&action=Read&pageSize=10&page=2");
            var firstIds = page1.Body.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("auditLogId").GetInt64()).ToArray();
            var secondIds = page2.Body.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("auditLogId").GetInt64()).ToArray();
            Check(firstIds.Length == 10 && secondIds.Length > 0 && firstIds.Intersect(secondIds).Count() == 0 && firstIds.Last() > secondIds.First() && page1.Body.GetProperty("total").GetInt32() == page2.Body.GetProperty("total").GetInt32(), "Audit server pagination is stable, 10 per page, with no repeated IDs");
            var allEvents = await Call("admin", "GET", "/api/audit-logs?entity=MedicalRecord&pageSize=200");
            var sources = allEvents.Body.GetProperty("items").EnumerateArray().Where(a => a.GetProperty("source").ValueKind == JsonValueKind.String).Select(a => a.GetProperty("source").GetString()).ToHashSet();
            var missingSources = new[] { "CreateRecord", "UpdateRecord", "FinalizeRecord", "CreatePrescription", "UpdatePrescription", "OrderService", "CancelServiceOrder", "SaveLabResult", "Record", "Prescription", "ServiceOrders", "RecordList", "DoctorQueueHistory", "PatientHistorySummary", "LabQueue", "PrescribingContext" }.Where(source => !sources.Contains(source)).ToArray();
            Check(missingSources.Length == 0, "All supported clinical read/write sources are traceable", string.Join(", ", missingSources));
            foreach (var role in new[] { "doctor", "receptionist", "technician", "pharmacist" })
                await Reject(role, "GET", "/api/audit-logs?entity=MedicalRecord", null, 403, role + " cannot read admin audit");
            foreach (var query in new[] { "userId=0", "page=2147483647", "pageSize=201", "from=0001-01-01", "from=2001-01-03&to=2001-01-02" })
                await Reject("admin", "GET", "/api/audit-logs?entity=MedicalRecord&" + query, null, 400, "Invalid audit filter rejected: " + query);
            var beforeUnsupported = await RecordAuditCount();
            var updateAudit = await Call("admin", "PUT", "/api/audit-logs", new { auditLogId = legacyAuditId });
            var deleteAudit = await Call("admin", "DELETE", "/api/audit-logs");
            Check(updateAudit.Status == 405 && deleteAudit.Status == 405 && await RecordAuditCount() == beforeUnsupported, "Audit API is append-only: no update/delete routes");
            await PaymentCashAudit.RunAsync(options, Call, Check);
            if (args.Contains("--browser"))
            {
                // Seed only identities/catalog/stock, never the journey's clinical or billing state.
                // Every appointment, record, order, result, prescription and payment below is made in UI.
                int journeyDoctorId, journeyDoctorUserId, journeyMedicineId, journeyServiceId;
                var journeyPatients = new List<object>();
                var journeyPatientIds = new List<int>();
                await using (var seed = new FoMedDbContext(options))
                {
                    var roles = await seed.Roles.ToDictionaryAsync(r => r.Name);
                    User Account(string role, string username, string fullName)
                    {
                        var user = new User { Username = username, FullName = fullName, Email = username + "@test.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), IsActive = true, CreatedAt = DateTime.UtcNow };
                        user.UserRoles.Add(new UserRole { Role = roles[role] }); seed.Add(user); return user;
                    }
                    var doctor = Account("Doctor", "journey-doctor", "Journey doctor");
                    doctor.Doctor = new Doctor { FullName = doctor.FullName!, Specialty = new Specialty { Name = "Journey specialty", IsActive = true }, ConsultationFee = 300, Room = "JOURNEY", IsActive = true };
                    for (byte day = 0; day < 7; day++) doctor.Doctor.DoctorSchedules.Add(new DoctorSchedule { DayOfWeek = day, StartTime = new(0, 0), EndTime = new(23, 59), SlotMinutes = 15, IsActive = true });
                    var medicine = new Medicine { Name = "Workflow medicine", Unit = "tablet", Price = 10, IsActive = true };
                    medicine.MedicineBatches.Add(new MedicineBatch { LotNumber = "JOURNEY-EARLY", ExpiryDate = today.AddDays(30), Quantity = 1, CreatedAt = DateTime.UtcNow });
                    medicine.MedicineBatches.Add(new MedicineBatch { LotNumber = "JOURNEY-LATE", ExpiryDate = today.AddDays(60), Quantity = 9, CreatedAt = DateTime.UtcNow });
                    var service = new Service { Name = "Workflow lab", Price = 100, IsActive = true };
                    seed.AddRange(medicine, service);
                    await seed.SaveChangesAsync();
                    journeyDoctorId = doctor.Doctor.Id; journeyDoctorUserId = doctor.Id; journeyMedicineId = medicine.Id; journeyServiceId = service.Id;
                    foreach (var width in new[] { 1440, 390 })
                    {
                        var variant = width == 1440 ? "desktop" : "mobile";
                        var patient = Account("Patient", "journey-patient-" + variant, "Journey patient " + variant);
                        patient.Patient = new Patient { FullName = patient.FullName!, PatientCode = "JOURNEY-" + variant, Phone = width == 1440 ? "0911111111" : "0922222222", Allergies = "Workflow allergen", DateOfBirth = new(1995, 1, 1), IsActive = true };
                        await seed.SaveChangesAsync();
                        journeyPatientIds.Add(patient.Patient.Id);
                        journeyPatients.Add(new { username = patient.Username, patientId = patient.Patient.Id, patientName = patient.FullName, width });
                    }
                }
                var browser = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "FoMed-Frontend"), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                browser.ArgumentList.Add("tests/clinic-workflow.smoke.cjs");
                browser.Environment["FOMED_AUDIT_DATA"] = JsonSerializer.Serialize(new { recordId, invoiceId, prescriptionId, draftId, draftRxId, uiRxId, prescribingRecordId, browserMedicineId, focusDispensing, doctorUserId, legacyAuditId, orderId, labAttachmentId, recordAttachmentId, pendingUiOrderId });
                browser.Environment["FOMED_JOURNEY_DATA"] = JsonSerializer.Serialize(new { doctorId = journeyDoctorId, doctorUserId = journeyDoctorUserId, medicineId = journeyMedicineId, serviceId = journeyServiceId, cancelServiceId, patients = journeyPatients });
                browser.Environment["FOMED_JOURNEY_ONLY"] = args.Contains("--journey-only") ? "1" : "0";
                using var process = Process.Start(browser) ?? throw new Exception("Cannot start browser audit");
                process.OutputDataReceived += (_, output) => { if (output.Data is not null) Console.WriteLine(output.Data); };
                process.ErrorDataReceived += (_, output) => { if (output.Data is not null) Console.Error.WriteLine(output.Data); };
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    throw new TimeoutException("Browser audit timed out; owned browser process stopped.");
                }
                Check(process.ExitCode == 0, "Browser workflow page/role/responsive smoke", process.ExitCode);
                await using var db = new FoMedDbContext(options);
                if (!args.Contains("--journey-only"))
                {
                Check(await db.Set<PrescriptionDispense>().Where(d => d.PrescriptionItem.PrescriptionId == uiRxId).SumAsync(d => d.Quantity) == 2,
                    "Browser confirmation creates exactly one dispensing allocation");
                var savedBrowserRx = await db.Prescriptions.Include(p => p.PrescriptionItems).SingleAsync(p => p.MedicalRecordId == prescribingRecordId);
                Check(savedBrowserRx.PrescriptionItems.Single().MedicineId == browserMedicineId && savedBrowserRx.PrescriptionItems.Single().Quantity == 3 &&
                    await db.MedicineBatches.Where(b => b.MedicineId == browserMedicineId && b.ExpiryDate >= today).SumAsync(b => b.Quantity) == 10,
                    "Browser creates and updates prescription without deducting stock");
                }
                foreach (var journeyPatientId in journeyPatientIds)
                {
                    var appointments = await db.Appointments.Include(a => a.AppointmentStatusHistories).Where(a => a.PatientId == journeyPatientId).ToListAsync();
                    Check(appointments.Count == 1 && appointments[0].Status == 3 && appointments[0].CheckedInAt.HasValue && appointments[0].QueueNumber.HasValue,
                        $"UI journey patient {journeyPatientId}: exactly one completed checked-in appointment");
                    Check(appointments.Count == 1 && appointments[0].AppointmentStatusHistories.Select(h => h.ToStatus).Distinct().Order().SequenceEqual(new byte[] { 0, 1, 2, 3 }),
                        $"UI journey patient {journeyPatientId}: pending/confirmed/in-progress/completed history persists");
                    var records = await db.MedicalRecords.Include(r => r.Prescription!).ThenInclude(p => p.PrescriptionItems).Include(r => r.MedicalRecordServices).ThenInclude(s => s.LabResult).Where(r => r.PatientId == journeyPatientId).ToListAsync();
                    var journeyRecord = records.SingleOrDefault();
                    Check(journeyRecord?.IsFinalized == true && journeyRecord.Diagnosis == (journeyPatientId == journeyPatientIds[0] ? "Journey diagnosis desktop" : "Journey diagnosis mobile") && journeyRecord.FollowUpDate == null, $"UI journey patient {journeyPatientId}: finalized clinical record and cleared follow-up persist");
                    Check(journeyRecord?.Prescription?.PrescriptionItems.Count == 1 && journeyRecord.Prescription.PrescriptionItems.Single().Quantity == 2 && journeyRecord.Prescription.PrescriptionItems.Single().UnitPriceSnapshot == 10,
                        $"UI journey patient {journeyPatientId}: prescription quantity and price snapshot");
                    Check(journeyRecord?.MedicalRecordServices.Count == 2 && journeyRecord.MedicalRecordServices.Count(s => s.Status == 1 && s.LabResult != null) == 1 && journeyRecord.MedicalRecordServices.Count(s => s.Status == 2) == 1,
                        $"UI journey patient {journeyPatientId}: completed result and canceled order are distinct");
                    var invoices = await db.Invoices.Include(i => i.Payments).Where(i => i.PatientId == journeyPatientId).ToListAsync();
                    Check(invoices.Count == 1 && invoices[0].Status == 1 && invoices[0].TotalAmount == 420 && invoices[0].Payments.Count == 1 && invoices[0].Payments.Sum(p => p.Amount) == 420,
                        $"UI journey patient {journeyPatientId}: one invoice/settlement, no cash change or canceled service charged");
                    var cash = invoices.Single().Payments.Single();
                    Check(cash.CashReceived == 500 && cash.ReceivedByNameSnapshot == "Audit receptionist" && cash.ReceivedBy.HasValue && cash.IdempotencyKey.HasValue,
                        $"UI journey patient {journeyPatientId}: tender, collector snapshot and retry key persist");
                    Check(journeyRecord?.Prescription != null && await db.Set<PrescriptionDispense>().Where(d => d.PrescriptionItem.PrescriptionId == journeyRecord.Prescription.Id).SumAsync(d => d.Quantity) == 2,
                        $"UI journey patient {journeyPatientId}: exactly two units dispensed");
                    if (journeyRecord != null)
                    {
                        var resultIds = journeyRecord.MedicalRecordServices.Where(s => s.LabResult != null).Select(s => s.LabResult!.Id).ToArray();
                        Check(await db.Attachments.CountAsync(a => (a.OwnerType == "MedicalRecord" && a.OwnerId == journeyRecord.Id) || (a.OwnerType == "LabResult" && resultIds.Contains(a.OwnerId))) == 2,
                            $"UI journey patient {journeyPatientId}: doctor and technician files have correct owners");
                        Check(await db.AuditLogs.AnyAsync(a => a.Entity == "MedicalRecord" && a.EntityId == journeyRecord.Id && a.Action == "Finalize"),
                            $"UI journey patient {journeyPatientId}: finalization audited");
                    }
                }
                Check(await db.MedicineBatches.Where(b => b.MedicineId == journeyMedicineId).SumAsync(b => b.Quantity) == 6 && await db.MedicineBatches.Where(b => b.MedicineId == journeyMedicineId && b.LotNumber == "JOURNEY-EARLY").SumAsync(b => b.Quantity) == 0,
                    "Two full UI journeys consume exactly four units, earliest-expiry batch first");
            }
        }
        finally
        {
            if (api is not null) { if (!api.HasExited) { api.Kill(entireProcessTree: true); await api.WaitForExitAsync(); } api.Dispose(); }
            SqlConnection.ClearAllPools();
            var expectedAttachmentParent = Path.GetFullPath(Path.Combine(root, "tests", "ClinicWorkflow", "bin")) + Path.DirectorySeparatorChar;
            if (!attachmentRoot.StartsWith(expectedAttachmentParent, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(attachmentRoot) != name)
                throw new Exception("Unsafe audit attachment cleanup path");
            if (Directory.Exists(attachmentRoot)) Directory.Delete(attachmentRoot, recursive: true);
            if (!Regex.IsMatch(name, "^FoMed_Audit_[a-f0-9]{32}$")) throw new Exception("Invalid audit database name");
            await new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", admin).ExecuteNonQueryAsync();
            Directory.CreateDirectory("tests/ClinicWorkflow/bin/audit-results");
            await File.WriteAllTextAsync("tests/ClinicWorkflow/bin/audit-results/http-workflow.json", JsonSerializer.Serialize(new { executedAtUtc = DateTime.UtcNow, passed, failed, observations }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Audit: {passed} passed, {failed} failed. Isolated database removed; application data untouched.");
        }
        if (failed > 0) Environment.ExitCode = 1;
    }
}
