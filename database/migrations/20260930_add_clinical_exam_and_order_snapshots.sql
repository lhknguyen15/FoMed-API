/* VC-13/VC-14: dữ liệu khám có cấu trúc và snapshot chỉ định. */
USE FoMedDb;
GO

IF COL_LENGTH(N'clinical.medical_records', N'vitals_json') IS NULL
    ALTER TABLE clinical.medical_records ADD vitals_json NVARCHAR(4000) NULL;
IF COL_LENGTH(N'clinical.medical_records', N'icd10_code') IS NULL
    ALTER TABLE clinical.medical_records ADD icd10_code VARCHAR(20) NULL;
IF COL_LENGTH(N'clinical.medical_records', N'treatment_plan') IS NULL
    ALTER TABLE clinical.medical_records ADD treatment_plan NVARCHAR(4000) NULL;
IF COL_LENGTH(N'clinical.medical_records', N'follow_up_date') IS NULL
    ALTER TABLE clinical.medical_records ADD follow_up_date DATE NULL;
IF COL_LENGTH(N'clinical.medical_records', N'is_finalized') IS NULL
    ALTER TABLE clinical.medical_records ADD is_finalized BIT NOT NULL CONSTRAINT DF_medical_records_is_finalized DEFAULT (0);
IF COL_LENGTH(N'clinical.medical_records', N'finalized_at') IS NULL
    ALTER TABLE clinical.medical_records ADD finalized_at DATETIME2 NULL;
GO

UPDATE r
SET r.is_finalized = 1,
    r.finalized_at = COALESCE(r.finalized_at, a.updated_at, r.created_at)
FROM clinical.medical_records r
JOIN scheduling.appointments a ON a.id = r.appointment_id
WHERE a.status = 3 AND r.is_finalized = 0;
GO

IF COL_LENGTH(N'clinical.medical_record_services', N'quantity') IS NULL
    ALTER TABLE clinical.medical_record_services ADD quantity INT NOT NULL CONSTRAINT DF_mrs_quantity DEFAULT (1);
IF COL_LENGTH(N'clinical.medical_record_services', N'unit_price_snapshot') IS NULL
    ALTER TABLE clinical.medical_record_services ADD unit_price_snapshot DECIMAL(12,2) NOT NULL CONSTRAINT DF_mrs_unit_price DEFAULT (0);
GO

IF COL_LENGTH(N'clinical.lab_results', N'reference_range') IS NULL
    ALTER TABLE clinical.lab_results ADD reference_range NVARCHAR(1000) NULL;
GO

UPDATE o
SET o.unit_price_snapshot = s.price
FROM clinical.medical_record_services o
JOIN billing.services s ON s.id = o.service_id
WHERE o.unit_price_snapshot = 0;
GO
