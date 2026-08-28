-- Migration 015: vínculo direto entre ticket e setor.
-- Sem isso, os relatórios "por setor" não têm como saber quantos ingressos
-- pertencem a cada setor (Camarote, Pista, VIP etc.) — hoje contam o evento inteiro.

ALTER TABLE fp_tickets
    ADD COLUMN sector_id CHAR(36) NULL DEFAULT NULL;

ALTER TABLE fp_tickets
    ADD CONSTRAINT fk_fp_tickets_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id);

ALTER TABLE fp_tickets
    ADD KEY ix_fp_tickets_sector (sector_id);

-- Rastreabilidade: qual nome de setor veio na linha do CSV (útil para auditoria/depuração)
ALTER TABLE fp_ticket_import_rows
    ADD COLUMN sector_id CHAR(36) NULL DEFAULT NULL;

ALTER TABLE fp_ticket_import_rows
    ADD COLUMN sector_name_raw VARCHAR(160) NULL DEFAULT NULL;
