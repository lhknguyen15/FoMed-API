using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Pharmacy;

public sealed class PharmacyService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<IReadOnlyList<InventoryBatchResponse>> ListInventoryAsync(int userId, int page, int? medicineId, DateOnly? expiringBefore, CancellationToken ct)
    {
        await RequirePharmacistAsync(userId, ct);
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        var query = repository.Query<MedicineBatch>().AsNoTracking().Include(b => b.Medicine)
            .Where(b => b.Medicine.IsActive && (medicineId == null || b.MedicineId == medicineId));
        if (expiringBefore.HasValue) query = query.Where(b => b.ExpiryDate <= expiringBefore.Value);
        return (await query.OrderBy(b => b.ExpiryDate).ThenBy(b => b.MedicineId).ThenBy(b => b.Id)
            .Skip((page - 1) * 50).Take(50).ToListAsync(ct))
            .Select(b => new InventoryBatchResponse(b.Id, b.MedicineId, b.Medicine.Name, b.Medicine.Unit,
                b.LotNumber, b.ExpiryDate, b.Quantity, b.Medicine.Price, b.ExpiryDate < today)).ToList();
    }

    public async Task<InventoryBatchResponse> ReceiveStockAsync(int userId, ReceiveStockRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequirePharmacistAsync(userId, ct);
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        if (request.ExpiryDate <= today) throw new ClinicException(400, "Không thể nhập lô thuốc đã hết hạn.");
        var medicine = await repository.Query<Medicine>().SingleOrDefaultAsync(m => m.Id == request.MedicineId && m.IsActive, ct)
            ?? throw new ClinicException(404, "Thuốc không tồn tại hoặc đã ngừng sử dụng.");
        var lot = request.LotNumber.Trim();
        var batch = await repository.Query<MedicineBatch>().SingleOrDefaultAsync(b => b.MedicineId == request.MedicineId && b.LotNumber == lot, ct);
        if (batch is null)
        {
            batch = new MedicineBatch { MedicineId = medicine.Id, Medicine = medicine, LotNumber = lot, ExpiryDate = request.ExpiryDate, Quantity = request.Quantity, CreatedAt = DateTime.UtcNow };
            repository.Add(batch);
        }
        else
        {
            if (batch.ExpiryDate != request.ExpiryDate) throw new ClinicException(409, "Lô thuốc đã tồn tại với hạn dùng khác.");
            batch.Quantity += request.Quantity;
        }
        repository.Add(new StockTransaction { Batch = batch, Type = 0, Quantity = request.Quantity, RefType = "Receipt", RefId = batch.Id == 0 ? null : batch.Id, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
        repository.Add(new AuditLog { UserId = userId, Action = "Receive", Entity = "MedicineBatch", EntityId = batch.Id == 0 ? null : batch.Id, NewValue = $"lot={lot};quantity={request.Quantity}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return new InventoryBatchResponse(batch.Id, batch.MedicineId, medicine.Name, medicine.Unit, batch.LotNumber, batch.ExpiryDate, batch.Quantity, medicine.Price, false);
    }

    public async Task<InventoryReceiptResponse> ReceiveReceiptAsync(int userId, ReceiveStockReceiptRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequirePharmacistAsync(userId, ct);
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0 || i.ExpiryDate <= DateOnly.FromDateTime(ClinicTime.Now)))
            throw new ClinicException(400, "Phiếu nhập phải có dòng hợp lệ và lô chưa hết hạn.");
        if (request.Items.Select(i => $"{i.MedicineId}:{i.LotNumber.Trim()}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Items.Count)
            throw new ClinicException(400, "Phiếu nhập không được lặp thuốc và số lô.");
        if (await repository.Query<InventoryReceipt>().AnyAsync(r => r.DocumentNo == request.DocumentNo.Trim(), ct))
            throw new ClinicException(409, "Số chứng từ nhập đã tồn tại.");
        var medicineIds = request.Items.Select(i => i.MedicineId).Distinct().ToArray();
        var medicines = await repository.Query<Medicine>().Where(m => medicineIds.Contains(m.Id) && m.IsActive).ToDictionaryAsync(m => m.Id, ct);
        if (medicines.Count != medicineIds.Length) throw new ClinicException(404, "Thuốc không tồn tại hoặc đã ngừng sử dụng.");
        var receipt = new InventoryReceipt
        {
            SupplierName = request.SupplierName.Trim(), DocumentNo = request.DocumentNo.Trim(),
            Note = request.Note?.Trim(), ReceivedBy = userId, ReceivedAt = DateTime.UtcNow
        };
        foreach (var line in request.Items)
        {
            var medicine = medicines[line.MedicineId];
            var lot = line.LotNumber.Trim();
            var batch = await repository.Query<MedicineBatch>().SingleOrDefaultAsync(b => b.MedicineId == line.MedicineId && b.LotNumber == lot, ct);
            if (batch is null)
            {
                batch = new MedicineBatch { MedicineId = medicine.Id, Medicine = medicine, LotNumber = lot, ExpiryDate = line.ExpiryDate, Quantity = line.Quantity, CreatedAt = DateTime.UtcNow };
                repository.Add(batch);
            }
            else
            {
                if (batch.ExpiryDate != line.ExpiryDate) throw new ClinicException(409, "Lô thuốc đã tồn tại với hạn dùng khác.");
                batch.Quantity += line.Quantity;
            }
            receipt.Items.Add(new InventoryReceiptItem
            {
                Medicine = medicine, Batch = batch, Quantity = line.Quantity, UnitCost = line.UnitCost,
                LineAmount = line.Quantity * line.UnitCost
            });
            repository.Add(new StockTransaction { Batch = batch, Type = 0, Quantity = line.Quantity, RefType = "Receipt", CreatedBy = userId, CreatedAt = DateTime.UtcNow });
        }
        repository.Add(receipt);
        repository.Add(new AuditLog { UserId = userId, Action = "Receive", Entity = "InventoryReceipt", NewValue = $"document={receipt.DocumentNo};supplier={receipt.SupplierName}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return new InventoryReceiptResponse(receipt.Id, receipt.SupplierName, receipt.DocumentNo, receipt.ReceivedAt,
            receipt.Items.Sum(i => i.LineAmount), receipt.Items.Select(i => new InventoryReceiptLineResponse(
                i.MedicineId, i.Medicine.Name, i.Batch.LotNumber, i.Batch.ExpiryDate, i.Quantity, i.UnitCost, i.LineAmount)).ToList());
    }

    public async Task<InventoryBatchResponse> AdjustStockAsync(int userId, AdjustStockRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequirePharmacistAsync(userId, ct);
        if (request.Quantity == 0) throw new ClinicException(400, "Số lượng điều chỉnh phải khác 0.");
        var batch = await repository.Query<MedicineBatch>().Include(b => b.Medicine).SingleOrDefaultAsync(b => b.Id == request.BatchId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy lô thuốc.");
        if (!batch.Medicine.IsActive) throw new ClinicException(409, "Thuốc đã ngừng sử dụng.");
        if (batch.Quantity + request.Quantity < 0) throw new ClinicException(409, "Số lượng điều chỉnh vượt tồn kho.");
        batch.Quantity += request.Quantity;
        repository.Add(new StockTransaction { BatchId = batch.Id, Type = 3, Quantity = request.Quantity, RefType = "Adjustment", RefId = batch.Id, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
        repository.Add(new AuditLog { UserId = userId, Action = "Adjust", Entity = "MedicineBatch", EntityId = batch.Id, NewValue = $"quantity={request.Quantity};reason={request.Reason}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        return new InventoryBatchResponse(batch.Id, batch.MedicineId, batch.Medicine.Name, batch.Medicine.Unit, batch.LotNumber, batch.ExpiryDate, batch.Quantity, batch.Medicine.Price, batch.ExpiryDate < today);
    }

    public async Task<IReadOnlyList<StockTransactionResponse>> ListTransactionsAsync(int userId, int batchId, int page, CancellationToken ct)
    {
        await RequirePharmacistAsync(userId, ct);
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        if (!await repository.Query<MedicineBatch>().AnyAsync(b => b.Id == batchId, ct))
            throw new ClinicException(404, "Không tìm thấy lô thuốc.");
        return await repository.Query<StockTransaction>().AsNoTracking().Where(t => t.BatchId == batchId)
            .OrderByDescending(t => t.Id).Skip((page - 1) * 50).Take(50)
            .Select(t => new StockTransactionResponse(t.Id, t.BatchId, t.Type, t.Quantity, t.RefType, t.RefId, t.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<DispensePrescriptionResponse> DispenseAsync(int userId, int prescriptionId, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequirePharmacistAsync(userId, ct);
        var prescription = await repository.Query<Prescription>().Include(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .Include(p => p.PrescriptionItems).ThenInclude(i => i.PrescriptionDispenses)
            .SingleOrDefaultAsync(p => p.Id == prescriptionId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy đơn thuốc.");
        if (prescription.PrescriptionItems.Count == 0) throw new ClinicException(409, "Đơn thuốc không có thuốc để phát.");
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        var lines = new List<DispensedLineResponse>();
        var alreadyDispensed = true;
        foreach (var item in prescription.PrescriptionItems)
        {
            var remaining = item.Quantity - item.PrescriptionDispenses.Sum(d => d.Quantity);
            foreach (var existing in item.PrescriptionDispenses)
            {
                var existingBatch = await repository.Query<MedicineBatch>().AsNoTracking().SingleAsync(b => b.Id == existing.BatchId, ct);
                lines.Add(new DispensedLineResponse(item.Id, item.MedicineId, item.Medicine.Name, existing.BatchId, existingBatch.LotNumber, existing.Quantity, existingBatch.ExpiryDate));
            }
            if (item.PrescriptionDispenses.Count == 0 && item.BatchId.HasValue && await repository.Query<StockTransaction>()
                .AnyAsync(t => t.BatchId == item.BatchId && t.RefId == prescription.Id && t.Type == 1 && t.Quantity <= -item.Quantity, ct))
            {
                var legacyBatch = await repository.Query<MedicineBatch>().AsNoTracking().SingleAsync(b => b.Id == item.BatchId.Value, ct);
                lines.Add(new DispensedLineResponse(item.Id, item.MedicineId, item.Medicine.Name, legacyBatch.Id, legacyBatch.LotNumber, item.Quantity, legacyBatch.ExpiryDate));
                remaining = 0;
                continue;
            }
            if (remaining <= 0) continue;
            alreadyDispensed = false;
            var batches = await repository.Query<MedicineBatch>().Where(b => b.MedicineId == item.MedicineId && b.ExpiryDate >= today && b.Quantity > 0)
                .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id).ToListAsync(ct);
            if (batches.Sum(b => b.Quantity) < remaining) throw new ClinicException(409, $"Không đủ tồn kho cho thuốc {item.Medicine.Name}.");
            foreach (var batch in batches)
            {
                if (remaining == 0) break;
                var quantity = Math.Min(remaining, batch.Quantity);
                batch.Quantity -= quantity;
                repository.Add(new PrescriptionDispense { PrescriptionItemId = item.Id, BatchId = batch.Id, Quantity = quantity, DispensedBy = userId, DispensedAt = DateTime.UtcNow });
                repository.Add(new StockTransaction { BatchId = batch.Id, Type = 1, Quantity = -quantity, RefType = "Prescription", RefId = prescription.Id, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
                lines.Add(new DispensedLineResponse(item.Id, item.MedicineId, item.Medicine.Name, batch.Id, batch.LotNumber, quantity, batch.ExpiryDate));
                remaining -= quantity;
            }
        }
        if (!alreadyDispensed)
            repository.Add(new AuditLog { UserId = userId, Action = "Dispense", Entity = "Prescription", EntityId = prescription.Id, NewValue = $"lines={lines.Count}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return new DispensePrescriptionResponse(prescription.Id, DateTime.UtcNow, alreadyDispensed, lines);
    }

    private async Task RequirePharmacistAsync(int userId, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Pharmacist", ct) && !await access.HasRoleAsync(userId, "Admin", ct))
            throw new ClinicException(403, "Chỉ dược sĩ hoặc quản trị viên được thao tác kho thuốc.");
    }
}
