/* VC-08: additional patient fields maintained by the reception desk. */
USE [FoMedDb];
GO

IF COL_LENGTH('scheduling.patients', 'national_id') IS NULL
    ALTER TABLE scheduling.patients ADD national_id VARCHAR(20) NULL;
IF COL_LENGTH('scheduling.patients', 'insurance_number') IS NULL
    ALTER TABLE scheduling.patients ADD insurance_number VARCHAR(50) NULL;
IF COL_LENGTH('scheduling.patients', 'emergency_contact_name') IS NULL
    ALTER TABLE scheduling.patients ADD emergency_contact_name NVARCHAR(255) NULL;
IF COL_LENGTH('scheduling.patients', 'emergency_contact_phone') IS NULL
    ALTER TABLE scheduling.patients ADD emergency_contact_phone VARCHAR(20) NULL;
IF COL_LENGTH('scheduling.patients', 'allergies') IS NULL
    ALTER TABLE scheduling.patients ADD allergies NVARCHAR(1000) NULL;
GO
