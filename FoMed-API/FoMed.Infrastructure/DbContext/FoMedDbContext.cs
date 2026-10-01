using System;
using System.Collections.Generic;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.DbContext;

public partial class FoMedDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public FoMedDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<FoMedDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Appointment> Appointments { get; set; }

    public virtual DbSet<AppointmentStatusHistory> AppointmentStatusHistories { get; set; }

    public virtual DbSet<Attachment> Attachments { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Doctor> Doctors { get; set; }

    public virtual DbSet<DoctorSchedule> DoctorSchedules { get; set; }

    public virtual DbSet<DoctorTimeOff> DoctorTimeOffs { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceItem> InvoiceItems { get; set; }

    public virtual DbSet<InventoryReceipt> InventoryReceipts { get; set; }

    public virtual DbSet<InventoryReceiptItem> InventoryReceiptItems { get; set; }

    public virtual DbSet<LabResult> LabResults { get; set; }

    public virtual DbSet<MedicalRecord> MedicalRecords { get; set; }

    public virtual DbSet<MedicalRecordService> MedicalRecordServices { get; set; }

    public virtual DbSet<Medicine> Medicines { get; set; }

    public virtual DbSet<MedicineBatch> MedicineBatches { get; set; }

    public virtual DbSet<Patient> Patients { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Prescription> Prescriptions { get; set; }

    public virtual DbSet<PrescriptionItem> PrescriptionItems { get; set; }

    public virtual DbSet<PrescriptionDispense> PrescriptionDispenses { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Service> Services { get; set; }

    public virtual DbSet<Specialty> Specialties { get; set; }

    public virtual DbSet<StockTransaction> StockTransactions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__appointm__3213E83F6C234834");

            entity.ToTable("appointments", "scheduling");

            entity.HasIndex(e => e.PatientId, "IX_appointments_patient");

            entity.HasIndex(e => e.StartTime, "IX_appointments_start_time");

            entity.HasIndex(e => e.AppointmentCode, "UQ_appointments_code").IsUnique();

            entity.HasIndex(e => new { e.DoctorId, e.StartTime }, "UX_appointments_doctor_slot")
                .IsUnique()
                .HasFilter("([status]<(4))");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentCode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("appointment_code");
            entity.Property(e => e.CheckedInAt).HasColumnName("checked_in_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DoctorId).HasColumnName("doctor_id");
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.QueueNumber).HasColumnName("queue_number");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("reason");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.FeeSnapshot)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("fee_snapshot");
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.StartTime).HasColumnName("start_time");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_appointments_created_by");

            entity.HasOne(d => d.Doctor).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.DoctorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appointments_doctor");

            entity.HasOne(d => d.Patient).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appointments_patient");

            entity.HasOne(d => d.Service).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.ServiceId)
                .HasConstraintName("FK_appointments_service");
        });

        modelBuilder.Entity<AppointmentStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__appointm__3213E83F72690EF3");

            entity.ToTable("appointment_status_history", "scheduling");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.FromStatus).HasColumnName("from_status");
            entity.Property(e => e.Reason)
                .HasMaxLength(500)
                .HasColumnName("reason");
            entity.Property(e => e.ToStatus).HasColumnName("to_status");

            entity.HasOne(d => d.Appointment).WithMany(p => p.AppointmentStatusHistories)
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_appt_status_history_appt");

            entity.HasOne(d => d.ChangedByNavigation).WithMany(p => p.AppointmentStatusHistories)
                .HasForeignKey(d => d.ChangedBy)
                .HasConstraintName("FK_appt_status_history_user");
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__attachme__3213E83F4989B857");

            entity.ToTable("attachments", "clinical");

            entity.HasIndex(e => new { e.OwnerType, e.OwnerId }, "IX_attachments_owner");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(500)
                .HasColumnName("file_url");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.OwnerType)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("owner_type");
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("uploaded_at");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__audit_lo__3213E83F2BA6B508");

            entity.ToTable("audit_logs", "audit");

            entity.HasIndex(e => e.CreatedAt, "IX_audit_logs_created_at");

            entity.HasIndex(e => new { e.Entity, e.EntityId }, "IX_audit_logs_entity");

            entity.HasIndex(e => e.UserId, "IX_audit_logs_user");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Entity)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("entity");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.NewValue).HasColumnName("new_value");
            entity.Property(e => e.OldValue).HasColumnName("old_value");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_audit_logs_user");
        });

        modelBuilder.Entity<Doctor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__doctors__3213E83F326EE8E6");

            entity.ToTable("doctors", "scheduling");

            entity.HasIndex(e => e.UserId, "UQ_doctors_user").IsUnique();

            entity.HasIndex(e => e.LicenseNumber, "UX_doctors_license")
                .IsUnique()
                .HasFilter("([license_number] IS NOT NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConsultationFee)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("consultation_fee");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LicenseNumber)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("license_number");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("phone");
            entity.Property(e => e.Room)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("room");
            entity.Property(e => e.SpecialtyId).HasColumnName("specialty_id");
            entity.Property(e => e.Title)
                .HasMaxLength(50)
                .HasColumnName("title");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Specialty).WithMany(p => p.Doctors)
                .HasForeignKey(d => d.SpecialtyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_doctors_specialty");

            entity.HasOne(d => d.User).WithOne(p => p.Doctor)
                .HasForeignKey<Doctor>(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_doctors_user");
        });

        modelBuilder.Entity<DoctorSchedule>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__doctor_s__3213E83FD66855DC");

            entity.ToTable("doctor_schedules", "scheduling");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.DoctorId).HasColumnName("doctor_id");
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.SlotMinutes)
                .HasDefaultValue(30)
                .HasColumnName("slot_minutes");
            entity.Property(e => e.StartTime).HasColumnName("start_time");

            entity.HasOne(d => d.Doctor).WithMany(p => p.DoctorSchedules)
                .HasForeignKey(d => d.DoctorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_doctor_schedules_doctor");
        });

        modelBuilder.Entity<DoctorTimeOff>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__doctor_t__3213E83FC935D80F");

            entity.ToTable("doctor_time_off", "scheduling");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DoctorId).HasColumnName("doctor_id");
            entity.Property(e => e.EndAt).HasColumnName("end_at");
            entity.Property(e => e.Reason)
                .HasMaxLength(255)
                .HasColumnName("reason");
            entity.Property(e => e.StartAt).HasColumnName("start_at");

            entity.HasOne(d => d.Doctor).WithMany(p => p.DoctorTimeOffs)
                .HasForeignKey(d => d.DoctorId)
                .HasConstraintName("FK_doctor_time_off_doctor");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__invoices__3213E83F57C8A89B");

            entity.ToTable("invoices", "billing");

            entity.HasIndex(e => e.PatientId, "IX_invoices_patient");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_invoices_status_created");

            entity.HasIndex(e => e.InvoiceNo, "UQ_invoices_no").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.InvoiceNo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("invoice_no");
            entity.Property(e => e.MedicalRecordId).HasColumnName("medical_record_id");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.PatientName)
                .HasMaxLength(255)
                .HasColumnName("patient_name");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.TotalAmount)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("total_amount");
            entity.Property(e => e.ConsultationFee)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("consultation_fee");

            entity.HasOne(d => d.Appointment).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.AppointmentId)
                .HasConstraintName("FK_invoices_appt");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_invoices_created_by");

            entity.HasOne(d => d.MedicalRecord).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.MedicalRecordId)
                .HasConstraintName("FK_invoices_record");

            entity.HasOne(d => d.Patient).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_invoices_patient");
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__invoice___3213E83F6A3F4394");

            entity.ToTable("invoice_items", "billing");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("amount");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.MedicineId).HasColumnName("medicine_id");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.UnitPrice)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("unit_price");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_invoice_items_invoice");

            entity.HasOne(d => d.Medicine).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_invoice_items_medicine");

            entity.HasOne(d => d.Service).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.ServiceId)
                .HasConstraintName("FK_invoice_items_service");
        });

        modelBuilder.Entity<InventoryReceipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_inventory_receipts");
            entity.ToTable("inventory_receipts", "clinical");
            entity.HasIndex(e => e.DocumentNo, "UQ_inventory_receipts_document_no").IsUnique();
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SupplierName).HasMaxLength(255).HasColumnName("supplier_name");
            entity.Property(e => e.DocumentNo).HasMaxLength(100).IsUnicode(false).HasColumnName("document_no");
            entity.Property(e => e.ReceivedBy).HasColumnName("received_by");
            entity.Property(e => e.ReceivedAt).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("received_at");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("note");
            entity.HasOne(e => e.ReceivedByNavigation).WithMany()
                .HasForeignKey(e => e.ReceivedBy).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_inventory_receipts_user");
        });

        modelBuilder.Entity<InventoryReceiptItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_inventory_receipt_items");
            entity.ToTable("inventory_receipt_items", "clinical");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.MedicineId).HasColumnName("medicine_id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(12, 2)").HasColumnName("unit_cost");
            entity.Property(e => e.LineAmount).HasColumnType("decimal(14, 2)").HasColumnName("line_amount");
            entity.HasOne(e => e.Receipt).WithMany(e => e.Items).HasForeignKey(e => e.ReceiptId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_inventory_receipt_items_receipt");
            entity.HasOne(e => e.Medicine).WithMany(e => e.InventoryReceiptItems).HasForeignKey(e => e.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_inventory_receipt_items_medicine");
            entity.HasOne(e => e.Batch).WithMany(e => e.InventoryReceiptItems).HasForeignKey(e => e.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_inventory_receipt_items_batch");
        });

        modelBuilder.Entity<LabResult>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__lab_resu__3213E83F71BDD45B");

            entity.ToTable("lab_results", "clinical");

            entity.HasIndex(e => e.MedicalRecordServiceId, "UQ_lab_results_mrs").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Conclusion)
                .HasMaxLength(1000)
                .HasColumnName("conclusion");
            entity.Property(e => e.MedicalRecordServiceId).HasColumnName("medical_record_service_id");
            entity.Property(e => e.ResultAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("result_at");
            entity.Property(e => e.ResultSummary)
                .HasMaxLength(2000)
                .HasColumnName("result_summary");
            entity.Property(e => e.ReferenceRange)
                .HasMaxLength(1000)
                .HasColumnName("reference_range");
            entity.Property(e => e.TechnicianId).HasColumnName("technician_id");

            entity.HasOne(d => d.MedicalRecordService).WithOne(p => p.LabResult)
                .HasForeignKey<LabResult>(d => d.MedicalRecordServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_lab_results_mrs");

            entity.HasOne(d => d.Technician).WithMany(p => p.LabResults)
                .HasForeignKey(d => d.TechnicianId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_lab_results_technician");
        });

        modelBuilder.Entity<MedicalRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__medical___3213E83F273A512D");

            entity.ToTable("medical_records", "clinical");

            entity.HasIndex(e => e.AppointmentId, "UQ_medical_records_appt").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppointmentId).HasColumnName("appointment_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Diagnosis)
                .HasMaxLength(4000)
                .HasColumnName("diagnosis");
            entity.Property(e => e.DoctorId).HasColumnName("doctor_id");
            entity.Property(e => e.Note)
                .HasMaxLength(4000)
                .HasColumnName("note");
            entity.Property(e => e.VitalsJson)
                .HasMaxLength(4000)
                .HasColumnName("vitals_json");
            entity.Property(e => e.Icd10Code)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("icd10_code");
            entity.Property(e => e.TreatmentPlan)
                .HasMaxLength(4000)
                .HasColumnName("treatment_plan");
            entity.Property(e => e.FollowUpDate).HasColumnName("follow_up_date");
            entity.Property(e => e.IsFinalized)
                .HasDefaultValue(false)
                .HasColumnName("is_finalized");
            entity.Property(e => e.FinalizedAt).HasColumnName("finalized_at");
            entity.Property(e => e.PatientId).HasColumnName("patient_id");
            entity.Property(e => e.Symptoms)
                .HasMaxLength(4000)
                .HasColumnName("symptoms");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.Appointment).WithOne(p => p.MedicalRecord)
                .HasForeignKey<MedicalRecord>(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_medical_records_appt");

            entity.HasOne(d => d.Doctor).WithMany(p => p.MedicalRecords)
                .HasForeignKey(d => d.DoctorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_medical_records_doctor");

            entity.HasOne(d => d.Patient).WithMany(p => p.MedicalRecords)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_medical_records_patient");
        });

        modelBuilder.Entity<MedicalRecordService>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__medical___3213E83F7B775C48");

            entity.ToTable("medical_record_services", "clinical");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MedicalRecordId).HasColumnName("medical_record_id");
            entity.Property(e => e.OrderedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("ordered_at");
            entity.Property(e => e.OrderedBy).HasColumnName("ordered_by");
            entity.Property(e => e.ServiceId).HasColumnName("service_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Quantity).HasDefaultValue(1).HasColumnName("quantity");
            entity.Property(e => e.UnitPriceSnapshot)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("unit_price_snapshot");

            entity.HasOne(d => d.MedicalRecord).WithMany(p => p.MedicalRecordServices)
                .HasForeignKey(d => d.MedicalRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mrs_record");

            entity.HasOne(d => d.OrderedByNavigation).WithMany(p => p.MedicalRecordServices)
                .HasForeignKey(d => d.OrderedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mrs_doctor");

            entity.HasOne(d => d.Service).WithMany(p => p.MedicalRecordServices)
                .HasForeignKey(d => d.ServiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_mrs_service");
        });

        modelBuilder.Entity<Medicine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__medicine__3213E83F6EA24ADC");

            entity.ToTable("medicines", "clinical");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Price)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("price");
            entity.Property(e => e.Unit)
                .HasMaxLength(50)
                .HasColumnName("unit");
        });

        modelBuilder.Entity<MedicineBatch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__medicine__3213E83FC9704231");

            entity.ToTable("medicine_batches", "clinical");

            entity.HasIndex(e => e.ExpiryDate, "IX_medicine_batches_expiry");

            entity.HasIndex(e => new { e.MedicineId, e.LotNumber }, "UQ_medicine_batches").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.LotNumber)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("lot_number");
            entity.Property(e => e.MedicineId).HasColumnName("medicine_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicineBatches)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_medicine_batches_medicine");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__patients__3213E83FB0B7AC98");

            entity.ToTable("patients", "scheduling");

            entity.HasIndex(e => e.FullName, "IX_patients_full_name");

            entity.HasIndex(e => e.Phone, "IX_patients_phone");

            entity.HasIndex(e => e.PatientCode, "UQ_patients_code").IsUnique();

            entity.HasIndex(e => e.UserId, "UX_patients_user")
                .IsUnique()
                .HasFilter("([user_id] IS NOT NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.NationalId)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("national_id");
            entity.Property(e => e.InsuranceNumber)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("insurance_number");
            entity.Property(e => e.EmergencyContactName)
                .HasMaxLength(255)
                .HasColumnName("emergency_contact_name");
            entity.Property(e => e.EmergencyContactPhone)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("emergency_contact_phone");
            entity.Property(e => e.Allergies)
                .HasMaxLength(1000)
                .HasColumnName("allergies");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PatientCode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("patient_code");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("phone");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.Patient)
                .HasForeignKey<Patient>(d => d.UserId)
                .HasConstraintName("FK_patients_user");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__payments__3213E83FBD8CD68D");

            entity.ToTable("payments", "billing");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("amount");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.Method).HasColumnName("method");
            entity.Property(e => e.Note)
                .HasMaxLength(255)
                .HasColumnName("note");
            entity.Property(e => e.PaidAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("paid_at");

            entity.HasOne(d => d.Invoice).WithMany(p => p.Payments)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_payments_invoice");
        });

        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__prescrip__3213E83F4C2C8FB1");

            entity.ToTable("prescriptions", "clinical");

            entity.HasIndex(e => e.MedicalRecordId, "UQ_prescriptions_record").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.MedicalRecordId).HasColumnName("medical_record_id");
            entity.Property(e => e.Note)
                .HasMaxLength(500)
                .HasColumnName("note");

            entity.HasOne(d => d.MedicalRecord).WithOne(p => p.Prescription)
                .HasForeignKey<Prescription>(d => d.MedicalRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescriptions_record");
        });

        modelBuilder.Entity<PrescriptionItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__prescrip__3213E83FE58BEF33");

            entity.ToTable("prescription_items", "clinical");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.Dosage)
                .HasMaxLength(255)
                .HasColumnName("dosage");
            entity.Property(e => e.Instruction)
                .HasMaxLength(255)
                .HasColumnName("instruction");
            entity.Property(e => e.MedicineId).HasColumnName("medicine_id");
            entity.Property(e => e.PrescriptionId).HasColumnName("prescription_id");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
            entity.Property(e => e.UnitPriceSnapshot)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("unit_price_snapshot");

            entity.HasOne(d => d.Batch).WithMany(p => p.PrescriptionItems)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK_prescription_items_batch");

            entity.HasOne(d => d.Medicine).WithMany(p => p.PrescriptionItems)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescription_items_medicine");

            entity.HasOne(d => d.Prescription).WithMany(p => p.PrescriptionItems)
                .HasForeignKey(d => d.PrescriptionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescription_items_presc");
        });

        modelBuilder.Entity<PrescriptionDispense>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_prescription_dispenses");
            entity.ToTable("prescription_dispenses", "clinical");
            entity.HasIndex(e => e.PrescriptionItemId, "IX_prescription_dispenses_item");
            entity.HasIndex(e => e.BatchId, "IX_prescription_dispenses_batch");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PrescriptionItemId).HasColumnName("prescription_item_id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.DispensedBy).HasColumnName("dispensed_by");
            entity.Property(e => e.DispensedAt).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("dispensed_at");
            entity.HasOne(e => e.PrescriptionItem).WithMany(e => e.PrescriptionDispenses)
                .HasForeignKey(e => e.PrescriptionItemId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescription_dispenses_item");
            entity.HasOne(e => e.Batch).WithMany(e => e.PrescriptionDispenses)
                .HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescription_dispenses_batch");
            entity.HasOne(e => e.DispensedByNavigation).WithMany()
                .HasForeignKey(e => e.DispensedBy).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_prescription_dispenses_user");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__refresh___3213E83FC62CCBF9");

            entity.ToTable("refresh_tokens", "auth");

            entity.HasIndex(e => e.TokenHash, "UQ_refresh_tokens_hash").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("token_hash");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_refresh_tokens_user");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__roles__3213E83F9D21B6EE");

            entity.ToTable("roles", "auth");

            entity.HasIndex(e => e.Name, "UQ_roles_name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__services__3213E83F9EEDE666");

            entity.ToTable("services", "billing");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("code");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Price)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("price");
            entity.Property(e => e.SpecialtyId).HasColumnName("specialty_id");
            entity.Property(e => e.DurationMinutes).HasDefaultValue(30).HasColumnName("duration_minutes");
            entity.HasIndex(e => e.Code, "UX_services_code").IsUnique().HasFilter("([code] IS NOT NULL)");
            entity.HasOne(e => e.Specialty).WithMany().HasForeignKey(e => e.SpecialtyId).OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_services_specialty");
        });

        modelBuilder.Entity<Specialty>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__specialt__3213E83F682F4239");

            entity.ToTable("specialties", "scheduling");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<StockTransaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__stock_tr__3213E83FB1446331");

            entity.ToTable("stock_transactions", "clinical");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.RefId).HasColumnName("ref_id");
            entity.Property(e => e.RefType)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("ref_type");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Batch).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_stock_transactions_batch");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_stock_transactions_user");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__users__3213E83FFAB5282D");

            entity.ToTable("users", "auth");

            entity.HasIndex(e => e.Username, "UQ_users_username").IsUnique();

            entity.HasIndex(e => e.Email, "UX_users_email")
                .IsUnique()
                .HasFilter("([email] IS NOT NULL)");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("phone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("username");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__user_rol__3213E83F31F0DE6B");

            entity.ToTable("user_roles", "auth");

            entity.HasIndex(e => new { e.UserId, e.RoleId }, "UQ_user_roles").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_user_roles_role");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_user_roles_user");
        });
        modelBuilder.HasSequence<int>("seq_appointment_code", "scheduling");
        modelBuilder.HasSequence<int>("seq_invoice_no", "billing");
        modelBuilder.HasSequence<int>("seq_patient_code", "scheduling");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
