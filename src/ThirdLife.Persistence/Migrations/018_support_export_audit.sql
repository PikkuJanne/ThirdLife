CREATE TABLE support_export_audit (
    job_id TEXT NOT NULL REFERENCES jobs(job_id),
    support_id TEXT NOT NULL CHECK (typeof(support_id) = 'text' AND length(support_id) = 36),
    phase INTEGER NOT NULL CHECK (typeof(phase) = 'integer' AND phase IN (1, 2)),
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 1024),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, support_id, phase)
) WITHOUT ROWID;
