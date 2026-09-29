/* Add an optional catalog-service link to existing appointments.
   Existing rows remain valid with service_id = NULL. */
USE [FoMedDb];
GO
IF COL_LENGTH('scheduling.appointments', 'service_id') IS NULL
BEGIN
    ALTER TABLE scheduling.appointments ADD service_id INT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_appointments_service'
      AND parent_object_id = OBJECT_ID('scheduling.appointments')
)
BEGIN
    ALTER TABLE scheduling.appointments
        ADD CONSTRAINT FK_appointments_service FOREIGN KEY (service_id)
        REFERENCES billing.services(id);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_appointments_service'
      AND object_id = OBJECT_ID('scheduling.appointments')
)
BEGIN
    CREATE INDEX IX_appointments_service
        ON scheduling.appointments(service_id);
END;
GO
