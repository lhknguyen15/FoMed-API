using FoMed.Application.DTO;
using FoMed.Application.DTO.Clinical;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class PublicCatalogController(
    DoctorService doctorService,
    ClinicalService clinicalService) : ControllerBase
{
    [HttpGet("doctors/{id:int:min(1)}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HTTPResponseData<PublicDoctorDetailResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<PublicDoctorDetailResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<PublicDoctorDetailResponse?>>> GetDoctor(
        int id, CancellationToken cancellationToken)
    {
        var response = await doctorService.GetPublicDoctorAsync(id, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("doctors")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<PublicDoctorResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HTTPResponseData<IReadOnlyList<PublicDoctorResponse>>>> GetDoctors(
        [FromQuery] int? specialtyId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var response = await doctorService.GetPublicDoctorsAsync(
            specialtyId,
            search,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("specialties")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<PublicSpecialtyResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HTTPResponseData<IReadOnlyList<PublicSpecialtyResponse>>>> GetSpecialties(
        CancellationToken cancellationToken)
    {
        var response = await doctorService.GetPublicSpecialtiesAsync(cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("services")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<CatalogResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HTTPResponseData<IReadOnlyList<CatalogResponse>>>> GetServices(
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var services = await clinicalService.CatalogAsync(false, page, cancellationToken);
        var response = new HTTPResponseData<IReadOnlyList<CatalogResponse>>
        {
            DataResponse = services,
            Message = "Lấy danh sách dịch vụ thành công.",
            StatusCode = StatusCodes.Status200OK
        };

        return Ok(response);
    }
}
