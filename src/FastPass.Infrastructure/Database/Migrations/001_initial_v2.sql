CREATE TABLE IF NOT EXISTS fp_schema_migrations (
    version VARCHAR(100) NOT NULL,
    applied_at DATETIME(6) NOT NULL,
    PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_venues (
    id CHAR(36) NOT NULL,
    name VARCHAR(160) NOT NULL,
    address VARCHAR(255) NULL,
    city VARCHAR(120) NULL,
    state VARCHAR(80) NULL,
    capacity INT NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_venues_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_events (
    id CHAR(36) NOT NULL,
    venue_id CHAR(36) NOT NULL,
    name VARCHAR(200) NOT NULL,
    organizer VARCHAR(160) NULL,
    starts_at DATETIME(6) NOT NULL,
    ends_at DATETIME(6) NOT NULL,
    status VARCHAR(30) NOT NULL,
    settings_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_events_venue (venue_id),
    KEY ix_fp_events_status (status),
    CONSTRAINT fk_fp_events_venue FOREIGN KEY (venue_id) REFERENCES fp_venues (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_sectors (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    capacity INT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_sectors_event_name (event_id, name),
    CONSTRAINT fk_fp_sectors_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_gates (
    id CHAR(36) NOT NULL,
    venue_id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    code VARCHAR(60) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_gates_venue_code (venue_id, code),
    KEY ix_fp_gates_venue (venue_id),
    CONSTRAINT fk_fp_gates_venue FOREIGN KEY (venue_id) REFERENCES fp_venues (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_devices (
    id CHAR(36) NOT NULL,
    gate_id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    identifier VARCHAR(120) NULL,
    device_type VARCHAR(30) NOT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    last_seen_at DATETIME(6) NULL,
    configuration_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_devices_identifier (identifier),
    KEY ix_fp_devices_gate (gate_id),
    CONSTRAINT fk_fp_devices_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_event_gates (
    event_id CHAR(36) NOT NULL,
    gate_id CHAR(36) NOT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (event_id, gate_id),
    CONSTRAINT fk_fp_event_gates_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_event_gates_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_gate_sectors (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    gate_id CHAR(36) NOT NULL,
    sector_id CHAR(36) NOT NULL,
    direction VARCHAR(10) NOT NULL,
    active_from DATETIME(6) NULL,
    active_until DATETIME(6) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_gate_sectors_rule (event_id, gate_id, sector_id, direction),
    KEY ix_fp_gate_sectors_gate (gate_id),
    KEY ix_fp_gate_sectors_sector (sector_id),
    CONSTRAINT fk_fp_gate_sectors_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_gate_sectors_gate FOREIGN KEY (event_id, gate_id) REFERENCES fp_event_gates (event_id, gate_id),
    CONSTRAINT fk_fp_gate_sectors_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_ticket_types (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    allows_reentry TINYINT(1) NOT NULL DEFAULT 0,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_ticket_types_event_name (event_id, name),
    CONSTRAINT fk_fp_ticket_types_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_ticket_batches (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    ticket_type_id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    maximum_quantity INT NULL,
    sales_starts_at DATETIME(6) NULL,
    sales_ends_at DATETIME(6) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_ticket_batches_event_name (event_id, name),
    CONSTRAINT fk_fp_ticket_batches_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_ticket_batches_type FOREIGN KEY (ticket_type_id) REFERENCES fp_ticket_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_tickets (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    batch_id CHAR(36) NULL,
    ticket_type_id CHAR(36) NOT NULL,
    external_id VARCHAR(160) NOT NULL,
    code VARCHAR(160) NOT NULL,
    maximum_uses INT NOT NULL DEFAULT 1,
    uses INT NOT NULL DEFAULT 0,
    status VARCHAR(30) NOT NULL DEFAULT 'active',
    metadata_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_tickets_event_external (event_id, external_id),
    UNIQUE KEY uq_fp_tickets_event_code (event_id, code),
    KEY ix_fp_tickets_batch (batch_id),
    KEY ix_fp_tickets_type (ticket_type_id),
    CONSTRAINT fk_fp_tickets_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_tickets_batch FOREIGN KEY (batch_id) REFERENCES fp_ticket_batches (id),
    CONSTRAINT fk_fp_tickets_type FOREIGN KEY (ticket_type_id) REFERENCES fp_ticket_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_access_policies (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    ticket_type_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    gate_id CHAR(36) NULL,
    sector_id CHAR(36) NULL,
    direction VARCHAR(10) NOT NULL,
    priority INT NOT NULL DEFAULT 0,
    allows TINYINT(1) NOT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    conditions_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_access_policies_event (event_id),
    KEY ix_fp_access_policies_type (ticket_type_id),
    KEY ix_fp_access_policies_batch (batch_id),
    KEY ix_fp_access_policies_gate (gate_id),
    KEY ix_fp_access_policies_sector (sector_id),
    CONSTRAINT fk_fp_access_policies_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_access_policies_type FOREIGN KEY (ticket_type_id) REFERENCES fp_ticket_types (id),
    CONSTRAINT fk_fp_access_policies_batch FOREIGN KEY (batch_id) REFERENCES fp_ticket_batches (id),
    CONSTRAINT fk_fp_access_policies_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id),
    CONSTRAINT fk_fp_access_policies_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_access_attempts (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    ticket_id CHAR(36) NULL,
    gate_id CHAR(36) NOT NULL,
    device_id CHAR(36) NULL,
    credential_code VARCHAR(160) NOT NULL,
    direction VARCHAR(10) NOT NULL,
    decision VARCHAR(20) NOT NULL,
    reason VARCHAR(255) NULL,
    status VARCHAR(30) NOT NULL,
    idempotency_key VARCHAR(160) NOT NULL,
    requested_at DATETIME(6) NOT NULL,
    device_result_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_access_attempts_idempotency (idempotency_key),
    KEY ix_fp_access_attempts_event_time (event_id, requested_at),
    KEY ix_fp_access_attempts_ticket (ticket_id),
    KEY ix_fp_access_attempts_gate (gate_id),
    CONSTRAINT fk_fp_access_attempts_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_access_attempts_ticket FOREIGN KEY (ticket_id) REFERENCES fp_tickets (id),
    CONSTRAINT fk_fp_access_attempts_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id),
    CONSTRAINT fk_fp_access_attempts_device FOREIGN KEY (device_id) REFERENCES fp_devices (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_users (
    id CHAR(36) NOT NULL,
    user_name VARCHAR(120) NOT NULL,
    display_name VARCHAR(160) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_users_user_name (user_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_roles (
    id CHAR(36) NOT NULL,
    name VARCHAR(120) NOT NULL,
    description VARCHAR(255) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_roles_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_permissions (
    id CHAR(36) NOT NULL,
    code VARCHAR(120) NOT NULL,
    description VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_permissions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_user_roles (
    user_id CHAR(36) NOT NULL,
    role_id CHAR(36) NOT NULL,
    PRIMARY KEY (user_id, role_id),
    CONSTRAINT fk_fp_user_roles_user FOREIGN KEY (user_id) REFERENCES fp_users (id),
    CONSTRAINT fk_fp_user_roles_role FOREIGN KEY (role_id) REFERENCES fp_roles (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_role_permissions (
    role_id CHAR(36) NOT NULL,
    permission_id CHAR(36) NOT NULL,
    PRIMARY KEY (role_id, permission_id),
    CONSTRAINT fk_fp_role_permissions_role FOREIGN KEY (role_id) REFERENCES fp_roles (id),
    CONSTRAINT fk_fp_role_permissions_permission FOREIGN KEY (permission_id) REFERENCES fp_permissions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_user_event_scopes (
    id CHAR(36) NOT NULL,
    user_id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    gate_id CHAR(36) NULL,
    sector_id CHAR(36) NULL,
    PRIMARY KEY (id),
    KEY ix_fp_user_event_scopes_scope (user_id, event_id, gate_id, sector_id),
    CONSTRAINT fk_fp_user_event_scopes_user FOREIGN KEY (user_id) REFERENCES fp_users (id),
    CONSTRAINT fk_fp_user_event_scopes_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_user_event_scopes_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id),
    CONSTRAINT fk_fp_user_event_scopes_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_audit_events (
    id CHAR(36) NOT NULL,
    user_id CHAR(36) NULL,
    event_id CHAR(36) NULL,
    action VARCHAR(120) NOT NULL,
    entity_name VARCHAR(120) NOT NULL,
    entity_id VARCHAR(120) NULL,
    before_json JSON NULL,
    after_json JSON NULL,
    occurred_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_audit_events_event_time (event_id, occurred_at),
    KEY ix_fp_audit_events_user_time (user_id, occurred_at),
    CONSTRAINT fk_fp_audit_events_user FOREIGN KEY (user_id) REFERENCES fp_users (id),
    CONSTRAINT fk_fp_audit_events_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_system_logs (
    id CHAR(36) NOT NULL,
    level VARCHAR(20) NOT NULL,
    category VARCHAR(120) NOT NULL,
    message VARCHAR(1000) NOT NULL,
    correlation_id VARCHAR(120) NULL,
    details_json JSON NULL,
    occurred_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_system_logs_time (occurred_at),
    KEY ix_fp_system_logs_correlation (correlation_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_sync_outbox (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NULL,
    aggregate_type VARCHAR(120) NOT NULL,
    aggregate_id VARCHAR(120) NOT NULL,
    operation VARCHAR(30) NOT NULL,
    idempotency_key VARCHAR(160) NOT NULL,
    payload_json JSON NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'pending',
    attempts INT NOT NULL DEFAULT 0,
    next_attempt_at DATETIME(6) NULL,
    last_error VARCHAR(1000) NULL,
    sent_at DATETIME(6) NULL,
    created_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_sync_outbox_idempotency (idempotency_key),
    KEY ix_fp_sync_outbox_status (status, next_attempt_at),
    CONSTRAINT fk_fp_sync_outbox_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_backup_jobs (
    id CHAR(36) NOT NULL,
    destination VARCHAR(255) NOT NULL,
    file_name VARCHAR(255) NULL,
    status VARCHAR(30) NOT NULL,
    size_bytes BIGINT NULL,
    checksum VARCHAR(128) NULL,
    error_message VARCHAR(1000) NULL,
    started_at DATETIME(6) NOT NULL,
    finished_at DATETIME(6) NULL,
    PRIMARY KEY (id),
    KEY ix_fp_backup_jobs_started (started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
