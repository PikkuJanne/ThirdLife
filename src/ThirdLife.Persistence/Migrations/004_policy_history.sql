CREATE TABLE policy_snapshots (
    snapshot_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    policy_id TEXT NOT NULL
        CHECK (length(policy_id) BETWEEN 1 AND 64),
    policy_version TEXT NOT NULL
        CHECK (length(policy_version) BETWEEN 1 AND 64),
    policy_sha256 TEXT NOT NULL
        CHECK (length(policy_sha256) = 64 AND policy_sha256 NOT GLOB '*[^0-9a-f]*'),
    policy_json BLOB NOT NULL
        CHECK (typeof(policy_json) = 'blob' AND length(policy_json) BETWEEN 1 AND 65536),
    UNIQUE (policy_id, policy_version),
    UNIQUE (policy_id, policy_version, policy_sha256)
);

CREATE INDEX ix_policy_snapshots_policy_sequence
    ON policy_snapshots(policy_id, snapshot_sequence);

CREATE TABLE policy_decisions (
    decision_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    decision_id TEXT NOT NULL UNIQUE
        CHECK (length(decision_id) BETWEEN 1 AND 128),
    job_id TEXT NOT NULL,
    assessment_id TEXT NOT NULL
        CHECK (length(assessment_id) BETWEEN 1 AND 128),
    policy_id TEXT NOT NULL,
    policy_version TEXT NOT NULL,
    policy_sha256 TEXT NOT NULL,
    evaluated_at_utc TEXT NOT NULL
        CHECK (length(evaluated_at_utc) BETWEEN 20 AND 35),
    previous_decision_id TEXT NULL
        CHECK (previous_decision_id IS NULL OR previous_decision_id <> decision_id),
    payload_json TEXT NOT NULL
        CHECK (length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 2097152),
    payload_sha256 TEXT NOT NULL
        CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id),
    FOREIGN KEY (policy_id, policy_version, policy_sha256)
        REFERENCES policy_snapshots(policy_id, policy_version, policy_sha256),
    FOREIGN KEY (job_id, assessment_id, previous_decision_id)
        REFERENCES policy_decisions(job_id, assessment_id, decision_id),
    UNIQUE (job_id, assessment_id, decision_id),
    UNIQUE (job_id, assessment_id, previous_decision_id)
);

CREATE INDEX ix_policy_decisions_job_assessment_sequence
    ON policy_decisions(job_id, assessment_id, decision_sequence);
