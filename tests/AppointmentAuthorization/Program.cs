using System.Reflection;
using FoMed.Api.Controllers;
using FoMed.Application.DTO.Appointment;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Auth;
using FoMed.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

// Dependency-free regression checks: dotnet run --project tests/AppointmentAuthorization
var cases = 0;
foreach (var operation in new[] { "confirm", "checkin", "complete", "cancel", "history" })
foreach (var actor in new[] { "patient", "other-patient", "doctor", "other-doctor", "Receptionist", "Admin", "inactive-doctor", "inactive-user", "missing-user" })
{
    var appointment = new Appointment
    {
        Id = 1, DoctorId = 10, PatientId = 20, AppointmentCode = "AP1",
        Doctor = new Doctor { Id = 10, FullName = "Doctor" },
        Patient = new Patient { Id = 20, FullName = "Patient" },
        StartTime = operation == "cancel"
            ? ClinicTime.Now.AddDays(2)
            : DateOnly.FromDateTime(ClinicTime.Now).ToDateTime(TimeOnly.FromDateTime(ClinicTime.Now)),
        Status = (byte)(operation == "complete" ? AppointmentStatus.InProgress
            : operation == "checkin" ? AppointmentStatus.Confirmed : AppointmentStatus.Pending)
    };
    var medicalRecord = new MedicalRecord { AppointmentId = appointment.Id, Diagnosis = "Test diagnosis" };
    var originalStatus = appointment.Status;
    var role = actor.Contains("doctor") || actor == "inactive-user" ? "Doctor"
        : actor.Contains("patient") ? "Patient" : actor;
    var user = actor == "missing-user" ? null : new User
    {
        Id = 100, IsActive = actor != "inactive-user",
        UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = role } }]
    };
    var doctor = role == "Doctor" ? new Doctor
    {
        Id = actor == "other-doctor" ? 11 : 10, IsActive = actor != "inactive-doctor"
    } : null;
    var patient = role == "Patient" ? new Patient { Id = actor == "patient" ? 20 : 21 } : null;
    var saves = 0;
    var writes = 0;
    var historyReads = 0;
    var appointments = Stub.Create<IAppointmentRepository>((name, _) => name switch
    {
        "GetByIdWithDetailsAsync" => Task.FromResult<Appointment?>(appointment),
        "GetMedicalRecordForCompletionAsync" => Task.FromResult<MedicalRecord?>(medicalRecord),
        "GetNextQueueNumberAsync" => Task.FromResult(1),
        "AddStatusHistoryAsync" => WriteHistory(),
        "GetStatusHistoryAsync" => ReadHistory(),
        _ => throw new InvalidOperationException(name)
    });
    Task WriteHistory() { writes++; return Task.CompletedTask; }
    Task<IReadOnlyList<AppointmentStatusHistory>> ReadHistory()
    {
        historyReads++;
        return Task.FromResult<IReadOnlyList<AppointmentStatusHistory>>([]);
    }
    var users = Stub.Create<IUserRepository>((_, _) => Task.FromResult(user));
    var doctors = Stub.Create<IDoctorRepository>((_, _) => Task.FromResult(doctor));
    var patients = Stub.Create<IPatientRepository>((_, _) => Task.FromResult(patient));
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "BeginWriteAsync" => Task.FromResult(Stub.Create<IWriteScope>((name, _) => name == "DisposeAsync" ? ValueTask.CompletedTask : Task.CompletedTask)),
        "get_AppointmentRepository" => appointments,
        "get_UserRepository" => users,
        "get_DoctorRepository" => doctors,
        "get_PatientRepository" => patients,
        "SaveChangesAsync" => Task.FromResult(++saves),
        _ => throw new InvalidOperationException(name)
    });
    var service = new AppointmentService(uow);
    var status = operation switch
    {
        "confirm" => (await service.ConfirmAppointmentAsync(100, 1, new ChangeAppointmentStatusRequest())).StatusCode,
        "checkin" => (await service.CheckInAppointmentAsync(100, 1, new ChangeAppointmentStatusRequest())).StatusCode,
        "complete" => (await service.CompleteAppointmentAsync(100, 1, new ChangeAppointmentStatusRequest())).StatusCode,
        "cancel" => (await service.CancelAppointmentAsync(100, 1, new CancelAppointmentRequest { Reason = "Thay doi ke hoach" })).StatusCode,
        _ => (await service.GetStatusHistoryAsync(100, 1)).StatusCode
    };
    var allowed = operation switch
    {
        "confirm" => actor is "doctor" or "Receptionist" or "Admin",
        "checkin" => actor is "Receptionist" or "Admin",
        "complete" => actor == "doctor",
        "cancel" => actor is "patient" or "doctor" or "Receptionist" or "Admin",
        _ => actor is "patient" or "doctor" or "Receptionist" or "Admin"
    };
    Check(status == (allowed ? 200 : 403), $"{operation}/{actor}: status {status}");
    Check(saves == (allowed && operation != "history" ? 1 : 0), "Unexpected save");
    Check(writes == saves, "Unexpected history write");
    if (operation == "checkin" && allowed)
        Check(appointment.CheckedInAt.HasValue && appointment.QueueNumber == 1, "Check-in did not persist attendance and queue number");
    Check(historyReads == (allowed && operation == "history" ? 1 : 0), "Unauthorized history read");
    if (!allowed) Check(appointment.Status == originalStatus && appointment.UpdatedAt == null, "Unauthorized mutation");
    cases++;
}

