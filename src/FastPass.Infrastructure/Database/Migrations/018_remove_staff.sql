-- Migration 018: remoção completa da função de STAFF.
-- O crachá de acesso físico agora pertence ao usuário (fp_users.access_badge_code),
-- validado diretamente pela tabela de usuários. As tabelas de staff não são mais usadas.
--
-- IMPORTANTE: fp_access_attempts.staff_credential_id é PRESERVADA (agora guarda o user_id
-- de quem validou por crachá), mas a FK para fp_staff_credentials é removida.
--
-- Erros 1091 (drop de item inexistente) são tolerados pelo migrator (re-execução).

-- 1) Remove a FK de access_attempts para staff_credentials
ALTER TABLE fp_access_attempts
    DROP FOREIGN KEY fk_fp_access_attempts_staff_credential;

-- 2) Remove a coluna staff_member_id de fp_users (sem FK)
ALTER TABLE fp_users
    DROP COLUMN staff_member_id;

-- 3) Remove as tabelas de staff (ordem respeita dependências)
DROP TABLE IF EXISTS fp_staff_event_access;

DROP TABLE IF EXISTS fp_staff_credentials;

DROP TABLE IF EXISTS fp_staff_members;
