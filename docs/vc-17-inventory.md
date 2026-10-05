# VC-17 — Kho thuốc

Các endpoint dành cho `Pharmacist` hoặc `Admin`:

- `GET /api/pharmacy/inventory?page=&medicineId=&expiringBefore=` xem tồn theo lô và cảnh báo hết hạn.
- `POST /api/pharmacy/inventory/receipts` nhập thêm lô, ghi giao dịch loại `0`.
- `POST /api/pharmacy/inventory/adjustments` điều chỉnh tăng/giảm có lý do, ghi giao dịch loại `3` và audit log.
- `GET /api/pharmacy/inventory/{batchId}/transactions?page=...` xem thẻ kho của từng lô.

Không cho nhập lô đã hết hạn hoặc điều chỉnh làm tồn âm. Tồn kho được khóa trong transaction workflow để tránh phát trùng khi có nhiều request đồng thời.
