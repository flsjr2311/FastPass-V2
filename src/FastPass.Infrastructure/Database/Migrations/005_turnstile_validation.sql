ALTER TABLE fp_event_gates
    ADD COLUMN operation_mode VARCHAR(40) NOT NULL DEFAULT 'EntryAndExitValidated' AFTER active;

ALTER TABLE fp_tickets
    ADD COLUMN maximum_entries INT NOT NULL DEFAULT 1 AFTER maximum_uses;

ALTER TABLE fp_tickets
    ADD COLUMN entries_used INT NOT NULL DEFAULT 0 AFTER maximum_entries;

ALTER TABLE fp_tickets
    ADD COLUMN people_inside INT NOT NULL DEFAULT 0 AFTER entries_used;

UPDATE fp_tickets
SET maximum_entries = GREATEST(maximum_uses, 1),
    entries_used = LEAST(uses, GREATEST(maximum_uses, 1));

ALTER TABLE fp_access_attempts
    ADD COLUMN channel VARCHAR(20) NOT NULL DEFAULT 'Turnstile' AFTER device_id;

ALTER TABLE fp_access_attempts
    ADD COLUMN arm_action VARCHAR(30) NOT NULL DEFAULT 'None' AFTER decision;

ALTER TABLE fp_access_attempts
    ADD COLUMN pictogram VARCHAR(40) NOT NULL DEFAULT 'None' AFTER arm_action;

ALTER TABLE fp_access_attempts
    ADD COLUMN entries_before INT NULL AFTER pictogram;

ALTER TABLE fp_access_attempts
    ADD COLUMN entries_after INT NULL AFTER entries_before;

ALTER TABLE fp_access_attempts
    ADD COLUMN people_inside_before INT NULL AFTER entries_after;

ALTER TABLE fp_access_attempts
    ADD COLUMN people_inside_after INT NULL AFTER people_inside_before;
