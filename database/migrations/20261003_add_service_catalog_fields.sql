/* VC-21: bổ sung mã, chuyên khoa và thời lượng cho danh mục dịch vụ. */
IF COL_LENGTH('billing.services', 'code') IS NULL
    EXEC(N'ALTER TABLE billing.services ADD code VARCHAR(50) NULL');
IF COL_LENGTH('billing.services', 'specialty_id') IS NULL
    EXEC(N'ALTER TABLE billing.services ADD specialty_id INT NULL');
IF COL_LENGTH('billing.services', 'duration_minutes') IS NULL
    EXEC(N'ALTER TABLE billing.services ADD duration_minutes INT NOT NULL CONSTRAINT DF_services_duration_minutes DEFAULT 30');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_services_code' AND object_id = OBJECT_ID('billing.services'))
    EXEC(N'CREATE UNIQUE INDEX UX_services_code ON billing.services(code) WHERE code IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_services_specialty')
    EXEC(N'ALTER TABLE billing.services ADD CONSTRAINT FK_services_specialty FOREIGN KEY (specialty_id) REFERENCES scheduling.specialties(id)');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_services_duration')
    EXEC(N'ALTER TABLE billing.services ADD CONSTRAINT CK_services_duration CHECK (duration_minutes BETWEEN 5 AND 1440)');
