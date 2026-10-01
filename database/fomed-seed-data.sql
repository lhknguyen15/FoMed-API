/* ============================================================
   FoMed - Du lieu test (chay SAU fomed-create-database.sql)
   Mat khau cho TAT CA tai khoan test: Test@123
   (bcrypt hash that, sinh boi thu vien bcrypt - dang nhap duoc ngay qua API)
   An toan de chay lai: xoa du lieu cu (theo dung thu tu FK) truoc khi insert lai.
   ============================================================ */

USE FoMedDb;
GO

DECLARE @PwHash VARCHAR(255) = '$2b$12$bUZZ.gtZ7P4tDrd/gTbRneMXBGxrW5vMLeVpLwQW0CDOIQJxEAxA.';

/* ============================================================
   0. DON DEP DU LIEU CU (thu tu nguoc voi FK)
   ============================================================ */
DELETE FROM billing.payments;
DELETE FROM billing.invoice_items;
DELETE FROM billing.invoices;
DELETE FROM clinical.prescription_items;
DELETE FROM clinical.prescriptions;
DELETE FROM clinical.stock_transactions;
DELETE FROM clinical.medicine_batches;
DELETE FROM clinical.medicines;
DELETE FROM clinical.attachments;
DELETE FROM clinical.lab_results;
DELETE FROM clinical.medical_record_services;
DELETE FROM clinical.medical_records;
DELETE FROM billing.services;
DELETE FROM scheduling.appointment_status_history;
DELETE FROM scheduling.appointments;
DELETE FROM scheduling.doctor_time_off;
DELETE FROM scheduling.doctor_schedules;
DELETE FROM scheduling.patients;
DELETE FROM scheduling.doctors;
DELETE FROM scheduling.specialties;
DELETE FROM auth.refresh_tokens;
DELETE FROM auth.user_roles;
DELETE FROM auth.users;
GO

DECLARE @PwHash VARCHAR(255) = '$2b$12$bUZZ.gtZ7P4tDrd/gTbRneMXBGxrW5vMLeVpLwQW0CDOIQJxEAxA.';

/* ============================================================
   1. ROLES (co the da co tu buoc tao DB, chi insert neu thieu)
   ============================================================ */
IF NOT EXISTS (SELECT 1 FROM auth.roles)
    INSERT INTO auth.roles (name) VALUES ('Admin'), ('Receptionist'), ('Doctor'), ('Technician'), ('Pharmacist'), ('Patient');

DECLARE @RoleAdmin INT = (SELECT id FROM auth.roles WHERE name = 'Admin');
DECLARE @RoleReceptionist INT = (SELECT id FROM auth.roles WHERE name = 'Receptionist');
DECLARE @RoleDoctor INT = (SELECT id FROM auth.roles WHERE name = 'Doctor');
DECLARE @RoleTechnician INT = (SELECT id FROM auth.roles WHERE name = 'Technician');
DECLARE @RolePharmacist INT = (SELECT id FROM auth.roles WHERE name = 'Pharmacist');
DECLARE @RolePatient INT = (SELECT id FROM auth.roles WHERE name = 'Patient');

/* ============================================================
   2. USERS + USER_ROLES
   ============================================================ */
-- Admin
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('admin', @PwHash, 'admin@fomed.vn', N'Quản trị viên', '0900000001');
DECLARE @UserAdmin INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserAdmin, @RoleAdmin);

-- Le tan
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('letan01', @PwHash, 'letan01@fomed.vn', N'Nguyễn Thị Lễ Tân', '0900000002');
DECLARE @UserReceptionist INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserReceptionist, @RoleReceptionist);

-- Bac si (3 nguoi)
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('bs.hoa', @PwHash, 'bs.hoa@fomed.vn', N'BS. Trần Thị Hoa', '0900000011');
DECLARE @UserDoctor1 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserDoctor1, @RoleDoctor);

INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('bs.minh', @PwHash, 'bs.minh@fomed.vn', N'ThS.BS Lê Văn Minh', '0900000012');
DECLARE @UserDoctor2 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserDoctor2, @RoleDoctor);

INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('bs.an', @PwHash, 'bs.an@fomed.vn', N'BS. Phạm Văn An', '0900000013');
DECLARE @UserDoctor3 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserDoctor3, @RoleDoctor);

-- Ky thuat vien XN
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('ktv.linh', @PwHash, 'ktv.linh@fomed.vn', N'Đỗ Thị Linh', '0900000021');
DECLARE @UserTechnician1 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserTechnician1, @RoleTechnician);

-- Duoc si
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('duoc01', @PwHash, 'duoc01@fomed.vn', N'Dược sĩ FoMed', '0900000031');
DECLARE @UserPharmacist INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserPharmacist, @RolePharmacist);

-- Benh nhan co tai khoan (3 nguoi)
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('patient01', @PwHash, 'patient01@gmail.com', N'Nguyễn Văn Bình', '0912345001');
DECLARE @UserPatient1 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserPatient1, @RolePatient);

INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('patient02', @PwHash, 'patient02@gmail.com', N'Trần Thị Cẩm', '0912345002');
DECLARE @UserPatient2 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserPatient2, @RolePatient);

INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('patient03', @PwHash, 'patient03@gmail.com', N'Lê Hoàng Dũng', '0912345003');
DECLARE @UserPatient3 INT = SCOPE_IDENTITY();
INSERT INTO auth.user_roles (user_id, role_id) VALUES (@UserPatient3, @RolePatient);
GO

/* ============================================================
   3. SPECIALTIES
   ============================================================ */
INSERT INTO scheduling.specialties (name, description) VALUES
(N'Nội tổng quát', N'Khám và điều trị các bệnh lý nội khoa thông thường'),
(N'Da liễu', N'Khám và điều trị các bệnh về da'),
(N'Tai Mũi Họng', N'Khám và điều trị các bệnh về tai, mũi, họng');
GO

/* ============================================================
   4. DOCTORS
   ============================================================ */
DECLARE @UserDoctor1 INT = (SELECT id FROM auth.users WHERE username = 'bs.hoa');
DECLARE @UserDoctor2 INT = (SELECT id FROM auth.users WHERE username = 'bs.minh');
DECLARE @UserDoctor3 INT = (SELECT id FROM auth.users WHERE username = 'bs.an');
DECLARE @SpecNoiTongQuat INT = (SELECT id FROM scheduling.specialties WHERE name = N'Nội tổng quát');
DECLARE @SpecDaLieu INT = (SELECT id FROM scheduling.specialties WHERE name = N'Da liễu');
DECLARE @SpecTMH INT = (SELECT id FROM scheduling.specialties WHERE name = N'Tai Mũi Họng');

INSERT INTO scheduling.doctors (user_id, specialty_id, full_name, title, license_number, phone, room, consultation_fee)
VALUES (@UserDoctor1, @SpecNoiTongQuat, N'Trần Thị Hoa', N'BS', 'GPHN-2019-001', '0900000011', 'P.101', 150000);

INSERT INTO scheduling.doctors (user_id, specialty_id, full_name, title, license_number, phone, room, consultation_fee)
VALUES (@UserDoctor2, @SpecDaLieu, N'Lê Văn Minh', N'ThS.BS', 'GPHN-2017-045', '0900000012', 'P.102', 200000);

INSERT INTO scheduling.doctors (user_id, specialty_id, full_name, title, license_number, phone, room, consultation_fee)
VALUES (@UserDoctor3, @SpecTMH, N'Phạm Văn An', N'BS', 'GPHN-2020-078', '0900000013', 'P.103', 150000);
GO

/* ============================================================
   5. DOCTOR_SCHEDULES (Thu 2 - Thu 6, 08:00-11:30 va 13:30-17:00)
   ============================================================ */
