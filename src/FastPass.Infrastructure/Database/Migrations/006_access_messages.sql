CREATE TABLE IF NOT EXISTS fp_access_messages (
    id CHAR(36) NOT NULL,
    event_id CHAR(36) NOT NULL,
    code VARCHAR(80) NOT NULL,
    title VARCHAR(120) NOT NULL,
    message VARCHAR(500) NOT NULL,
    background_start CHAR(7) NOT NULL DEFAULT '#EB3349',
    background_end CHAR(7) NOT NULL DEFAULT '#F45C43',
    title_color CHAR(7) NOT NULL DEFAULT '#FFFFFF',
    message_color CHAR(7) NOT NULL DEFAULT '#FFFFFF',
    title_size VARCHAR(20) NOT NULL DEFAULT '28dp',
    message_size VARCHAR(20) NOT NULL DEFAULT '18dp',
    title_bold TINYINT(1) NOT NULL DEFAULT 1,
    message_bold TINYINT(1) NOT NULL DEFAULT 1,
    active TINYINT(1) NOT NULL DEFAULT 1,
    system_message TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fp_access_messages_event_code (event_id, code),
    KEY ix_fp_access_messages_event (event_id, active),
    CONSTRAINT fk_fp_access_messages_event FOREIGN KEY (event_id) REFERENCES fp_events (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE fp_access_attempts
    ADD COLUMN reason_code VARCHAR(80) NULL AFTER reason;

ALTER TABLE fp_access_attempts
    ADD COLUMN message VARCHAR(500) NULL AFTER reason_code;

ALTER TABLE fp_access_attempts
    ADD COLUMN message_presentation_json JSON NULL AFTER message;
