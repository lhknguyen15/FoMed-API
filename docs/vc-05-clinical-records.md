# VC-05 — Lịch sử khám, đơn thuốc và chỉ định

API clinical đã tách theo nhóm Review:

- `GET /api/clinical/records?page=1`: bác sĩ xem hồ sơ do mình phụ trách; bệnh nhân chỉ nhận hồ sơ của mình đã gắn với lịch `Completed`.
- `GET /api/clinical/records/{id}`: đọc chi tiết bệnh án theo cùng quyền; hồ sơ đang soạn không được công bố cho bệnh nhân.
- `GET /api/clinical/records/{id}/prescription`: đọc đơn thuốc.
- `GET /api/clinical/records/{id}/services`: đọc chỉ định và kết quả.
- Bác sĩ dùng các route `POST/PUT` tương ứng để tạo hoặc cập nhật trong lúc khám; kỹ thuật viên nhập kết quả xét nghiệm.

Việc dùng trạng thái lịch `Completed` làm mốc công bố không cần thêm cột mới: chốt bệnh án hiện được thực hiện khi bác sĩ hoàn tất ca khám sau khi có chẩn đoán và xử lý xong chỉ định.

Mục 4: nút Tải file đính kèm mở danh sách thật, giữ 3 bảng bệnh án/đơn thuốc/kết quả. GET `/api/clinical/records/{id}/attachments` và GET `/api/clinical/attachments/{attachmentId}/download` kiểm tra bệnh nhân sở hữu và bệnh án đã chốt + Completed; không trả URL công khai, không có quyền upload cho bệnh nhân. Cần migration metadata và restart API, xem [Mục 4](clinical-workflow-item4.md).