var missingReason = new AppointmentService(Stub.Create<IUnitOfWork>((name, _) =>
    throw new InvalidOperationException($"Repository should not be reached: {name}")));
Check((await missingReason.CancelAppointmentAsync(100, 1, new CancelAppointmentRequest())).StatusCode == 400,
    "Cancellation without reason was accepted");

var inProgressAppointment = new Appointment
{
    Id = 1, DoctorId = 10, PatientId = 20, AppointmentCode = "AP1",
    Doctor = new Doctor { Id = 10, FullName = "Doctor" },
    Patient = new Patient { Id = 20, FullName = "Patient" },
    StartTime = ClinicTime.Now.AddDays(2),
    Status = (byte)AppointmentStatus.InProgress
};
var inProgressRepository = Stub.Create<IAppointmentRepository>((name, _) => name switch
{
    "GetByIdWithDetailsAsync" => Task.FromResult<Appointment?>(inProgressAppointment),
    "AddStatusHistoryAsync" => Task.CompletedTask,
    _ => throw new InvalidOperationException(name)
});
var inProgressUow = Stub.Create<IUnitOfWork>((name, _) => name switch
{
    "BeginWriteAsync" => Task.FromResult(Stub.Create<IWriteScope>((scopeName, _) => scopeName == "DisposeAsync" ? ValueTask.CompletedTask : Task.CompletedTask)),
    "get_AppointmentRepository" => inProgressRepository,
    "get_PatientRepository" => Stub.Create<IPatientRepository>((_, _) => Task.FromResult<Patient?>(new Patient { Id = 20 })),
    "get_UserRepository" => Stub.Create<IUserRepository>((_, _) => Task.FromResult(new User
    {
        Id = 100, IsActive = true,
        UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Patient" } }]
    })),
    _ => throw new InvalidOperationException(name)
});
Check((await new AppointmentService(inProgressUow).CancelAppointmentAsync(
    100, 1, new CancelAppointmentRequest { Reason = "Khong the den" })).StatusCode == 409,
    "In-progress appointment was cancelled");

var completionCases = new[]
{
    (Name: "confirmed appointment", Status: AppointmentStatus.Confirmed, Record: (MedicalRecord?)new MedicalRecord { Diagnosis = "Test diagnosis" }, Expected: 409),
    (Name: "missing medical record", Status: AppointmentStatus.InProgress, Record: (MedicalRecord?)null, Expected: 409),
    (Name: "missing diagnosis", Status: AppointmentStatus.InProgress, Record: (MedicalRecord?)new MedicalRecord(), Expected: 409),
    (Name: "pending clinical order", Status: AppointmentStatus.InProgress, Record: (MedicalRecord?)new MedicalRecord
    {
        Diagnosis = "Test diagnosis",
        MedicalRecordServices = { new MedicalRecordService { Status = 0 } }
    }, Expected: 409),
    (Name: "valid clinical record", Status: AppointmentStatus.InProgress, Record: (MedicalRecord?)new MedicalRecord
    {
        Diagnosis = "Test diagnosis",
        MedicalRecordServices = { new MedicalRecordService { Status = 1 } }
    }, Expected: 200)
};

