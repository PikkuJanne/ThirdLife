CREATE TABLE schema_migrations (
    version INTEGER NOT NULL PRIMARY KEY,
    migration_name TEXT NOT NULL UNIQUE,
    script_sha256 TEXT NOT NULL
        CHECK (length(script_sha256) = 64),
    schema_sha256 TEXT NOT NULL
        CHECK (length(schema_sha256) = 64),
    applied_at_utc TEXT NOT NULL
        CHECK (length(applied_at_utc) BETWEEN 20 AND 35)
) WITHOUT ROWID;

CREATE TABLE jobs (
    job_id TEXT NOT NULL PRIMARY KEY
        CHECK (length(job_id) BETWEEN 1 AND 128),
    device_id TEXT NOT NULL
        CHECK (length(device_id) BETWEEN 1 AND 128),
    created_at_utc TEXT NOT NULL
        CHECK (length(created_at_utc) BETWEEN 20 AND 35),
    job_payload_json TEXT NOT NULL
        CHECK (length(CAST(job_payload_json AS BLOB)) BETWEEN 2 AND 65536),
    job_payload_sha256 TEXT NOT NULL
        CHECK (length(job_payload_sha256) = 64)
) WITHOUT ROWID;
