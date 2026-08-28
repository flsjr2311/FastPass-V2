-- Migration 017: campo de LOTE (batch) livre no ticket.
-- As empresas trazem o lote na planilha de importação (ex.: "Lote 1", "Pré-venda", "VIP Camarote").
-- Diferente de fp_ticket_batches (gestão formal de lotes) e fp_ticket_imports (arquivo importado),
-- este é apenas o texto do lote que veio no CSV, para consulta e filtro.

ALTER TABLE fp_tickets
    ADD COLUMN batch_name VARCHAR(160) NULL DEFAULT NULL;

ALTER TABLE fp_tickets
    ADD KEY ix_fp_tickets_batch_name (batch_name);

-- Rastreabilidade: guarda o lote bruto que veio na linha do CSV
ALTER TABLE fp_ticket_import_rows
    ADD COLUMN batch_name_raw VARCHAR(160) NULL DEFAULT NULL;
