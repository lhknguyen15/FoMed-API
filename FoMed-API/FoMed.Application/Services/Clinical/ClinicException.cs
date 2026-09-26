namespace FoMed.Application.Services.Clinical;

public sealed class ClinicException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
