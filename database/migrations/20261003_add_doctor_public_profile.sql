-- Hồ sơ bác sĩ công khai. Chạy trên database FoMedDb hiện có; có thể chạy lại.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'scheduling.doctors', N'U') IS NULL
    THROW 50001, N'Không tìm thấy bảng scheduling.doctors. Kiểm tra database đích.', 1;

IF COL_LENGTH(N'scheduling.doctors', N'avatar_url') IS NULL
    EXEC(N'ALTER TABLE scheduling.doctors ADD avatar_url NVARCHAR(2048) NULL');
IF COL_LENGTH(N'scheduling.doctors', N'biography') IS NULL
    EXEC(N'ALTER TABLE scheduling.doctors ADD biography NVARCHAR(MAX) NULL');
IF COL_LENGTH(N'scheduling.doctors', N'practice_start_year') IS NULL
    EXEC(N'ALTER TABLE scheduling.doctors ADD practice_start_year INT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'scheduling.doctors')
                 AND name = N'CK_doctors_practice_start_year')
    EXEC(N'ALTER TABLE scheduling.doctors ADD CONSTRAINT CK_doctors_practice_start_year
           CHECK (practice_start_year IS NULL OR practice_start_year BETWEEN 1900 AND 2100)');

COMMIT TRANSACTION;
