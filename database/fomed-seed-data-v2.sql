/* ============================================================
   FoMed - Du lieu test v2 (THAY THE fomed-seed-data.sql cu, khong chay chung)
   Chay sau fomed-create-database.sql. Mat khau chung: Test@123
   Script tu don dep du lieu cu truoc khi insert - chay lai bao nhieu lan cung an toan.

   Noi dung mo rong so voi v1:
   - Them 2 chuyen khoa, 2 bac si (tong 5 bac si)
   - Them nhieu benh nhan (tong ~18: 12 co tai khoan + 6 vang lai)
   - Them dich vu (tong 8), thuoc (tong 10)
   - Sinh HANG LOAT lich hen bang tap-based generation (~15 ngay lam viec
     truoc + 5 ngay toi, 5 bac si, 3 khung gio/ngay) thay vi go tay tung dong
   - Tu dong sinh benh an, chi dinh XN, ket qua XN, don thuoc, hoa don,
     thanh toan cho cac lich hen da Completed
   ============================================================ */

USE FoMedDb;
GO

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

/* ============================================================
   1. ROLES
   ============================================================ */
IF NOT EXISTS (SELECT 1 FROM auth.roles)
    INSERT INTO auth.roles (name) VALUES ('Admin'), ('Receptionist'), ('Doctor'), ('Technician'), ('Pharmacist'), ('Patient');
GO

/* ============================================================
   2. USERS + USER_ROLES
   ============================================================ */
DECLARE @PwHash VARCHAR(255) = '$2b$12$bUZZ.gtZ7P4tDrd/gTbRneMXBGxrW5vMLeVpLwQW0CDOIQJxEAxA.';
DECLARE @RoleAdmin INT = (SELECT id FROM auth.roles WHERE name = 'Admin');
DECLARE @RoleReceptionist INT = (SELECT id FROM auth.roles WHERE name = 'Receptionist');
DECLARE @RoleDoctor INT = (SELECT id FROM auth.roles WHERE name = 'Doctor');
DECLARE @RoleTechnician INT = (SELECT id FROM auth.roles WHERE name = 'Technician');
DECLARE @RolePharmacist INT = (SELECT id FROM auth.roles WHERE name = 'Pharmacist');
DECLARE @RolePatient INT = (SELECT id FROM auth.roles WHERE name = 'Patient');

-- Admin + 2 le tan
INSERT INTO auth.users (username, password_hash, email, full_name, phone) VALUES
('admin', @PwHash, 'admin@fomed.vn', N'Quản trị viên', '0900000001'),
('letan01', @PwHash, 'letan01@fomed.vn', N'Nguyễn Thị Lễ Tân', '0900000002'),
('letan02', @PwHash, 'letan02@fomed.vn', N'Huỳnh Thị Kim Ngân', '0900000003');

-- 5 bac si
INSERT INTO auth.users (username, password_hash, email, full_name, phone) VALUES
('bs.hoa', @PwHash, 'bs.hoa@fomed.vn', N'BS. Trần Thị Hoa', '0900000011'),
('bs.minh', @PwHash, 'bs.minh@fomed.vn', N'ThS.BS Lê Văn Minh', '0900000012'),
('bs.an', @PwHash, 'bs.an@fomed.vn', N'BS. Phạm Văn An', '0900000013'),
('bs.trang', @PwHash, 'bs.trang@fomed.vn', N'BS. Võ Thị Trang', '0900000014'),
('bs.tuan', @PwHash, 'bs.tuan@fomed.vn', N'ThS.BS Ngô Anh Tuấn', '0900000015');

-- 2 ky thuat vien
INSERT INTO auth.users (username, password_hash, email, full_name, phone) VALUES
('ktv.linh', @PwHash, 'ktv.linh@fomed.vn', N'Đỗ Thị Linh', '0900000021'),
('ktv.hai', @PwHash, 'ktv.hai@fomed.vn', N'Bùi Văn Hải', '0900000022');

