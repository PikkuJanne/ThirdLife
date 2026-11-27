-- Each confirmation retains its actor and exact job-state binding.
CREATE TABLE finalization_confirmations (
    job_id TEXT NOT NULL,
    sequence INTEGER NOT NULL CHECK (typeof(sequence) = 'integer' AND sequence BETWEEN 1 AND 512),
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 1024),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, sequence),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id)
) WITHOUT ROWID;