foreach (var scenario in completionCases)
{
    var appointment = new Appointment
    {
        Id = 1,
        DoctorId = 10,
        PatientId = 20,
        AppointmentCode = "AP-FINALIZE",
        Patient = new Patient { Id = 20, FullName = "Patient" },
        Doctor = new Doctor { Id = 10, FullName = "Doctor" },
        Status = (byte)scenario.Status
    };
    var user = new User
    {
        Id = 100,
        IsActive = true,
        UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Doctor" } }]
    };
    var doctor = new Doctor { Id = 10, IsActive = true };
    var saves = 0;
    var historyWrites = 0;
    var appointments = Stub.Create<IAppointmentRepository>((name, _) => name switch
    {
        "GetByIdWithDetailsAsync" => Task.FromResult<Appointment?>(appointment),
        "GetMedicalRecordForCompletionAsync" => Task.FromResult(scenario.Record),
        "AddStatusHistoryAsync" => WriteHistory(),
        _ => throw new InvalidOperationException(name)
    });
    Task WriteHistory() { historyWrites++; return Task.CompletedTask; }
    var users = Stub.Create<IUserRepository>((_, _) => Task.FromResult<User?>(user));
    var doctors = Stub.Create<IDoctorRepository>((_, _) => Task.FromResult<Doctor?>(doctor));
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "BeginWriteAsync" => Task.FromResult(Stub.Create<IWriteScope>((method, _) => method == "DisposeAsync" ? ValueTask.CompletedTask : Task.CompletedTask)),
        "get_AppointmentRepository" => appointments,
        "get_UserRepository" => users,
        "get_DoctorRepository" => doctors,
        "SaveChangesAsync" => Task.FromResult(++saves),
        _ => throw new InvalidOperationException(name)
    });
    var response = await new AppointmentService(uow)
        .CompleteAppointmentAsync(100, 1, new ChangeAppointmentStatusRequest());
    Check(response.StatusCode == scenario.Expected, $"Completion/{scenario.Name}: status {response.StatusCode}");
    Check(saves == (scenario.Expected == 200 ? 1 : 0), $"Completion/{scenario.Name}: unexpected save");
    Check(historyWrites == saves, $"Completion/{scenario.Name}: unexpected history write");
    if (scenario.Expected == 200)
        Check(scenario.Record!.IsFinalized && scenario.Record.FinalizedAt.HasValue, "Completed record was not finalized");
    cases++;
}

var controllerRoleGuards = 0;
foreach (var (method, roles) in new[]
{
    ("Confirm", "Doctor,Receptionist,Admin"),
    ("Complete", "Doctor"),
    ("CheckIn", "Receptionist,Admin"),
    ("GetStaffAppointments", "Doctor,Receptionist,Admin"),
    ("GetWaitingQueue", "Doctor,Receptionist,Admin"),
    ("GetDoctorQueue", "Doctor"),
    ("CallNext", "Doctor,Receptionist,Admin"),
    ("MoveToEnd", "Doctor,Receptionist,Admin"),
    ("NoShow", "Receptionist,Admin"),
    ("Cancel", "Patient,Doctor,Receptionist,Admin"),
    ("Reschedule", "Patient")
})
{
    var attribute = typeof(AppointmentController).GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>();
    Check(attribute?.Roles == roles, $"Missing role guard on {method}");
    controllerRoleGuards++;
}
Check(typeof(InvoiceController).GetMethod("Eligible")?.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Receptionist,Admin",
    "Invoice eligible route is missing cashier role guard");
Check(typeof(InvoiceController).GetMethod("Cancel")?.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Receptionist,Admin",
    "Invoice cancel route is missing cashier role guard");
Check(typeof(ClinicalController).GetMethod("CancelOrder")?.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Doctor",
    "Clinical order cancel route is missing doctor role guard");
foreach (var method in new[] { "Inventory", "Receive", "ReceiveReceipt", "Adjust", "Transactions", "Dispense" })
    Check(typeof(PharmacyController).GetMethod(method)?.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Pharmacist,Admin",
        $"Pharmacy route is missing pharmacist role guard on {method}");
