using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Application.Services.Patient;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/patients/staff")]
[Authorize(Roles = "Receptionist,Admin")]
public sealed class PatientStaffController(PatientStaffService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] PatientStaffSearchRequest request, CancellationToken ct) =>
        Result(await service.SearchAsync(UserId, request, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Result(await service.GetAsync(UserId, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateWalkInPatientRequest request, CancellationToken ct) =>
        Result(await service.CreateAsync(UserId, request, ct), 201);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdatePatientProfileRequest request, CancellationToken ct) =>
        Result(await service.UpdateAsync(UserId, id, request, ct));

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id, CancellationToken ct) =>
        Result(await service.HistoryAsync(UserId, id, ct));

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ClinicException(401, "Token không hợp lệ.");
    private ObjectResult Result<T>(HTTPResponseData<T> response, int? successStatus = null) =>
        StatusCode(successStatus is not null && response.StatusCode < 300 ? successStatus.Value : response.StatusCode, response);
}