-- 12 benh nhan co tai khoan
INSERT INTO auth.users (username, password_hash, email, full_name, phone) VALUES
('patient01', @PwHash, 'patient01@gmail.com', N'Nguyễn Văn Bình', '0912345001'),
('patient02', @PwHash, 'patient02@gmail.com', N'Trần Thị Cẩm', '0912345002'),
('patient03', @PwHash, 'patient03@gmail.com', N'Lê Hoàng Dũng', '0912345003'),
('patient04', @PwHash, 'patient04@gmail.com', N'Phạm Thị Hồng', '0912345004'),
('patient05', @PwHash, 'patient05@gmail.com', N'Đặng Văn Khoa', '0912345005'),
('patient06', @PwHash, 'patient06@gmail.com', N'Vũ Thị Lan', '0912345006'),
('patient07', @PwHash, 'patient07@gmail.com', N'Hoàng Văn Nam', '0912345007'),
('patient08', @PwHash, 'patient08@gmail.com', N'Ngô Thị Oanh', '0912345008'),
('patient09', @PwHash, 'patient09@gmail.com', N'Bùi Văn Phong', '0912345009'),
('patient10', @PwHash, 'patient10@gmail.com', N'Đỗ Thị Quỳnh', '0912345010'),
('patient11', @PwHash, 'patient11@gmail.com', N'Trịnh Văn Sơn', '0912345011'),
('patient12', @PwHash, 'patient12@gmail.com', N'Lý Thị Thu', '0912345012');

-- Gan role theo tung nhom user vua tao
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RoleAdmin FROM auth.users WHERE username = 'admin';
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RoleReceptionist FROM auth.users WHERE username IN ('letan01', 'letan02');
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RoleDoctor FROM auth.users WHERE username IN ('bs.hoa', 'bs.minh', 'bs.an', 'bs.trang', 'bs.tuan');
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RoleTechnician FROM auth.users WHERE username IN ('ktv.linh', 'ktv.hai');
INSERT INTO auth.users (username, password_hash, email, full_name, phone)
VALUES ('duoc01', @PwHash, 'duoc01@fomed.vn', N'Dược sĩ FoMed', '0900000031');
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RolePharmacist FROM auth.users WHERE username = 'duoc01';
INSERT INTO auth.user_roles (user_id, role_id)
SELECT id, @RolePatient FROM auth.users WHERE username LIKE 'patient%';
GO

/* ============================================================
   3. SPECIALTIES (5)
   ============================================================ */
INSERT INTO scheduling.specialties (name, description) VALUES
(N'Nội tổng quát', N'Khám và điều trị các bệnh lý nội khoa thông thường'),
(N'Da liễu', N'Khám và điều trị các bệnh về da'),
(N'Tai Mũi Họng', N'Khám và điều trị các bệnh về tai, mũi, họng'),
(N'Nhi khoa', N'Khám và điều trị bệnh cho trẻ em'),
(N'Tim mạch', N'Khám và điều trị các bệnh lý tim mạch');
GO

/* ============================================================
   4. DOCTORS (5) + DOCTOR_SCHEDULES
   ============================================================ */
DECLARE @UserDoctor1 INT = (SELECT id FROM auth.users WHERE username = 'bs.hoa');
DECLARE @UserDoctor2 INT = (SELECT id FROM auth.users WHERE username = 'bs.minh');
DECLARE @UserDoctor3 INT = (SELECT id FROM auth.users WHERE username = 'bs.an');
DECLARE @UserDoctor4 INT = (SELECT id FROM auth.users WHERE username = 'bs.trang');
DECLARE @UserDoctor5 INT = (SELECT id FROM auth.users WHERE username = 'bs.tuan');
DECLARE @SpecNoiTongQuat INT = (SELECT id FROM scheduling.specialties WHERE name = N'Nội tổng quát');
DECLARE @SpecDaLieu INT = (SELECT id FROM scheduling.specialties WHERE name = N'Da liễu');
DECLARE @SpecTMH INT = (SELECT id FROM scheduling.specialties WHERE name = N'Tai Mũi Họng');
DECLARE @SpecNhi INT = (SELECT id FROM scheduling.specialties WHERE name = N'Nhi khoa');
DECLARE @SpecTimMach INT = (SELECT id FROM scheduling.specialties WHERE name = N'Tim mạch');

