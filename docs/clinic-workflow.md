# Luồng đặt lịch, khám và thanh toán

## Giờ và đặt lịch

- `StartTime`, `EndTime`, lịch làm việc, lịch nghỉ trong SQL là giờ Việt Nam (UTC+07), không kèm offset. Dữ liệu lịch hiện có không cần chuyển đổi.
- Gửi `startTime` dạng `2030-01-07T09:00:00+07:00`, UTC tương đương hoặc `2030-01-07T09:00:00` (hiểu là giờ Việt Nam). Giờ có offset được chuẩn hóa trước khi kiểm tra/lưu. Response lịch trả giờ Việt Nam không có hậu tố Z.
- `CreatedAt`, `UpdatedAt`, lịch sử trạng thái, kết quả xét nghiệm và thanh toán dùng UTC.
- Giờ bắt đầu phải đúng lưới slot của ca làm, không có giây lẻ; toàn bộ slot phải nằm trong ca. Slot quá khứ, lịch nghỉ và lịch trùng bị từ chối.
- Mã lịch hẹn dùng `scheduling.seq_appointment_code`. Sequence có thể có khoảng trống khi rollback.
- Transaction giữ khóa `FoMed:clinic-workflow` tại SQL Server từ trước khi đọc/kiểm tra đến sau khi lưu. Khóa áp dụng cho đặt lịch, chuyển trạng thái, sửa lịch bác sĩ và các thao tác ghi clinical/billing. Nó hoạt động giữa nhiều tiến trình API; đang dùng một khóa chung để ưu tiên tính đúng đắn, có thể giới hạn thông lượng khi tải lớn.
- Khóa chỉ bảo vệ các thao tác đi qua API này; ứng dụng khác hoặc SQL thủ công phải tuân thủ cùng cơ chế. Không thay thế mọi ràng buộc database.
- Lỗi xung đột database/timeout khóa trả 409; client tải lại trước khi thử lại. Hủy hoặc NoShow giải phóng slot, Completed vẫn chiếm slot.

## Test bằng Swagger

1. Patient: `POST /api/appointments/book`, lấy ID lịch hẹn.
2. Receptionist/Admin: tra cứu hoặc tạo hồ sơ tại `/api/patients/staff`, sau đó dùng `POST /api/appointments/staff-book` với `patientId` và `source` (`1` = Phone, `2` = WalkIn) để đặt hộ.
3. Receptionist hoặc bác sĩ phụ trách: `PUT /api/appointments/{id}/confirm`.
4. Receptionist/Admin: `PUT /api/appointments/{id}/check-in` với body `{}`. Lịch mới chỉ nhận số thứ tự tại bước này; có thể xem hàng đã check-in bằng `GET /api/appointments/waiting-queue?date=YYYY-MM-DD`.
5. Doctor phụ trách: `POST /api/clinical/appointments/{appointmentId}/record` với `symptoms`, `diagnosis`, `note`. Tạo bệnh án duy nhất và chuyển Confirmed → InProgress trong cùng transaction. API từ chối nếu lễ tân chưa check-in.
6. Doctor: `PUT /api/clinical/records/{id}` để cập nhật bệnh án khi đang khám.
7. Doctor: tra `GET /api/clinical/medicines`, sau đó `POST /api/clinical/records/{id}/prescription` (gửi `allergyAcknowledged: true` nếu bệnh nhân có dị ứng). Có thể dùng `PUT` để sửa đơn khi bệnh án chưa chốt:

```json
{
  "note": "Ghi chú của bác sĩ",
  "allergyAcknowledged": false,
  "items": [{ "medicineId": 1, "quantity": 2, "dosage": "Theo chỉ định bác sĩ", "instruction": "Hướng dẫn sử dụng" }]
}
```

8. Doctor: tra `GET /api/clinical/services`, chỉ định qua `POST /api/clinical/records/{id}/services` với `{ "serviceId": 1 }`.
9. Technician: xem `GET /api/clinical/lab-orders`, nhập `POST /api/clinical/lab-orders/{id}/result` với `resultSummary`, `conclusion`. Chỉ định chuyển 0 (chờ) → 1 (hoàn thành), mỗi chỉ định chỉ có một kết quả.
10. Doctor: `PUT /api/appointments/{id}/complete`. Sau khi hoàn thành, bệnh án/đơn thuốc/chỉ định không được thêm hoặc sửa qua các API này.
11. Receptionist/Admin: `POST /api/invoices` với `{ "medicalRecordId": 1 }`. Chỉ lập khi ca khám hoàn thành và không còn dịch vụ chờ; mỗi bệnh án chỉ có một hóa đơn chưa hủy.
12. Receptionist/Admin: `POST /api/invoices/{id}/payments` với `{ "amount": 120, "method": 0, "note": "Thu tại quầy" }`. Cho phép trả từng phần, không vượt số còn thiếu; trả đủ thì hóa đơn chuyển 0 → 1. Mã method chấp nhận 0–3 theo schema hiện có.
13. Pharmacist/Admin: xem `GET /api/pharmacy/inventory`; nhập lô bằng `POST /api/pharmacy/inventory/receipts`; điều chỉnh tồn bằng `POST /api/pharmacy/inventory/adjustments` có lý do; phát đơn bằng `POST /api/pharmacy/prescriptions/{id}/dispense`. Phát thuốc dùng FEFO và ghi phân bổ từng lô.
14. Pharmacist/Admin: dùng `POST /api/pharmacy/receipts` cho phiếu nhập nhiều dòng có nhà cung cấp, số chứng từ và giá nhập.
15. Admin: quản lý bác sĩ/chuyên khoa qua `api/admin/doctors` và `api/admin/specialties`; bác sĩ quản lý lịch nghỉ qua `api/doctor/time-off`, Admin quản lý nghỉ bác sĩ hoặc nghỉ toàn phòng khám qua `api/admin/time-off`.

