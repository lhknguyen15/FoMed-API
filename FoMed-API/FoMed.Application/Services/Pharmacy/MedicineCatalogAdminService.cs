using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Pharmacy;

public sealed class MedicineCatalogAdminService(ClinicRepository repository, ClinicAccess access)
{
    private const int PageSize = 20;

    public async Task<MedicineCatalogPage> ListAsync(int userId, MedicineCatalogQuery request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var query = BuildQuery(request);
        var count = await query.CountAsync(ct);
        var medicines = await query.OrderBy(m => m.Name).ThenBy(m => m.Id)
            .Skip((request.Page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new(await RowsAsync(medicines, ct), request.Page, PageSize, count);
    }

    public async Task<MedicineCatalogRow> GetAsync(int userId, int id, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var medicine = await repository.Query<Medicine>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new ClinicException(404, "Không tìm thấy thuốc.");
        return (await RowsAsync([medicine], ct)).Single();
    }

    public async Task<MedicineCatalogRow> CreateAsync(int userId, SaveMedicineRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var normalized = await ValidateAsync(request, null, ct);
        var medicine = new Medicine { Name = normalized.Name, Unit = normalized.Unit,
            Price = normalized.Price, Description = normalized.Description, IsActive = true };
        repository.Add(medicine);
        await repository.SaveAsync(ct);
        Audit(userId, "Create", medicine, null);
        await repository.SaveAsync(ct);
        var row = (await RowsAsync([medicine], ct)).Single();
        await write.CommitAsync(ct);
        return row;
    }

    public async Task<MedicineCatalogRow> UpdateAsync(int userId, int id, UpdateMedicineRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var medicine = await FindAsync(id, ct);
        CheckVersion(medicine, request.ExpectedVersion);
        var normalized = await ValidateAsync(request, id, ct);
        if (!string.IsNullOrWhiteSpace(medicine.Unit) && !string.Equals(medicine.Unit.Trim(), normalized.Unit, StringComparison.OrdinalIgnoreCase) &&
            (await repository.Query<MedicineBatch>().AnyAsync(b => b.MedicineId == id, ct) ||
             await repository.Query<PrescriptionItem>().AnyAsync(i => i.MedicineId == id, ct) ||
             await repository.Query<InvoiceItem>().AnyAsync(i => i.MedicineId == id, ct) ||
             await repository.Query<InventoryReceiptItem>().AnyAsync(i => i.MedicineId == id, ct)))
            throw new ClinicException(409, "Thuốc đã có lô kho hoặc chứng từ. Không thể đổi đơn vị vì sẽ làm sai ý nghĩa số lượng đã lưu. Vui lòng tạo thuốc theo quy cách mới.");
        var oldValue = Snapshot(medicine);
        medicine.Name = normalized.Name; medicine.Unit = normalized.Unit;
        medicine.Price = normalized.Price; medicine.Description = normalized.Description;
        // Catalog edits never modify stock, prescription snapshots or invoice items.
        Audit(userId, "Update", medicine, oldValue);
        await repository.SaveAsync(ct);
        var row = (await RowsAsync([medicine], ct)).Single();
        await write.CommitAsync(ct);
        return row;
    }

    public async Task<MedicineCatalogRow> SetStatusAsync(int userId, int id, MedicineStatusRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        if (!request.IsActive.HasValue) throw new ClinicException(400, "Vui lòng chọn trạng thái sử dụng thuốc.");
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireAdminAsync(userId, ct);
        var medicine = await FindAsync(id, ct);
        CheckVersion(medicine, request.ExpectedVersion);
        if (medicine.IsActive != request.IsActive.Value)
        {
            if (!request.IsActive.Value && await PendingPrescriptionItems(id).AnyAsync(ct))
                throw new ClinicException(409, "Thuốc còn trong đơn đang khám hoặc chưa cấp phát đủ. Vui lòng xử lý các đơn liên quan trước khi ngừng sử dụng.");
            var oldValue = Snapshot(medicine);
            medicine.IsActive = request.IsActive.Value;
            Audit(userId, medicine.IsActive ? "Activate" : "Deactivate", medicine, oldValue);
            await repository.SaveAsync(ct);
        }
        var row = (await RowsAsync([medicine], ct)).Single();
        await write.CommitAsync(ct);
        return row;
    }

    private IQueryable<PrescriptionItem> PendingPrescriptionItems(int id) => repository.Query<PrescriptionItem>()
        .Where(i => i.MedicineId == id && (i.Prescription.MedicalRecord.Appointment.Status == 2 ||
            (i.Prescription.MedicalRecord.Appointment.Status == 3 && !i.BatchId.HasValue &&
             (i.PrescriptionDispenses.Sum(d => (long?)d.Quantity) ?? 0) < i.Quantity)));

    private IQueryable<Medicine> BuildQuery(MedicineCatalogQuery request)
    {
        var keyword = request.Keyword?.Trim() ?? "";
        var status = request.Status?.Trim().ToLowerInvariant() ?? "all";
        if (request.Page is < 1 or > 100000 || keyword.Length > 100 || ContainsControl(keyword) ||
            status is not ("all" or "active" or "inactive"))
            throw new ClinicException(400, "Bộ lọc thuốc không hợp lệ.");
        var query = repository.Query<Medicine>().AsNoTracking();
        if (status != "all") query = query.Where(m => m.IsActive == (status == "active"));
        if (keyword.Length > 0) query = query.Where(m => m.Name.Contains(keyword) ||
            (m.Unit != null && m.Unit.Contains(keyword)) || (m.Description != null && m.Description.Contains(keyword)));
        return query;
    }

    private async Task<SaveMedicineRequest> ValidateAsync(SaveMedicineRequest request, int? id, CancellationToken ct)
    {
        var name = (request.Name ?? "").Trim().Normalize(NormalizationForm.FormC);
        var unit = (request.Unit ?? "").Trim().Normalize(NormalizationForm.FormC);
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim().Normalize(NormalizationForm.FormC);
        if (name.Length is < 1 or > 255 || ContainsControl(name)) throw new ClinicException(400, "Tên thuốc phải có từ 1 đến 255 ký tự hợp lệ.");
        if (unit.Length is < 1 or > 50 || ContainsControl(unit)) throw new ClinicException(400, "Đơn vị thuốc phải có từ 1 đến 50 ký tự hợp lệ.");
        if (description?.Length > 500 || description?.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) == true)
            throw new ClinicException(400, "Mô tả thuốc phải không quá 500 ký tự hợp lệ.");
        if (request.Price is < 0 or > 9999999999.99m || decimal.Round(request.Price, 2) != request.Price)
            throw new ClinicException(400, "Giá bán phải từ 0 đến 9.999.999.999,99 đồng và có tối đa hai chữ số thập phân.");
        if (await repository.Query<Medicine>().AnyAsync(m => (!id.HasValue || m.Id != id.Value) &&
            m.Name.Trim().ToLower() == name.ToLower() && (m.Unit ?? "").Trim().ToLower() == unit.ToLower(), ct))
            throw new ClinicException(409, "Đã có thuốc cùng tên và đơn vị. Vui lòng kiểm tra danh mục, kể cả thuốc đã ngừng sử dụng.");
        return new SaveMedicineRequest { Name = name, Unit = unit, Price = request.Price, Description = description };
    }

    private async Task<IReadOnlyList<MedicineCatalogRow>> RowsAsync(IReadOnlyList<Medicine> medicines, CancellationToken ct)
    {
        var ids = medicines.Select(m => m.Id).ToArray();
        if (ids.Length == 0) return [];
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        var stock = await repository.Query<MedicineBatch>().AsNoTracking().Where(b => ids.Contains(b.MedicineId))
            .GroupBy(b => b.MedicineId).Select(g => new { Id = g.Key,
                Total = g.Sum(b => (long)b.Quantity),
                Available = g.Sum(b => b.ExpiryDate >= today && b.Quantity > 0 ? (long)b.Quantity : 0) }).ToDictionaryAsync(g => g.Id, ct);
        return medicines.Select(m => new MedicineCatalogRow(m.Id, m.Name, m.Unit, m.Price, m.Description,
            m.IsActive, stock.GetValueOrDefault(m.Id)?.Total ?? 0,
            m.IsActive ? stock.GetValueOrDefault(m.Id)?.Available ?? 0 : 0, Version(m))).ToList();
    }

    private async Task<Medicine> FindAsync(int id, CancellationToken ct) =>
        await repository.Query<Medicine>().SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy thuốc.");
    private async Task RequireAdminAsync(int userId, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý danh mục thuốc.");
    }
    private static bool ContainsControl(string value) => value.Any(char.IsControl);
    private static string Snapshot(Medicine m) => JsonSerializer.Serialize(new { m.Id, m.Name, m.Unit, m.Price, m.Description, m.IsActive });
    private static string Version(Medicine m)
    {
        // SQL decimal(12,2) returns 75.00 where the request may contain 75.
        // Canonicalize the price so unchanged rows keep their version across a DB round trip.
        var canonical = JsonSerializer.Serialize(new { m.Id, m.Name, m.Unit,
            Price = m.Price.ToString("F2", CultureInfo.InvariantCulture), m.Description, m.IsActive });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static void CheckVersion(Medicine medicine, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new ClinicException(400, "Thiếu thông tin phiên bản thuốc. Vui lòng tải lại danh mục.");
        if (!string.Equals(Version(medicine), expected, StringComparison.Ordinal))
            throw new ClinicException(409, "Thông tin thuốc đã được người khác cập nhật. Vui lòng tải lại danh mục trước khi sửa.");
    }
    private void Audit(int userId, string action, Medicine medicine, string? oldValue) => repository.Add(new AuditLog
    {
        UserId = userId, Action = action, Entity = "Medicine", EntityId = medicine.Id,
        OldValue = oldValue, NewValue = Snapshot(medicine), CreatedAt = DateTime.UtcNow
    });
}
