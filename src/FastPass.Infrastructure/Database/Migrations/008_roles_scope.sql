-- Marca perfis de sistema como imutáveis (somente o seed cria com system_role = 1)
ALTER TABLE fp_roles ADD COLUMN system_role TINYINT(1) NOT NULL DEFAULT 0 AFTER active;

-- Modo de escopo de eventos por usuário: TODOS | ATIVOS | ESPECIFICOS
ALTER TABLE fp_users ADD COLUMN event_scope_mode VARCHAR(20) NOT NULL DEFAULT 'TODOS' AFTER active;

-- Portarias autorizadas explicitamente por usuário (usado quando sem escopo.global)
CREATE TABLE IF NOT EXISTS fp_user_gates (
    user_id CHAR(36) NOT NULL,
    gate_id CHAR(36) NOT NULL,
    PRIMARY KEY (user_id, gate_id),
    CONSTRAINT fk_fp_user_gates_user FOREIGN KEY (user_id) REFERENCES fp_users (id),
    CONSTRAINT fk_fp_user_gates_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Eventos específicos por usuário (usado quando event_scope_mode = 'ESPECIFICOS')
CREATE TABLE IF NOT EXISTS fp_user_events (
    user_id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    PRIMARY KEY (user_id, event_id),
    CONSTRAINT fk_fp_user_events_user FOREIGN KEY (user_id) REFERENCES fp_users (id),
    CONSTRAINT fk_fp_user_events_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