ID trong ví dụ là minh họa: dùng ID từ response hoặc danh mục thực tế.

## Quyền đọc và phạm vi

- Patient chỉ xem bệnh án, đơn thuốc, chỉ định/kết quả và hóa đơn của mình.
- Hồ sơ tạo tại quầy được ghi `created_by` trên lịch hẹn và có audit khi tạo/cập nhật hồ sơ.
- Admin có thể quản lý danh mục dịch vụ, người dùng/role, lịch nghỉ và tra cứu audit log; Receptionist/Admin có báo cáo theo kỳ và bác sĩ.
- VC-24: nhật ký bệnh án ghi đọc/tạo/sửa/chốt và cập nhật đơn, chỉ định, kết quả; lưu người thực hiện/vai trò tại thời điểm thao tác và IP/trace server. Log ghi cùng transaction với nghiệp vụ; UI Admin chỉ xem metadata, không xem nội dung bệnh án. Xem `docs/medical-record-audit.md`.
- Check-in chỉ dành cho Receptionist/Admin và lịch đã xác nhận có ngày hẹn trùng ngày hiện tại của phòng khám. `CheckedInAt` và `QueueNumber` được thiết lập tại quầy. Sau giờ bắt đầu, lễ tân/Admin có thể dùng `PUT /api/appointments/{id}/no-show` nếu bệnh nhân chưa check-in.
- Doctor chỉ xem/sửa bệnh án của lịch được phân công. Technician chỉ xem hàng đợi chỉ định và nhập kết quả, không có quyền xem toàn bộ bệnh án.
- Mục 4: dược sĩ chọn đơn/preview FEFO trước phát; kỹ thuật viên có lịch sử kết quả server của mình; file lâm sàng upload/download qua quyền sở hữu, không URL công khai. Kỹ thuật viên được append file cho kết quả của mình sau chốt, không sửa nội dung kết quả/bệnh án. Cần migration metadata và restart API; xem `docs/clinical-workflow-item4.md`.
- Receptionist/Admin được xem hóa đơn và thu tiền, không được sửa bệnh án.
- `GET /api/clinical/records`, `GET /api/invoices`, danh mục và hàng đợi nhận `page` (mặc định 1), 20 bản ghi/trang.
- Tiền hóa đơn lấy từ giá danh mục tại thời điểm lập, lưu vào từng dòng; client không gửi giá/tổng tiền. Gồm `Appointment.FeeSnapshot`, thuốc trong đơn và dịch vụ đã hoàn thành. Phí khám được lưu riêng ở `billing.invoices.consultation_fee`, không tạo dòng giả trong `invoice_items`.
- Kê đơn chưa phải cấp phát thuốc: chưa trừ tồn kho, chọn lô hay ghi xuất kho. Thanh toán là ghi nhận thu tiền tại quầy, chưa kết nối cổng thanh toán.
- Phiên bản này cho phép sửa đơn khi bệnh án chưa chốt, phát thuốc FEFO và quản lý tồn theo lô; chưa có sửa kết quả xét nghiệm, hoàn tiền hay xuất PDF. Hóa đơn chưa phát sinh thanh toán có thể hủy qua `POST /api/invoices/{id}/cancel`. Không chạy lại script seed để dùng API mới.

## Kiểm thử

Chạy từ thư mục gốc:

```powershell
dotnet run --project tests/AppointmentAuthorization
dotnet run --project tests/ClinicWorkflow
dotnet run --project tests/ClinicWorkflow -- --sql
```

Lệnh cuối lấy kết nối từ appsettings.Development.json, tạo database ngẫu nhiên `FoMed_Test_*`, tạo schema và dữ liệu riêng, chạy các request service đồng thời rồi xóa đúng database đó trong finally. Cần SQL Server và quyền tạo/xóa database. Không sửa dữ liệu FoMedDb. Test SQL kiểm tra repository/transaction/nghiệp vụ thật; không thay thế kiểm thử HTTP/JWT qua Swagger.
