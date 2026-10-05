using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Attachment
{
    public int Id { get; set; }

    public string OwnerType { get; set; } = null!;

    public int OwnerId { get; set; }

    public string FileUrl { get; set; } = null!;

    public DateTime UploadedAt { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }
    public int? UploadedBy { get; set; }
}