INSERT INTO scheduling.doctors (user_id, specialty_id, full_name, title, license_number, phone, room, consultation_fee) VALUES
(@UserDoctor1, @SpecNoiTongQuat, N'Trần Thị Hoa', N'BS', 'GPHN-2019-001', '0900000011', 'P.101', 150000),
(@UserDoctor2, @SpecDaLieu, N'Lê Văn Minh', N'ThS.BS', 'GPHN-2017-045', '0900000012', 'P.102', 200000),
(@UserDoctor3, @SpecTMH, N'Phạm Văn An', N'BS', 'GPHN-2020-078', '0900000013', 'P.103', 150000),
(@UserDoctor4, @SpecNhi, N'Võ Thị Trang', N'BS', 'GPHN-2021-102', '0900000014', 'P.104', 160000),
(@UserDoctor5, @SpecTimMach, N'Ngô Anh Tuấn', N'ThS.BS', 'GPHN-2016-033', '0900000015', 'P.105', 250000);

INSERT INTO scheduling.doctor_schedules (doctor_id, day_of_week, start_time, end_time, slot_minutes)
SELECT d.id, dow.day_of_week, '08:00', '11:30', 30
FROM scheduling.doctors d CROSS JOIN (VALUES (1),(2),(3),(4),(5)) AS dow(day_of_week);

INSERT INTO scheduling.doctor_schedules (doctor_id, day_of_week, start_time, end_time, slot_minutes)
SELECT d.id, dow.day_of_week, '13:30', '17:00', 30
FROM scheduling.doctors d CROSS JOIN (VALUES (1),(2),(3),(4),(5)) AS dow(day_of_week);
GO

/* ============================================================
   5. PATIENTS (12 co tai khoan + 6 vang lai = 18)
   ============================================================ */
DECLARE @p TABLE (username VARCHAR(50), full_name NVARCHAR(255), gender TINYINT, dob DATE, phone VARCHAR(20), address NVARCHAR(500));
INSERT INTO @p VALUES
('patient01', N'Nguyễn Văn Bình', 0, '1990-05-12', '0912345001', N'12 Nguyễn Trãi, Q1, TP.HCM'),
('patient02', N'Trần Thị Cẩm', 1, '1995-08-20', '0912345002', N'45 Lê Lợi, Q1, TP.HCM'),
('patient03', N'Lê Hoàng Dũng', 0, '1988-01-30', '0912345003', N'78 Cách Mạng Tháng 8, Q3, TP.HCM'),
('patient04', N'Phạm Thị Hồng', 1, '1992-03-14', '0912345004', N'34 Nguyễn Đình Chiểu, Q3, TP.HCM'),
('patient05', N'Đặng Văn Khoa', 0, '1985-11-02', '0912345005', N'56 Võ Văn Tần, Q3, TP.HCM'),
('patient06', N'Vũ Thị Lan', 1, '1998-07-19', '0912345006', N'89 Hai Bà Trưng, Q1, TP.HCM'),
('patient07', N'Hoàng Văn Nam', 0, '1979-09-25', '0912345007', N'11 Pasteur, Q3, TP.HCM'),
('patient08', N'Ngô Thị Oanh', 1, '2000-02-08', '0912345008', N'67 Nam Kỳ Khởi Nghĩa, Q1, TP.HCM'),
('patient09', N'Bùi Văn Phong', 0, '1993-12-16', '0912345009', N'23 Lý Tự Trọng, Q1, TP.HCM'),
('patient10', N'Đỗ Thị Quỳnh', 1, '1987-04-05', '0912345010', N'90 Điện Biên Phủ, Bình Thạnh, TP.HCM'),
('patient11', N'Trịnh Văn Sơn', 0, '1996-06-27', '0912345011', N'15 Phan Xích Long, Phú Nhuận, TP.HCM'),
('patient12', N'Lý Thị Thu', 1, '1991-10-11', '0912345012', N'48 Trường Sa, Bình Thạnh, TP.HCM');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
SELECT
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    u.id, p.full_name, p.gender, p.dob, p.phone, p.address
FROM @p p
JOIN auth.users u ON u.username = p.username;

