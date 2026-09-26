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

// Dependency-free regression checks: dotnet run --project tests/AppointmentAuthorization
var cases = 0;
foreach (var operation in new[] { "confirm", "complete", "history" })
foreach (var actor in new[] { "patient", "other-patient", "doctor", "other-doctor", "Receptionist", "Admin", "inactive-doctor", "inactive-user", "missing-user" })
{
    var appointment = new Appointment
    {
        Id = 1, DoctorId = 10, PatientId = 20, AppointmentCode = "AP1",
        Doctor = new Doctor { Id = 10, FullName = "Doctor" },
        Patient = new Patient { Id = 20, FullName = "Patient" },
        Status = (byte)(operation == "complete" ? AppointmentStatus.Confirmed : AppointmentStatus.Pending)
    };
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
        "complete" => (await service.CompleteAppointmentAsync(100, 1, new ChangeAppointmentStatusRequest())).StatusCode,
        _ => (await service.GetStatusHistoryAsync(100, 1)).StatusCode
    };
    var allowed = operation switch
    {
        "confirm" => actor is "doctor" or "Receptionist",
        "complete" => actor == "doctor",
        // History follows the existing detail endpoint's ownership rules.
        _ => actor is "patient" or "doctor" or "inactive-doctor" or "inactive-user"
    };
    Check(status == (allowed ? 200 : 403), $"{operation}/{actor}: status {status}");
    Check(saves == (allowed && operation != "history" ? 1 : 0), "Unexpected save");
    Check(writes == saves, "Unexpected history write");
    Check(historyReads == (allowed && operation == "history" ? 1 : 0), "Unauthorized history read");
    if (!allowed) Check(appointment.Status == originalStatus && appointment.UpdatedAt == null, "Unauthorized mutation");
    cases++;
}

foreach (var (method, roles) in new[] { ("Confirm", "Doctor,Receptionist"), ("Complete", "Doctor") })
{
    var attribute = typeof(AppointmentController).GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>();
    Check(attribute?.Roles == roles, $"Missing role guard on {method}");
}
Console.WriteLine($"PASS: {cases} service authorization cases and 2 controller role guards.");

foreach (var scenario in new[] { "success", "duplicate", "missing-role", "save-failure" })
{
    User? added = null;
    var saves = 0;
    var tokens = 0;
    var codes = 0;
    var patientRole = new Role { Id = 5, Name = "Patient" };
    var users = Stub.Create<IUserRepository>((name, args) => name switch
    {
        "GetByEmailAsync" => FindUser((string)args![0]!),
        "GetRoleByNameAsync" => Task.FromResult<Role?>(scenario == "missing-role" ? null : patientRole),
        "AddAsync" => Add((User)args![0]!),
        _ => throw new InvalidOperationException(name)
    });
    Task<User?> FindUser(string email)
    {
        Check(email == "patient@example.com", "Email was not normalized");
        return Task.FromResult<User?>(scenario == "duplicate" ? new User() : null);
    }
    Task Add(User user) { added = user; return Task.CompletedTask; }
    var patients = Stub.Create<IPatientRepository>((_, _) =>
    {
        codes++;
        return Task.FromResult("BN000042");
    });
    var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_UserRepository" => users,
        "get_PatientRepository" => patients,
        "SaveChangesAsync" => Save(),
        _ => throw new InvalidOperationException(name)
    });
    Task<int> Save()
    {
        saves++;
        if (scenario == "save-failure") throw new InvalidOperationException("Simulated save failure");
        added!.Id = 42;
        return Task.FromResult(3);
    }
    var hasher = Stub.Create<IPasswordHasher>((_, _) => "hashed-password");
    var tokenService = Stub.Create<ITokenService>((_, args) =>
    {
        tokens++;
        var user = (User)args![0]!;
        Check(saves == 1 && user.Id == 42, "Token issued before persistence");
        Check(user.UserRoles.Single().Role == patientRole, "Token lacks persisted Patient role");
        return "token";
    });
    var auth = new AuthService(uow, hasher, tokenService);
    var failed = false;
    try
    {
        var response = await auth.RegisterAsync(new RegisterRequest
        {
            Email = " Patient@Example.com ", FullName = " Patient Name ", Password = "password123"
        }, default);
        Check(response.StatusCode == (scenario == "duplicate" ? 409 : 201), "Incorrect registration status");
        if (scenario == "success") Check(response.DataResponse?.Role == "Patient", "Incorrect response role");
    }
    catch (InvalidOperationException) when (scenario is "missing-role" or "save-failure")
    {
        failed = true;
    }
    Check(failed == (scenario is "missing-role" or "save-failure"), "Expected registration failure");
    var createsGraph = scenario is "success" or "save-failure";
    Check(saves == (createsGraph ? 1 : 0) && codes == (createsGraph ? 1 : 0), "Unexpected registration side effect");
    Check(tokens == (scenario == "success" ? 1 : 0), "Token issued for failed registration");
    if (createsGraph)
    {
        Check(added!.PasswordHash == "hashed-password", "Password was not hashed");
        Check(added.Patient is { IsActive: true, PatientCode: "BN000042", FullName: "Patient Name" }, "Missing patient profile");
        Check(added.Patient!.User == added && added.UserRoles.Single().User == added, "Broken user relationships");
        Check(added.UserRoles.Single().RoleId == 5, "Incorrect persisted role");
    }
    else Check(added is null, "Unexpected user creation");
}
Console.WriteLine("PASS: 4 registration scenarios (success, duplicate email, missing role, save failure).");

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
