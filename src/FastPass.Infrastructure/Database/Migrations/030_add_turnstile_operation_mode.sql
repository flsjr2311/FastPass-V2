-- Migration: Adicionar operation_mode para portarias e catracas
-- Data: 2026-08-27
-- Descrição: 
--   Portaria (fp_event_gates): Modo padrão para todas as catracas
--   Catraca (fp_devices): Pode override o modo da portaria (NULL = herda da portaria)
--
-- Modos disponíveis:
--   - Active: Ativa (lê ingresso, valida, libera se ok)
--   - Free: Livre (gira para ambos os lados, sem validação)
--   - Blocked: Bloqueada (não gira, totalmente travada)

-- ─────────────────────────────────────────────────────────────────
-- Adicionar operation_mode na portaria (event_gates)
-- ─────────────────────────────────────────────────────────────────
ALTER TABLE fp_event_gates
ADD COLUMN operation_mode VARCHAR(50) DEFAULT 'Active'
COMMENT 'Modo operacional padrão: Active, Free, ou Blocked';

CREATE INDEX idx_event_gates_operation_mode ON fp_event_gates(operation_mode);

-- ─────────────────────────────────────────────────────────────────
-- Adicionar operation_mode_override na catraca (fp_devices)
-- NULL = herda da portaria; caso contrário = override local
-- ─────────────────────────────────────────────────────────────────
ALTER TABLE fp_devices
ADD COLUMN operation_mode VARCHAR(50) NULL
COMMENT 'Override do modo operacional: NULL = herda da portaria, senão Active/Free/Blocked';

CREATE INDEX idx_devices_operation_mode ON fp_devices(operation_mode);

