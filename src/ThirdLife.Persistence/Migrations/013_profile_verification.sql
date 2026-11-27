-- Final verification is independent of the evidence cited by the approved plan.
CREATE TABLE profile_verifications (
    job_id TEXT NOT NULL,
    sequence INTEGER NOT NULL CHECK (sequence BETWEEN 1 AND 64),
    maintenance_revision INTEGER NOT NULL,
    scan_id TEXT NOT NULL UNIQUE,
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 65536),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, sequence),
    FOREIGN KEY (job_id, maintenance_revision) REFERENCES maintenance_checkpoints(job_id, revision),
    FOREIGN KEY (scan_id) REFERENCES windows_update_journal(entry_id)
) WITHOUT ROWID;