DECLARE @Doc1 INT = (SELECT id FROM scheduling.doctors WHERE license_number = 'GPHN-2019-001');
DECLARE @Doc2 INT = (SELECT id FROM scheduling.doctors WHERE license_number = 'GPHN-2017-045');
DECLARE @Doc3 INT = (SELECT id FROM scheduling.doctors WHERE license_number = 'GPHN-2020-078');

INSERT INTO scheduling.doctor_schedules (doctor_id, day_of_week, start_time, end_time, slot_minutes)
SELECT d.id, dow.day_of_week, '08:00', '11:30', 30
FROM (VALUES (@Doc1), (@Doc2), (@Doc3)) AS d(id)
CROSS JOIN (VALUES (1),(2),(3),(4),(5)) AS dow(day_of_week);

INSERT INTO scheduling.doctor_schedules (doctor_id, day_of_week, start_time, end_time, slot_minutes)
SELECT d.id, dow.day_of_week, '13:30', '17:00', 30
FROM (VALUES (@Doc1), (@Doc2), (@Doc3)) AS d(id)
CROSS JOIN (VALUES (1),(2),(3),(4),(5)) AS dow(day_of_week);
GO

/* ============================================================
   6. PATIENTS (3 co tai khoan + 2 vang lai khong tai khoan)
   ============================================================ */
DECLARE @UserPatient1 INT = (SELECT id FROM auth.users WHERE username = 'patient01');
DECLARE @UserPatient2 INT = (SELECT id FROM auth.users WHERE username = 'patient02');
DECLARE @UserPatient3 INT = (SELECT id FROM auth.users WHERE username = 'patient03');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
VALUES (
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    @UserPatient1, N'Nguyễn Văn Bình', 0, '1990-05-12', '0912345001', N'12 Nguyễn Trãi, Q1, TP.HCM');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
VALUES (
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    @UserPatient2, N'Trần Thị Cẩm', 1, '1995-08-20', '0912345002', N'45 Lê Lợi, Q1, TP.HCM');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
VALUES (
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    @UserPatient3, N'Lê Hoàng Dũng', 0, '1988-01-30', '0912345003', N'78 Cách Mạng Tháng 8, Q3, TP.HCM');

-- Benh nhan vang lai, khong co tai khoan (user_id = NULL)
INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
VALUES (
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    NULL, N'Phan Thị Em', 1, '2001-11-02', '0912345004', N'23 Trần Hưng Đạo, Q5, TP.HCM');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
VALUES (
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    NULL, N'Võ Văn Phúc', 0, '1975-03-15', '0912345005', N'9 Điện Biên Phủ, Bình Thạnh, TP.HCM');
GO

/* ============================================================
   7. BILLING.SERVICES
   ============================================================ */
INSERT INTO billing.services (name, description, price) VALUES
(N'Khám tổng quát', N'Khám lâm sàng tổng quát', 150000),
(N'Xét nghiệm công thức máu', N'Xét nghiệm huyết học cơ bản', 120000),
(N'Siêu âm bụng tổng quát', N'Siêu âm ổ bụng', 250000),
(N'X-quang phổi thẳng', N'Chụp X-quang ngực thẳng', 180000),
(N'Xét nghiệm đường huyết', N'Đo nồng độ glucose máu', 60000);
GO

/* ============================================================
   8. MEDICINES + MEDICINE_BATCHES + STOCK_TRANSACTIONS (nhap kho ban dau)
   ============================================================ */
DECLARE @UserAdmin INT = (SELECT id FROM auth.users WHERE username = 'admin');

INSERT INTO clinical.medicines (name, unit, price, description) VALUES
(N'Paracetamol 500mg', N'viên', 1000, N'Hạ sốt, giảm đau'),
(N'Amoxicillin 500mg', N'viên', 2500, N'Kháng sinh nhóm beta-lactam'),
(N'Vitamin C 500mg', N'viên', 800, N'Bổ sung vitamin C'),
(N'Cetirizine 10mg', N'viên', 1500, N'Kháng histamin, trị dị ứng');

