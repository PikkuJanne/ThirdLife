-- Each reviewed maintenance boundary retains the exact plan, approval and independent observations.
CREATE TABLE maintenance_checkpoints (
    job_id TEXT NOT NULL,
    revision INTEGER NOT NULL CHECK (typeof(revision) = 'integer' AND revision BETWEEN 1 AND 1024),
    plan_sha256 TEXT NOT NULL,
    impact_sha256 TEXT NOT NULL,
    approval_id TEXT NOT NULL,
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 4096),
    payload_sha256 TEXT NOT NULL CHECK (typeof(payload_sha256) = 'text' AND length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, revision),
    FOREIGN KEY (job_id, plan_sha256, impact_sha256) REFERENCES action_journal_plans(job_id, plan_sha256, impact_sha256),
    FOREIGN KEY (job_id, approval_id) REFERENCES action_journal_approvals(job_id, approval_id)
);
