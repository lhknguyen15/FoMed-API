/* VC-11: lưu phí khám đã chốt tại thời điểm đặt lịch trên hóa đơn.
   Chạy trong database FoMedDb sau khi đã có bảng billing.invoices. */
USE FoMedDb;
GO

IF COL_LENGTH(N'billing.invoices', N'consultation_fee') IS NULL
BEGIN
    ALTER TABLE billing.invoices
        ADD consultation_fee DECIMAL(12,2) NOT NULL
            CONSTRAINT DF_invoices_consultation_fee DEFAULT (0);
END;
GO