-- 6 benh nhan vang lai (khong tai khoan)
DECLARE @w TABLE (full_name NVARCHAR(255), gender TINYINT, dob DATE, phone VARCHAR(20), address NVARCHAR(500));
INSERT INTO @w VALUES
(N'Phan Thị Em', 1, '2001-11-02', '0912345101', N'23 Trần Hưng Đạo, Q5, TP.HCM'),
(N'Võ Văn Phúc', 0, '1975-03-15', '0912345102', N'9 Điện Biên Phủ, Bình Thạnh, TP.HCM'),
(N'Trương Thị Giang', 1, '1983-05-22', '0912345103', N'102 Cống Quỳnh, Q1, TP.HCM'),
(N'Lâm Văn Hùng', 0, '1969-08-30', '0912345104', N'55 Nguyễn Thị Minh Khai, Q1, TP.HCM'),
(N'Đinh Thị Kim', 1, '1994-01-18', '0912345105', N'71 Lê Văn Sỹ, Q3, TP.HCM'),
(N'Mai Văn Long', 0, '2005-09-09', '0912345106', N'33 Nguyễn Văn Cừ, Q5, TP.HCM');

INSERT INTO scheduling.patients (patient_code, user_id, full_name, gender, date_of_birth, phone, address)
SELECT
    'BN' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_patient_code AS VARCHAR), 6),
    NULL, full_name, gender, dob, phone, address
FROM @w;
GO

/* ============================================================
   6. BILLING.SERVICES (8)
   ============================================================ */
INSERT INTO billing.services (name, description, price) VALUES
(N'Khám tổng quát', N'Khám lâm sàng tổng quát', 150000),
(N'Xét nghiệm công thức máu', N'Xét nghiệm huyết học cơ bản', 120000),
(N'Siêu âm bụng tổng quát', N'Siêu âm ổ bụng', 250000),
(N'X-quang phổi thẳng', N'Chụp X-quang ngực thẳng', 180000),
(N'Xét nghiệm đường huyết', N'Đo nồng độ glucose máu', 60000),
(N'Nội soi dạ dày', N'Nội soi thực quản - dạ dày - tá tràng', 450000),
(N'Điện tâm đồ ECG', N'Đo điện tâm đồ 12 chuyển đạo', 90000),
(N'Xét nghiệm nước tiểu', N'Tổng phân tích nước tiểu', 50000);
GO

/* ============================================================
   7. MEDICINES (10) + BATCHES + NHAP KHO BAN DAU
   ============================================================ */
DECLARE @UserAdmin INT = (SELECT id FROM auth.users WHERE username = 'admin');

INSERT INTO clinical.medicines (name, unit, price, description) VALUES
(N'Paracetamol 500mg', N'viên', 1000, N'Hạ sốt, giảm đau'),
(N'Amoxicillin 500mg', N'viên', 2500, N'Kháng sinh nhóm beta-lactam'),
(N'Vitamin C 500mg', N'viên', 800, N'Bổ sung vitamin C'),
(N'Cetirizine 10mg', N'viên', 1500, N'Kháng histamin, trị dị ứng'),
(N'Ibuprofen 400mg', N'viên', 1200, N'Giảm đau, kháng viêm'),
(N'Omeprazole 20mg', N'viên', 2000, N'Điều trị viêm loét dạ dày'),
(N'Loratadine 10mg', N'viên', 1300, N'Kháng histamin thế hệ 2'),
(N'Ambroxol 30mg', N'viên', 900, N'Long đờm, trị ho có đờm'),
(N'Berberin 10mg', N'viên', 500, N'Điều trị tiêu chảy, rối loạn tiêu hóa'),
(N'Oresol', N'gói', 3000, N'Bù nước điện giải');

INSERT INTO clinical.medicine_batches (medicine_id, lot_number, expiry_date, quantity)
SELECT id, 'LOT-' + CAST(id AS VARCHAR) + '-2601', DATEADD(MONTH, 12 + id, GETUTCDATE()), 3000
FROM clinical.medicines;

INSERT INTO clinical.stock_transactions (batch_id, type, quantity, ref_type, ref_id, created_by)
SELECT id, 0, quantity, 'initial_import', NULL, @UserAdmin
FROM clinical.medicine_batches;
GO

/* ============================================================
   8. SINH HANG LOAT LICH HEN (15 ngay lam viec qua khu + 5 ngay toi)
   5 bac si x 3 khung gio/ngay
   ============================================================ */
DECLARE @UserReceptionist INT = (SELECT id FROM auth.users WHERE username = 'letan01');

