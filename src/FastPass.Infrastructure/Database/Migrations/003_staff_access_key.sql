ALTER TABLE fp_staff_event_access
    ADD COLUMN access_key CHAR(64) NULL AFTER id;

UPDATE fp_staff_event_access
SET access_key = SHA2(
    CONCAT(
        event_id,
        ':',
        staff_member_id,
        ':',
        COALESCE(gate_id, '*'),
        ':',
        COALESCE(sector_id, '*'),
        ':',
        profile,
        ':',
        direction
    ),
    256
)
WHERE access_key IS NULL;

ALTER TABLE fp_staff_event_access
    MODIFY COLUMN access_key CHAR(64) NOT NULL,
    ADD UNIQUE KEY uq_fp_staff_event_access_key (access_key);
