namespace FoMed.Application.DTO;

public sealed record HTTPResponseData<T>
{
    public required T DataResponse { get; init; }

    public required string Message { get; init; }

    public int StatusCode { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}