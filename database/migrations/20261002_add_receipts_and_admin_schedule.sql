/* VC-18/19/20 - phiếu nhập nhiều dòng, quản trị bác sĩ/chuyên khoa và lịch nghỉ */
USE FoMedDb;
GO

IF OBJECT_ID('clinical.inventory_receipts', 'U') IS NULL
BEGIN
    CREATE TABLE clinical.inventory_receipts (
        id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_inventory_receipts PRIMARY KEY,
        supplier_name NVARCHAR(255) NOT NULL,
        document_no VARCHAR(100) NOT NULL,
        received_by INT NOT NULL,
        received_at DATETIME2 NOT NULL CONSTRAINT DF_inventory_receipts_at DEFAULT SYSUTCDATETIME(),
        note NVARCHAR(500) NULL,
        CONSTRAINT UQ_inventory_receipts_document_no UNIQUE (document_no),
        CONSTRAINT FK_inventory_receipts_user FOREIGN KEY (received_by) REFERENCES auth.users(id)
    );
END;
GO

IF OBJECT_ID('clinical.inventory_receipt_items', 'U') IS NULL
BEGIN
    CREATE TABLE clinical.inventory_receipt_items (
        id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_inventory_receipt_items PRIMARY KEY,
        receipt_id INT NOT NULL,
        medicine_id INT NOT NULL,
        batch_id INT NOT NULL,
        quantity INT NOT NULL,
        unit_cost DECIMAL(12,2) NOT NULL CONSTRAINT DF_inventory_receipt_items_cost DEFAULT 0,
        line_amount DECIMAL(14,2) NOT NULL CONSTRAINT DF_inventory_receipt_items_amount DEFAULT 0,
        CONSTRAINT FK_inventory_receipt_items_receipt FOREIGN KEY (receipt_id) REFERENCES clinical.inventory_receipts(id),
        CONSTRAINT FK_inventory_receipt_items_medicine FOREIGN KEY (medicine_id) REFERENCES clinical.medicines(id),
        CONSTRAINT FK_inventory_receipt_items_batch FOREIGN KEY (batch_id) REFERENCES clinical.medicine_batches(id),
        CONSTRAINT CK_inventory_receipt_items_qty CHECK (quantity > 0),
        CONSTRAINT CK_inventory_receipt_items_cost CHECK (unit_cost >= 0)
    );
END;
GO
