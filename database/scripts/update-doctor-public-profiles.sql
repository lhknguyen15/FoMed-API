/*
    Cập nhật hàng loạt hồ sơ bác sĩ, không cần đăng nhập từng tài khoản.
    Chạy migration 20261003_add_doctor_public_profile.sql trước.
    1. @Profiles đã có dữ liệu DEMO theo yêu cầu; có thể sửa nội dung bên dưới.
    2. Chạy với @Apply = 0 để xem dữ liệu trước/sau.
    3. Khi đã kiểm tra đúng, đổi @Apply = 1 và chạy lại để lưu.

    NULL = giữ nguyên trường hiện tại, KHÔNG phải xóa dữ liệu.
    Nội dung và năm hành nghề là GIẢ LẬP, không dùng làm hồ sơ y tế thực tế.
    Chỉ chạy trên database phát triển/demo; thay dữ liệu thật trước khi production.
    Avatar là hình minh họa DiceBear Notionists (CC0), không phải ảnh bác sĩ thật.
    Nguồn: https://www.dicebear.com/styles/notionists/
    Script chỉ UPDATE ba trường công khai, không tạo tài khoản/bác sĩ mới.
*/
USE FoMedDb;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Apply BIT = 0;
DECLARE @Profiles TABLE (
    username VARCHAR(100) PRIMARY KEY,
    avatar_url NVARCHAR(MAX) NULL,
    biography NVARCHAR(MAX) NULL,
    practice_start_year INT NULL
);