DECLARE @MedParacetamol INT = (SELECT id FROM clinical.medicines WHERE name = N'Paracetamol 500mg');
DECLARE @MedAmoxicillin INT = (SELECT id FROM clinical.medicines WHERE name = N'Amoxicillin 500mg');
DECLARE @MedVitaminC INT = (SELECT id FROM clinical.medicines WHERE name = N'Vitamin C 500mg');
DECLARE @MedCetirizine INT = (SELECT id FROM clinical.medicines WHERE name = N'Cetirizine 10mg');

INSERT INTO clinical.medicine_batches (medicine_id, lot_number, expiry_date, quantity) VALUES
(@MedParacetamol, 'LOT-PARA-2601', '2027-06-30', 5000),
(@MedAmoxicillin, 'LOT-AMOX-2601', '2026-12-31', 2000),
(@MedVitaminC, 'LOT-VITC-2601', '2027-03-31', 3000),
(@MedCetirizine, 'LOT-CETI-2601', '2027-01-31', 1500);

INSERT INTO clinical.stock_transactions (batch_id, type, quantity, ref_type, ref_id, created_by)
SELECT id, 0, quantity, 'initial_import', NULL, @UserAdmin
FROM clinical.medicine_batches;
GO

/* ============================================================
   9. APPOINTMENTS (nhieu trang thai khac nhau de test)
   ============================================================ */
DECLARE @UserReceptionist INT = (SELECT id FROM auth.users WHERE username = 'letan01');
DECLARE @UserPatient2 INT = (SELECT id FROM auth.users WHERE username = 'patient02');
DECLARE @Doc1 INT = (SELECT id FROM scheduling.doctors WHERE license_number = 'GPHN-2019-001');
DECLARE @Doc2 INT = (SELECT id FROM scheduling.doctors WHERE license_number = 'GPHN-2017-045');
DECLARE @Pat1 INT = (SELECT id FROM scheduling.patients WHERE full_name = N'Nguyễn Văn Bình');
DECLARE @Pat2 INT = (SELECT id FROM scheduling.patients WHERE full_name = N'Trần Thị Cẩm');
DECLARE @Pat3 INT = (SELECT id FROM scheduling.patients WHERE full_name = N'Lê Hoàng Dũng');
DECLARE @Pat4 INT = (SELECT id FROM scheduling.patients WHERE full_name = N'Phan Thị Em');

-- Lich #1: Da hoan tat (Completed) - hom qua, bac si Hoa kham cho Binh
INSERT INTO scheduling.appointments
(appointment_code, patient_id, doctor_id, start_time, end_time, status, queue_number, checked_in_at, source, reason, created_by)
VALUES (
    'LH' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_appointment_code AS VARCHAR), 6),
    @Pat1, @Doc1,
    DATEADD(HOUR, 8, CAST(CAST(GETUTCDATE()-1 AS DATE) AS DATETIME2)),
    DATEADD(HOUR, 8, DATEADD(MINUTE, 30, CAST(CAST(GETUTCDATE()-1 AS DATE) AS DATETIME2))),
    3, 1,
    DATEADD(HOUR, 8, CAST(CAST(GETUTCDATE()-1 AS DATE) AS DATETIME2)),
    0, N'Sốt, ho, đau họng 3 ngày', @UserReceptionist);
DECLARE @Appt1 INT = SCOPE_IDENTITY();

INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
VALUES (@Appt1, NULL, 0, @UserReceptionist, N'Tạo lịch hẹn'),
       (@Appt1, 0, 2, @UserReceptionist, N'Bệnh nhân check-in'),
       (@Appt1, 2, 3, NULL, N'Hoàn tất khám');

