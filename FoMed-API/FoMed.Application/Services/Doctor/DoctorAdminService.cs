using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Authentication;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using DoctorEntity = FoMed.Infrastructure.Models.Doctor;
using UserRoleEntity = FoMed.Infrastructure.Models.UserRole;
using AppointmentEntity = FoMed.Infrastructure.Models.Appointment;

namespace FoMed.Application.Services.Doctor;

public sealed class DoctorAdminService(ClinicRepository repository, ClinicAccess access, IPasswordHasher passwordHasher)
{
    public async Task<HTTPResponseData<IReadOnlyList<DoctorResponse>>> ListDoctorsAsync(int userId, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var doctors = await repository.Query<DoctorEntity>().AsNoTracking().Include(d => d.Specialty).OrderBy(d => d.FullName).ToListAsync(ct);
        return Ok(doctors.Select(Map).ToList());
    }

    public async Task<HTTPResponseData<DoctorResponse?>> CreateDoctorAsync(int userId, CreateDoctorRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var specialty = await ActiveSpecialtyAsync(request.SpecialtyId, ct);
        if (await repository.Query<User>().AnyAsync(u => u.Username == request.Username.Trim() || (request.Email != null && u.Email == request.Email.Trim()), ct))
            throw new ClinicException(409, "Tên đăng nhập hoặc email đã tồn tại.");
        var role = await repository.Query<Role>().SingleOrDefaultAsync(r => r.Name == "Doctor", ct)
            ?? throw new ClinicException(500, "Role Doctor chưa được khởi tạo.");
        var user = new User
        {
            Username = request.Username.Trim(), Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            FullName = request.FullName.Trim(), Phone = Normalize(request.Phone), PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        user.UserRoles.Add(new UserRoleEntity { User = user, Role = role });
        user.Doctor = new DoctorEntity
        {
            User = user, Specialty = specialty, FullName = request.FullName.Trim(), Title = Normalize(request.Title),
            LicenseNumber = Normalize(request.LicenseNumber), Phone = Normalize(request.Phone), Room = Normalize(request.Room),
            AvatarUrl = Normalize(request.AvatarUrl), Biography = Normalize(request.Biography), PracticeStartYear = request.PracticeStartYear,
            ConsultationFee = request.ConsultationFee, IsActive = true, CreatedAt = DateTime.UtcNow
        };
        repository.Add(user);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return new HTTPResponseData<DoctorResponse?> { DataResponse = Map(user.Doctor), Message = "Tạo bác sĩ thành công.", StatusCode = 201 };
    }

    public async Task<HTTPResponseData<DoctorResponse?>> UpdateDoctorAsync(int userId, int doctorId, UpdateDoctorAdminRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var doctor = await repository.Query<DoctorEntity>().Include(d => d.Specialty).Include(d => d.User).SingleOrDefaultAsync(d => d.Id == doctorId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bác sĩ.");
        var isActive = request.IsActive ?? doctor.IsActive;
        if (!isActive && doctor.IsActive && await HasFutureAppointmentsAsync(doctorId, ct))
            throw new ClinicException(409, "Không thể ngừng bác sĩ đang có lịch hẹn tương lai.");
        var specialty = await ActiveSpecialtyAsync(request.SpecialtyId, ct);
        doctor.FullName = request.FullName.Trim(); doctor.SpecialtyId = specialty.Id; doctor.Specialty = specialty;
        doctor.Title = Normalize(request.Title); doctor.LicenseNumber = Normalize(request.LicenseNumber);
        doctor.Phone = Normalize(request.Phone); doctor.Room = Normalize(request.Room);
        doctor.AvatarUrl = Normalize(request.AvatarUrl); doctor.Biography = Normalize(request.Biography);
        doctor.PracticeStartYear = request.PracticeStartYear;
        doctor.ConsultationFee = request.ConsultationFee; doctor.IsActive = isActive;
        doctor.User.FullName = doctor.FullName; doctor.User.Phone = doctor.Phone; doctor.User.IsActive = isActive;
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return new HTTPResponseData<DoctorResponse?> { DataResponse = Map(doctor), Message = "Cập nhật bác sĩ thành công.", StatusCode = 200 };
    }

    public async Task<HTTPResponseData<IReadOnlyList<PublicSpecialtyResponse>>> ListSpecialtiesAsync(int userId, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var list = await repository.Query<Specialty>().AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return Ok(list.Select(s => new PublicSpecialtyResponse(s.Id, s.Name, s.Description)).ToList());
    }

    public async Task<HTTPResponseData<PublicSpecialtyResponse?>> CreateSpecialtyAsync(int userId, CreateSpecialtyRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var name = request.Name.Trim();
        if (await repository.Query<Specialty>().AnyAsync(s => s.Name == name, ct)) throw new ClinicException(409, "Tên chuyên khoa đã tồn tại.");
        var specialty = new Specialty { Name = name, Description = Normalize(request.Description), IsActive = true };
        repository.Add(specialty); await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return new HTTPResponseData<PublicSpecialtyResponse?> { DataResponse = new(specialty.Id, specialty.Name, specialty.Description), Message = "Tạo chuyên khoa thành công.", StatusCode = 201 };
    }

    public async Task<HTTPResponseData<PublicSpecialtyResponse?>> UpdateSpecialtyAsync(int userId, int specialtyId, UpdateSpecialtyRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var specialty = await repository.Query<Specialty>().SingleOrDefaultAsync(s => s.Id == specialtyId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy chuyên khoa.");
        if (await repository.Query<Specialty>().AnyAsync(s => s.Id != specialtyId && s.Name == request.Name.Trim(), ct)) throw new ClinicException(409, "Tên chuyên khoa đã tồn tại.");
        if (!request.IsActive && specialty.IsActive && await repository.Query<DoctorEntity>().AnyAsync(d => d.SpecialtyId == specialtyId && d.IsActive, ct))
            throw new ClinicException(409, "Không thể ngừng chuyên khoa đang có bác sĩ hoạt động.");
        specialty.Name = request.Name.Trim(); specialty.Description = Normalize(request.Description); specialty.IsActive = request.IsActive;
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return new HTTPResponseData<PublicSpecialtyResponse?> { DataResponse = new(specialty.Id, specialty.Name, specialty.Description), Message = "Cập nhật chuyên khoa thành công.", StatusCode = 200 };
    }

    private async Task<Specialty> ActiveSpecialtyAsync(int id, CancellationToken ct) => await repository.Query<Specialty>().SingleOrDefaultAsync(s => s.Id == id && s.IsActive, ct)
        ?? throw new ClinicException(404, "Chuyên khoa không tồn tại hoặc đã ngừng hoạt động.");
    private Task<bool> HasFutureAppointmentsAsync(int doctorId, CancellationToken ct) => repository.Query<AppointmentEntity>().AnyAsync(a => a.DoctorId == doctorId && a.StartTime > ClinicTime.Now && a.Status < (byte)AppointmentStatus.Completed, ct);
    private async Task RequireAdminAsync(int userId, CancellationToken ct) { if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý bác sĩ và chuyên khoa."); }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DoctorResponse Map(DoctorEntity d) => new(d.Id, d.UserId, d.SpecialtyId, d.Specialty.Name, d.FullName, d.Title, d.LicenseNumber, d.Phone, d.Room, d.ConsultationFee, d.IsActive, d.AvatarUrl, d.Biography, d.PracticeStartYear);
    private static HTTPResponseData<IReadOnlyList<T>> Ok<T>(IReadOnlyList<T> data) => new() { DataResponse = data, Message = "Thành công.", StatusCode = 200 };
}
