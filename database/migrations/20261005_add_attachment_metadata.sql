-- Private clinical attachment metadata; no public URLs or clinical bytes are exposed by list APIs.
-- Run against the intended FoMed database after backup; safe to run again.
USE [FoMedDb];
GO
IF COL_LENGTH('clinical.attachments', 'file_name') IS NULL
    ALTER TABLE clinical.attachments ADD file_name nvarchar(180) NULL;
IF COL_LENGTH('clinical.attachments', 'content_type') IS NULL
    ALTER TABLE clinical.attachments ADD content_type varchar(100) NULL;
IF COL_LENGTH('clinical.attachments', 'file_size') IS NULL
    ALTER TABLE clinical.attachments ADD file_size bigint NULL;
IF COL_LENGTH('clinical.attachments', 'uploaded_by') IS NULL
    ALTER TABLE clinical.attachments ADD uploaded_by int NULL;
GO
