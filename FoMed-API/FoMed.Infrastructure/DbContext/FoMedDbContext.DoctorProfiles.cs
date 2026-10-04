using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.DbContext;

public partial class FoMedDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        var doctor = modelBuilder.Entity<Models.Doctor>();
        doctor.Property(d => d.AvatarUrl).HasMaxLength(2048).HasColumnName("avatar_url");
        doctor.Property(d => d.Biography).HasMaxLength(5000).HasColumnType("nvarchar(max)").HasColumnName("biography");
        doctor.Property(d => d.PracticeStartYear).HasColumnName("practice_start_year");
    }
}