-- Danh sách tài khoản đã đối chiếu với database phát triển.
-- Nội dung minh họa từng chuyên khoa; không bổ sung học vị/thành tích giả.
-- Bỏ dòng không muốn cập nhật; NULL giữ nguyên trường đang có.
INSERT INTO @Profiles (username, avatar_url, biography, practice_start_year)
VALUES
    ('bs.hoa', N'https://api.dicebear.com/10.x/notionists/svg?seed=FoMedDemo01&backgroundColor=d1fae5',
     N'Hồ sơ minh họa cho hệ thống FoMed. Nội dung và năm hành nghề dưới đây là dữ liệu giả lập, không phải thông tin chuyên môn đã xác minh.

Bác sĩ Trần Thị Hoa phụ trách chuyên khoa Nội tổng quát. Hồ sơ mẫu tập trung vào khám sức khỏe tổng quát, đánh giá các triệu chứng thường gặp và theo dõi sức khỏe định kỳ.

Định hướng chăm sóc: lắng nghe tình trạng của người bệnh, giải thích rõ kế hoạch thăm khám và hỗ trợ xây dựng lịch tái khám phù hợp. Các thông tin này phục vụ trình diễn giao diện giới thiệu và đặt lịch khám.', 2012),
    ('bs.minh', N'https://api.dicebear.com/10.x/notionists/svg?seed=FoMedDemo02&backgroundColor=d1fae5',
     N'Hồ sơ minh họa cho hệ thống FoMed. Nội dung và năm hành nghề dưới đây là dữ liệu giả lập, không phải thông tin chuyên môn đã xác minh.

Bác sĩ Lê Văn Minh phụ trách chuyên khoa Da liễu. Hồ sơ mẫu giới thiệu các nhóm vấn đề như mụn, viêm da, tình trạng da nhạy cảm và nhu cầu tư vấn chăm sóc da.

Định hướng chăm sóc: trao đổi về biểu hiện và thói quen sinh hoạt, giải thích mục tiêu theo dõi, đồng thời giúp người bệnh hiểu các bước trong quá trình thăm khám. Nội dung phục vụ kiểm thử trang hồ sơ bác sĩ.', 2010),
    ('bs.an', N'https://api.dicebear.com/10.x/notionists/svg?seed=FoMedDemo03&backgroundColor=d1fae5',
     N'Hồ sơ minh họa cho hệ thống FoMed. Nội dung và năm hành nghề dưới đây là dữ liệu giả lập, không phải thông tin chuyên môn đã xác minh.

Bác sĩ Phạm Văn An phụ trách chuyên khoa Tai Mũi Họng. Hồ sơ mẫu tập trung vào các vấn đề thường gặp ở tai, mũi, họng và nhu cầu theo dõi sức khỏe đường hô hấp trên.

Định hướng chăm sóc: khai thác triệu chứng, hướng dẫn người bệnh chuẩn bị thông tin trước buổi khám và giải thích rõ các bước kiểm tra cần thiết. Nội dung giúp minh họa quy trình xem thông tin bác sĩ trước khi đặt lịch.', 2015),
    ('bs.trang', N'https://api.dicebear.com/10.x/notionists/svg?seed=FoMedDemo04&backgroundColor=d1fae5',
     N'Hồ sơ minh họa cho hệ thống FoMed. Nội dung và năm hành nghề dưới đây là dữ liệu giả lập, không phải thông tin chuyên môn đã xác minh.

Bác sĩ Võ Thị Trang phụ trách chuyên khoa Nhi khoa. Hồ sơ mẫu giới thiệu hoạt động thăm khám trẻ em, theo dõi tăng trưởng và trao đổi với gia đình về các vấn đề sức khỏe thường gặp ở trẻ.

Định hướng chăm sóc: tạo không gian thăm khám thân thiện, lắng nghe thông tin từ phụ huynh và giải thích kế hoạch theo dõi bằng ngôn ngữ dễ hiểu. Thông tin chỉ phục vụ trình diễn giao diện FoMed.', 2016),
    ('bs.tuan', N'https://api.dicebear.com/10.x/notionists/svg?seed=FoMedDemo05&backgroundColor=d1fae5',
     N'Hồ sơ minh họa cho hệ thống FoMed. Nội dung và năm hành nghề dưới đây là dữ liệu giả lập, không phải thông tin chuyên môn đã xác minh.

Bác sĩ Ngô Anh Tuấn phụ trách chuyên khoa Tim mạch. Hồ sơ mẫu tập trung vào thăm khám sức khỏe tim mạch, theo dõi huyết áp và đánh giá các yếu tố liên quan đến sức khỏe tuần hoàn.

Định hướng chăm sóc: giải thích mục tiêu thăm khám, giúp người bệnh nắm rõ lịch theo dõi và khuyến khích cung cấp đầy đủ lịch sử sức khỏe. Nội dung và mốc năm được dùng để kiểm thử màn hình chi tiết bác sĩ.', 2008);

UPDATE @Profiles
SET avatar_url = NULLIF(LTRIM(RTRIM(avatar_url)), N''),
    biography = NULLIF(LTRIM(RTRIM(biography)), N'');

IF EXISTS (
    SELECT 1 FROM @Profiles
    WHERE DATALENGTH(avatar_url) > 4096 OR DATALENGTH(biography) > 10000
       OR (avatar_url IS NOT NULL AND avatar_url NOT LIKE N'https://_%'
                                   AND avatar_url NOT LIKE N'http://_%')
       OR practice_start_year NOT BETWEEN 1900 AND YEAR(SYSUTCDATETIME())
)
    THROW 50001, N'Dữ liệu không hợp lệ: ảnh phải dùng http/https, tối đa 2048 ký tự; giới thiệu tối đa 5000 ký tự; năm hành nghề từ 1900 đến năm hiện tại.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Không bỏ qua âm thầm tài khoản nhập sai hoặc chưa có hồ sơ bác sĩ.
    IF EXISTS (
        SELECT 1 FROM @Profiles p
        WHERE NOT EXISTS (
            SELECT 1 FROM scheduling.doctors d
            JOIN auth.users u ON u.id = d.user_id
            WHERE u.username = p.username
        )
    )
    BEGIN
        SELECT p.username AS unmatched_username
        FROM @Profiles p
        WHERE NOT EXISTS (
            SELECT 1 FROM scheduling.doctors d
            JOIN auth.users u ON u.id = d.user_id
            WHERE u.username = p.username
        );
        THROW 50002, N'Có tài khoản không tìm thấy hồ sơ bác sĩ. Kiểm tra danh sách trước khi cập nhật.', 1;
    END;

    SELECT d.id AS doctor_id, u.username, d.full_name,
           d.avatar_url AS current_avatar_url,
           COALESCE(p.avatar_url, d.avatar_url) AS next_avatar_url,
           d.biography AS current_biography,
           COALESCE(p.biography, d.biography) AS next_biography,
           d.practice_start_year AS current_practice_start_year,
           COALESCE(p.practice_start_year, d.practice_start_year) AS next_practice_start_year
    FROM @Profiles p
    JOIN auth.users u ON u.username = p.username
    JOIN scheduling.doctors d ON d.user_id = u.id
    ORDER BY d.id;

    IF @Apply = 1
    BEGIN
        UPDATE d
        SET avatar_url = COALESCE(p.avatar_url, d.avatar_url),
            biography = COALESCE(p.biography, d.biography),
            practice_start_year = COALESCE(p.practice_start_year, d.practice_start_year)
        FROM scheduling.doctors d
        JOIN auth.users u ON u.id = d.user_id
        JOIN @Profiles p ON p.username = u.username
        WHERE p.avatar_url IS NOT NULL OR p.biography IS NOT NULL
           OR p.practice_start_year IS NOT NULL;

        SELECT @@ROWCOUNT AS updated_doctor_count;
        COMMIT TRANSACTION;
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'Chế độ xem trước: chưa cập nhật database. Đổi @Apply = 1 để lưu.';
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
