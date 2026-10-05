# VC-16 — Phát thuốc

`POST /api/pharmacy/prescriptions/{id}/dispense` dành cho `Pharmacist` hoặc `Admin`.

API phân bổ thuốc theo FEFO (lô hết hạn sớm được dùng trước), cho phép dùng nhiều lô cho một dòng đơn, giảm tồn kho và ghi `clinical.prescription_dispenses` cùng `stock_transactions` loại `1` (xuất). Gọi lại sau khi đã phát đủ là thao tác idempotent.

Mục 4: `GET /api/pharmacy/prescriptions?keyword=&status=pending&page=1` trả danh sách đơn đã chốt + Completed, 10/trang. `GET /api/pharmacy/prescriptions/{id}` bổ sung tồn khả dụng/số lượng thiếu/lô đề xuất FEFO. `/pharmacy/dispense` chọn đơn; trang chi tiết preview trước xác nhận và có Tải lại tồn kho. GET không giữ chỗ/trừ kho; POST kiểm tra lại, tự phân bổ trong transaction. Chưa có API báo thiếu/gửi thông báo bác sĩ. Chi tiết: [Mục 4](clinical-workflow-item4.md).
