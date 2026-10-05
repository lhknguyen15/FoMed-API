# VC-18 — Nhập thuốc

`POST /api/pharmacy/receipts` nhận một phiếu nhập nhiều dòng gồm nhà cung cấp, số chứng từ, lô, hạn dùng, số lượng và giá nhập. API cập nhật các lô trong một transaction, ghi `inventory_receipts`, `inventory_receipt_items`, `stock_transactions` loại `0` và audit log. Số chứng từ là duy nhất.
