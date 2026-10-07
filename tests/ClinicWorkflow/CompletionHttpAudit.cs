using System.Text.Json;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

internal static class CompletionHttpAudit
{
    internal static async Task RunAsync(DbContextOptions<FoMedDbContext> options,
        Func<string?, string, string, object?, bool, Task<(int Status, JsonElement Body)>> call,
        Action<bool, string, object?> check)
    {
        async Task<JsonElement> Expect(string? role, string method, string path, object? data, int status, string label)
        {
            var response = await call(role, method, path, data, false);
            check(response.Status == status, "FM completion: " + label, response.Status);
            if (response.Status != status) throw new InvalidOperationException("Completion audit stopped at " + label);
            return response.Body.ValueKind == JsonValueKind.Object && response.Body.TryGetProperty("dataResponse", out var value) ? value : response.Body;
        }

        await Expect(null, "GET", "/api/admin/medicines", null, 401, "medicine catalog requires login");
        foreach (var role in new[] { "patient", "doctor", "receptionist", "technician", "pharmacist" })
            await Expect(role, "GET", "/api/admin/medicines", null, 403, role + " cannot manage medicine catalog");
        await Expect("admin", "GET", "/api/admin/medicines?page=0", null, 400, "invalid medicine page rejected");
        await Expect("admin", "POST", "/api/admin/medicines", new { name = "", unit = "viên", price = -1 }, 400, "invalid medicine rejected");
        var medicine = await Expect("admin", "POST", "/api/admin/medicines",
            new { name = "Thuốc kiểm thử FM-04", unit = "viên", price = 1250, description = "Chỉ dùng kiểm thử" }, 201, "Admin creates medicine");
        var medicineId = medicine.GetProperty("id").GetInt32();
        var oldVersion = medicine.GetProperty("version").GetString();
        check(medicine.GetProperty("stockQuantity").GetInt64() == 0 && medicine.GetProperty("availableQuantity").GetInt64() == 0,
            "FM completion: catalog creation does not invent inventory", null);
        var edit = new { name = "Thuốc kiểm thử FM-04", unit = "viên", price = 1500, description = "Đã cập nhật", expectedVersion = oldVersion };
        medicine = await Expect("admin", "PUT", $"/api/admin/medicines/{medicineId}", edit, 200, "Admin updates medicine");
        await Expect("admin", "PUT", $"/api/admin/medicines/{medicineId}", edit, 409, "stale medicine edit rejected");
        var catalog = await Expect("admin", "GET", "/api/admin/medicines?keyword=FM-04&status=active&page=1", null, 200, "catalog search and active filter");
        check(catalog.GetProperty("totalCount").GetInt32() == 1 && catalog.GetProperty("items")[0].GetProperty("price").GetDecimal() == 1500,
            "FM completion: filtered medicine retains saved price", null);
        medicine = await Expect("admin", "PUT", $"/api/admin/medicines/{medicineId}/status",
            new { isActive = false, expectedVersion = medicine.GetProperty("version").GetString() }, 200, "Admin deactivates unused medicine");
        catalog = await Expect("admin", "GET", "/api/admin/medicines?keyword=FM-04&status=active", null, 200, "active catalog after deactivation");
        check(catalog.GetProperty("totalCount").GetInt32() == 0, "FM completion: inactive medicine excluded from active catalog", null);
        await Expect("admin", "GET", $"/api/admin/medicines/{medicineId}", null, 200, "deactivation preserves medicine history");

        int doctorId;
        await using (var db = new FoMedDbContext(options))
        {
            var role = await db.Roles.SingleAsync(r => r.Name == "Doctor");
            var user = new User { Username = "fm04-schedule-doctor", FullName = "Bác sĩ kiểm thử FM-04", PasswordHash = "unused-fixture-no-login", IsActive = true, CreatedAt = DateTime.UtcNow };
            user.UserRoles.Add(new UserRole { Role = role });
            user.Doctor = new Doctor { FullName = user.FullName, SpecialtyId = await db.Specialties.Select(s => s.Id).FirstAsync(), ConsultationFee = 500, IsActive = true };
            db.Add(user); await db.SaveChangesAsync(); doctorId = user.Doctor.Id;
        }
        await Expect(null, "GET", "/api/reception/schedules", null, 401, "schedules require login");
        foreach (var role in new[] { "patient", "doctor", "technician", "pharmacist" })
            await Expect(role, "GET", "/api/reception/schedules", null, 403, role + " cannot manage reception schedules");
        var doctors = await Expect("receptionist", "GET", "/api/reception/schedules/doctors", null, 200, "Reception reads scheduling choices");
        check(doctors.EnumerateArray().Any(d => d.GetProperty("doctorId").GetInt32() == doctorId)
            && doctors.EnumerateArray().All(d => !d.TryGetProperty("passwordHash", out _) && !d.TryGetProperty("email", out _) && !d.TryGetProperty("phone", out _)),
            "FM completion: doctor choices contain no private account fields", null);
        var date = DateOnly.FromDateTime(ClinicTime.Now.AddDays(7));
        var weekday = (byte)date.DayOfWeek;
        // byte[] serializes as base64, unlike the numeric weekday array sent by the frontend.
        var batch = new { doctorId, dayOfWeeks = new[] { (int)weekday }, startTime = "08:00:00", endTime = "12:00:00", slotMinutes = 30, isActive = true };
        var shifts = await Expect("receptionist", "POST", "/api/reception/schedules/batch", batch, 201, "Reception creates working shift");
        var shift = shifts[0]; var shiftId = shift.GetProperty("id").GetInt32();
        await Expect("receptionist", "POST", "/api/reception/schedules/batch", batch, 409, "overlapping schedule rejected");
        var slots = await Expect(null, "GET", $"/api/appointments/available-slots?doctorId={doctorId}&date={date:yyyy-MM-dd}", null, 200, "public booking reflects new shift");
        check(slots.EnumerateArray().Count(s => s.GetProperty("isAvailable").GetBoolean()) == 8, "FM completion: new shift exposes eight 30-minute slots", null);
        var appointment = await Expect("other-patient", "POST", "/api/appointments/book",
            new { doctorId, startTime = date.ToDateTime(new TimeOnly(11, 30)) }, 201, "patient books Reception-created shift");
        var appointmentId = appointment.GetProperty("id").GetInt32();
        var version = shift.GetProperty("version").GetString();
        await Expect("receptionist", "DELETE", $"/api/reception/schedules/{shiftId}", null, 400, "deactivation requires version");
        await Expect("receptionist", "DELETE", $"/api/reception/schedules/{shiftId}?expectedVersion={version}", null, 409, "booked shift cannot be deactivated");
        await Expect("receptionist", "PUT", $"/api/reception/schedules/{shiftId}",
            new { doctorId, dayOfWeek = weekday, startTime = "08:00:00", endTime = "11:00:00", slotMinutes = 30, isActive = true, expectedVersion = version }, 409, "shortening cannot orphan booking");
        shift = await Expect("receptionist", "PUT", $"/api/reception/schedules/{shiftId}",
            new { doctorId, dayOfWeek = weekday, startTime = "08:00:00", endTime = "13:00:00", slotMinutes = 30, isActive = true, expectedVersion = version }, 200, "safe extension preserves booking");
        await Expect("receptionist", "DELETE", $"/api/reception/schedules/{shiftId}?expectedVersion={version}", null, 409, "stale schedule version rejected");
        await Expect("receptionist", "GET", $"/api/reception/schedules/time-off?doctorId={doctorId}", null, 200, "Reception reads time off");
        await Expect("receptionist", "POST", "/api/reception/schedules/time-off", new { doctorId }, 405, "Reception time off remains read only");
        await using (var db = new FoMedDbContext(options))
        {
            check(await db.Appointments.CountAsync(a => a.Id == appointmentId && a.Status == 0) == 1,
                "FM completion: failed shift changes preserve pending booking", null);
            check(await db.AuditLogs.AnyAsync(a => a.Entity == "DoctorSchedule" && a.EntityId == shiftId && a.Action == "Create")
                && await db.AuditLogs.AnyAsync(a => a.Entity == "DoctorSchedule" && a.EntityId == shiftId && a.Action == "Update"),
                "FM completion: HTTP schedule writes retain audit history", null);
            check(await db.Medicines.CountAsync(m => m.Id == medicineId && !m.IsActive && m.Price == 1500) == 1,
                "FM completion: stale edit never overwrites saved medicine", null);
        }
    }
}
