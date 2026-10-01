using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/admin/doctors"), Authorize(Roles = "Admin")]
public sealed class DoctorAdminController(DoctorAdminService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Result(await service.ListDoctorsAsync(UserId, ct));
    [HttpPost] public async Task<IActionResult> Create(CreateDoctorRequest request, CancellationToken ct) => Result(await service.CreateDoctorAsync(UserId, request, ct), 201);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdateDoctorAdminRequest request, CancellationToken ct) => Result(await service.UpdateDoctorAsync(UserId, id, request, ct));
    private ObjectResult Result<T>(HTTPResponseData<T> response, int? status = null) => StatusCode(status ?? response.StatusCode, response);
}

[ApiController, Route("api/admin/specialties"), Authorize(Roles = "Admin")]
public sealed class SpecialtyAdminController(DoctorAdminService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Result(await service.ListSpecialtiesAsync(UserId, ct));
    [HttpPost] public async Task<IActionResult> Create(CreateSpecialtyRequest request, CancellationToken ct) => Result(await service.CreateSpecialtyAsync(UserId, request, ct), 201);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdateSpecialtyRequest request, CancellationToken ct) => Result(await service.UpdateSpecialtyAsync(UserId, id, request, ct));
    private ObjectResult Result<T>(HTTPResponseData<T> response, int? status = null) => StatusCode(status ?? response.StatusCode, response);
}
