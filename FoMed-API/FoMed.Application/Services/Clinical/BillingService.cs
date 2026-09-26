using FoMed.Application.DTO.Billing;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Clinical;

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
        if (record.Appointment.Status != (byte)AppointmentStatus.Completed) throw new ClinicException(409, "Chỉ lập hóa đơn khi ca khám đã hoàn thành.");
        if (record.MedicalRecordServices.Any(o => o.Status == 0)) throw new ClinicException(409, "Còn dịch vụ chưa có kết quả.");
        if (await repository.Query<Invoice>().AnyAsync(i => i.MedicalRecordId == record.Id && i.Status != 2, ct))
            throw new ClinicException(409, "Bệnh án đã có hóa đơn.");
        var invoice = new Invoice
        {
            InvoiceNo = await repository.NextInvoiceNumberAsync(ct), MedicalRecordId = record.Id,
            AppointmentId = record.AppointmentId, PatientId = record.PatientId, PatientName = record.Patient.FullName,
            CreatedBy = userId, CreatedAt = DateTime.UtcNow
        };
        foreach (var order in record.MedicalRecordServices.Where(o => o.Status == 1))
            invoice.InvoiceItems.Add(new InvoiceItem { ServiceId = order.ServiceId, Description = order.Service.Name, Quantity = 1, UnitPrice = order.Service.Price, Amount = order.Service.Price });
        if (record.Prescription != null)
            foreach (var item in record.Prescription.PrescriptionItems)
                invoice.InvoiceItems.Add(new InvoiceItem { MedicineId = item.MedicineId, Description = item.Medicine.Name, Quantity = item.Quantity, UnitPrice = item.Medicine.Price, Amount = item.Quantity * item.Medicine.Price });
        if (invoice.InvoiceItems.Count == 0) throw new ClinicException(400, "Không có dịch vụ hoặc thuốc để lập hóa đơn.");
        invoice.TotalAmount = invoice.InvoiceItems.Sum(i => i.Amount);
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

    private IQueryable<Invoice> Invoices() => repository.Query<Invoice>().Include(i => i.InvoiceItems).Include(i => i.Payments).AsSplitQuery();
    private async Task<bool> IsCashierAsync(int userId, CancellationToken ct) => await access.HasRoleAsync(userId, "Receptionist", ct) || await access.HasRoleAsync(userId, "Admin", ct);
    private async Task RequireCashierAsync(int userId, CancellationToken ct)
    {
        if (!await IsCashierAsync(userId, ct)) throw new ClinicException(403, "Chỉ lễ tân hoặc quản trị viên được thu tiền.");
    }
    private static InvoiceResponse Map(Invoice i) => new(i.Id, i.InvoiceNo, i.PatientId, i.MedicalRecordId, i.TotalAmount,
        i.Payments.Sum(p => p.Amount), i.Status,
        i.InvoiceItems.Select(l => new InvoiceLineResponse(l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList(),
        i.Payments.Select(p => new PaymentResponse(p.Id, p.Amount, p.Method, p.PaidAt)).ToList());
}
