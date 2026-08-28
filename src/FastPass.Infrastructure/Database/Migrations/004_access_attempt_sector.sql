ALTER TABLE fp_access_attempts
    ADD COLUMN sector_id CHAR(36) NULL AFTER gate_id;

ALTER TABLE fp_access_attempts
    ADD KEY ix_fp_access_attempts_sector (sector_id);

ALTER TABLE fp_access_attempts
    ADD CONSTRAINT fk_fp_access_attempts_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id);
