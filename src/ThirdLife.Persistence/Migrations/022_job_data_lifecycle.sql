CREATE TABLE job_local_files (
    job_id TEXT NOT NULL REFERENCES jobs(job_id),
    kind INTEGER NOT NULL CHECK (kind IN (1, 2)),
    file_name TEXT NOT NULL CHECK (length(file_name) BETWEEN 1 AND 64),
    byte_length INTEGER NOT NULL CHECK (byte_length BETWEEN 1 AND 8388608),
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64 AND sha256 NOT GLOB '*[^0-9a-f]*'),
    PRIMARY KEY (kind, file_name)
) WITHOUT ROWID;

CREATE INDEX ix_job_local_files_job ON job_local_files(job_id);

CREATE TABLE job_deletion_approvals (
    job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(job_id),
    checkpoint_id TEXT NOT NULL REFERENCES store_checkpoints(checkpoint_id),
    manifest_sha256 TEXT NOT NULL CHECK (length(manifest_sha256) = 64 AND manifest_sha256 NOT GLOB '*[^0-9a-f]*'),
    operator_id TEXT NOT NULL CHECK (length(operator_id) BETWEEN 1 AND 128),
    approved_at_utc TEXT NOT NULL CHECK (length(approved_at_utc) BETWEEN 20 AND 35)
) WITHOUT ROWID;
