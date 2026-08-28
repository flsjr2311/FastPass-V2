-- Migration 016: código sequencial legível para eventos, portarias e setores.
-- O `id` (GUID) continua sendo a chave interna e usada pela API/devices via id.
-- O `code` é um número curto legível, útil para digitação/seleção rápida:
--   - fp_events.code  → sequencial GLOBAL (1, 2, 3... no sistema)
--   - fp_gates.code_num e fp_sectors.code_num → sequencial POR EVENTO
--
-- Observação: fp_gates já tem uma coluna `code` (texto, código físico da portaria).
-- Por isso a coluna numérica legível chama-se `code_num` para não colidir.
-- O backfill dos valores é feito pela aplicação (DatabaseMigrator.BackfillReadableCodesAsync)
-- porque o migrator executa statement a statement e não suporta variáveis de sessão.

ALTER TABLE fp_events
    ADD COLUMN code INT NULL DEFAULT NULL;

ALTER TABLE fp_gates
    ADD COLUMN code_num INT NULL DEFAULT NULL;

ALTER TABLE fp_sectors
    ADD COLUMN code_num INT NULL DEFAULT NULL;
