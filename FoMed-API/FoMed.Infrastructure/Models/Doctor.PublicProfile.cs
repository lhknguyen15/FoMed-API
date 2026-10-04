namespace FoMed.Infrastructure.Models;

// Extension cho model database-first; khớp migration 20261003_add_doctor_public_profile.sql.
public partial class Doctor
{
    public string? AvatarUrl { get; set; }
    public string? Biography { get; set; }
    public int? PracticeStartYear { get; set; }
}
