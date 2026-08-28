-- Migration 013: acesso físico por usuário
-- Adiciona colunas em fp_users para suportar crachá/QR de acesso físico à catraca.
-- Erros 1060 (coluna duplicada) são tolerados pelo migrator caso re-executado.

ALTER TABLE fp_users
    ADD COLUMN physical_access_enabled TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE fp_users
    ADD COLUMN access_badge_code VARCHAR(120) NULL DEFAULT NULL;

ALTER TABLE fp_users
    ADD COLUMN staff_member_id CHAR(36) NULL DEFAULT NULL;
