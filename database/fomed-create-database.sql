    /* ============================================================
   FoMed - Clinic Management System
   Buoc 2: Tao Database (SQL Server 2019+)
   Chay tuan tu tu tren xuong. An toan de chay lai (co kiem tra ton tai).
   ============================================================ */

IF DB_ID('FoMedDb') IS NULL
BEGIN
    CREATE DATABASE FoMedDb;
END
GO

USE FoMedDb;
GO

/* ============================================================
   1. SCHEMAS
   ============================================================ */
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'auth')       EXEC('CREATE SCHEMA auth');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'audit')      EXEC('CREATE SCHEMA audit');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'scheduling') EXEC('CREATE SCHEMA scheduling');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'clinical')   EXEC('CREATE SCHEMA clinical');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'billing')    EXEC('CREATE SCHEMA billing');
GO

/* ============================================================
   2. SEQUENCES - sinh ma nghiep vu an toan khi nhieu nguoi thao tac cung luc
   Dung: SELECT 'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6)
   ============================================================ */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'seq_patient_code')
    CREATE SEQUENCE scheduling.seq_patient_code AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'seq_appointment_code')
    CREATE SEQUENCE scheduling.seq_appointment_code AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'seq_invoice_no')
    CREATE SEQUENCE billing.seq_invoice_no AS INT START WITH 1 INCREMENT BY 1;
GO

/* ============================================================
   3. AUTH SCHEMA
   ============================================================ */
CREATE TABLE auth.users (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    username        VARCHAR(100)  NOT NULL,
    password_hash   VARCHAR(255)  NOT NULL,
    email           VARCHAR(255)  NULL,
    full_name       NVARCHAR(255) NULL,
    phone           VARCHAR(20)   NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NULL,
    CONSTRAINT UQ_users_username UNIQUE (username)
);
CREATE UNIQUE INDEX UX_users_email ON auth.users(email) WHERE email IS NOT NULL;
GO

CREATE TABLE auth.roles (
    id      INT IDENTITY(1,1) PRIMARY KEY,
    name    VARCHAR(50) NOT NULL,
    CONSTRAINT UQ_roles_name UNIQUE (name)
);
GO

CREATE TABLE auth.user_roles (
    id       INT IDENTITY(1,1) PRIMARY KEY,
    user_id  INT NOT NULL,
    role_id  INT NOT NULL,
    CONSTRAINT FK_user_roles_user FOREIGN KEY (user_id) REFERENCES auth.users(id),
    CONSTRAINT FK_user_roles_role FOREIGN KEY (role_id) REFERENCES auth.roles(id),
    CONSTRAINT UQ_user_roles UNIQUE (user_id, role_id)
);
GO

CREATE TABLE auth.refresh_tokens (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    user_id     INT NOT NULL,
    token_hash  CHAR(64) NOT NULL,
    expires_at  DATETIME2 NOT NULL,
    revoked_at  DATETIME2 NULL,
    created_at  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_refresh_tokens_user FOREIGN KEY (user_id) REFERENCES auth.users(id),
    CONSTRAINT UQ_refresh_tokens_hash UNIQUE (token_hash)
);
GO

/* ============================================================
   4. AUDIT SCHEMA
   ============================================================ */
CREATE TABLE audit.audit_logs (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    user_id     INT NULL,
    action      VARCHAR(50) NOT NULL,
    entity      VARCHAR(100) NOT NULL,
    entity_id   INT NULL,
    old_value   NVARCHAR(MAX) NULL,
    new_value   NVARCHAR(MAX) NULL,
    created_at  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_audit_logs_user FOREIGN KEY (user_id) REFERENCES auth.users(id)
);
CREATE INDEX IX_audit_logs_entity ON audit.audit_logs(entity, entity_id);
CREATE INDEX IX_audit_logs_user ON audit.audit_logs(user_id);
CREATE INDEX IX_audit_logs_created_at ON audit.audit_logs(created_at);
GO

