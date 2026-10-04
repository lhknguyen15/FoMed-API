# Hồ sơ bác sĩ công khai

## Database

Database-first SQL Server. Với database đang có, chạy `database/migrations/20261003_add_doctor_public_profile.sql` trên FoMedDb. Script thêm các cột nullable `avatar_url`, `biography`, `practice_start_year` và có thể chạy lại. Không cần chạy lại seed hoặc tạo lại database. Schema cho cài đặt mới cũng đã cập nhật trong `fomed-create-database.sql`.

Model và mapping bổ sung nằm trong các file partial `Doctor.PublicProfile.cs` và `FoMedDbContext.DoctorProfiles.cs`, giữ nguyên các file scaffold. Khi scaffold lại và các trường này đã được sinh vào model chính, cần gỡ các khai báo trùng trong phần extension.

### Cập nhật bằng SQL, không cần đăng nhập từng bác sĩ

File `database/scripts/update-doctor-public-profiles.sql` có sẵn dữ liệu DEMO cho 5 username bác sĩ trong database phát triển. Các phần giới thiệu theo chuyên khoa và năm hành nghề là thông tin giả lập theo yêu cầu, không phải hồ sơ chuyên môn đã xác minh; mỗi phần giới thiệu có ghi chú minh họa hiển thị trên trang. Avatar là hình minh họa từ [DiceBear Notionists](https://www.dicebear.com/styles/notionists/) (CC0), không phải ảnh người thật; tải ảnh cần kết nối Internet. Username được dùng thay ID vì ID có thể thay đổi sau mỗi lần seed.

Chạy `@Apply = 0` để đối chiếu giá trị hiện tại và giá trị sau cập nhật; đổi sang `@Apply = 1` khi muốn lưu trên database phát triển/demo. Mọi thay đổi được thực hiện trong một transaction; tài khoản không khớp hoặc dữ liệu sai sẽ báo lỗi. `NULL`/chuỗi trắng giữ nguyên trường hiện tại, khác với semantics xóa trường của API PUT. Script chỉ kiểm tra sơ bộ tiền tố URL http/https; cần dùng liên kết ảnh thực tế hợp lệ. Không chạy lại seed hoặc dùng dữ liệu giả lập trên môi trường production. SQL không gọi API và không đi qua cơ chế audit của API; chỉ dành cho người quản trị database được phép cập nhật.

## API

- `GET /api/doctors`: DTO danh sách gọn, bổ sung `avatarUrl` để trang chủ/danh sách hiển thị ảnh mà không gọi API chi tiết cho từng bác sĩ.
- `GET /api/doctors/{id}`: cho phép khách công khai; trả 404 khi bác sĩ/chuyên khoa ngừng hoạt động hoặc không tồn tại.
- DTO chi tiết: doctorId, specialtyId, specialtyName, fullName, title, consultationFee, room, avatarUrl, biography, practiceStartYear, specialtyDescription. Không trả userId, phone hoặc licenseNumber.
- `GET/PUT /api/doctor/me`: bác sĩ cập nhật hồ sơ của chính mình.
- `POST/PUT /api/admin/doctors`: admin tạo/cập nhật hồ sơ gồm các trường công khai mới.

Các PUT gửi toàn bộ hồ sơ; null hoặc chuỗi trắng xóa trường công khai tùy chọn. AvatarUrl là URL ảnh http/https, tối đa 2048 ký tự; chưa có chức năng tải file ảnh lên server. Biography là văn bản thuần, tối đa 5000 ký tự; giao diện không render HTML. PracticeStartYear là năm từ 1900 đến năm hiện tại, cho phép null.

## Giao diện

Admin thêm/sửa thông tin công khai trong form bác sĩ. Bác sĩ vào Tài khoản → Hồ sơ bác sĩ (`/account/doctor-profile`) để cập nhật ảnh, giới thiệu, năm hành nghề và phòng khám; form giữ nguyên các trường hồ sơ còn lại khi lưu. Trang `/doctors/{id}` gọi API chi tiết, hiển thị các mục có dữ liệu và giữ một CTA đặt khám. Avatar lỗi hoặc thiếu dùng chữ viết tắt tên bác sĩ.

## Kiểm tra

`dotnet run --project tests/DoctorProfiles` kiểm tra DTO công khai, 404, lưu/xóa dữ liệu và validation. Thêm `-- --database` để kiểm tra EF đọc/ghi thực tế trên database phát triển; thay đổi kiểm thử được rollback trong transaction.