-- Lich #2: Da xac nhan, chua den (Confirmed) - ngay mai, bac si Minh kham cho Cam
INSERT INTO scheduling.appointments
(appointment_code, patient_id, doctor_id, start_time, end_time, status, source, reason, created_by)
VALUES (
    'LH' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_appointment_code AS VARCHAR), 6),
    @Pat2, @Doc2,
    DATEADD(HOUR, 9, CAST(CAST(GETUTCDATE()+1 AS DATE) AS DATETIME2)),
    DATEADD(HOUR, 9, DATEADD(MINUTE, 30, CAST(CAST(GETUTCDATE()+1 AS DATE) AS DATETIME2))),
    1, 0, N'Nổi mẩn ngứa vùng tay', @UserPatient2);
DECLARE @Appt2 INT = SCOPE_IDENTITY();

INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
VALUES (@Appt2, NULL, 0, @UserPatient2, N'Bệnh nhân đặt lịch online'),
       (@Appt2, 0, 1, @UserReceptionist, N'Lễ tân xác nhận lịch');

-- Lich #3: Dang cho kham (CheckedIn) - hom nay, bac si Hoa kham cho Dung
INSERT INTO scheduling.appointments
(appointment_code, patient_id, doctor_id, start_time, end_time, status, queue_number, checked_in_at, source, reason, created_by)
VALUES (
    'LH' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_appointment_code AS VARCHAR), 6),
    @Pat3, @Doc1,
    DATEADD(HOUR, 9, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME2)),
    DATEADD(HOUR, 9, DATEADD(MINUTE, 30, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME2))),
    2, 2, SYSUTCDATETIME(), 1, N'Đau bụng âm ỉ 2 ngày', @UserReceptionist);
DECLARE @Appt3 INT = SCOPE_IDENTITY();

INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
VALUES (@Appt3, NULL, 0, @UserReceptionist, N'Lễ tân tạo lịch trực tiếp'),
       (@Appt3, 0, 2, @UserReceptionist, N'Bệnh nhân check-in');

-- Lich #4: Da huy (Cancelled) - benh nhan vang lai
INSERT INTO scheduling.appointments
(appointment_code, patient_id, doctor_id, start_time, end_time, status, source, reason, created_by)
VALUES (
    'LH' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_appointment_code AS VARCHAR), 6),
    @Pat4, @Doc2,
    DATEADD(HOUR, 14, CAST(CAST(GETUTCDATE()+2 AS DATE) AS DATETIME2)),
    DATEADD(HOUR, 14, DATEADD(MINUTE, 30, CAST(CAST(GETUTCDATE()+2 AS DATE) AS DATETIME2))),
    4, 1, N'Khám da liễu định kỳ', @UserReceptionist);
DECLARE @Appt4 INT = SCOPE_IDENTITY();

INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
VALUES (@Appt4, NULL, 0, @UserReceptionist, N'Tạo lịch hẹn'),
       (@Appt4, 0, 4, @UserReceptionist, N'Bệnh nhân gọi điện báo bận, hẹn lại sau');
GO

/* ============================================================
   10. MEDICAL_RECORDS + MEDICAL_RECORD_SERVICES + LAB_RESULTS
   (cho lich #1 - da Completed)
   ============================================================ */
DECLARE @Appt1 INT = (SELECT id FROM scheduling.appointments WHERE status = 3);
DECLARE @Pat1 INT = (SELECT patient_id FROM scheduling.appointments WHERE id = @Appt1);
DECLARE @Doc1 INT = (SELECT doctor_id FROM scheduling.appointments WHERE id = @Appt1);
DECLARE @UserTechnician1 INT = (SELECT id FROM auth.users WHERE username = 'ktv.linh');
DECLARE @SvcXetNghiemMau INT = (SELECT id FROM billing.services WHERE name = N'Xét nghiệm công thức máu');
DECLARE @SvcKhamTongQuat INT = (SELECT id FROM billing.services WHERE name = N'Khám tổng quát');

