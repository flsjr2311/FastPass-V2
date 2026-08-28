-- Trilha de auditoria de AÇÕES no sistema (criação, edição, exclusão, importação, etc.).
-- Capturada centralizadamente pelo middleware para toda requisição que muda estado
-- (POST/PUT/DELETE/PATCH) e conclui com sucesso. Diferente de fp_access_attempts
-- (acesso físico ao evento) e de fp_login_log (autenticação).
CREATE TABLE IF NOT EXISTS fp_audit_trail (
    id CHAR(36) NOT NULL,
    user_id CHAR(36) NULL COMMENT 'Usuário autenticado que executou a ação (NULL se anônimo)',
    user_name VARCHAR(128) NULL,
    action VARCHAR(128) NOT NULL COMMENT 'Ação legível derivada da rota, ex: Criou usuário',
    method VARCHAR(8) NOT NULL COMMENT 'Método HTTP: POST/PUT/DELETE/PATCH',
    path VARCHAR(512) NOT NULL COMMENT 'Rota chamada, ex: /api/users/{id}',
    target_id VARCHAR(128) NULL COMMENT 'Identificador do alvo extraído da rota',
    status_code INT NOT NULL COMMENT 'HTTP status da resposta',
    summary TEXT NULL COMMENT 'Resumo do corpo da requisição (campos sensíveis mascarados)',
    ip VARCHAR(64) NULL,
    user_agent VARCHAR(512) NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_audit_trail_created (created_at),
    KEY ix_fp_audit_trail_user (user_id),
    KEY ix_fp_audit_trail_action (action)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
