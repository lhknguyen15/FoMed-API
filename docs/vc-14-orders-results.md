# VC-14 — Chỉ định cận lâm sàng và kết quả

- `POST /api/clinical/records/{id}/services`: bác sĩ tạo chỉ định với `serviceId` và `quantity`; API lưu `unitPriceSnapshot` tại thời điểm chỉ định.
- `GET /api/clinical/records/{id}/services`: trả số lượng, giá snapshot, trạng thái và kết quả.
- `PUT /api/clinical/orders/{id}/cancel`: bác sĩ phụ trách được hủy chỉ định còn chờ (`status = 0`); chỉ định đã có kết quả không thể hủy.
- `GET /api/clinical/lab-orders`: Technician xem hàng chờ.
- `POST /api/clinical/lab-orders/{id}/result`: Technician nhập kết quả, kết luận và `referenceRange`; chỉ định chuyển sang hoàn thành và không nhận kết quả lần hai.

`BillingService` dùng `quantity * unitPriceSnapshot` khi lập hóa đơn, không đọc lại giá danh mục cho các chỉ định đã lưu. Migration VC-13/14 bổ sung các cột snapshot, số lượng và khoảng tham chiếu; dữ liệu cũ được mặc định số lượng 1 và cập nhật giá hiện tại một lần.

Mục 4 bổ sung `GET /api/clinical/lab-results` (lịch sử kết quả của kỹ thuật viên, server 10/trang) và upload/list/download file riêng qua `api/clinical/.../attachments`. UI bác sĩ/kỹ thuật viên/bệnh nhân dùng panel đồng bộ. Cần chạy migration `20261005_add_attachment_metadata.sql` trên đúng DB và restart API; chưa tự chạy lên DB ứng dụng. Hợp đồng phân quyền, định dạng/dung lượng và giới hạn: [Mục 4](clinical-workflow-item4.md).
