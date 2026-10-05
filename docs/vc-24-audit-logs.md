# VC-24 — Nhật ký truy cập và thay đổi bệnh án

`GET /api/audit-logs?entity=&action=&userId=&from=&to=&page=&pageSize=` chỉ dành cho Admin, hỗ trợ lọc và phân trang. API chỉ đọc bảng `audit.audit_logs`; không có endpoint sửa/xóa. Các audit nghiệp vụ khác được giữ nguyên, không trộn vào trang bệnh án.

Trang `/admin/audit-logs` luôn truy vấn `entity=MedicalRecord`, mặc định `action=Read`, có bộ lọc Read/Create/Update/Finalize hoặc tất cả ghi nhận bệnh án. Bảng phân trang server 10 bản ghi, lọc theo mã người dùng/khoảng ngày UTC+07 khi bấm Lọc. Modal chi tiết chỉ metadata người thực hiện, vai trò, IP/trace và nhóm trường thay đổi; không hiển thị nội dung bệnh án hoặc oldValue/newValue.

Log mới lưu danh tính/vai trò tại thời điểm đọc/ghi; log cũ chưa có snapshot ghi rõ fallback tài khoản hiện tại, không giả lập IP. Ghi nhận tạo/sửa/chốt, đơn thuốc, chỉ định/kết quả có transaction cùng thay đổi nghiệp vụ; truy cập ghi theo ID thực sự trả về trong chi tiết/danh sách/hàng đợi. Xem `docs/medical-record-audit.md` cho phạm vi endpoint và giới hạn append-only/proxy.
