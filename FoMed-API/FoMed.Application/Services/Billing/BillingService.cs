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
            AppointmentId = record.AppointmentId, PatientId = record.PatientId, PatientName = record.Patient.FullName,
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
                var unitPrice = item.UnitPriceSnapshot > 0 ? item.UnitPriceSnapshot : item.Medicine.Price;
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
                + (r.Prescription?.PrescriptionItems.Sum(i => i.Quantity * (i.UnitPriceSnapshot > 0 ? i.UnitPriceSnapshot : i.Medicine.Price)) ?? 0m);
            var consultationFee = r.Appointment.FeeSnapshot ?? 0m;
            return new InvoiceCandidateResponse(r.Id, r.AppointmentId, r.PatientId, r.Patient.FullName,
                r.Appointment.StartTime, consultationFee, lineAmount, consultationFee + lineAmount);
        }).ToList();
    }

    public async Task<InvoiceResponse> PayAsync(int userId, int invoiceId, RecordPaymentRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireCashierAsync(userId, ct);
        var invoice = await Invoices().SingleOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new ClinicException(404, "Không tìm thấy hóa đơn.");
        var remaining = invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount);
        if (invoice.Status != 0) throw new ClinicException(409, "Hóa đơn đã thanh toán hoặc đã hủy.");
        if (request.Amount <= 0 || request.Amount > remaining || decimal.Round(request.Amount, 2) != request.Amount || request.Method > 3)
            throw new ClinicException(400, "Số tiền hoặc phương thức thanh toán không hợp lệ.");
        invoice.Payments.Add(new Payment { Amount = request.Amount, Method = request.Method, Note = request.Note, PaidAt = DateTime.UtcNow });
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

    private IQueryable<Invoice> Invoices() => repository.Query<Invoice>().Include(i => i.InvoiceItems).Include(i => i.Payments).AsSplitQuery();
    private async Task<bool> IsCashierAsync(int userId, CancellationToken ct) => await access.HasRoleAsync(userId, "Receptionist", ct) || await access.HasRoleAsync(userId, "Admin", ct);
    private async Task RequireCashierAsync(int userId, CancellationToken ct)
    {
        if (!await IsCashierAsync(userId, ct)) throw new ClinicException(403, "Chỉ lễ tân hoặc quản trị viên được thu tiền.");
    }
    private static InvoiceResponse Map(Invoice i) => new(i.Id, i.InvoiceNo, i.PatientId, i.MedicalRecordId, i.TotalAmount,
        i.Payments.Sum(p => p.Amount), i.Status,
        i.InvoiceItems.Select(l => new InvoiceLineResponse(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList(),
        i.Payments.Select(p => new PaymentResponse(p.Id, p.Amount, p.Method, p.PaidAt)).ToList(), i.ConsultationFee);
}
