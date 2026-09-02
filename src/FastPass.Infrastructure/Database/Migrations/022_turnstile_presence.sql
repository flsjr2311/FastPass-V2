-- Presença de catracas vistas via MQTT (independente de cadastro em fp_devices).
-- Alimentada pelo Worker a cada telemetria (status/keepalive/info). Permite o painel
-- de monitoramento mostrar catracas online mesmo antes de serem atribuídas a uma portaria.
CREATE TABLE IF NOT EXISTS fp_turnstile_presence (
    device_id VARCHAR(120) NOT NULL COMMENT 'Nome MQTT da catraca (Device ID). Casa com fp_devices.identifier',
    status VARCHAR(20) NOT NULL DEFAULT 'connected' COMMENT 'connected | disconnected',
    first_seen_at DATETIME(6) NOT NULL,
    last_seen_at DATETIME(6) NOT NULL,
    firmware VARCHAR(40) NULL,
    board_id VARCHAR(60) NULL,
    serial_id VARCHAR(60) NULL,
    ip_local VARCHAR(45) NULL,
    media VARCHAR(20) NULL COMMENT 'wired | wifi etc.',
    PRIMARY KEY (device_id),
    KEY ix_fp_turnstile_presence_last_seen (last_seen_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
