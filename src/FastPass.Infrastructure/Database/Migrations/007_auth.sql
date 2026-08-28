-- Adiciona colunas de autenticação em fp_users (password_hash já existe desde a 001)
ALTER TABLE fp_users ADD COLUMN failed_attempts INT NOT NULL DEFAULT 0 AFTER password_hash;

ALTER TABLE fp_users ADD COLUMN blocked_until DATETIME(6) NULL AFTER failed_attempts;

ALTER TABLE fp_users ADD COLUMN last_login_at DATETIME(6) NULL AFTER blocked_until;

-- Sessões autenticadas (token por cookie, só o hash SHA-256 é armazenado)
CREATE TABLE IF NOT EXISTS fp_sessions (
    id CHAR(36) NOT NULL,
    user_id CHAR(36) NOT NULL,
    token_hash CHAR(64) NOT NULL COMMENT 'SHA-256 hex do token enviado ao cliente',
    ip VARCHAR(64) NULL,
    user_agent VARCHAR(512) NULL,
    created_at DATETIME(6) NOT NULL,
    last_used_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    revoked_at DATETIME(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_sessions_token (token_hash),
    KEY ix_fp_sessions_user (user_id),
    KEY ix_fp_sessions_expires (expires_at),
    CONSTRAINT fk_fp_sessions_user FOREIGN KEY (user_id) REFERENCES fp_users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
