-- A cold boot is requested against the exact saved final maintenance boundary.
CREATE TABLE cold_boot_checkpoints (
    job_id TEXT NOT NULL,
    sequence INTEGER NOT NULL CHECK (sequence BETWEEN 1 AND 64),
    maintenance_revision INTEGER NOT NULL,
    checkpoint_id TEXT NOT NULL UNIQUE,
    payload_json TEXT NOT NULL CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 8192),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    authenticator BLOB NOT NULL CHECK (typeof(authenticator) = 'blob' AND length(authenticator) BETWEEN 1 AND 1024),
    PRIMARY KEY (job_id, sequence),
    FOREIGN KEY (job_id, maintenance_revision) REFERENCES maintenance_checkpoints(job_id, revision)
) WITHOUT ROWID;
