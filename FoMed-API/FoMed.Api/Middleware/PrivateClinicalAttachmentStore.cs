using System.Text.RegularExpressions;
using FoMed.Application.Services.Clinical;

namespace FoMed.Api.Middleware;

public sealed class PrivateClinicalAttachmentStore(IWebHostEnvironment environment, IConfiguration configuration) : IClinicalAttachmentStore
{
    // Outside wwwroot; never mounted by static-file middleware. Override with a persistent private volume in production.
    private readonly string root = Path.GetFullPath(configuration["ClinicalAttachments:StoragePath"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "clinical-attachments"));
    private string Resolve(string key)
    {
        if (!Regex.IsMatch(key, "^[a-f0-9]{32}\\.(jpg|jpeg|png|pdf|dicom|dcm)$", RegexOptions.CultureInvariant))
            throw new ClinicException(404, "File không nằm trong kho lưu trữ riêng.");
        return Path.Combine(root, key);
    }
    public async Task<string> SaveAsync(byte[] bytes, string extension, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N") + extension;
        var path = Resolve(key);
        try { await File.WriteAllBytesAsync(path, bytes, ct); }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
        return key;
    }
    public async Task<byte[]> ReadAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) throw new ClinicException(404, "File không còn trong kho lưu trữ.");
        if (new FileInfo(path).Length is <= 0 or > ClinicalAttachmentService.MaxBytes)
            throw new ClinicException(409, "File trong kho lưu trữ không hợp lệ.");
        return await File.ReadAllBytesAsync(path, ct);
    }
    public Task DeleteAsync(string key, CancellationToken ct) { File.Delete(Resolve(key)); return Task.CompletedTask; }
}
