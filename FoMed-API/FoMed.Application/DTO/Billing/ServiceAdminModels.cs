using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Billing;

public sealed record ServiceCatalogResponse(int ServiceId, string? Code, string Name, string? Description, decimal Price, int? SpecialtyId, string? SpecialtyName, int DurationMinutes, bool IsActive);
public sealed record CreateServiceRequest
{
    [MaxLength(50)] public string? Code { get; init; }
    [Required, MaxLength(255)] public string Name { get; init; } = string.Empty;
    [MaxLength(500)] public string? Description { get; init; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal Price { get; init; }
    public int? SpecialtyId { get; init; }
    [Range(5, 1440)] public int DurationMinutes { get; init; } = 30;
}
public sealed record UpdateServiceRequest
{
    [MaxLength(50)] public string? Code { get; init; }
    [Required, MaxLength(255)] public string Name { get; init; } = string.Empty;
    [MaxLength(500)] public string? Description { get; init; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal Price { get; init; }
    public int? SpecialtyId { get; init; }
    [Range(5, 1440)] public int DurationMinutes { get; init; } = 30;
    public bool? IsActive { get; init; }
}
