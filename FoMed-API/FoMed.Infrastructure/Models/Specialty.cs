using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Specialty
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
}
