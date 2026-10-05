using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
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

    public async Task<PharmacyPrescriptionResponse> GetPrescriptionAsync(int userId, int prescriptionId, CancellationToken ct)
    {
        await RequirePharmacistAsync(userId, ct);
        var prescription = await repository.Query<Prescription>().AsNoTracking()
            .Include(p => p.MedicalRecord).ThenInclude(r => r.Appointment)
            .Include(p => p.MedicalRecord).ThenInclude(r => r.Patient)
            .Include(p => p.MedicalRecord).ThenInclude(r => r.Doctor)
            .Include(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .Include(p => p.PrescriptionItems).ThenInclude(i => i.PrescriptionDispenses)
            .SingleOrDefaultAsync(p => p.Id == prescriptionId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy đơn thuốc.");
        var legacy = await repository.Query<StockTransaction>().AsNoTracking()
            .Where(t => t.RefId == prescriptionId && t.Type == 1 && t.Quantity < 0).ToListAsync(ct);
        var medicineIds = prescription.PrescriptionItems.Select(i => i.MedicineId).Distinct().ToArray();
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        var availableBatches = await repository.Query<MedicineBatch>().AsNoTracking()
            .Where(b => medicineIds.Contains(b.MedicineId) && b.Medicine.IsActive && b.ExpiryDate >= today && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id).ToListAsync(ct);
        var items = prescription.PrescriptionItems.Select(item =>
        {
            var dispensed = item.PrescriptionDispenses.Sum(d => d.Quantity);
            if (dispensed == 0 && item.BatchId.HasValue && legacy.Any(t => t.BatchId == item.BatchId && t.Quantity <= -item.Quantity))
                dispensed = item.Quantity;
            var batches = availableBatches.Where(b => b.MedicineId == item.MedicineId).ToArray();
            var remaining = Math.Max(0, item.Quantity - dispensed);
            var need = remaining;
            var proposals = new List<PharmacyBatchProposal>();
            foreach (var batch in batches)
            {
                if (need == 0) break;
                var take = Math.Min(need, batch.Quantity);
                proposals.Add(new(batch.Id, batch.LotNumber, batch.ExpiryDate, batch.Quantity, take));
                need -= take;
            }
            return new PharmacyPrescriptionLineResponse(item.MedicineId, item.Medicine.Name, item.Quantity,
                dispensed, item.Dosage, item.Instruction) { Unit = item.Medicine.Unit, AvailableQuantity = batches.Sum(b => (long)b.Quantity),
                RemainingQuantity = remaining, ShortageQuantity = need, ProposedBatches = proposals };
        }).ToList();
        var fullyDispensed = items.Count > 0 && items.All(i => i.DispensedQuantity >= i.Quantity);
        var record = prescription.MedicalRecord;
        var blocked = DispensingBlockedReason(prescription) ?? (fullyDispensed ? "Đơn thuốc đã được phát đủ." :
            items.Any(i => i.ShortageQuantity > 0) ? "Tồn khả dụng không đủ để phát toàn bộ đơn. Vui lòng bổ sung thuốc và tải lại tồn kho." : null);
        return new PharmacyPrescriptionResponse(prescription.Id, record.Id, record.Patient.FullName,
            record.Patient.PatientCode, record.Doctor.FullName, record.IsFinalized, record.Appointment.Status,
            await PrescriptionDispensingState.HasDispensedAsync(repository, prescriptionId, ct), fullyDispensed,
            blocked is null, blocked, items);
    }

    public async Task<PharmacyPrescriptionPage> ListPrescriptionsAsync(int userId, string? keyword, string? status, int page, CancellationToken ct)
    {
        await RequirePharmacistAsync(userId, ct);
        if (page is < 1 or > 100000 || keyword?.Length > 100 || status is not (null or "pending" or "dispensed"))
            throw new ClinicException(400, "Bộ lọc đơn thuốc không hợp lệ.");
        // Match the legacy completion rule used by preview/dispense; never list draft consultations as ready.
        var eligible = repository.Query<Prescription>().AsNoTracking().Where(p => p.MedicalRecord.IsFinalized &&
            p.MedicalRecord.Appointment.Status == (byte)AppointmentStatus.Completed && p.PrescriptionItems.Any());
        var term = keyword?.Trim();
        if (!string.IsNullOrEmpty(term)) eligible = eligible.Where(p => p.MedicalRecord.Patient.FullName.Contains(term) ||
            p.MedicalRecord.Patient.PatientCode.Contains(term) || p.Id.ToString() == term);
        var transactions = repository.Query<StockTransaction>();
        // Filter an SQL-translatable projection; a positional DTO constructor cannot be queried by its properties.
        var query = eligible.Select(p => new { PrescriptionId = p.Id, p.MedicalRecordId,
            PatientName = p.MedicalRecord.Patient.FullName, PatientCode = p.MedicalRecord.Patient.PatientCode,
            DoctorName = p.MedicalRecord.Doctor.FullName, p.CreatedAt,
            IsFullyDispensed = p.PrescriptionItems.All(i => (i.PrescriptionDispenses.Sum(d => (int?)d.Quantity) ?? 0) >= i.Quantity ||
                (!i.PrescriptionDispenses.Any() && i.BatchId.HasValue && transactions.Any(t => t.BatchId == i.BatchId && t.RefId == p.Id && t.Type == 1 && t.Quantity <= -i.Quantity))) });
        if (status is null or "pending") query = query.Where(p => !p.IsFullyDispensed);
        else query = query.Where(p => p.IsFullyDispensed);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.PrescriptionId).Skip((page - 1) * 10).Take(10).ToListAsync(ct);
        var items = rows.Select(p => new PharmacyPrescriptionListItem(p.PrescriptionId, p.MedicalRecordId,
            p.PatientName, p.PatientCode, p.DoctorName, DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc), p.IsFullyDispensed)).ToList();
        return new(items, page, 10, total);
    }

    public async Task<DispensePrescriptionResponse> DispenseAsync(int userId, int prescriptionId, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequirePharmacistAsync(userId, ct);
        var prescription = await repository.Query<Prescription>().Include(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .Include(p => p.PrescriptionItems).ThenInclude(i => i.PrescriptionDispenses)
            .Include(p => p.MedicalRecord).ThenInclude(r => r.Appointment)
            .SingleOrDefaultAsync(p => p.Id == prescriptionId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy đơn thuốc.");
        var blocked = DispensingBlockedReason(prescription);
        if (blocked is not null) throw new ClinicException(409, blocked);
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
            if (!item.Medicine.IsActive) throw new ClinicException(409, $"Thuốc {item.Medicine.Name} đã ngừng sử dụng.");
            var batches = await repository.Query<MedicineBatch>().Where(b => b.MedicineId == item.MedicineId && b.ExpiryDate >= today && b.Quantity > 0)
                .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id).ToListAsync(ct);
            if (batches.Sum(b => (long)b.Quantity) < remaining) throw new ClinicException(409, $"Không đủ tồn kho cho thuốc {item.Medicine.Name}.");
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

    private static string? DispensingBlockedReason(Prescription prescription)
    {
        if (!prescription.MedicalRecord.IsFinalized || prescription.MedicalRecord.Appointment.Status != (byte)AppointmentStatus.Completed)
            return "Bệnh án chưa chốt hoặc lượt khám chưa hoàn tất, chưa thể phát thuốc.";
        return prescription.PrescriptionItems.Count == 0 ? "Đơn thuốc không có thuốc để phát." : null;
    }

    private async Task RequirePharmacistAsync(int userId, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Pharmacist", ct) && !await access.HasRoleAsync(userId, "Admin", ct))
            throw new ClinicException(403, "Chỉ dược sĩ hoặc quản trị viên được thao tác kho thuốc.");
    }
}