IF OBJECT_ID('tempdb..#DoctorList') IS NOT NULL DROP TABLE #DoctorList;
SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS rn INTO #DoctorList FROM scheduling.doctors;

IF OBJECT_ID('tempdb..#PatientList') IS NOT NULL DROP TABLE #PatientList;
SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS rn INTO #PatientList FROM scheduling.patients;
DECLARE @PatientCount INT = (SELECT COUNT(*) FROM #PatientList);

IF OBJECT_ID('tempdb..#DayOffsets') IS NOT NULL DROP TABLE #DayOffsets;
SELECT TOP 25 (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 20) AS day_offset
INTO #DayOffsets
FROM sys.all_objects; -- sinh day_offset tu -19 den +5

IF OBJECT_ID('tempdb..#Slots') IS NOT NULL DROP TABLE #Slots;
SELECT * INTO #Slots FROM (VALUES (8,0),(9,30),(14,0)) AS s(hh, mm);

IF OBJECT_ID('tempdb..#Combo') IS NOT NULL DROP TABLE #Combo;
SELECT
    d.day_offset,
    doc.id AS doctor_id,
    s.hh, s.mm,
    ROW_NUMBER() OVER (ORDER BY d.day_offset, doc.id, s.hh, s.mm) AS overall_rn,
    ROW_NUMBER() OVER (PARTITION BY doc.id, d.day_offset ORDER BY s.hh, s.mm) AS queue_no
INTO #Combo
FROM #DayOffsets d
CROSS JOIN #DoctorList doc
CROSS JOIN #Slots s
WHERE DATENAME(WEEKDAY, DATEADD(DAY, d.day_offset, CAST(GETUTCDATE() AS DATE))) NOT IN ('Saturday', 'Sunday');

INSERT INTO scheduling.appointments
(appointment_code, patient_id, doctor_id, start_time, end_time, status, queue_number, checked_in_at, source, reason, created_by)
SELECT
    'LH' + RIGHT('000000' + CAST(NEXT VALUE FOR scheduling.seq_appointment_code AS VARCHAR), 6),
    p.id,
    c.doctor_id,
    DATEADD(MINUTE, c.mm, DATEADD(HOUR, c.hh, DATEADD(DAY, c.day_offset, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME2)))),
    DATEADD(MINUTE, 30, DATEADD(MINUTE, c.mm, DATEADD(HOUR, c.hh, DATEADD(DAY, c.day_offset, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME2))))),
    CASE
        WHEN c.day_offset < 0 AND c.overall_rn % 12 = 0 THEN 4  -- Cancelled
        WHEN c.day_offset < 0 AND c.overall_rn % 17 = 0 THEN 5  -- NoShow
        WHEN c.day_offset < 0 THEN 3                            -- Completed
        WHEN c.day_offset = 0 THEN 2                            -- CheckedIn
        WHEN c.overall_rn % 5 = 0 THEN 0                        -- Pending
        ELSE 1                                                  -- Confirmed
    END,
    CASE WHEN c.day_offset <= 0 THEN c.queue_no ELSE NULL END,
    CASE WHEN c.day_offset <= 0
         THEN DATEADD(MINUTE, c.mm, DATEADD(HOUR, c.hh, DATEADD(DAY, c.day_offset, CAST(CAST(GETUTCDATE() AS DATE) AS DATETIME2))))
         ELSE NULL END,
    CASE WHEN c.overall_rn % 3 = 0 THEN 1 ELSE 0 END,
    N'Khám theo triệu chứng / tái khám định kỳ',
    @UserReceptionist
FROM #Combo c
JOIN #PatientList p ON p.rn = ((c.overall_rn - 1) % @PatientCount) + 1;

-- Ghi lich su trang thai co ban cho toan bo lich hen vua sinh
INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
SELECT id, NULL, 0, @UserReceptionist, N'Tạo lịch hẹn'
FROM scheduling.appointments;

INSERT INTO scheduling.appointment_status_history (appointment_id, from_status, to_status, changed_by, reason)
SELECT id, 0, status, @UserReceptionist,
    CASE status
        WHEN 1 THEN N'Lễ tân xác nhận lịch'
        WHEN 2 THEN N'Bệnh nhân check-in'
        WHEN 3 THEN N'Hoàn tất khám'
        WHEN 4 THEN N'Bệnh nhân báo hủy'
        WHEN 5 THEN N'Bệnh nhân không đến'
        ELSE N'Cập nhật trạng thái'
    END
