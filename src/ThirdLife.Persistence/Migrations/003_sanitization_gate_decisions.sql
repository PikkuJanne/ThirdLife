CREATE UNIQUE INDEX ux_evidence_records_job_evidence
    ON evidence_records(job_id, evidence_id);

CREATE TABLE sanitization_gate_decisions (
    decision_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    decision_id TEXT NOT NULL UNIQUE
        CHECK (length(decision_id) BETWEEN 1 AND 128),
    job_id TEXT NOT NULL,
    evidence_id TEXT NOT NULL,
    policy_version TEXT NOT NULL
        CHECK (length(policy_version) BETWEEN 1 AND 128),
    outcome TEXT NOT NULL
        CHECK (outcome IN ('allow_assessment', 'blocked')),
    reason_code TEXT NOT NULL
        CHECK (reason_code IN (
            'sanitization_verified',
            'replacement_storage_verified',
            'no_donor_storage_verified',
            'sanitization_unknown',
            'sanitization_failed')),
    evaluated_at_utc TEXT NOT NULL
        CHECK (length(evaluated_at_utc) BETWEEN 20 AND 35),
    payload_json TEXT NOT NULL
        CHECK (length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 65536),
    payload_sha256 TEXT NOT NULL
        CHECK (length(payload_sha256) = 64),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id),
    FOREIGN KEY (job_id, evidence_id) REFERENCES evidence_records(job_id, evidence_id),
    UNIQUE (job_id, evidence_id)
);

CREATE INDEX ix_sanitization_gate_decisions_job_sequence
    ON sanitization_gate_decisions(job_id, decision_sequence);
