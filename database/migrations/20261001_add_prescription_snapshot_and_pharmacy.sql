/* VC-15/16/17 - snapshot giá kê đơn, phân bổ phát thuốc và quyền dược */
USE FoMedDb;
GO

IF COL_LENGTH('clinical.prescription_items', 'unit_price_snapshot') IS NULL
    ALTER TABLE clinical.prescription_items ADD unit_price_snapshot DECIMAL(12,2) NOT NULL CONSTRAINT DF_prescription_items_unit_price_snapshot DEFAULT 0;
GO

UPDATE pi
SET unit_price_snapshot = m.price
FROM clinical.prescription_items pi
JOIN clinical.medicines m ON m.id = pi.medicine_id
WHERE pi.unit_price_snapshot = 0;
GO

IF OBJECT_ID('clinical.prescription_dispenses', 'U') IS NULL
BEGIN
    CREATE TABLE clinical.prescription_dispenses (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_prescription_dispenses PRIMARY KEY,
        prescription_item_id INT NOT NULL,
        batch_id INT NOT NULL,
        quantity INT NOT NULL,
        dispensed_by INT NOT NULL,
        dispensed_at DATETIME2 NOT NULL CONSTRAINT DF_prescription_dispenses_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_prescription_dispenses_item FOREIGN KEY (prescription_item_id) REFERENCES clinical.prescription_items(id),
        CONSTRAINT FK_prescription_dispenses_batch FOREIGN KEY (batch_id) REFERENCES clinical.medicine_batches(id),
        CONSTRAINT FK_prescription_dispenses_user FOREIGN KEY (dispensed_by) REFERENCES auth.users(id),
        CONSTRAINT CK_prescription_dispenses_qty CHECK (quantity > 0)
    );
    CREATE INDEX IX_prescription_dispenses_item ON clinical.prescription_dispenses(prescription_item_id);
    CREATE INDEX IX_prescription_dispenses_batch ON clinical.prescription_dispenses(batch_id);
END;
GO

/* Giữ nguyên lịch sử các đơn cũ đã xuất kho bằng batch_id + stock_transactions. */
INSERT INTO clinical.prescription_dispenses (prescription_item_id, batch_id, quantity, dispensed_by, dispensed_at)
SELECT pi.id, pi.batch_id, pi.quantity, st.created_by, st.created_at
FROM clinical.prescription_items pi
JOIN clinical.stock_transactions st
  ON st.batch_id = pi.batch_id AND st.ref_id = pi.prescription_id
 AND st.type = 1 AND st.quantity <= -pi.quantity
WHERE pi.batch_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM clinical.prescription_dispenses pd WHERE pd.prescription_item_id = pi.id);
GO

IF NOT EXISTS (SELECT 1 FROM auth.roles WHERE name = 'Pharmacist')
    INSERT INTO auth.roles (name) VALUES ('Pharmacist');
GO
