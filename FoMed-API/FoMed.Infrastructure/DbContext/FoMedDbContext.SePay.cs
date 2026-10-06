using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.DbContext;

public partial class FoMedDbContext
{
    private static void ConfigureSePay(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.Provider).HasMaxLength(16).IsUnicode(false).HasColumnName("provider");
            entity.Property(p => p.ProviderEnvironment).HasMaxLength(8).IsUnicode(false).HasColumnName("provider_environment");
            entity.Property(p => p.ProviderTransactionId).HasColumnName("provider_transaction_id");
            entity.HasIndex(p => new { p.Provider, p.ProviderEnvironment, p.ProviderTransactionId }, "UX_payments_provider_transaction")
                .IsUnique().HasFilter("[provider_transaction_id] IS NOT NULL");
        });
        modelBuilder.Entity<SePayPaymentRequest>(entity =>
        {
            entity.ToTable("sepay_payment_requests", "billing");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(p => p.InvoiceId).HasColumnName("invoice_id");
            entity.Property(p => p.Environment).HasMaxLength(8).IsUnicode(false).HasColumnName("environment");
            entity.Property(p => p.Code).HasMaxLength(26).IsUnicode(false).HasColumnName("code");
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)").HasColumnName("amount");
            entity.Property(p => p.BankCode).HasMaxLength(64).IsUnicode(false).HasColumnName("bank_code");
            entity.Property(p => p.Gateway).HasMaxLength(64).HasColumnName("gateway");
            entity.Property(p => p.AccountNumber).HasMaxLength(34).IsUnicode(false).HasColumnName("account_number");
            entity.Property(p => p.AccountName).HasMaxLength(255).HasColumnName("account_name");
            entity.Property(p => p.CreatedBy).HasColumnName("created_by");
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
            entity.Property(p => p.ExpiresAt).HasColumnName("expires_at");
            entity.Property(p => p.Status).HasMaxLength(32).IsUnicode(false).HasColumnName("status");
            entity.HasIndex(p => p.Code, "UX_sepay_requests_code").IsUnique();
            entity.HasIndex(p => new { p.Environment, p.InvoiceId }, "UX_sepay_requests_pending")
                .IsUnique().HasFilter("[status] = 'Pending'");
            entity.HasOne(p => p.Invoice).WithMany().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SePayTransaction>(entity =>
        {
            entity.ToTable("sepay_transactions", "billing");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("id");
            entity.Property(t => t.Environment).HasMaxLength(8).IsUnicode(false).HasColumnName("environment");
            entity.Property(t => t.ProviderTransactionId).HasColumnName("provider_transaction_id");
            entity.Property(t => t.Gateway).HasMaxLength(64).HasColumnName("gateway");
            entity.Property(t => t.AccountNumber).HasMaxLength(34).IsUnicode(false).HasColumnName("account_number");
            entity.Property(t => t.TransferType).HasMaxLength(3).IsUnicode(false).HasColumnName("transfer_type");
            entity.Property(t => t.Amount).HasColumnType("decimal(18,2)").HasColumnName("amount");
            entity.Property(t => t.Code).HasMaxLength(128).HasColumnName("code");
            entity.Property(t => t.Content).HasMaxLength(2000).HasColumnName("content");
            entity.Property(t => t.ReferenceCode).HasMaxLength(255).HasColumnName("reference_code");
            entity.Property(t => t.PayloadHash).HasMaxLength(64).IsUnicode(false).HasColumnName("payload_hash");
            entity.Property(t => t.TransactionAt).HasColumnName("transaction_at");
            entity.Property(t => t.ReceivedAt).HasColumnName("received_at");
            entity.Property(t => t.PaymentRequestId).HasColumnName("payment_request_id");
            entity.Property(t => t.PaymentId).HasColumnName("payment_id");
            entity.Property(t => t.Status).HasMaxLength(32).IsUnicode(false).HasColumnName("status");
            entity.Property(t => t.Reason).HasMaxLength(64).IsUnicode(false).HasColumnName("reason");
            entity.HasIndex(t => new { t.Environment, t.ProviderTransactionId }, "UX_sepay_transactions_identity").IsUnique();
            entity.HasIndex(t => new { t.Status, t.ReceivedAt }, "IX_sepay_transactions_review");
            entity.HasOne(t => t.PaymentRequest).WithMany().HasForeignKey(t => t.PaymentRequestId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(t => t.Payment).WithMany().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
