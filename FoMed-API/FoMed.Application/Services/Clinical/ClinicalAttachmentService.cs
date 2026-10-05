using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Clinical;

public interface IClinicalAttachmentStore
{
    Task<string> SaveAsync(byte[] bytes, string extension, CancellationToken ct);
    Task<byte[]> ReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
public sealed record ClinicalAttachmentResponse(int Id, int MedicalRecordId, int? OrderId, string FileName,
    string ContentType, long FileSize, DateTime UploadedAt, bool Downloadable);
public sealed record ClinicalAttachmentPage(IReadOnlyList<ClinicalAttachmentResponse> Items, int Page, int PageSize, int Total);
public sealed record ClinicalAttachmentDownload(byte[] Bytes, string FileName, string ContentType);

public sealed class ClinicalAttachmentService(ClinicRepository repository, ClinicAccess access,
    IClinicalAttachmentStore store, IClinicalAuditContext? auditContext = null)
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private async Task<MedicalRecord> AuthorizeAsync(int userId, int recordId, int? orderId, bool upload, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().Include(r => r.Appointment).SingleOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (orderId.HasValue)
        {
            var ownResult = await repository.Query<LabResult>().AnyAsync(r => r.MedicalRecordServiceId == orderId &&
                r.MedicalRecordService.MedicalRecordId == recordId && r.TechnicianId == userId && r.MedicalRecordService.Status == 1, ct);
            if (ownResult && await access.HasRoleAsync(userId, "Technician", ct)) return record;
            if (!await repository.Query<MedicalRecordService>().AnyAsync(o => o.Id == orderId && o.MedicalRecordId == recordId && o.Status == 1, ct))
                throw new ClinicException(409, "Chỉ đính kèm/xem file kết quả của chỉ định đã hoàn thành.");
        }
        if (await access.IsDoctorAsync(userId, record.DoctorId, ct))
        {
            if (upload && (record.IsFinalized || record.Appointment.Status != (byte)AppointmentStatus.InProgress))
                throw new ClinicException(409, "Bệnh án đã chốt hoặc không đang khám, không thể thêm file.");
            return record;
        }
        if (!upload && record.IsFinalized && record.Appointment.Status == (byte)AppointmentStatus.Completed &&
            await access.IsPatientOwnerAsync(userId, record.PatientId, ct)) return record;
        throw new ClinicException(403, "Không có quyền truy cập file bệnh án này.");
    }

    public async Task<ClinicalAttachmentResponse> UploadAsync(int userId, int recordId, int? orderId, string fileName, byte[] bytes, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await AuthorizeAsync(userId, recordId, orderId, true, ct);
        var (extension, contentType) = Validate(fileName, bytes);
        var ownerId = orderId.HasValue ? await repository.Query<LabResult>()
            .Where(r => r.MedicalRecordServiceId == orderId.Value).Select(r => (int?)r.Id).SingleOrDefaultAsync(ct)
                ?? throw new ClinicException(409, "Chỉ định chưa có kết quả đã lưu để đính kèm file.") : recordId;
        var key = await store.SaveAsync(bytes, extension, ct);
        try
        {
            var attachment = new Attachment { OwnerType = orderId.HasValue ? "LabResult" : "MedicalRecord", OwnerId = ownerId,
                FileUrl = key, FileName = fileName, ContentType = contentType, FileSize = bytes.LongLength, UploadedBy = userId, UploadedAt = DateTime.UtcNow };
            repository.Add(attachment);
            var actor = await ActorAsync(userId, ct);
            repository.Add(MedicalRecordAudit.Create(userId, actor, "Update", recordId, "UploadAttachment", auditContext, ["Attachments"]));
            await repository.SaveAsync(ct);
            await write.CommitAsync(ct);
            return Map(attachment, recordId, orderId);
        }
        catch { await store.DeleteAsync(key, CancellationToken.None); throw; }
    }