FROM scheduling.appointments
WHERE status <> 0;
GO

/* ============================================================
   9. MEDICAL_RECORDS cho toan bo lich hen Completed (status = 3)
   ============================================================ */
IF OBJECT_ID('tempdb..#CompletedAppts') IS NOT NULL DROP TABLE #CompletedAppts;
SELECT a.id AS appointment_id, a.patient_id, a.doctor_id,
       ROW_NUMBER() OVER (ORDER BY a.id) AS rn
INTO #CompletedAppts
FROM scheduling.appointments a
WHERE a.status = 3;

;WITH SymptomBank AS (
    SELECT * FROM (VALUES
    (1, N'Sốt, ho, đau họng 3 ngày', N'Viêm họng cấp do virus', N'Nghỉ ngơi, uống nhiều nước, tái khám nếu sốt kéo dài'),
    (2, N'Đau bụng âm ỉ vùng thượng vị', N'Viêm dạ dày cấp', N'Ăn nhẹ, tránh đồ cay nóng, uống thuốc theo toa'),
    (3, N'Nổi mẩn ngứa toàn thân', N'Dị ứng thời tiết', N'Tránh tiếp xúc dị nguyên, uống thuốc kháng histamin'),
    (4, N'Đau đầu, chóng mặt kéo dài', N'Rối loạn tiền đình', N'Nghỉ ngơi, tránh thay đổi tư thế đột ngột'),
    (5, N'Ho khan kéo dài, khó thở nhẹ', N'Viêm phế quản cấp', N'Uống thuốc theo toa, tái khám sau 5 ngày'),
    (6, N'Đau khớp gối khi vận động', N'Viêm khớp gối nhẹ', N'Hạn chế vận động mạnh, chườm ấm')
    ) AS t(rn, symptom, diagnosis, note)
)
INSERT INTO clinical.medical_records (appointment_id, patient_id, doctor_id, symptoms, diagnosis, note)
SELECT ca.appointment_id, ca.patient_id, ca.doctor_id, sb.symptom, sb.diagnosis, sb.note
FROM #CompletedAppts ca
JOIN SymptomBank sb ON sb.rn = ((ca.rn - 1) % 6) + 1;
GO

/* ============================================================
   10. CHI DINH XET NGHIEM + KET QUA (cho 1/2 so ho so benh an)
   ============================================================ */
DECLARE @UserTechnician1 INT = (SELECT id FROM auth.users WHERE username = 'ktv.linh');
DECLARE @UserTechnician2 INT = (SELECT id FROM auth.users WHERE username = 'ktv.hai');

IF OBJECT_ID('tempdb..#RecordList') IS NOT NULL DROP TABLE #RecordList;
SELECT mr.id AS medical_record_id, mr.doctor_id,
       ROW_NUMBER() OVER (ORDER BY mr.id) AS rn
INTO #RecordList
FROM clinical.medical_records mr;

IF OBJECT_ID('tempdb..#ServiceBank') IS NOT NULL DROP TABLE #ServiceBank;
SELECT id AS service_id, ROW_NUMBER() OVER (ORDER BY id) AS rn
INTO #ServiceBank
FROM billing.services
WHERE name <> N'Khám tổng quát'; -- loai tru dich vu kham co ban, chi lay cac dich vu XN/CDHA

