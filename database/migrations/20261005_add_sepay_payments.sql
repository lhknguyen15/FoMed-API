-- Back up first and select the intended database deliberately.
-- Test requires demo-only data and explicit SePay:TestDatabaseName acknowledgement.
-- FoMedDb is permitted ONLY if all of its patient/payment data is disposable demo data.
USE [FoMedDb];
GO
SET XACT_ABORT ON;
IF OBJECT_ID(N'billing.payments', N'U') IS NULL
    THROW 51000, N'Không tìm thấy billing.payments. Kiểm tra database trước khi chạy.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH('billing.payments', 'provider') IS NULL
    ALTER TABLE billing.payments ADD provider varchar(16) NULL;
IF COL_LENGTH('billing.payments', 'provider_environment') IS NULL
    ALTER TABLE billing.payments ADD provider_environment varchar(8) NULL;
IF COL_LENGTH('billing.payments', 'provider_transaction_id') IS NULL
    ALTER TABLE billing.payments ADD provider_transaction_id bigint NULL;
COMMIT;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_payments_provider_transaction' AND object_id = OBJECT_ID(N'billing.payments'))
    CREATE UNIQUE INDEX UX_payments_provider_transaction ON billing.payments(provider, provider_environment, provider_transaction_id) WHERE provider_transaction_id IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_payments_provider' AND parent_object_id = OBJECT_ID(N'billing.payments'))
    ALTER TABLE billing.payments WITH CHECK ADD CONSTRAINT CK_payments_provider CHECK (
        (provider IS NULL AND provider_environment IS NULL AND provider_transaction_id IS NULL)
        OR (provider IS NOT NULL AND provider = 'SePay' AND provider_environment IS NOT NULL AND provider_environment IN ('Test','Live')
            AND provider_transaction_id IS NOT NULL AND provider_transaction_id > 0 AND method = 2));

IF OBJECT_ID(N'billing.sepay_payment_requests', N'U') IS NULL
BEGIN
    CREATE TABLE billing.sepay_payment_requests (
        id uniqueidentifier NOT NULL PRIMARY KEY,
        invoice_id int NOT NULL REFERENCES billing.invoices(id),
        environment varchar(8) NOT NULL CHECK (environment IN ('Test','Live')),
        code varchar(26) NOT NULL,
        amount decimal(18,2) NOT NULL CHECK (amount > 0 AND amount <= 9999999999.99 AND amount = FLOOR(amount)),
        bank_code varchar(64) NOT NULL,
        gateway nvarchar(64) NOT NULL,
        account_number varchar(34) NOT NULL,
        account_name nvarchar(255) NOT NULL,
        created_by int NOT NULL REFERENCES auth.users(id),
        created_at datetime2 NOT NULL,
        expires_at datetime2 NOT NULL,
        status varchar(32) NOT NULL CHECK (status IN ('Pending','Paid','Expired','Superseded','ReviewRequired')),
        CONSTRAINT CK_sepay_request_expiry CHECK (expires_at > created_at)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_sepay_requests_code' AND object_id = OBJECT_ID(N'billing.sepay_payment_requests'))
    CREATE UNIQUE INDEX UX_sepay_requests_code ON billing.sepay_payment_requests(code);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_sepay_requests_pending' AND object_id = OBJECT_ID(N'billing.sepay_payment_requests'))
    CREATE UNIQUE INDEX UX_sepay_requests_pending ON billing.sepay_payment_requests(environment, invoice_id) WHERE status = 'Pending';

IF OBJECT_ID(N'billing.sepay_transactions', N'U') IS NULL
BEGIN
    CREATE TABLE billing.sepay_transactions (
        id bigint IDENTITY NOT NULL PRIMARY KEY,
        environment varchar(8) NOT NULL CHECK (environment IN ('Test','Live')),
        provider_transaction_id bigint NOT NULL CHECK (provider_transaction_id > 0),
        gateway nvarchar(64) NOT NULL,
        account_number varchar(34) NOT NULL,
        transfer_type varchar(3) NOT NULL CHECK (transfer_type IN ('in','out')),
        amount decimal(18,2) NOT NULL CHECK (amount > 0 AND amount = FLOOR(amount)),
        code nvarchar(128) NULL,
        content nvarchar(2000) NOT NULL,
        reference_code nvarchar(255) NULL,
        payload_hash varchar(64) NOT NULL,
        transaction_at datetime2 NOT NULL,
        received_at datetime2 NOT NULL,
        payment_request_id uniqueidentifier NULL REFERENCES billing.sepay_payment_requests(id),
        payment_id int NULL REFERENCES billing.payments(id),
        status varchar(32) NOT NULL CHECK (status IN ('Applied','ReviewRequired','Ignored')),
        reason varchar(64) NULL,
        CONSTRAINT CK_sepay_transaction_allocation CHECK (
            (status = 'Applied' AND payment_id IS NOT NULL AND payment_request_id IS NOT NULL AND reason IS NULL)
            OR (status <> 'Applied' AND payment_id IS NULL AND reason IS NOT NULL))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_sepay_transactions_identity' AND object_id = OBJECT_ID(N'billing.sepay_transactions'))
    CREATE UNIQUE INDEX UX_sepay_transactions_identity ON billing.sepay_transactions(environment, provider_transaction_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sepay_transactions_review' AND object_id = OBJECT_ID(N'billing.sepay_transactions'))
    CREATE INDEX IX_sepay_transactions_review ON billing.sepay_transactions(status, received_at);
COMMIT;
GO