Check(typeof(DoctorAdminController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin" &&
      typeof(SpecialtyAdminController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin" &&
      typeof(AdminTimeOffController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin" &&
      typeof(DoctorTimeOffController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Doctor",
    "Admin/doctor schedule role guards are missing");
Console.WriteLine("PASS: VC-10 queue, VC-11 cashier, VC-15 clinical and VC-16/17 pharmacy role guards.");
var queueAppointment = new Appointment
{
    Id = 501, AppointmentCode = "AP-QUEUE", PatientId = 20, DoctorId = 10,
    Patient = new Patient { Id = 20, FullName = "Queue Patient" },
    Doctor = new Doctor { Id = 10, FullName = "Queue Doctor", Specialty = new Specialty { Name = "General" } },
    Status = (byte)AppointmentStatus.Confirmed, QueueNumber = 1,
    CheckedInAt = DateTime.UtcNow, StartTime = ClinicTime.Now, EndTime = ClinicTime.Now.AddMinutes(30)
};
var queueUser = new User
{
    Id = 100, IsActive = true,
    UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Receptionist" } }]
};
var queueWrites = 0;
var queueSaves = 0;
var queueRepo = Stub.Create<IAppointmentRepository>((name, _) => name switch
{
    "GetStaffAppointmentsAsync" => Task.FromResult<IReadOnlyList<Appointment>>([queueAppointment]),
    "GetByIdWithDetailsAsync" => Task.FromResult<Appointment?>(queueAppointment),
    "GetNextQueueNumberAsync" => Task.FromResult(2),
    "AddStatusHistoryAsync" => QueueWrite(),
    _ => throw new InvalidOperationException(name)
});
Task QueueWrite() { queueWrites++; return Task.CompletedTask; }
var queueUow = Stub.Create<IUnitOfWork>((name, _) => name switch
{
    "BeginWriteAsync" => Task.FromResult(Stub.Create<IWriteScope>((scope, _) => scope == "DisposeAsync" ? ValueTask.CompletedTask : Task.CompletedTask)),
    "get_AppointmentRepository" => queueRepo,
    "get_UserRepository" => Stub.Create<IUserRepository>((_, _) => Task.FromResult<User?>(queueUser)),
    "SaveChangesAsync" => Task.FromResult(++queueSaves),
    _ => throw new InvalidOperationException(name)
});
var queueService = new AppointmentService(queueUow);
var called = await queueService.CallNextAsync(100, DateOnly.FromDateTime(ClinicTime.Now), 10);
Check(called.StatusCode == 200 && queueWrites == 1 && queueSaves == 1, "Call-next did not persist queue history");
var moved = await queueService.MoveToEndAsync(100, queueAppointment.Id);
Check(moved.StatusCode == 200 && queueAppointment.QueueNumber == 2 && queueWrites == 2 && queueSaves == 2,
    "Move-to-end did not reassign queue number");
Console.WriteLine("PASS: VC-10 call-next and move-to-end service behavior.");
var publicCatalogSpecialties = typeof(PublicCatalogController).GetMethod("GetSpecialties");
Check(publicCatalogSpecialties?.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()?.Template == "specialties",
    "Public specialty catalog route is missing");
Check(typeof(AppointmentController).GetMethod("GetAvailableSlots")?.GetParameters()
    .Any(parameter => parameter.Name == "serviceId") == true,
    "Available-slots endpoint does not accept serviceId");
var patientStaffType = typeof(PatientStaffController);
foreach (var (method, roles) in new[]
{
    ("Search", "Receptionist,Admin"), ("Get", "Receptionist,Admin"),
    ("Create", "Receptionist,Admin"), ("Update", "Receptionist,Admin"), ("History", "Receptionist,Admin")
})
{
    var methodInfo = patientStaffType.GetMethod(method)!;
    var classGuard = patientStaffType.GetCustomAttribute<AuthorizeAttribute>();
    Check(classGuard?.Roles == roles, $"Missing staff patient role guard on {method}");
}
var staffBook = typeof(AppointmentController).GetMethod("StaffBook");
Check(staffBook?.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Receptionist,Admin" &&
      staffBook.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()?.Template == "staff-book",
    "Staff booking route or role guard is missing");
var noShowAppointment = new Appointment
{
    Id = 99, AppointmentCode = "AP99", PatientId = 20, DoctorId = 10,
    Patient = new Patient { Id = 20, FullName = "Patient" },
    Doctor = new Doctor { Id = 10, FullName = "Doctor" },
    StartTime = ClinicTime.Now.AddMinutes(-5),
    Status = (byte)AppointmentStatus.Confirmed
};
var noShowUser = new User
{
    Id = 100, IsActive = true,
    UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Receptionist" } }]
};
var noShowRepo = Stub.Create<IAppointmentRepository>((name, _) => name switch
{
    "GetByIdWithDetailsAsync" => Task.FromResult<Appointment?>(noShowAppointment),
    "AddStatusHistoryAsync" => Task.CompletedTask,
    _ => throw new InvalidOperationException(name)
});
var noShowUsers = Stub.Create<IUserRepository>((_, _) => Task.FromResult<User?>(noShowUser));
var noShowUow = Stub.Create<IUnitOfWork>((name, _) => name switch
{
    "BeginWriteAsync" => Task.FromResult(Stub.Create<IWriteScope>((scopeName, _) => scopeName == "DisposeAsync" ? ValueTask.CompletedTask : Task.CompletedTask)),
    "get_AppointmentRepository" => noShowRepo,
    "get_UserRepository" => noShowUsers,
    "SaveChangesAsync" => Task.FromResult(1),
    _ => throw new InvalidOperationException(name)
});
var noShow = await new AppointmentService(noShowUow).MarkNoShowAsync(100, 99, new ChangeAppointmentStatusRequest());
Check(noShow.StatusCode == 200 && noShowAppointment.Status == (byte)AppointmentStatus.NoShow, "No-show transition failed");
Console.WriteLine("PASS: VC-03 public specialty and service-aware slot contracts.");

foreach (var actor in new[] { "Doctor", "Receptionist", "Admin", "Patient", "InactiveReceptionist" })
{
    var role = actor == "InactiveReceptionist" ? "Receptionist" : actor;
    var user = new User
    {
        Id = 100,
        IsActive = actor != "InactiveReceptionist",
        UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = role } }]
    };
    var doctor = role == "Doctor" ? new Doctor { Id = 10, IsActive = true } : null;
    var staffReads = 0;
    var doctorReads = 0;
    var appointments = Stub.Create<IAppointmentRepository>((name, _) => name switch
    {
        "GetStaffAppointmentsAsync" => ReadStaffAppointments(),
        "GetDoctorAppointmentsAsync" => ReadDoctorAppointments(),
        _ => throw new InvalidOperationException(name)
    });
    Task<IReadOnlyList<Appointment>> ReadStaffAppointments()
    {
        staffReads++;
        return Task.FromResult<IReadOnlyList<Appointment>>([]);
    }
    Task<IReadOnlyList<Appointment>> ReadDoctorAppointments()
    {
        doctorReads++;
        return Task.FromResult<IReadOnlyList<Appointment>>([]);
    }
    var users = Stub.Create<IUserRepository>((_, _) => Task.FromResult<User?>(user));
    var doctors = Stub.Create<IDoctorRepository>((_, _) => Task.FromResult<Doctor?>(doctor));
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_AppointmentRepository" => appointments,
        "get_UserRepository" => users,
        "get_DoctorRepository" => doctors,
        _ => throw new InvalidOperationException(name)
    });
    var response = await new AppointmentService(uow).GetStaffAppointmentsAsync(100);
    var expectedStatus = (role is "Doctor" or "Receptionist" or "Admin") && actor != "InactiveReceptionist" ? 200 : 403;
    Check(response.StatusCode == expectedStatus, $"staff-list/{actor}: status {response.StatusCode}");
    Check(staffReads == (expectedStatus == 200 && role != "Doctor" ? 1 : 0), "Unexpected clinic-wide appointment read");
    Check(doctorReads == (expectedStatus == 200 && role == "Doctor" ? 1 : 0), "Unexpected doctor appointment read");
    cases++;
}
Console.WriteLine($"PASS: {cases} service authorization cases and {controllerRoleGuards} controller role guards.");

