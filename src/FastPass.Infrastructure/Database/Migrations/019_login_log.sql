-- Log de acesso ao SISTEMA (autenticação): tentativas de login bem-sucedidas e falhas.
-- Diferente de fp_access_attempts (acesso físico ao evento).
CREATE TABLE IF NOT EXISTS fp_login_log (
    id CHAR(36) NOT NULL,
    user_id CHAR(36) NULL COMMENT 'Preenchido quando o usuário existe (mesmo em falha de senha)',
    user_name VARCHAR(128) NOT NULL COMMENT 'Nome informado na tentativa (mantido mesmo se o usuário não existir)',
    outcome VARCHAR(32) NOT NULL COMMENT 'Success | InvalidPassword | UnknownUser | Blocked | Inactive',
    ip VARCHAR(64) NULL,
    user_agent VARCHAR(512) NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_login_log_created (created_at),
    KEY ix_fp_login_log_user (user_id),
    KEY ix_fp_login_log_outcome (outcome)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