INSERT INTO clinical.medical_records (appointment_id, patient_id, doctor_id, symptoms, diagnosis, note)
VALUES (@Appt1, @Pat1, @Doc1,
    N'Sốt 38.5 độ, ho khan, đau rát họng, mệt mỏi',
    N'Viêm họng cấp do virus',
    N'Nghỉ ngơi, uống nhiều nước, tái khám nếu sốt kéo dài quá 3 ngày');
DECLARE @Record1 INT = SCOPE_IDENTITY();

-- Chi dinh xet nghiem trong luot kham
INSERT INTO clinical.medical_record_services (medical_record_id, service_id, status, ordered_by)
VALUES (@Record1, @SvcXetNghiemMau, 2, @Doc1);
DECLARE @MRS1 INT = SCOPE_IDENTITY();

INSERT INTO clinical.lab_results (medical_record_service_id, result_summary, conclusion, technician_id)
VALUES (@MRS1,
    N'Bạch cầu: 7.200/mm3 (bình thường), Hồng cầu: 4.8 triệu/mm3, Hct: 42%',
    N'Chỉ số trong giới hạn bình thường, không có dấu hiệu nhiễm trùng nặng',
    @UserTechnician1);
GO

/* ============================================================
   11. PRESCRIPTIONS + PRESCRIPTION_ITEMS (cho lich #1)
   ============================================================ */
DECLARE @Record1 INT = (SELECT id FROM clinical.medical_records);
DECLARE @MedParacetamol INT = (SELECT id FROM clinical.medicines WHERE name = N'Paracetamol 500mg');
DECLARE @MedVitaminC INT = (SELECT id FROM clinical.medicines WHERE name = N'Vitamin C 500mg');
DECLARE @BatchParacetamol INT = (SELECT id FROM clinical.medicine_batches WHERE lot_number = 'LOT-PARA-2601');
DECLARE @BatchVitaminC INT = (SELECT id FROM clinical.medicine_batches WHERE lot_number = 'LOT-VITC-2601');
DECLARE @UserAdmin INT = (SELECT id FROM auth.users WHERE username = 'admin');

INSERT INTO clinical.prescriptions (medical_record_id, note)
VALUES (@Record1, N'Uống thuốc sau ăn, tái khám nếu không đỡ sau 3 ngày');
DECLARE @Presc1 INT = SCOPE_IDENTITY();

INSERT INTO clinical.prescription_items (prescription_id, medicine_id, batch_id, quantity, dosage, instruction)
VALUES
(@Presc1, @MedParacetamol, @BatchParacetamol, 10, N'Sáng 1 - Chiều 1', N'Uống sau ăn khi sốt trên 38.5 độ'),
(@Presc1, @MedVitaminC, @BatchVitaminC, 10, N'Sáng 1', N'Uống sau ăn sáng');

-- Tru kho theo don thuoc da xuat
INSERT INTO clinical.stock_transactions (batch_id, type, quantity, ref_type, ref_id, created_by)
VALUES
(@BatchParacetamol, 1, -10, 'prescription', @Presc1, @UserAdmin),
(@BatchVitaminC, 1, -10, 'prescription', @Presc1, @UserAdmin);

UPDATE clinical.medicine_batches SET quantity = quantity - 10 WHERE id = @BatchParacetamol;
UPDATE clinical.medicine_batches SET quantity = quantity - 10 WHERE id = @BatchVitaminC;
GO

/* ============================================================
   12. INVOICES + INVOICE_ITEMS + PAYMENTS (cho lich #1 - da thanh toan du)
   ============================================================ */