foreach (var scenario in new[] { "success", "inactive", "wrong-password", "missing" })
{
    var user = scenario == "missing" ? null : new User
    {
        Id = 41,
        Username = "khoa@example.com",
        Email = "khoa@example.com",
        FullName = "Lê Văn Khoa",
        IsActive = scenario != "inactive",
        PasswordHash = "hashed",
        UserRoles =
        [
            new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Patient" } },
            new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Receptionist" } }
        ],
        Patient = new Patient { Id = 70 },
        Doctor = new Doctor { Id = 80 }
    };
    var saves = 0;
    var users = Stub.Create<IUserRepository>((name, args) => name switch
    {
        "GetByLoginAsync" => Task.FromResult(user),
        _ => throw new InvalidOperationException(name)
    });
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => users,
        "SaveChangesAsync" => Task.FromResult(++saves),
        _ => throw new InvalidOperationException(name)
    });
    var hasher = Stub.Create<IPasswordHasher>((name, args) =>
        name == "Verify" && (string)args![0]! == "correct-password" && scenario != "wrong-password");
    var tokens = 0;
    var tokenService = Stub.Create<ITokenService>((_, _) => { tokens++; return "access-token"; });
    var service = new AuthService(uow, hasher, tokenService,
        Options.Create(new JwtOptions { Key = "test-key-with-at-least-32-bytes", Issuer = "test", Audience = "test" }));
    var request = System.Text.Json.JsonSerializer.Deserialize<LoginRequest>(scenario == "success"
        ? "{\"username\":\"Khoa@Example.com\",\"password\":\"correct-password\"}"
        : "{\"email\":\"Khoa@Example.com\",\"password\":\"correct-password\"}",
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var response = await service.LoginAsync(request, default);
    var expected = scenario == "success" ? 200 : scenario == "inactive" ? 403 : 401;
    Check(response.StatusCode == expected, $"Login/{scenario}: status {response.StatusCode}");
    Check(saves == (scenario == "success" ? 1 : 0), "Unexpected login persistence");
    Check(tokens == (scenario == "success" ? 1 : 0), "Unexpected access token issue");
    if (scenario == "success")
    {
        var session = response.DataResponse!;
        Check(session.User?.Roles.SequenceEqual(["Patient", "Receptionist"]) == true, "Login response omitted assigned roles");
        Check(session.User?.DoctorId == 80 && session.User.PatientId == 70, "Login response omitted linked profile IDs");
        Check(session.RefreshToken is { Length: > 40 } && session.ExpiresIn == 3600, "Session token response is incomplete");
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            session, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Check(json.RootElement.TryGetProperty("accessToken", out _) && json.RootElement.TryGetProperty("refreshToken", out _) &&
            json.RootElement.TryGetProperty("expiresIn", out _) && json.RootElement.TryGetProperty("user", out var loginUser) &&
            loginUser.TryGetProperty("roles", out _) && !json.RootElement.TryGetProperty("userId", out _),
            "LoginResponse JSON does not follow the published nested DTO contract");
        Check(user!.RefreshTokens.Single().TokenHash != session.RefreshToken && user.RefreshTokens.Single().TokenHash.Length == 64,
            "Raw refresh token was persisted instead of its SHA-256 hash");
    }
}
Console.WriteLine("PASS: 4 login contract scenarios (success, inactive, invalid password, missing account) and legacy email-body compatibility.");

