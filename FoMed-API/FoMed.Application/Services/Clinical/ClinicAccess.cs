using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Clinical;

public sealed class ClinicAccess(ClinicRepository repository)
{
    public Task<bool> HasRoleAsync(int userId, string role, CancellationToken ct) => repository.Query<User>()
        .AnyAsync(u => u.Id == userId && u.IsActive && u.UserRoles.Any(r => r.Role.Name == role), ct);
    public async Task<bool> IsDoctorAsync(int userId, int doctorId, CancellationToken ct) =>
        await HasRoleAsync(userId, "Doctor", ct) && await repository.Query<FoMed.Infrastructure.Models.Doctor>()
            .AnyAsync(d => d.Id == doctorId && d.UserId == userId && d.IsActive, ct);
    public async Task<bool> CanReadAsync(int userId, int patientId, int doctorId, CancellationToken ct) =>
        await IsDoctorAsync(userId, doctorId, ct) ||
        await repository.Query<FoMed.Infrastructure.Models.Patient>().AnyAsync(p => p.Id == patientId && p.UserId == userId && p.IsActive && p.User!.IsActive, ct);
}
