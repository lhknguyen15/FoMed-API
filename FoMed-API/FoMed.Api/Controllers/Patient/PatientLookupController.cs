using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Application.Services.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/patients")]
[AllowAnonymous]
public sealed class PatientLookupController(PatientService patientService) : ControllerBase
{
    [HttpGet("lookup")]
    [ProducesResponseType(typeof(HTTPResponseData<PatientDto?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientDto?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientDto?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<PatientDto?>>> Lookup(
        [FromQuery] string phone, CancellationToken cancellationToken)
    {
        var response = await patientService.LookupByPhoneAsync(phone, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
