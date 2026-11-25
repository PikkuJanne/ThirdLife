CREATE TABLE reboot_checkpoints (
    job_id TEXT NOT NULL,
    checkpoint_id TEXT NOT NULL CHECK (
        typeof(checkpoint_id) = 'text' AND length(checkpoint_id) = 36
        AND length(CAST(checkpoint_id AS BLOB)) = 36
        AND substr(checkpoint_id, 9, 1) = '-' AND substr(checkpoint_id, 14, 1) = '-'
        AND substr(checkpoint_id, 19, 1) = '-' AND substr(checkpoint_id, 24, 1) = '-'
        AND length(replace(checkpoint_id, '-', '')) = 32
        AND replace(checkpoint_id, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND checkpoint_id <> '00000000-0000-0000-0000-000000000000'
    ),
    plan_sha256 TEXT NOT NULL,
    impact_sha256 TEXT NOT NULL,
    journal_entry_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
        CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 4096),
    payload_sha256 TEXT NOT NULL
        CHECK (typeof(payload_sha256) = 'text' AND length(payload_sha256) = 64
            AND length(CAST(payload_sha256 AS BLOB)) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    authenticity_blob BLOB NOT NULL
        CHECK (typeof(authenticity_blob) = 'blob' AND length(authenticity_blob) BETWEEN 1 AND 2048),
    PRIMARY KEY (job_id, checkpoint_id),
    UNIQUE (job_id, journal_entry_id),
    FOREIGN KEY (job_id, plan_sha256, impact_sha256)
        REFERENCES action_journal_plans(job_id, plan_sha256, impact_sha256),
    FOREIGN KEY (job_id, journal_entry_id) REFERENCES action_journal_entries(job_id, entry_id)
);

CREATE INDEX ix_reboot_checkpoints_plan
    ON reboot_checkpoints(job_id, plan_sha256, impact_sha256);

CREATE TABLE reboot_checkpoint_dispositions (
    job_id TEXT NOT NULL,
    checkpoint_id TEXT NOT NULL,
    disposition INTEGER NOT NULL
        CHECK (typeof(disposition) = 'integer' AND disposition IN (1, 2)),
    decided_at_utc TEXT NOT NULL CHECK (length(decided_at_utc) BETWEEN 20 AND 35),
    payload_json TEXT NOT NULL
        CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 4096),
    payload_sha256 TEXT NOT NULL
        CHECK (typeof(payload_sha256) = 'text' AND length(payload_sha256) = 64
            AND length(CAST(payload_sha256 AS BLOB)) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, checkpoint_id),
    FOREIGN KEY (job_id, checkpoint_id) REFERENCES reboot_checkpoints(job_id, checkpoint_id)
);
