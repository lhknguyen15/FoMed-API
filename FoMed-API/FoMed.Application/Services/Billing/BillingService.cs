using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Billing;

public sealed class BillingService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<InvoiceResponse> CreateAsync(int userId, CreateInvoiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireCashierAsync(userId, ct);
        var record = await repository.Query<MedicalRecord>().Include(r => r.Appointment).Include(r => r.Patient)
            .Include(r => r.MedicalRecordServices).ThenInclude(o => o.Service)
            .Include(r => r.Prescription!).ThenInclude(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == request.MedicalRecordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (record.Appointment.Status != (byte)AppointmentStatus.Completed || !record.IsFinalized)
            throw new ClinicException(409, "Chỉ lập hóa đơn khi ca khám đã hoàn thành và bệnh án đã chốt.");
        if (record.MedicalRecordServices.Any(o => o.Status == 0)) throw new ClinicException(409, "Còn dịch vụ chưa có kết quả.");
        if (await repository.Query<Invoice>().AnyAsync(i => i.MedicalRecordId == record.Id && i.Status != 2, ct))
            throw new ClinicException(409, "Bệnh án đã có hóa đơn.");
        var invoice = new Invoice
        {
            InvoiceNo = await repository.NextInvoiceNumberAsync(ct), MedicalRecordId = record.Id,
            AppointmentId = record.AppointmentId, PatientId = record.PatientId, Patient = record.Patient, PatientName = record.Patient.FullName,
            CreatedBy = userId, CreatedAt = DateTime.UtcNow,
            ConsultationFee = record.Appointment.FeeSnapshot ?? 0m
        };
        foreach (var order in record.MedicalRecordServices.Where(o => o.Status == 1))
            invoice.InvoiceItems.Add(new InvoiceItem
            {
                ServiceId = order.ServiceId, Description = order.Service.Name, Quantity = order.Quantity,
                UnitPrice = order.UnitPriceSnapshot > 0 ? order.UnitPriceSnapshot : order.Service.Price,
                Amount = order.Quantity * (order.UnitPriceSnapshot > 0 ? order.UnitPriceSnapshot : order.Service.Price)
            });
        if (record.Prescription != null)
        {
            foreach (var item in record.Prescription.PrescriptionItems)
            {
                // Zero is a valid saved price, not a signal to use today's catalog price.
                var unitPrice = item.UnitPriceSnapshot;
                invoice.InvoiceItems.Add(new InvoiceItem { MedicineId = item.MedicineId, Description = item.Medicine.Name, Quantity = item.Quantity, UnitPrice = unitPrice, Amount = item.Quantity * unitPrice });
            }
        }
        if (invoice.InvoiceItems.Count == 0 && invoice.ConsultationFee <= 0) throw new ClinicException(400, "Khong co phi kham, dich vu hoac thuoc de lap hoa don.");
        invoice.TotalAmount = invoice.ConsultationFee + invoice.InvoiceItems.Sum(i => i.Amount);
        if (invoice.TotalAmount > 9999999999.99m || invoice.InvoiceItems.Any(i => i.Amount > 9999999999.99m))
            throw new ClinicException(400, "Tổng tiền vượt giới hạn hóa đơn.");
        if (invoice.TotalAmount == 0) invoice.Status = 1;
        repository.Add(invoice);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(invoice);
    }

    public async Task<InvoiceResponse> GetAsync(int userId, int invoiceId, CancellationToken ct)
    {
        var invoice = await Invoices().SingleOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new ClinicException(404, "Không tìm thấy hóa đơn.");
        if (!await IsCashierAsync(userId, ct) && !await repository.Query<FoMed.Infrastructure.Models.Patient>().AnyAsync(p => p.Id == invoice.PatientId && p.UserId == userId && p.IsActive && p.User!.IsActive, ct))
            throw new ClinicException(403, "Không có quyền xem hóa đơn.");
        return Map(invoice);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(int userId, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var query = Invoices().AsNoTracking();
        if (!await IsCashierAsync(userId, ct)) query = query.Where(i => i.Patient.UserId == userId && i.Patient.IsActive && i.Patient.User!.IsActive);
        return (await query.OrderByDescending(i => i.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<InvoiceSearchResponse> SearchAsync(int userId, InvoiceSearchRequest request, CancellationToken ct)
    {
        await RequireCashierAsync(userId, ct);
        var query = BuildSearchQuery(request);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Skip((request.Page - 1) * 20).Take(20)
            .Select(i => new InvoiceSummaryResponse(i.Id, i.InvoiceNo, i.PatientId,
                i.Patient.PatientCode, i.PatientName ?? i.Patient.FullName, i.MedicalRecordId, i.CreatedAt,
                i.TotalAmount, i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m, i.Status)).ToListAsync(ct);
        // SQL timestamps are UTC even when the provider materializes an unspecified DateTime.
        return new InvoiceSearchResponse(items.Select(i => i with { CreatedAt = DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc) }).ToList(), request.Page, 20, total);
    }

    private IQueryable<Invoice> BuildSearchQuery(InvoiceSearchRequest request)
    {
        if (request.Page < 1 || request.Page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var keyword = request.Keyword?.Trim() ?? "";
        if (keyword.Length > 100) throw new ClinicException(400, "Nội dung tìm kiếm không được vượt quá 100 ký tự.");
        if (request.Status is not ("all" or "outstanding" or "unpaid" or "partial" or "paid" or "cancelled"))
            throw new ClinicException(400, "Trạng thái hóa đơn không hợp lệ.");
        if (request.FromDate > request.ToDate) throw new ClinicException(400, "Ngày bắt đầu không được sau ngày kết thúc.");
        foreach (var date in new[] { request.FromDate, request.ToDate })
            if (date.HasValue && (date.Value.Year < 1900 || date.Value.Year > 9998)) throw new ClinicException(400, "Ngày lọc hóa đơn không hợp lệ.");

        var invoices = repository.Query<Invoice>().AsNoTracking();
        if (request.FromDate.HasValue)
        {
            var fromUtc = request.FromDate.Value.ToDateTime(TimeOnly.MinValue).AddHours(-7);
            invoices = invoices.Where(i => i.CreatedAt >= fromUtc);
        }
        if (request.ToDate.HasValue)
        {
            var untilUtc = request.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
            invoices = invoices.Where(i => i.CreatedAt < untilUtc);
        }
        if (keyword.Length > 0)
        {
            var patientId = int.TryParse(keyword.TrimStart('#'), out var id) ? id : -1;
            invoices = invoices.Where(i => i.InvoiceNo.Contains(keyword) || i.Patient.PatientCode.Contains(keyword)
                || (i.PatientName ?? i.Patient.FullName).Contains(keyword) || i.PatientId == patientId);
        }
        return request.Status switch
        {
            "outstanding" => invoices.Where(i => i.Status == 0 && (i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) < i.TotalAmount),
            "unpaid" => invoices.Where(i => i.Status == 0 && (i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) == 0m),
            "partial" => invoices.Where(i => i.Status == 0 && (i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) > 0m && (i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) < i.TotalAmount),
            "paid" => invoices.Where(i => i.Status == 1),
            "cancelled" => invoices.Where(i => i.Status == 2),
            _ => invoices
        };
    }

    public async Task<IReadOnlyList<InvoiceCandidateResponse>> ListEligibleAsync(int userId, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang khong hop le.");
        await RequireCashierAsync(userId, ct);
        var records = await repository.Query<MedicalRecord>()
            .Include(r => r.Appointment).Include(r => r.Patient)
            .Include(r => r.MedicalRecordServices).ThenInclude(o => o.Service)
            .Include(r => r.Prescription!).ThenInclude(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .Include(r => r.Invoices)
            .AsSplitQuery().AsNoTracking()
            .Where(r => r.Appointment.Status == (byte)AppointmentStatus.Completed
                && r.IsFinalized
                && !r.MedicalRecordServices.Any(o => o.Status == 0)
                && !r.Invoices.Any(i => i.Status != 2))
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * 20).Take(20)
            .ToListAsync(ct);

        return records.Select(r =>
        {
            var lineAmount = r.MedicalRecordServices.Where(o => o.Status == 1)
                .Sum(o => o.Quantity * (o.UnitPriceSnapshot > 0 ? o.UnitPriceSnapshot : o.Service.Price))
                + (r.Prescription?.PrescriptionItems.Sum(i => i.Quantity * i.UnitPriceSnapshot) ?? 0m);
            var consultationFee = r.Appointment.FeeSnapshot ?? 0m;
            return new InvoiceCandidateResponse(r.Id, r.AppointmentId, r.PatientId, r.Patient.FullName,
                r.Appointment.StartTime, consultationFee, lineAmount, consultationFee + lineAmount);
        }).ToList();
    }

    public async Task<InvoiceResponse> PayAsync(int userId, int invoiceId, RecordPaymentRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireCashierAsync(userId, ct);
        if (request.Amount <= 0 || request.Amount > 9999999999.99m || decimal.Round(request.Amount, 2) != request.Amount || request.Method > 3)
            throw new ClinicException(400, "Số tiền hoặc phương thức thanh toán không hợp lệ.");
        if (request.CashReceived.HasValue && (request.Method != 0 || request.CashReceived.Value < request.Amount ||
            request.CashReceived.Value <= 0 || request.CashReceived.Value > 10000000000m || decimal.Truncate(request.CashReceived.Value) != request.CashReceived.Value))
            throw new ClinicException(400, "Tiền khách đưa phải là số đồng nguyên, không nhỏ hơn tiền thanh toán và chỉ áp dụng cho tiền mặt.");
        if (request.IdempotencyKey == Guid.Empty) throw new ClinicException(400, "Mã xác nhận thanh toán không hợp lệ.");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 255) throw new ClinicException(400, "Ghi chú thanh toán không được vượt quá 255 ký tự.");
        if (request.IdempotencyKey.HasValue)
        {
            var previous = await repository.Query<Payment>().SingleOrDefaultAsync(p => p.ReceivedBy == userId && p.IdempotencyKey == request.IdempotencyKey, ct);
            if (previous != null)
            {
                if (previous.InvoiceId != invoiceId || previous.Amount != request.Amount || previous.Method != request.Method ||
                    previous.CashReceived != request.CashReceived || !string.Equals(previous.Note, note, StringComparison.Ordinal))
                    throw new ClinicException(409, "Mã xác nhận đã được dùng cho nội dung thanh toán khác. Vui lòng tải lại hóa đơn.");
                // Same authenticated cashier + same payload: replay safely, even after settlement.
                return Map(await Invoices().SingleAsync(i => i.Id == invoiceId, ct));
            }
        }
        var invoice = await Invoices().SingleOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new ClinicException(404, "Không tìm thấy hóa đơn.");
        var remaining = invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount);
        if (invoice.Status != 0) throw new ClinicException(409, "Hóa đơn đã thanh toán hoặc đã hủy.");
        if (request.Amount > remaining) throw new ClinicException(400, "Số tiền thanh toán vượt quá số dư hóa đơn.");
        var actor = await repository.Query<User>().Where(u => u.Id == userId).Select(u => new { u.FullName, u.Username }).SingleAsync(ct);
        invoice.Payments.Add(new Payment { Amount = request.Amount, Method = request.Method, Note = note, PaidAt = DateTime.UtcNow,
            CashReceived = request.CashReceived, ReceivedBy = userId,
            ReceivedByNameSnapshot = string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName.Trim(), IdempotencyKey = request.IdempotencyKey });
        if (request.Amount == remaining) invoice.Status = 1;
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(invoice);
    }

    public async Task<InvoiceResponse> CancelAsync(int userId, int invoiceId, CancelInvoiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireCashierAsync(userId, ct);
        var invoice = await Invoices().SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ClinicException(404, "Khong tim thay hoa don.");
        if (invoice.Status != 0) throw new ClinicException(409, "Chi co the huy hoa don chua thanh toan.");
        if (invoice.Payments.Any()) throw new ClinicException(409, "Khong the huy hoa don da phat sinh thanh toan.");

        invoice.Status = 2;
        repository.Add(new AuditLog
        {
            UserId = userId,
            Action = "Cancel",
            Entity = "Invoice",
            EntityId = invoice.Id,
            NewValue = request.Reason,
            CreatedAt = DateTime.UtcNow
        });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(invoice);
    }

    private IQueryable<Invoice> Invoices() => repository.Query<Invoice>().Include(i => i.Patient).Include(i => i.InvoiceItems).Include(i => i.Payments).AsSplitQuery();
    private async Task<bool> IsCashierAsync(int userId, CancellationToken ct) => await access.HasRoleAsync(userId, "Receptionist", ct) || await access.HasRoleAsync(userId, "Admin", ct);
    private async Task RequireCashierAsync(int userId, CancellationToken ct)
    {
        if (!await IsCashierAsync(userId, ct)) throw new ClinicException(403, "Chỉ lễ tân hoặc quản trị viên được thu tiền.");
    }
    private static InvoiceResponse Map(Invoice i) => new(i.Id, i.InvoiceNo, i.PatientId, i.MedicalRecordId, i.TotalAmount,
        i.Payments.Sum(p => p.Amount), i.Status,
        i.InvoiceItems.Select(l => new InvoiceLineResponse(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList(),
        i.Payments.OrderBy(p => p.Id).Select(p => new PaymentResponse(p.Id, p.Amount, p.Method, DateTime.SpecifyKind(p.PaidAt, DateTimeKind.Utc),
            p.CashReceived, p.ReceivedBy, p.ReceivedByNameSnapshot, p.IdempotencyKey,
            p.Provider, p.ProviderEnvironment, p.ProviderTransactionId)).ToList(), i.ConsultationFee,
        string.IsNullOrWhiteSpace(i.PatientName) ? i.Patient?.FullName : i.PatientName,
        i.Patient?.PatientCode, DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc));
}
