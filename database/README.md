# FoMed Database

Thu muc nay chua cac script SQL lam nguon du lieu cho database-first EF Core cua FoMed.

## Thu tu chay

Chay dung thu tu sau trong SQL Server Management Studio, Azure Data Studio hoac sqlcmd:

1. `fomed-create-database.sql`
2. `fomed-seed-data.sql`

Script seed phu thuoc vao database `FoMedDb`, cac schema, bang, sequence va role duoc tao boi script thu nhat.

## Yeu cau moi truong

- SQL Server 2019 tro len.
- Tai khoan chay script co quyen tao database, schema, sequence, table, index va foreign key.
- Database name mac dinh: `FoMedDb`.
- Connection string phat trien hien tai nam trong `FoMed-API/FoMed.Api/appsettings.Development.json`.

## Noi dung database

Database duoc chia thanh cac schema:

- `auth`: users, roles, user_roles, refresh_tokens.
- `audit`: audit_logs.
- `scheduling`: specialties, doctors, patients, doctor_schedules, doctor_time_off, appointments, appointment_status_history.
- `clinical`: medical_records, medicines, medicine_batches, stock_transactions, prescriptions, prescription_items, medical_record_services, lab_results, attachments.
- `billing`: services, invoices, invoice_items, payments.

## Quan he nghiep vu chinh

```text
auth.users
  -> auth.user_roles -> auth.roles
  -> scheduling.doctors
  -> scheduling.patients

scheduling.doctors + scheduling.patients
  -> scheduling.appointments
  -> clinical.medical_records

clinical.medical_records
  -> clinical.medical_record_services -> billing.services
  -> clinical.prescriptions -> clinical.prescription_items -> clinical.medicines
  -> clinical.lab_results

billing.invoices
  -> billing.invoice_items
  -> billing.payments
```

## Du lieu seed

Script `fomed-seed-data.sql` tao du lieu test cho:

- 5 role: `Admin`, `Receptionist`, `Doctor`, `Technician`, `Patient`.
- 9 tai khoan test.
- 3 chuyen khoa.
- 3 bac si.
- Lich lam viec tu thu 2 den thu 6.
- 5 benh nhan, trong do 2 benh nhan vang lai khong co tai khoan.
- Dich vu, thuoc, lo thuoc va giao dich kho.
- Lich hen voi nhieu trang thai.
- Ho so kham, xet nghiem, don thuoc, hoa don va thanh toan.

Mat khau chung cua cac tai khoan seed:

```text
Test@123
```

Tai khoan mau:

```text
admin
letan01
bs.hoa
bs.minh
bs.an
ktv.linh
patient01
patient02
patient03
```

API login hien tai dang tim theo email, vi vay khi test API hay dung email tuong ung, vi du `admin@fomed.vn`.

## Luu y khi chay lai seed

Script seed xoa du lieu nghiep vu truoc khi insert lai. Khong chay tren database co du lieu that.

Script khong reset `IDENTITY` va `SEQUENCE`, vi vay ID va ma nghiep vu co the tang tiep sau moi lan seed lai. Day la hanh vi chap nhan duoc trong moi truong phat trien.

## Diem can doi chieu voi code

1. Script tao database hien tai dung cac lenh `CREATE TABLE` khong co kiem tra `IF NOT EXISTS`. Phan tao database co the chay lai, nhung phan tao bang khong hoan toan idempotent. Neu chay lai tren database da co bang, script se loi o cac lenh tao bang.
2. `scheduling.appointments.status` cho phep gia tri tu `0` den `5`, trong khi enum `AppointmentStatus` hien tai moi co `Booked`, `Waiting`, `InProgress`, `Completed`, `Cancelled`. Seed dang su dung y tuong trang thai `NoShow = 5`, can bo sung enum hoac sua constraint/seed cho thong nhat.
3. Database co role `Technician`, nhung enum `UserRole` trong code can co gia tri tuong ung neu API xu ly quyen theo enum.
4. `AuthService.RegisterAsync` hien tao user nhung chua tao ban ghi `auth.user_roles`. User moi se chi duoc fallback role `Patient` khi tao token, khong phai role duoc luu that trong database. Can xu ly role mac dinh khi hoan thien Auth.
5. Script seed khong xoa `audit.audit_logs`. Neu database da co audit log tham chieu user, lenh xoa `auth.users` co the bi chan boi foreign key.
6. Sau khi thay doi schema, can scaffold lai `FoMedDbContext` va Models de mapping EF Core khop voi database that.

## Quy trinh database-first

```text
Chinh sua SQL schema
    -> chay fomed-create-database.sql
    -> chay fomed-seed-data.sql
    -> scaffold lai EF Core Models + DbContext
    -> dotnet build FoMed.sln
    -> test API qua Swagger
```

Khong sua thu cong cac model scaffold neu thay doi do bat nguon tu database. Hay sua SQL truoc, sau do scaffold lai de tranh model va schema bi lech nhau.
