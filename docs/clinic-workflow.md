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
2. Receptionist hoặc bác sĩ phụ trách: `PUT /api/appointments/{id}/confirm`.
3. Doctor phụ trách: `POST /api/clinical/appointments/{appointmentId}/record` với `symptoms`, `diagnosis`, `note`. Tạo bệnh án duy nhất và chuyển Confirmed → InProgress trong cùng transaction.
4. Doctor: `PUT /api/clinical/records/{id}` để cập nhật bệnh án khi đang khám.
5. Doctor: tra `GET /api/clinical/medicines`, sau đó `POST /api/clinical/records/{id}/prescription`:

```json
{
  "note": "Ghi chú của bác sĩ",
  "items": [{ "medicineId": 1, "quantity": 2, "dosage": "Theo chỉ định bác sĩ", "instruction": "Hướng dẫn sử dụng" }]
}
```

6. Doctor: tra `GET /api/clinical/services`, chỉ định qua `POST /api/clinical/records/{id}/services` với `{ "serviceId": 1 }`.
7. Technician: xem `GET /api/clinical/lab-orders`, nhập `POST /api/clinical/lab-orders/{id}/result` với `resultSummary`, `conclusion`. Chỉ định chuyển 0 (chờ) → 1 (hoàn thành), mỗi chỉ định chỉ có một kết quả.
8. Doctor: `PUT /api/appointments/{id}/complete`. Sau khi hoàn thành, bệnh án/đơn thuốc/chỉ định không được thêm hoặc sửa qua các API này.
9. Receptionist/Admin: `POST /api/invoices` với `{ "medicalRecordId": 1 }`. Chỉ lập khi ca khám hoàn thành và không còn dịch vụ chờ; mỗi bệnh án chỉ có một hóa đơn chưa hủy.
10. Receptionist/Admin: `POST /api/invoices/{id}/payments` với `{ "amount": 120, "method": 0, "note": "Thu tại quầy" }`. Cho phép trả từng phần, không vượt số còn thiếu; trả đủ thì hóa đơn chuyển 0 → 1. Mã method chấp nhận 0–3 theo schema hiện có.

ID trong ví dụ là minh họa: dùng ID từ response hoặc danh mục thực tế.

## Quyền đọc và phạm vi

- Patient chỉ xem bệnh án, đơn thuốc, chỉ định/kết quả và hóa đơn của mình.
- Doctor chỉ xem/sửa bệnh án của lịch được phân công. Technician chỉ xem hàng đợi chỉ định và nhập kết quả, không có quyền xem toàn bộ bệnh án.
- Receptionist/Admin được xem hóa đơn và thu tiền, không được sửa bệnh án.
- `GET /api/clinical/records`, `GET /api/invoices`, danh mục và hàng đợi nhận `page` (mặc định 1), 20 bản ghi/trang.
- Tiền hóa đơn lấy từ giá danh mục tại thời điểm lập, lưu vào từng dòng; client không gửi giá/tổng tiền. Gồm thuốc trong đơn và dịch vụ đã hoàn thành. Phí khám cần có dịch vụ tương ứng trong danh mục/chỉ định; chưa tự cộng `Doctor.ConsultationFee` vì schema invoice_items bắt buộc tham chiếu dịch vụ hoặc thuốc.
- Kê đơn chưa phải cấp phát thuốc: chưa trừ tồn kho, chọn lô hay ghi xuất kho. Thanh toán là ghi nhận thu tiền tại quầy, chưa kết nối cổng thanh toán.
- Phiên bản này tạo và đọc đơn thuốc; chưa có sửa/hủy đơn, sửa kết quả xét nghiệm, hoàn tiền hay hủy hóa đơn. Không chạy lại script seed để dùng API mới.

## Kiểm thử

Chạy từ thư mục gốc:

```powershell
dotnet run --project tests/AppointmentAuthorization
dotnet run --project tests/ClinicWorkflow
dotnet run --project tests/ClinicWorkflow -- --sql
```

Lệnh cuối lấy kết nối từ appsettings.Development.json, tạo database ngẫu nhiên `FoMed_Test_*`, tạo schema và dữ liệu riêng, chạy các request service đồng thời rồi xóa đúng database đó trong finally. Cần SQL Server và quyền tạo/xóa database. Không sửa dữ liệu FoMedDb. Test SQL kiểm tra repository/transaction/nghiệp vụ thật; không thay thế kiểm thử HTTP/JWT qua Swagger.
