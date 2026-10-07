using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Pharmacy;

public sealed record MedicineCatalogQuery
{
    [MaxLength(100)] public string? Keyword { get; init; }
    public string Status { get; init; } = "all";
    [Range(1, 100000)] public int Page { get; init; } = 1;
}

public record SaveMedicineRequest
{
    [Required, MaxLength(255)] public string Name { get; init; } = "";
    [Required, MaxLength(50)] public string Unit { get; init; } = "";
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal Price { get; init; }
    [MaxLength(500)] public string? Description { get; init; }
}

public sealed record UpdateMedicineRequest : SaveMedicineRequest
{
    [Required, RegularExpression("^[A-F0-9]{64}$")] public string ExpectedVersion { get; init; } = "";
}

public sealed record MedicineStatusRequest
{
    [Required] public bool? IsActive { get; init; }
    [Required, RegularExpression("^[A-F0-9]{64}$")] public string ExpectedVersion { get; init; } = "";
}

public sealed record MedicineCatalogRow(int Id, string Name, string? Unit, decimal Price,
    string? Description, bool IsActive, long StockQuantity, long AvailableQuantity, string Version);

public sealed record MedicineCatalogPage(IReadOnlyList<MedicineCatalogRow> Items, int Page, int PageSize, int TotalCount);
