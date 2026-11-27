CREATE TABLE file_history_sample_audit (
    job_id TEXT NOT NULL REFERENCES jobs(job_id),
    sequence INTEGER NOT NULL CHECK (typeof(sequence) = 'integer' AND sequence > 0),
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 1024),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (job_id, sequence)
) WITHOUT ROWID;
