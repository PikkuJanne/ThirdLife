ALTER TABLE jobs ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0
    CHECK (is_archived IN (0, 1));

ALTER TABLE jobs ADD COLUMN archived_at_utc TEXT NULL
    CHECK (archived_at_utc IS NULL OR length(archived_at_utc) BETWEEN 20 AND 35);

CREATE TABLE evidence_records (
    evidence_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    evidence_id TEXT NOT NULL UNIQUE
        CHECK (length(evidence_id) BETWEEN 1 AND 128),
    job_id TEXT NOT NULL,
    evidence_kind TEXT NOT NULL
        CHECK (evidence_kind IN ('observation', 'sanitization', 'human_test')),
    domain_record_id TEXT NOT NULL
        CHECK (length(domain_record_id) BETWEEN 1 AND 128),
    collected_at_utc TEXT NOT NULL
        CHECK (length(collected_at_utc) BETWEEN 20 AND 35),
    payload_json TEXT NOT NULL
        CHECK (length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 65536),
    payload_sha256 TEXT NOT NULL
        CHECK (length(payload_sha256) = 64),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id),
    UNIQUE (job_id, evidence_kind, domain_record_id)
);

CREATE INDEX ix_evidence_records_job_sequence
    ON evidence_records(job_id, evidence_sequence);

CREATE TABLE store_checkpoints (
    checkpoint_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    checkpoint_id TEXT NOT NULL UNIQUE
        CHECK (length(checkpoint_id) BETWEEN 1 AND 128),
    job_id TEXT NOT NULL,
    checkpoint_kind TEXT NOT NULL
        CHECK (checkpoint_kind IN ('job_created', 'evidence_committed', 'archived', 'restored')),
    recorded_at_utc TEXT NOT NULL
        CHECK (length(recorded_at_utc) BETWEEN 20 AND 35),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id)
);

CREATE INDEX ix_store_checkpoints_job_sequence
    ON store_checkpoints(job_id, checkpoint_sequence);