DECLARE @ServiceBankCount INT = (SELECT COUNT(*) FROM #ServiceBank);

INSERT INTO clinical.medical_record_services (medical_record_id, service_id, status, ordered_by)
SELECT r.medical_record_id, sb.service_id, 2, r.doctor_id -- 2 = ResultReady
FROM #RecordList r
JOIN #ServiceBank sb ON sb.rn = ((r.rn - 1) % @ServiceBankCount) + 1
WHERE r.rn % 2 = 0; -- chi 1/2 so ho so co chi dinh XN

INSERT INTO clinical.lab_results (medical_record_service_id, result_summary, conclusion, technician_id)
SELECT mrs.id,
    N'Kết quả trong giới hạn tham chiếu bình thường, không phát hiện bất thường đáng kể',
    N'Chưa ghi nhận dấu hiệu bệnh lý nghiêm trọng, theo dõi thêm nếu triệu chứng không cải thiện',
    CASE WHEN mrs.id % 2 = 0 THEN @UserTechnician1 ELSE @UserTechnician2 END
FROM clinical.medical_record_services mrs;
GO

/* ============================================================
   11. DON THUOC cho TOAN BO ho so benh an (2 loai thuoc/don)
   ============================================================ */
IF OBJECT_ID('tempdb..#RecordList') IS NOT NULL DROP TABLE #RecordList;
SELECT mr.id AS medical_record_id, ROW_NUMBER() OVER (ORDER BY mr.id) AS rn
INTO #RecordList
FROM clinical.medical_records mr;

IF OBJECT_ID('tempdb..#MedicineBank') IS NOT NULL DROP TABLE #MedicineBank;
SELECT m.id AS medicine_id, b.id AS batch_id, m.price,
       ROW_NUMBER() OVER (ORDER BY m.id) AS rn
INTO #MedicineBank
FROM clinical.medicines m
JOIN clinical.medicine_batches b ON b.medicine_id = m.id; -- moi thuoc dung dung 1 lo da tao o buoc 7
DECLARE @MedicineBankCount INT = (SELECT COUNT(*) FROM #MedicineBank);

INSERT INTO clinical.prescriptions (medical_record_id, note)
SELECT medical_record_id, N'Uống thuốc theo đúng liều đã kê, tái khám nếu không đỡ sau 5 ngày'
FROM #RecordList;

IF OBJECT_ID('tempdb..#PrescList') IS NOT NULL DROP TABLE #PrescList;
SELECT p.id AS prescription_id, r.rn
INTO #PrescList
FROM clinical.prescriptions p
JOIN #RecordList r ON r.medical_record_id = p.medical_record_id;

-- Thuoc thu 1 cua moi don
INSERT INTO clinical.prescription_items (prescription_id, medicine_id, batch_id, quantity, dosage, instruction)
SELECT pl.prescription_id, mb.medicine_id, mb.batch_id, 10, N'Sáng 1 - Chiều 1', N'Uống sau ăn'
FROM #PrescList pl
JOIN #MedicineBank mb ON mb.rn = ((pl.rn - 1) % @MedicineBankCount) + 1;

-- Thuoc thu 2 cua moi don (lech 1 vi tri de khac thuoc thu 1)
INSERT INTO clinical.prescription_items (prescription_id, medicine_id, batch_id, quantity, dosage, instruction)
SELECT pl.prescription_id, mb.medicine_id, mb.batch_id, 5, N'Sáng 1', N'Uống sau ăn sáng'
FROM #PrescList pl
JOIN #MedicineBank mb ON mb.rn = (pl.rn % @MedicineBankCount) + 1;

-- Tru kho theo tong so luong da xuat cho tung lo (gop nhom de tranh cap nhat nhieu lan)
;WITH Dispensed AS (
    SELECT batch_id, SUM(quantity) AS total_qty
    FROM clinical.prescription_items
    GROUP BY batch_id
)
UPDATE b
SET b.quantity = b.quantity - d.total_qty
FROM clinical.medicine_batches b
JOIN Dispensed d ON d.batch_id = b.id;

DECLARE @UserAdmin INT = (SELECT id FROM auth.users WHERE username = 'admin');
INSERT INTO clinical.stock_transactions (batch_id, type, quantity, ref_type, ref_id, created_by)
SELECT pi.batch_id, 1, -SUM(pi.quantity), 'prescription', MIN(pi.prescription_id), @UserAdmin
FROM clinical.prescription_items pi
GROUP BY pi.batch_id;
GO

/* ============================================================
   12. HOA DON + THANH TOAN cho toan bo lich hen Completed
   ============================================================ */
DECLARE @UserReceptionist INT = (SELECT id FROM auth.users WHERE username = 'letan01');
DECLARE @SvcKhamTongQuat INT = (SELECT id FROM billing.services WHERE name = N'Khám tổng quát');
DECLARE @SvcKhamPrice DECIMAL(12,2) = (SELECT price FROM billing.services WHERE name = N'Khám tổng quát');

IF OBJECT_ID('tempdb..#InvoiceSource') IS NOT NULL DROP TABLE #InvoiceSource;
SELECT
    mr.id AS medical_record_id,
    mr.appointment_id,
    mr.patient_id,
    p.full_name AS patient_name,
    ROW_NUMBER() OVER (ORDER BY mr.id) AS rn
INTO #InvoiceSource
FROM clinical.medical_records mr
JOIN scheduling.patients p ON p.id = mr.patient_id;

-- Tinh tong tien: kham co ban + dich vu XN (neu co) + tong tien thuoc trong don
IF OBJECT_ID('tempdb..#InvoiceTotal') IS NOT NULL DROP TABLE #InvoiceTotal;
SELECT
    src.medical_record_id, src.appointment_id, src.patient_id, src.patient_name, src.rn,
    @SvcKhamPrice
    + ISNULL((SELECT SUM(s.price) FROM clinical.medical_record_services mrs JOIN billing.services s ON s.id = mrs.service_id WHERE mrs.medical_record_id = src.medical_record_id), 0)
    + ISNULL((SELECT SUM(pi.quantity * m.price) FROM clinical.prescriptions pr JOIN clinical.prescription_items pi ON pi.prescription_id = pr.id JOIN clinical.medicines m ON m.id = pi.medicine_id WHERE pr.medical_record_id = src.medical_record_id), 0)
    AS total_amount
INTO #InvoiceTotal
FROM #InvoiceSource src;

INSERT INTO billing.invoices (invoice_no, patient_id, appointment_id, medical_record_id, patient_name, total_amount, status, created_by)
SELECT
    'HD' + RIGHT('000000' + CAST(NEXT VALUE FOR billing.seq_invoice_no AS VARCHAR), 6),
    patient_id, appointment_id, medical_record_id, patient_name, total_amount,
    CASE WHEN rn % 3 = 0 THEN 0 WHEN rn % 3 = 1 THEN 2 ELSE 1 END, -- xoay vong Unpaid/Paid/PartiallyPaid
    @UserReceptionist
FROM #InvoiceTotal;

-- invoice_items: dong kham co ban (moi hoa don deu co)
INSERT INTO billing.invoice_items (invoice_id, service_id, description, quantity, unit_price, amount)
SELECT i.id, @SvcKhamTongQuat, N'Khám tổng quát', 1, @SvcKhamPrice, @SvcKhamPrice
FROM billing.invoices i;

-- invoice_items: dong dich vu XN (neu ho so co chi dinh)
INSERT INTO billing.invoice_items (invoice_id, service_id, description, quantity, unit_price, amount)
SELECT i.id, s.id, s.name, 1, s.price, s.price
FROM billing.invoices i
JOIN clinical.medical_record_services mrs ON mrs.medical_record_id = i.medical_record_id
JOIN billing.services s ON s.id = mrs.service_id;

-- invoice_items: cac dong thuoc trong don
INSERT INTO billing.invoice_items (invoice_id, medicine_id, description, quantity, unit_price, amount)
SELECT i.id, m.id, m.name, pi.quantity, m.price, pi.quantity * m.price
FROM billing.invoices i
JOIN clinical.prescriptions pr ON pr.medical_record_id = i.medical_record_id
JOIN clinical.prescription_items pi ON pi.prescription_id = pr.id
JOIN clinical.medicines m ON m.id = pi.medicine_id;

-- Thanh toan: Paid = tra du, PartiallyPaid = tra 50%, Unpaid = khong co payment
INSERT INTO billing.payments (invoice_id, amount, method, note)
SELECT id, total_amount, 0, N'Thanh toán tiền mặt tại quầy'
FROM billing.invoices
WHERE status = 2;

INSERT INTO billing.payments (invoice_id, amount, method, note)
SELECT id, ROUND(total_amount * 0.5, 0), 0, N'Đặt cọc trước, thanh toán phần còn lại sau'
FROM billing.invoices
WHERE status = 1;
GO

PRINT 'Da them du lieu test v2 (mo rong) cho FoMedDb thanh cong.';
PRINT 'Mat khau chung cho moi tai khoan: Test@123';
