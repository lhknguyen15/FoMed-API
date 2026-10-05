-- Back up the intended database, then run before deploying the updated API.
-- Legacy payments remain NULL: do not infer tender or cashier from invoice metadata.
USE [FoMedDb];
GO
SET XACT_ABORT ON;
IF OBJECT_ID(N'billing.payments', N'U') IS NULL
    THROW 51000, N'Không tìm thấy billing.payments. Kiểm tra database và schema trước khi chạy.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH('billing.payments', 'cash_received') IS NULL
    ALTER TABLE billing.payments ADD cash_received decimal(18,2) NULL;
IF COL_LENGTH('billing.payments', 'received_by') IS NULL
    ALTER TABLE billing.payments ADD received_by int NULL;
IF COL_LENGTH('billing.payments', 'received_by_name_snapshot') IS NULL
    ALTER TABLE billing.payments ADD received_by_name_snapshot nvarchar(255) NULL;
IF COL_LENGTH('billing.payments', 'idempotency_key') IS NULL
    ALTER TABLE billing.payments ADD idempotency_key uniqueidentifier NULL;
COMMIT;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_payments_received_by' AND parent_object_id = OBJECT_ID(N'billing.payments'))
    ALTER TABLE billing.payments WITH CHECK ADD CONSTRAINT FK_payments_received_by FOREIGN KEY (received_by) REFERENCES auth.users(id);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_payments_cash_received' AND parent_object_id = OBJECT_ID(N'billing.payments'))
    ALTER TABLE billing.payments WITH CHECK ADD CONSTRAINT CK_payments_cash_received CHECK (
        cash_received IS NULL OR (method = 0 AND cash_received > 0 AND cash_received >= amount
            AND cash_received <= 10000000000 AND cash_received = FLOOR(cash_received)));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_payments_idempotency_actor' AND parent_object_id = OBJECT_ID(N'billing.payments'))
    ALTER TABLE billing.payments WITH CHECK ADD CONSTRAINT CK_payments_idempotency_actor CHECK (idempotency_key IS NULL OR received_by IS NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_payments_actor_idempotency' AND object_id = OBJECT_ID(N'billing.payments'))
    CREATE UNIQUE INDEX UX_payments_actor_idempotency ON billing.payments(received_by, idempotency_key) WHERE idempotency_key IS NOT NULL;
COMMIT;
GO
