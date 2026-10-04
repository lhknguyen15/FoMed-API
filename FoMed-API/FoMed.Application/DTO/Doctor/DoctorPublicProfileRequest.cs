using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Doctor;

public abstract record DoctorPublicProfileRequest : IValidatableObject
{
    [MaxLength(2048)] public string? AvatarUrl { get; init; }
    [MaxLength(5000)] public string? Biography { get; init; }
    [Range(1900, 2100)] public int? PracticeStartYear { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(AvatarUrl) &&
            (!Uri.TryCreate(AvatarUrl.Trim(), UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            yield return new ValidationResult("Liên kết ảnh phải là URL http hoặc https hợp lệ.", [nameof(AvatarUrl)]);

        if (PracticeStartYear > DateTime.UtcNow.Year)
            yield return new ValidationResult("Năm bắt đầu hành nghề không được lớn hơn năm hiện tại.", [nameof(PracticeStartYear)]);
    }
}
