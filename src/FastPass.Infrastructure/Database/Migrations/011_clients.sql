-- Clientes/organizadores
CREATE TABLE IF NOT EXISTS fp_clients (
    id CHAR(36) NOT NULL,
    name VARCHAR(160) NOT NULL,
    document VARCHAR(30) NULL COMMENT 'CPF ou CNPJ (somente dígitos)',
    email VARCHAR(190) NULL,
    phone VARCHAR(30) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    notes VARCHAR(500) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_clients_name (name),
    KEY ix_fp_clients_active (active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Víncula cada evento a um cliente (NULL = evento sem cliente definido, legado)
ALTER TABLE fp_events ADD COLUMN client_id CHAR(36) NULL AFTER venue_id;

ALTER TABLE fp_events ADD KEY ix_fp_events_client (client_id);

ALTER TABLE fp_events ADD CONSTRAINT fk_fp_events_client
    FOREIGN KEY (client_id) REFERENCES fp_clients (id);