/* ============================================================
   5. SCHEDULING SCHEMA
   ============================================================ */
CREATE TABLE scheduling.specialties (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    name         NVARCHAR(255) NOT NULL,
    description  NVARCHAR(500) NULL,
    is_active    BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE scheduling.doctors (
    id                 INT IDENTITY(1,1) PRIMARY KEY,
    user_id            INT NOT NULL,
    specialty_id       INT NOT NULL,
    full_name          NVARCHAR(255) NOT NULL,
    title              NVARCHAR(50) NULL,
    license_number     VARCHAR(50) NULL,
    phone              VARCHAR(20) NULL,
    room               VARCHAR(50) NULL,
    consultation_fee   DECIMAL(12,2) NOT NULL DEFAULT 0,
    is_active          BIT NOT NULL DEFAULT 1,
    created_at         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_doctors_user FOREIGN KEY (user_id) REFERENCES auth.users(id),
    CONSTRAINT FK_doctors_specialty FOREIGN KEY (specialty_id) REFERENCES scheduling.specialties(id),
    CONSTRAINT UQ_doctors_user UNIQUE (user_id)
);
CREATE UNIQUE INDEX UX_doctors_license ON scheduling.doctors(license_number) WHERE license_number IS NOT NULL;
GO

CREATE TABLE scheduling.patients (
    id             INT IDENTITY(1,1) PRIMARY KEY,
    patient_code   VARCHAR(20) NOT NULL,
    user_id        INT NULL,
    full_name      NVARCHAR(255) NOT NULL,
    gender         TINYINT NULL,
    date_of_birth  DATE NULL,
    phone          VARCHAR(20) NULL,
    address        NVARCHAR(500) NULL,
    is_active      BIT NOT NULL DEFAULT 1,
    created_at     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_patients_user FOREIGN KEY (user_id) REFERENCES auth.users(id),
    CONSTRAINT UQ_patients_code UNIQUE (patient_code),
    CONSTRAINT CK_patients_gender CHECK (gender IS NULL OR gender BETWEEN 0 AND 2)
);
CREATE UNIQUE INDEX UX_patients_user ON scheduling.patients(user_id) WHERE user_id IS NOT NULL;
CREATE INDEX IX_patients_full_name ON scheduling.patients(full_name);
CREATE INDEX IX_patients_phone ON scheduling.patients(phone);
GO

CREATE TABLE scheduling.doctor_schedules (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    doctor_id     INT NOT NULL,
    day_of_week   TINYINT NOT NULL,
    start_time    TIME NOT NULL,
    end_time      TIME NOT NULL,
    slot_minutes  INT NOT NULL DEFAULT 30,
    is_active     BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_doctor_schedules_doctor FOREIGN KEY (doctor_id) REFERENCES scheduling.doctors(id),
    CONSTRAINT CK_doctor_schedules_day CHECK (day_of_week BETWEEN 0 AND 6),
    CONSTRAINT CK_doctor_schedules_time CHECK (end_time > start_time)
);
GO

CREATE TABLE scheduling.doctor_time_off (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    doctor_id   INT NULL,
    start_at    DATETIME2 NOT NULL,
    end_at      DATETIME2 NOT NULL,
    reason      NVARCHAR(255) NULL,
    CONSTRAINT FK_doctor_time_off_doctor FOREIGN KEY (doctor_id) REFERENCES scheduling.doctors(id),
    CONSTRAINT CK_doctor_time_off_range CHECK (end_at > start_at)
);
GO

CREATE TABLE scheduling.appointments (
    id                 INT IDENTITY(1,1) PRIMARY KEY,
    appointment_code   VARCHAR(20) NOT NULL,
    patient_id         INT NOT NULL,
    doctor_id          INT NOT NULL,
    start_time         DATETIME2 NOT NULL,
    end_time           DATETIME2 NOT NULL,
    status             TINYINT NOT NULL DEFAULT 0,
    queue_number       INT NULL,
    checked_in_at      DATETIME2 NULL,
    source             TINYINT NOT NULL DEFAULT 0,
    reason             NVARCHAR(500) NULL,
    created_by         INT NULL,
    created_at         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at         DATETIME2 NULL,
    CONSTRAINT FK_appointments_patient FOREIGN KEY (patient_id) REFERENCES scheduling.patients(id),
    CONSTRAINT FK_appointments_doctor FOREIGN KEY (doctor_id) REFERENCES scheduling.doctors(id),
    CONSTRAINT FK_appointments_created_by FOREIGN KEY (created_by) REFERENCES auth.users(id),
    CONSTRAINT UQ_appointments_code UNIQUE (appointment_code),
    CONSTRAINT CK_appointments_status CHECK (status BETWEEN 0 AND 5),
    CONSTRAINT CK_appointments_time CHECK (end_time > start_time)
);
-- Chan trung lich khit gio (status < 4 = chua Cancelled/khac; 4=Cancelled,5=NoShow van cho trung)
CREATE UNIQUE INDEX UX_appointments_doctor_slot
    ON scheduling.appointments(doctor_id, start_time)
    WHERE status < 4;
CREATE INDEX IX_appointments_patient ON scheduling.appointments(patient_id);
CREATE INDEX IX_appointments_start_time ON scheduling.appointments(start_time);
GO

CREATE TABLE scheduling.appointment_status_history (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    appointment_id  INT NOT NULL,
    from_status     TINYINT NULL,
    to_status       TINYINT NOT NULL,
    changed_by      INT NULL,
    reason          NVARCHAR(500) NULL,
    changed_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_appt_status_history_appt FOREIGN KEY (appointment_id) REFERENCES scheduling.appointments(id),
    CONSTRAINT FK_appt_status_history_user FOREIGN KEY (changed_by) REFERENCES auth.users(id)
);
GO

/* ============================================================
   6. CLINICAL SCHEMA
   (medical_records phu thuoc appointments; billing.services phu thuoc
    duoc tao truoc medical_record_services nen se tao billing.services
    o phan 7 truoc, do do CLINICAL duoc chia lam 2 khoi - xem ghi chu duoi)
   ============================================================ */
CREATE TABLE clinical.medical_records (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    appointment_id  INT NOT NULL,
    patient_id      INT NOT NULL,
    doctor_id       INT NOT NULL,
    symptoms        NVARCHAR(4000) NULL,
    diagnosis       NVARCHAR(4000) NULL,
    note            NVARCHAR(4000) NULL,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NULL,
    CONSTRAINT FK_medical_records_appt FOREIGN KEY (appointment_id) REFERENCES scheduling.appointments(id),
    CONSTRAINT FK_medical_records_patient FOREIGN KEY (patient_id) REFERENCES scheduling.patients(id),
    CONSTRAINT FK_medical_records_doctor FOREIGN KEY (doctor_id) REFERENCES scheduling.doctors(id),
    CONSTRAINT UQ_medical_records_appt UNIQUE (appointment_id)
);
GO

CREATE TABLE clinical.medicines (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    name         NVARCHAR(255) NOT NULL,
    unit         NVARCHAR(50) NULL,
    price        DECIMAL(12,2) NOT NULL DEFAULT 0,
    description  NVARCHAR(500) NULL,
    is_active    BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_medicines_price CHECK (price >= 0)
);
GO

CREATE TABLE clinical.medicine_batches (
    id            INT IDENTITY(1,1) PRIMARY KEY,
    medicine_id   INT NOT NULL,
    lot_number    VARCHAR(50) NOT NULL,
    expiry_date   DATE NOT NULL,
    quantity      INT NOT NULL DEFAULT 0,
    created_at    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_medicine_batches_medicine FOREIGN KEY (medicine_id) REFERENCES clinical.medicines(id),
    CONSTRAINT UQ_medicine_batches UNIQUE (medicine_id, lot_number),
    CONSTRAINT CK_medicine_batches_qty CHECK (quantity >= 0)
);
CREATE INDEX IX_medicine_batches_expiry ON clinical.medicine_batches(expiry_date);
GO

CREATE TABLE clinical.stock_transactions (
    id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    batch_id     INT NOT NULL,
    type         TINYINT NOT NULL,
    quantity     INT NOT NULL,
    ref_type     VARCHAR(50) NULL,
    ref_id       INT NULL,
    created_by   INT NOT NULL,
    created_at   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_stock_transactions_batch FOREIGN KEY (batch_id) REFERENCES clinical.medicine_batches(id),
    CONSTRAINT FK_stock_transactions_user FOREIGN KEY (created_by) REFERENCES auth.users(id),
    CONSTRAINT CK_stock_transactions_type CHECK (type BETWEEN 0 AND 3)
);
GO

CREATE TABLE clinical.prescriptions (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    medical_record_id   INT NOT NULL,
    note                NVARCHAR(500) NULL,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_prescriptions_record FOREIGN KEY (medical_record_id) REFERENCES clinical.medical_records(id),
    CONSTRAINT UQ_prescriptions_record UNIQUE (medical_record_id)
);
GO

CREATE TABLE clinical.prescription_items (
    id                INT IDENTITY(1,1) PRIMARY KEY,
    prescription_id   INT NOT NULL,
    medicine_id       INT NOT NULL,
    batch_id          INT NULL,
    quantity          INT NOT NULL DEFAULT 1,
    dosage            NVARCHAR(255) NULL,
    instruction       NVARCHAR(255) NULL,
    CONSTRAINT FK_prescription_items_presc FOREIGN KEY (prescription_id) REFERENCES clinical.prescriptions(id),
    CONSTRAINT FK_prescription_items_medicine FOREIGN KEY (medicine_id) REFERENCES clinical.medicines(id),
    CONSTRAINT FK_prescription_items_batch FOREIGN KEY (batch_id) REFERENCES clinical.medicine_batches(id),
    CONSTRAINT CK_prescription_items_qty CHECK (quantity > 0)
);
GO

/* ============================================================
   7. BILLING SCHEMA - services phai tao truoc de clinical.medical_record_services tham chieu
   ============================================================ */
CREATE TABLE billing.services (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    name         NVARCHAR(255) NOT NULL,
    description  NVARCHAR(500) NULL,
    price        DECIMAL(12,2) NOT NULL DEFAULT 0,
    is_active    BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_services_price CHECK (price >= 0)
);
GO

CREATE TABLE billing.invoices (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    invoice_no          VARCHAR(20) NOT NULL,
    patient_id          INT NOT NULL,
    appointment_id      INT NULL,
    medical_record_id   INT NULL,
    patient_name        NVARCHAR(255) NULL,
    total_amount        DECIMAL(12,2) NOT NULL DEFAULT 0,
    status              TINYINT NOT NULL DEFAULT 0,
    created_by          INT NULL,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_invoices_patient FOREIGN KEY (patient_id) REFERENCES scheduling.patients(id),
    CONSTRAINT FK_invoices_appt FOREIGN KEY (appointment_id) REFERENCES scheduling.appointments(id),
    CONSTRAINT FK_invoices_record FOREIGN KEY (medical_record_id) REFERENCES clinical.medical_records(id),
    CONSTRAINT FK_invoices_created_by FOREIGN KEY (created_by) REFERENCES auth.users(id),
    CONSTRAINT UQ_invoices_no UNIQUE (invoice_no),
    CONSTRAINT CK_invoices_status CHECK (status BETWEEN 0 AND 2),
    CONSTRAINT CK_invoices_total CHECK (total_amount >= 0)
);
CREATE INDEX IX_invoices_patient ON billing.invoices(patient_id);
CREATE INDEX IX_invoices_status_created ON billing.invoices(status, created_at);
GO

CREATE TABLE billing.invoice_items (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    invoice_id   INT NOT NULL,
    service_id   INT NULL,
    medicine_id  INT NULL,
    description  NVARCHAR(255) NULL,
    quantity     INT NOT NULL DEFAULT 1,
    unit_price   DECIMAL(12,2) NOT NULL DEFAULT 0,
    amount       DECIMAL(12,2) NOT NULL DEFAULT 0,
    CONSTRAINT FK_invoice_items_invoice FOREIGN KEY (invoice_id) REFERENCES billing.invoices(id),
    CONSTRAINT FK_invoice_items_service FOREIGN KEY (service_id) REFERENCES billing.services(id),
    CONSTRAINT FK_invoice_items_medicine FOREIGN KEY (medicine_id) REFERENCES clinical.medicines(id),
    CONSTRAINT CK_invoice_items_one_ref CHECK (
        (CASE WHEN service_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN medicine_id IS NULL THEN 0 ELSE 1 END) = 1
    ),
    CONSTRAINT CK_invoice_items_qty CHECK (quantity > 0 AND amount >= 0 AND unit_price >= 0)
);
GO

CREATE TABLE billing.payments (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    invoice_id   INT NOT NULL,
    amount       DECIMAL(12,2) NOT NULL,
    method       TINYINT NOT NULL DEFAULT 0,
    paid_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    note         NVARCHAR(255) NULL,
    CONSTRAINT FK_payments_invoice FOREIGN KEY (invoice_id) REFERENCES billing.invoices(id),
    CONSTRAINT CK_payments_amount CHECK (amount > 0),
    CONSTRAINT CK_payments_method CHECK (method BETWEEN 0 AND 3)
);
GO

/* ============================================================
   8. CLINICAL - phan con lai (phu thuoc billing.services)
   ============================================================ */
CREATE TABLE clinical.medical_record_services (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    medical_record_id   INT NOT NULL,
    service_id          INT NOT NULL,
    status              TINYINT NOT NULL DEFAULT 0,
    ordered_by          INT NOT NULL,
    ordered_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_mrs_record FOREIGN KEY (medical_record_id) REFERENCES clinical.medical_records(id),
    CONSTRAINT FK_mrs_service FOREIGN KEY (service_id) REFERENCES billing.services(id),
    CONSTRAINT FK_mrs_doctor FOREIGN KEY (ordered_by) REFERENCES scheduling.doctors(id),
    CONSTRAINT CK_mrs_status CHECK (status BETWEEN 0 AND 2)
);
GO

CREATE TABLE clinical.lab_results (
    id                          INT IDENTITY(1,1) PRIMARY KEY,
    medical_record_service_id  INT NOT NULL,
    result_summary              NVARCHAR(2000) NULL,
    conclusion                  NVARCHAR(1000) NULL,
    technician_id                INT NOT NULL,
    result_at                    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_lab_results_mrs FOREIGN KEY (medical_record_service_id) REFERENCES clinical.medical_record_services(id),
    CONSTRAINT FK_lab_results_technician FOREIGN KEY (technician_id) REFERENCES auth.users(id),
    CONSTRAINT UQ_lab_results_mrs UNIQUE (medical_record_service_id)
);
GO

CREATE TABLE clinical.attachments (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    owner_type   VARCHAR(50) NOT NULL,
    owner_id     INT NOT NULL,
    file_url     NVARCHAR(500) NOT NULL,
    uploaded_at  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_attachments_owner ON clinical.attachments(owner_type, owner_id);
GO

/* ============================================================
   9. SEED DATA - vai tro co ban
   ============================================================ */
IF NOT EXISTS (SELECT 1 FROM auth.roles)
BEGIN
    INSERT INTO auth.roles (name) VALUES
    ('Admin'), ('Receptionist'), ('Doctor'), ('Technician'), ('Patient');
END
GO

PRINT 'FoMedDb da duoc tao thanh cong voi day du bang, constraint, index va seed du lieu vai tro.';