    public async Task<ClinicalAttachmentPage> ListAsync(int userId, int recordId, int? orderId, int page, CancellationToken ct)
    {
        await AuthorizeAsync(userId, recordId, orderId, false, ct);
        if (page is < 1 or > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var completedResults = repository.Query<LabResult>().Where(r => r.MedicalRecordService.MedicalRecordId == recordId && r.MedicalRecordService.Status == 1);
        var resultIds = completedResults.Select(r => r.Id);
        var scopedResultIds = completedResults.Where(r => r.MedicalRecordServiceId == orderId).Select(r => r.Id);
        var query = repository.Query<Attachment>().AsNoTracking().Where(a => orderId.HasValue
            ? a.OwnerType == "LabResult" && scopedResultIds.Contains(a.OwnerId)
            : (a.OwnerType == "MedicalRecord" && a.OwnerId == recordId) || (a.OwnerType == "LabResult" && resultIds.Contains(a.OwnerId)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Id).Skip((page - 1) * 10).Take(10).ToListAsync(ct);
        var pageResultIds = rows.Where(a => a.OwnerType == "LabResult").Select(a => a.OwnerId).ToArray();
        var resultOrders = await completedResults.Where(r => pageResultIds.Contains(r.Id))
            .Select(r => new { r.Id, r.MedicalRecordServiceId }).ToDictionaryAsync(r => r.Id, r => r.MedicalRecordServiceId, ct);
        if (rows.Count > 0)
        {
            repository.Add(MedicalRecordAudit.Create(userId, await ActorAsync(userId, ct), "Read", recordId, "AttachmentList", auditContext));
            await repository.SaveAsync(ct);
        }
        return new(rows.Select(a => Map(a, recordId, a.OwnerType == "LabResult" ? resultOrders.GetValueOrDefault(a.OwnerId) : null)).ToList(), page, 10, total);
    }

    public async Task<ClinicalAttachmentDownload> DownloadAsync(int userId, int attachmentId, CancellationToken ct)
    {
        var attachment = await repository.Query<Attachment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy file.");
        var orderId = attachment.OwnerType == "LabResult"
            ? await repository.Query<LabResult>().Where(r => r.Id == attachment.OwnerId).Select(r => (int?)r.MedicalRecordServiceId).SingleOrDefaultAsync(ct) : null;
        var recordId = attachment.OwnerType == "MedicalRecord" ? attachment.OwnerId : orderId.HasValue
            ? await repository.Query<MedicalRecordService>().Where(o => o.Id == orderId.Value).Select(o => o.MedicalRecordId).SingleOrDefaultAsync(ct) : 0;
        if (recordId <= 0) throw new ClinicException(404, "Không tìm thấy file bệnh án.");
        await AuthorizeAsync(userId, recordId, orderId, false, ct);
        if (attachment.FileName is null || attachment.ContentType is null) throw new ClinicException(404, "File cũ chưa được chuyển sang kho lưu trữ riêng.");
        var bytes = await store.ReadAsync(attachment.FileUrl, ct);
        repository.Add(MedicalRecordAudit.Create(userId, await ActorAsync(userId, ct), "Read", recordId, "DownloadAttachment", auditContext));
        await repository.SaveAsync(ct);
        return new(bytes, attachment.FileName, attachment.ContentType);
    }

    private Task<User> ActorAsync(int userId, CancellationToken ct) => repository.Query<User>().Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleAsync(u => u.Id == userId, ct);
    private static ClinicalAttachmentResponse Map(Attachment a, int recordId, int? orderId) => new(a.Id, recordId, orderId,
        a.FileName ?? "Tệp cũ chưa chuyển đổi", a.ContentType ?? "application/octet-stream", a.FileSize ?? 0,
        DateTime.SpecifyKind(a.UploadedAt, DateTimeKind.Utc), a.FileName is not null && a.ContentType is not null);

    private static (string Extension, string ContentType) Validate(string name, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(char.IsControl))
            throw new ClinicException(400, "Tên file không hợp lệ.");
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new ClinicException(400, "File phải có dữ liệu và không vượt quá 10 MB.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var (type, signature) = extension switch
        {
            ".jpg" or ".jpeg" => ("image/jpeg", bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })),
            ".png" => ("image/png", bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })),
            ".pdf" => ("application/pdf", bytes.AsSpan().StartsWith("%PDF-"u8)),
            ".dicom" or ".dcm" => ("application/dicom", bytes.Length > 132 && bytes.AsSpan(128, 4).SequenceEqual("DICM"u8)),
            _ => ("", false)
        };
        if (!signature) throw new ClinicException(400, "Chỉ nhận JPG, PNG, PDF hoặc DICOM Part 10 có chữ ký file phù hợp.");
        return (extension, type);
    }
}
