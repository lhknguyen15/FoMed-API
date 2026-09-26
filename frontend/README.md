# FoMed Frontend — Bước 1: Khởi tạo dự án

Ở bước này chỉ tạo bộ khung React + TypeScript + Vite và trang chào FoMed.
Chưa có router, gọi API, đăng nhập hoặc chức năng nghiệp vụ. Không cần chạy BE.

## Chạy dự án

```powershell
cd frontend
npm ci
npm run dev
```

Mở http://127.0.0.1:5173.

## Vai trò của từng công nghệ

- **React:** xây dựng giao diện từ các component (thành phần giao diện).
- **TypeScript:** kiểm tra kiểu dữ liệu trong quá trình phát triển.
- **Vite:** chạy server phát triển, cập nhật giao diện khi sửa file và đóng gói bản production.

## Đọc source theo thứ tự

1. `index.html`: chứa phần tử `<div id="root">` và nạp `src/main.tsx`.
2. `src/main.tsx`: tạo React root và render component `App` vào phần tử trên; nạp CSS dùng chung.
3. `src/App.tsx`: component gốc, hiện hiển thị trang chào bằng JSX. File `.tsx` cho phép viết JSX trong TypeScript.
4. `src/styles.css`: định dạng trang chào.
5. `package.json`: tên dự án, thư viện và các lệnh npm.
6. `package-lock.json`: khóa phiên bản dependency để các máy cài giống nhau bằng `npm ci`.
7. `vite.config.ts`: bật plugin React và chọn cổng 5173; chưa cấu hình proxy API.
8. `tsconfig.json`: cấu hình kiểm tra TypeScript, bật `strict` để phát hiện sai kiểu sớm.
9. `.gitignore`: loại thư viện đã cài, bản build và cấu hình local khỏi Git.

`StrictMode` trong main.tsx hỗ trợ phát hiện vấn đề khi phát triển. Dấu `!` sau getElementById cho TypeScript biết phần tử root chắc chắn tồn tại trong index.html.

## Ba lệnh cần hiểu

| Lệnh | Mục đích |
|---|---|
| `npm run dev` | Chạy frontend để phát triển |
| `npm run build` | Kiểm tra TypeScript rồi tạo bản production trong `dist` |
| `npm run preview` | Xem thử bản `dist` sau khi build, không phải server triển khai production |

Yêu cầu Node.js theo [tài liệu Vite](https://vite.dev/guide/): 20.19+ hoặc 22.12+.

## Phạm vi commit

`chore(frontend): khởi tạo React, TypeScript và Vite`

Chỉ commit bộ khung. Bản FE đầy đủ đã làm trước đây được giữ trong `.local-backups/` ở thư mục gốc, không được Git theo dõi. Chưa triển khai bước tiếp theo cho đến khi cùng xem và hiểu bước này.