var forgotMethod = typeof(AuthController).GetMethod("ForgotPassword");
Check(forgotMethod?.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()?.Template == "forgot-password",
    "Forgot-password route does not follow the published API contract");
Check(typeof(ForgotPasswordRequest).GetProperty(nameof(ForgotPasswordRequest.Email)) is not null,
    "ForgotPasswordRequest is missing the published email field");
foreach (var email in new[] { "known@example.com", "unknown@example.com" })
{
    var lookedUp = "";
    var forgotUsers = Stub.Create<IUserRepository>((name, args) =>
    {
        Check(name == "GetByEmailAsync", "Forgot-password used an unexpected repository operation");
        lookedUp = (string)args![0]!;
        return Task.FromResult<User?>(email.StartsWith("known") ? new User { Email = email } : null);
    });
    var forgotUow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => forgotUsers,
        _ => throw new InvalidOperationException(name)
    });
    var forgotAuth = new AuthService(
        forgotUow,
        Stub.Create<IPasswordHasher>((_, _) => false),
        Stub.Create<ITokenService>((_, _) => "unused"),
        Options.Create(new JwtOptions { Key = "test-key-with-at-least-32-bytes", Issuer = "test", Audience = "test" }));
    var forgotResponse = await forgotAuth.ForgotPasswordAsync(
        new ForgotPasswordRequest { Email = $" {email.ToUpperInvariant()} " }, default);
    Check(forgotResponse.StatusCode == 200 && forgotResponse.DataResponse is null,
        $"Forgot-password/{email}: response contract is invalid");
    Check(lookedUp == email, $"Forgot-password/{email}: email was not normalized");
}
Console.WriteLine("PASS: forgot-password route, DTO and non-enumerating response contract.");

var lookupMethod = typeof(PatientLookupController).GetMethod("Lookup");
Check(lookupMethod?.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()?.Template == "lookup",
    "Patient lookup route does not follow VC-02");
Check(FoMed.Application.Services.Patient.PatientService.NormalizePhone("+84 901-234-567") == "0901234567",
    "Patient phone normalization does not support the Vietnamese +84 format");
