using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

// Runs the production React page/API adapters over real loopback HTTP/JWT/SQL.
// Called only inside HttpWorkflowAudit's guarded disposable localhost database.
internal static class AdminScheduleFrontendAudit
{
    internal static async Task RunAsync(DbContextOptions<FoMedDbContext> options, Uri apiUrl, string root, bool browser,
        Action<bool, string, object?> check)
    {
        if (apiUrl.Host != "127.0.0.1" || apiUrl.Scheme != "http") throw new InvalidOperationException("Loopback audit API required.");
        var date = ClinicTime.Now.Date.AddDays(7);
        int doctorA, doctorB, inactiveId, historyId;
        await using (var db = new FoMedDbContext(options))
        {
            var doctors = await db.Doctors.OrderBy(d => d.Id).Take(2).ToListAsync();
            doctorA = doctors[0].Id; doctorB = doctors[1].Id;
            foreach (var shift in await db.DoctorSchedules.ToListAsync())
            { shift.StartTime = new(8, 0); shift.EndTime = new(12, 0); shift.SlotMinutes = 30; }
            var role = await db.Roles.SingleAsync(r => r.Name == "Doctor");
            var inactive = new Doctor
            {
                FullName = "Audit inactive doctor", IsActive = false, SpecialtyId = doctors[0].SpecialtyId,
                User = new User { Username = "inactive-schedule-audit", FullName = "Audit inactive doctor", IsActive = false,
                    PasswordHash = "synthetic-not-a-password", UserRoles = [new UserRole { Role = role }] }
            };
            db.Add(inactive); await db.SaveChangesAsync(); inactiveId = inactive.Id;
            var history = new DoctorTimeOff { DoctorId = inactiveId, StartAt = date.AddDays(-30).AddHours(8), EndAt = date.AddDays(-30).AddHours(9), Reason = "Audit inactive history" };
            db.Add(history); await db.SaveChangesAsync(); historyId = history.Id;
            var patient = await db.Patients.OrderBy(p => p.Id).FirstAsync();
            db.AddRange(
                new Appointment { AppointmentCode = "SCHEDULE-AUDIT-B", PatientId = patient.Id, DoctorId = doctorB,
                    StartTime = date.AddDays(1).AddHours(9), EndTime = date.AddDays(1).AddHours(9.5), Status = 1 },
                new Appointment { AppointmentCode = "SCHEDULE-AUDIT-A", PatientId = patient.Id, DoctorId = doctorA,
                    StartTime = date.AddDays(2).AddHours(9), EndTime = date.AddDays(2).AddHours(9.5), Status = 1 });
            await db.SaveChangesAsync();
        }
        var node = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "FoMed-Frontend"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        node.ArgumentList.Add("tests/admin-schedules.integration.cjs");
        node.Environment["FOMED_SCHEDULE_AUDIT"] = JsonSerializer.Serialize(new
        { apiUrl = new Uri(apiUrl, "api").ToString(), doctorA, doctorB, inactiveId, historyId, date = date.ToString("yyyy-MM-dd") });
        using (var process = Process.Start(node) ?? throw new InvalidOperationException("Cannot start React/HTTP integration audit."))
        {
            process.OutputDataReceived += (_, line) => { if (line.Data is not null) Console.WriteLine(line.Data); };
            process.ErrorDataReceived += (_, line) => { if (line.Data is not null) Console.Error.WriteLine(line.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                throw new TimeoutException("React/HTTP audit timed out.");
            }
            check(process.ExitCode == 0, "Production React page + HTTP adapters + JWT + SQL integration", process.ExitCode);
            if (process.ExitCode != 0) throw new InvalidOperationException("React/HTTP integration audit found a failure; see named check above.");
        }
        await using (var db = new FoMedDbContext(options))
        {
            check(await db.Appointments.CountAsync() == 2, "Scheduling UI never moves, cancels or duplicates existing appointments", null);
            check(!await db.DoctorTimeOffs.AnyAsync(t => t.DoctorId == int.MaxValue || t.DoctorId == 0 || t.DoctorId < 0), "No invalid doctor leave saved after real HTTP failures", null);
            check(await db.AuditLogs.AnyAsync(a => a.Entity == "DoctorTimeOff" && a.Action == "Delete"), "UI-confirmed leave deletion persisted its audit log", null);
        }
        if (!browser) return;

        // A separate strict-port Vite process is owned and stopped by this audit.
        var probe = new TcpListener(IPAddress.Loopback, 5194); probe.Start(); probe.Stop();
        var vite = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "FoMed-Frontend"), UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "5194", "--strictPort" }) vite.ArgumentList.Add(argument);
        vite.Environment["VITE_API_URL"] = "/api";
        vite.Environment["VITE_API_PROXY_TARGET"] = apiUrl.ToString();
        using var frontend = Process.Start(vite) ?? throw new InvalidOperationException("Cannot start isolated Vite.");
        var releasePath = Path.Combine(root, "tests", "ClinicWorkflow", "bin", "schedule-browser-" + Guid.NewGuid().ToString("N") + ".stop");
        try
        {
            Console.WriteLine("BROWSER AUDIT READY: http://127.0.0.1:5194/login?returnUrl=%2Fadmin%2Fschedules");
            Console.WriteLine("Synthetic localhost login: admin / Audit-Only!2026.");
            Console.WriteLine("BROWSER AUDIT STOP FILE: " + releasePath);
            // Keep the disposable database/API alive while the browser is inspected.
            var deadline = DateTime.UtcNow.AddMinutes(15);
            while (!File.Exists(releasePath) && DateTime.UtcNow < deadline) await Task.Delay(250);
        }
        finally
        {
            if (!frontend.HasExited) { frontend.Kill(entireProcessTree: true); await frontend.WaitForExitAsync(); }
            if (File.Exists(releasePath)) File.Delete(releasePath);
        }
    }
}
