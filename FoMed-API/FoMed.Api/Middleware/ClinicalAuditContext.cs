using FoMed.Application.Services.Clinical;

namespace FoMed.Api.Middleware;

public sealed class ClinicalAuditContext(IHttpContextAccessor accessor) : IClinicalAuditContext
{
    public string? IpAddress
    {
        get
        {
            var address = accessor.HttpContext?.Connection.RemoteIpAddress;
            return address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString();
        }
    }
    // Server-generated trace ID; never trust arbitrary actor or X-Forwarded-For headers.
    public string? RequestId => accessor.HttpContext?.TraceIdentifier;
}