DECLARE @Record1 INT = (SELECT id FROM clinical.medical_records);
DECLARE @Appt1 INT = (SELECT appointment_id FROM clinical.medical_records WHERE id = @Record1);
DECLARE @Pat1 INT = (SELECT patient_id FROM clinical.medical_records WHERE id = @Record1);
DECLARE @UserReceptionist INT = (SELECT id FROM auth.users WHERE username = 'letan01');
DECLARE @SvcKhamTongQuat INT = (SELECT id FROM billing.services WHERE name = N'Khám tổng quát');
DECLARE @SvcXetNghiemMau INT = (SELECT id FROM billing.services WHERE name = N'Xét nghiệm công thức máu');
DECLARE @MedParacetamol INT = (SELECT id FROM clinical.medicines WHERE name = N'Paracetamol 500mg');
DECLARE @MedVitaminC INT = (SELECT id FROM clinical.medicines WHERE name = N'Vitamin C 500mg');
DECLARE @PatientName NVARCHAR(255) = (SELECT full_name FROM scheduling.patients WHERE id = @Pat1);

INSERT INTO billing.invoices (invoice_no, patient_id, appointment_id, medical_record_id, patient_name, total_amount, status, created_by)
VALUES (
    'HD' + RIGHT('000000' + CAST(NEXT VALUE FOR billing.seq_invoice_no AS VARCHAR), 6),
    @Pat1, @Appt1, @Record1, @PatientName,
    150000 + 120000 + (10*1000) + (10*800), -- kham + xet nghiem + thuoc
    2, -- Paid
    @UserReceptionist);
DECLARE @Invoice1 INT = SCOPE_IDENTITY();

INSERT INTO billing.invoice_items (invoice_id, service_id, description, quantity, unit_price, amount) VALUES
(@Invoice1, @SvcKhamTongQuat, N'Khám tổng quát', 1, 150000, 150000),
(@Invoice1, @SvcXetNghiemMau, N'Xét nghiệm công thức máu', 1, 120000, 120000);

INSERT INTO billing.invoice_items (invoice_id, medicine_id, description, quantity, unit_price, amount) VALUES
(@Invoice1, @MedParacetamol, N'Paracetamol 500mg', 10, 1000, 10000),
(@Invoice1, @MedVitaminC, N'Vitamin C 500mg', 10, 800, 8000);

INSERT INTO billing.payments (invoice_id, amount, method, note)
VALUES (@Invoice1, 288000, 0, N'Thanh toán tiền mặt tại quầy');

-- Hoa don thu 2: benh nhan Cam - chua thanh toan het (test PartiallyPaid)
DECLARE @Pat2 INT = (SELECT id FROM scheduling.patients WHERE full_name = N'Trần Thị Cẩm');
DECLARE @PatientName2 NVARCHAR(255) = N'Trần Thị Cẩm';

INSERT INTO billing.invoices (invoice_no, patient_id, patient_name, total_amount, status, created_by)
VALUES (
    'HD' + RIGHT('000000' + CAST(NEXT VALUE FOR billing.seq_invoice_no AS VARCHAR), 6),
    @Pat2, @PatientName2, 250000, 1, @UserReceptionist); -- PartiallyPaid
DECLARE @Invoice2 INT = SCOPE_IDENTITY();

INSERT INTO billing.invoice_items (invoice_id, service_id, description, quantity, unit_price, amount)
VALUES (@Invoice2, (SELECT id FROM billing.services WHERE name = N'Siêu âm bụng tổng quát'), N'Siêu âm bụng tổng quát', 1, 250000, 250000);

INSERT INTO billing.payments (invoice_id, amount, method, note)
VALUES (@Invoice2, 100000, 0, N'Đặt cọc trước, thanh toán phần còn lại khi lấy kết quả');
GO

PRINT 'Da them du lieu test cho FoMedDb thanh cong.';
PRINT 'Dang nhap voi mat khau chung: Test@123';
PRINT 'Tai khoan: admin, letan01, bs.hoa, bs.minh, bs.an, ktv.linh, patient01, patient02, patient03';
