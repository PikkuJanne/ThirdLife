CREATE TABLE windows_update_journal (
    job_id TEXT NOT NULL,
    sequence INTEGER NOT NULL CHECK (sequence BETWEEN 1 AND 128),
    entry_id TEXT NOT NULL UNIQUE CHECK (length(entry_id) = 36),
    gate_id TEXT NOT NULL,
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 32768),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, sequence),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id),
    FOREIGN KEY (gate_id) REFERENCES sanitization_gate_decisions(decision_id)
) WITHOUT ROWID;
