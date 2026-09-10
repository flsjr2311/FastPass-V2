-- Migration: Adicionar operation_mode para catracas individuais
-- Data: 2026-08-27
-- Descrição: Permite controlar o modo de operação de cada catraca:
--   - Active: Ativa (lê ingresso, valida, libera se ok)
--   - Free: Livre (gira para ambos os lados, sem validação)
--   - Blocked: Bloqueada (não gira, totalmente travada)

ALTER TABLE fp_devices
ADD COLUMN operation_mode VARCHAR(50) DEFAULT 'Active'
COMMENT 'Modo operacional: Active (validar), Free (livre), Blocked (travada)';

CREATE INDEX idx_devices_operation_mode ON fp_devices(operation_mode);