Console.WriteLine("PASS: VC-02 patient lookup route and phone normalization contract.");

var multiRoleUser = new User
{
    Id = 41,
    Username = "khoa@example.com",
    FullName = "Lê Văn Khoa",
    UserRoles =
    [
        new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Patient" } },
        new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Doctor" } }
    ]
};
var jwt = new JwtTokenService(Options.Create(new JwtOptions
{
    Key = "test-signing-key-with-at-least-32-bytes",
    Issuer = "fomed-test",
    Audience = "fomed-test"
}));
var decodedJwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt.CreateToken(multiRoleUser));
Check(decodedJwt.Claims.Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role)
    .Select(claim => claim.Value).Order().SequenceEqual(["Doctor", "Patient"]), "JWT omitted multi-role claims");
Console.WriteLine("PASS: JWT contains every assigned role.");

foreach (var scenario in new[] { "valid", "expired", "revoked", "inactive" })
{
    var user = new User
    {
        Id = 50,
        Username = "refresh@example.com",
        FullName = "Refresh User",
        IsActive = scenario != "inactive",
        UserRoles = [new FoMed.Infrastructure.Models.UserRole { Role = new Role { Name = "Patient" } }]
    };
    var existing = new RefreshToken
    {
        UserId = user.Id,
        User = user,
        TokenHash = "stored-hash",
        ExpiresAt = scenario == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(1),
        RevokedAt = scenario == "revoked" ? DateTime.UtcNow.AddMinutes(-1) : null
    };
    user.RefreshTokens.Add(existing);
    var users = Stub.Create<IUserRepository>((name, args) =>
    {
        Check(name == "GetRefreshTokenByHashAsync" && ((string)args![0]!).Length == 64, "Refresh token lookup did not use SHA-256 hash");
        return Task.FromResult<RefreshToken?>(existing);
    });
    var saves = 0;
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => users,
        "SaveChangesAsync" => Task.FromResult(++saves),
        _ => throw new InvalidOperationException(name)
    });
    var tokenService = Stub.Create<ITokenService>((_, _) => "rotated-access");
    var service = new AuthService(uow, Stub.Create<IPasswordHasher>((_, _) => false), tokenService,
        Options.Create(new JwtOptions { Key = "test-key-with-at-least-32-bytes", Issuer = "test", Audience = "test" }));
    var response = await service.RefreshAsync(new RefreshTokenRequest { RefreshToken = "client-refresh-token" }, default);
    Check(response.StatusCode == (scenario == "valid" ? 200 : 401), $"Refresh/{scenario}: status {response.StatusCode}");
    Check(saves == (scenario == "valid" ? 1 : 0), "Unexpected refresh persistence");
    if (scenario == "valid")
    {
        Check(existing.RevokedAt.HasValue, "Refresh token was not revoked after rotation");
        Check(response.DataResponse?.RefreshToken is { Length: > 40 } && user.RefreshTokens.Count == 2,
            "Refresh did not issue and persist a replacement token");
    }
}
Console.WriteLine("PASS: 4 refresh-token scenarios (rotation, expiry, replay, inactive account).");

