-- Migration: separar o modo fisico da catraca do modo de validacao da portaria
-- Data: 2026-09-16
--
-- Problema corrigido:
--   fp_event_gates.operation_mode guardava DOIS conceitos distintos na mesma coluna.
--     GateOperationMode      -> EntryValidatedExitFree | EntryAndExitValidated
--                               (politica de validacao: como a portaria trata entrada/saida)
--     TurnstileOperationMode -> Active | Free | Blocked
--                               (modo fisico da catraca)
--   SetGateTurnstileModeAsync gravava o modo da catraca nessa coluna. A partir dai,
--   MySqlAccessValidationService.ReadGateAsync nao conseguia mais interpretar o valor
--   e toda validacao de ingresso naquela portaria passava a falhar com
--   "Modo operacional invalido configurado para a portaria."
--
-- Correcao: coluna dedicada turnstile_mode, e operation_mode volta ao seu dominio.
-- A migration 030 tentava criar operation_mode em fp_event_gates, mas a coluna ja
-- existia desde a 005 -- o erro 1060 era tolerado pelo migrator e passava batido.

ALTER TABLE fp_event_gates
ADD COLUMN turnstile_mode VARCHAR(20) NOT NULL DEFAULT 'Active'
COMMENT 'Modo fisico da catraca: Active, Free ou Blocked';

-- Preserva o modo de catraca que estava gravado na coluna errada
UPDATE fp_event_gates
SET turnstile_mode = operation_mode
WHERE operation_mode IN ('Active', 'Free', 'Blocked');

-- Devolve operation_mode ao dominio de politica de validacao.
-- Portarias contaminadas perderam o valor original, entao voltam para o default do schema.
UPDATE fp_event_gates
SET operation_mode = 'EntryAndExitValidated'
WHERE operation_mode IN ('Active', 'Free', 'Blocked');

CREATE INDEX idx_event_gates_turnstile_mode ON fp_event_gates(turnstile_mode);
