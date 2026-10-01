/* Preserve the consultation/service amount agreed when an appointment is booked. */
USE [FoMedDb];
GO

IF COL_LENGTH('scheduling.appointments', 'fee_snapshot') IS NULL
BEGIN
    ALTER TABLE scheduling.appointments
        ADD fee_snapshot DECIMAL(12, 2) NULL;
END;
GO