foreach (var scenario in new[] { "success", "duplicate", "link-existing", "invalid" })
{
    User? added = null;
    var saves = 0;
    var codes = 0;
    var patientRole = new Role { Id = 5, Name = "Patient" };
    var linkedPatient = scenario == "link-existing" ? new Patient
    {
        Id = 9, PatientCode = "BN000009", FullName = "Patient Name",
        Phone = "0901234567", DateOfBirth = new DateOnly(1992, 5, 12), IsActive = true
    } : null;
    var users = Stub.Create<IUserRepository>((name, args) => name switch
    {
        "GetByLoginAsync" => Task.FromResult<User?>(scenario == "duplicate" ? new User() : null),
        "GetByEmailAsync" => Task.FromResult<User?>(null),
        "GetRoleByNameAsync" => Task.FromResult<Role?>(patientRole),
        "AddAsync" => Add((User)args![0]!),
        _ => throw new InvalidOperationException(name)
    });
    Task Add(User user) { added = user; return Task.CompletedTask; }
    var patients = Stub.Create<IPatientRepository>((name, args) => name switch
    {
        "FindByPhoneAsync" => Task.FromResult<IReadOnlyList<Patient>>(linkedPatient is null ? [] : [linkedPatient]),
        "GeneratePatientCodeAsync" => GenerateCode(),
        "Update" => null,
        _ => throw new InvalidOperationException(name)
    });
    Task<string> GenerateCode() { codes++; return Task.FromResult("BN000042"); }
    var writeScope = Stub.Create<IWriteScope>((name, _) => name switch
    {
        "CommitAsync" => Task.CompletedTask,
        "DisposeAsync" => ValueTask.CompletedTask,
        _ => throw new InvalidOperationException(name)
    });
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => users,
        "get_PatientRepository" => patients,
        "BeginWriteAsync" => Task.FromResult(writeScope),
        "SaveChangesAsync" => Save(),
        _ => throw new InvalidOperationException(name)
    });
    Task<int> Save()
    {
        saves++;
        if (added is not null) added.Id = 42;
        return Task.FromResult(3);
    }
    var auth = new AuthService(uow, Stub.Create<IPasswordHasher>((_, _) => "hashed-password"),
        Stub.Create<ITokenService>((_, _) => "token"),
        Options.Create(new JwtOptions { Key = "test-key-with-at-least-32-bytes", Issuer = "test", Audience = "test" }));
    var response = await auth.RegisterPatientAsync(new RegisterPatientRequest
    {
        FullName = scenario == "invalid" ? "" : "Patient Name",
        Phone = scenario == "invalid" ? "bad" : "0901234567",
        Email = " Patient@Example.com ",
        DateOfBirth = new DateTime(1992, 5, 12),
        Password = "password123"
    }, default);
    Check(response.StatusCode == (scenario == "success" || scenario == "link-existing" ? 201 : scenario == "duplicate" ? 409 : 400),
        $"Register/{scenario}: status {response.StatusCode}");
    if (scenario == "success")
    {
        Check(added?.Patient is { IsActive: true, PatientCode: "BN000042", FullName: "Patient Name" }, "Missing new patient profile");
        Check(codes == 1 && saves == 2, "New patient registration did not persist expected graph");
    }
    if (scenario == "link-existing")
        Check(added?.Patient == linkedPatient && linkedPatient?.User == added && codes == 0, "Existing walk-in patient was not linked");
}
Console.WriteLine("PASS: VC-02 registration scenarios (new patient, duplicate account, link walk-in history, validation).");

foreach (var scenario in new[] { "success", "wrong-old", "mismatch", "short", "missing", "inactive" })
{
    var hasher = new BcryptPasswordHasher();
    var originalHash = hasher.Hash("OldPassword123!");
    var user = scenario == "missing" ? null : new User { Id = 42, IsActive = scenario != "inactive", PasswordHash = originalHash };
    var saves = 0;
    var users = Stub.Create<IUserRepository>((name, args) =>
    {
        Check(name == "GetByIdAsync" && (int)args![0]! == 42, "Incorrect password change target");
        return Task.FromResult(user);
    });
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => users,
        "SaveChangesAsync" => Task.FromResult(++saves),
        _ => throw new InvalidOperationException(name)
    });
    var service = new FoMed.Application.Services.Profile.ProfileService(uow, hasher);
    var next = scenario == "short" ? "short" : "NewPassword123!";
    var response = await service.ChangePasswordAsync(42, new FoMed.Application.DTO.Profile.ChangePasswordRequest
    {
        OldPassword = scenario == "wrong-old" ? "incorrect" : "OldPassword123!",
        NewPassword = next,
        ConfirmPassword = scenario == "mismatch" ? "different" : next
    }, default);
    Check(response.StatusCode == (scenario == "success" ? 200 : scenario is "missing" or "inactive" ? 404 : 400), "Incorrect password change result");
    Check(saves == (scenario == "success" ? 1 : 0), "Unexpected password save");
    if (scenario == "success")
        Check(hasher.Verify(next, user!.PasswordHash) && !hasher.Verify("OldPassword123!", user.PasswordHash) && user.UpdatedAt != null, "Password not replaced correctly");
    else if (user != null)
        Check(user.PasswordHash == originalHash && user.UpdatedAt == null, "Failed request mutated password");
    Check(response.DataResponse == null, "Password endpoint returned sensitive data");
}
Console.WriteLine("PASS: 6 password change scenarios with real BCrypt hashing.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public class Stub : DispatchProxy
{
    public Func<string, object?[]?, object?> Handler { get; set; } = null!;
    public static T Create<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Stub>();
        ((Stub)(object)proxy).Handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args);
}
