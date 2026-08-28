CREATE TABLE IF NOT EXISTS fp_staff_members (
    id CHAR(36) NOT NULL,
    employee_code VARCHAR(120) NOT NULL,
    name VARCHAR(180) NOT NULL,
    department VARCHAR(120) NULL,
    job_title VARCHAR(120) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_staff_members_employee_code (employee_code),
    KEY ix_fp_staff_members_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_staff_credentials (
    id CHAR(36) NOT NULL,
    staff_member_id CHAR(36) NOT NULL,
    code VARCHAR(160) NOT NULL,
    credential_type VARCHAR(40) NOT NULL DEFAULT 'employee_badge',
    valid_from DATETIME(6) NULL,
    valid_until DATETIME(6) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_staff_credentials_code (code),
    KEY ix_fp_staff_credentials_staff (staff_member_id),
    CONSTRAINT fk_fp_staff_credentials_staff FOREIGN KEY (staff_member_id) REFERENCES fp_staff_members (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS fp_staff_event_access (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    staff_member_id CHAR(36) NOT NULL,
    gate_id CHAR(36) NULL,
    sector_id CHAR(36) NULL,
    profile VARCHAR(80) NOT NULL DEFAULT 'operator',
    direction VARCHAR(10) NOT NULL DEFAULT 'Entry',
    valid_from DATETIME(6) NULL,
    valid_until DATETIME(6) NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_fp_staff_event_access_event (event_id),
    KEY ix_fp_staff_event_access_staff (staff_member_id),
    KEY ix_fp_staff_event_access_gate (gate_id),
    KEY ix_fp_staff_event_access_sector (sector_id),
    CONSTRAINT fk_fp_staff_event_access_event FOREIGN KEY (event_id) REFERENCES fp_events (id),
    CONSTRAINT fk_fp_staff_event_access_staff FOREIGN KEY (staff_member_id) REFERENCES fp_staff_members (id),
    CONSTRAINT fk_fp_staff_event_access_gate FOREIGN KEY (gate_id) REFERENCES fp_gates (id),
    CONSTRAINT fk_fp_staff_event_access_sector FOREIGN KEY (sector_id) REFERENCES fp_sectors (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE fp_access_attempts
    ADD COLUMN credential_type VARCHAR(20) NOT NULL DEFAULT 'Ticket' AFTER credential_code,
    ADD COLUMN staff_credential_id CHAR(36) NULL AFTER ticket_id,
    ADD KEY ix_fp_access_attempts_staff_credential (staff_credential_id),
    ADD CONSTRAINT fk_fp_access_attempts_staff_credential FOREIGN KEY (staff_credential_id) REFERENCES fp_staff_credentials (id);
