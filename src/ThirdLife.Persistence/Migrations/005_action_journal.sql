CREATE TABLE action_journal_plans (
    job_id TEXT NOT NULL,
    plan_sha256 TEXT NOT NULL
        CHECK (typeof(plan_sha256) = 'text' AND length(plan_sha256) = 64
            AND length(CAST(plan_sha256 AS BLOB)) = 64 AND plan_sha256 NOT GLOB '*[^0-9a-f]*'),
    impact_sha256 TEXT NOT NULL
        CHECK (typeof(impact_sha256) = 'text' AND length(impact_sha256) = 64
            AND length(CAST(impact_sha256 AS BLOB)) = 64 AND impact_sha256 NOT GLOB '*[^0-9a-f]*'),
    plan_json BLOB NOT NULL
        CHECK (typeof(plan_json) = 'blob' AND length(plan_json) BETWEEN 1 AND 65536),
    impact_json BLOB NOT NULL
        CHECK (typeof(impact_json) = 'blob' AND length(impact_json) BETWEEN 1 AND 16384),
    PRIMARY KEY (job_id, plan_sha256, impact_sha256),
    FOREIGN KEY (job_id) REFERENCES jobs(job_id)
);

CREATE TABLE action_journal_approvals (
    approval_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    job_id TEXT NOT NULL,
    approval_id TEXT NOT NULL CHECK (
        typeof(approval_id) = 'text' AND length(approval_id) = 36
        AND length(CAST(approval_id AS BLOB)) = 36
        AND substr(approval_id, 9, 1) = '-' AND substr(approval_id, 14, 1) = '-'
        AND substr(approval_id, 19, 1) = '-' AND substr(approval_id, 24, 1) = '-'
        AND length(replace(approval_id, '-', '')) = 32
        AND replace(approval_id, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND approval_id <> '00000000-0000-0000-0000-000000000000'
    ),
    plan_sha256 TEXT NOT NULL
        CHECK (typeof(plan_sha256) = 'text' AND length(plan_sha256) = 64
            AND length(CAST(plan_sha256 AS BLOB)) = 64 AND plan_sha256 NOT GLOB '*[^0-9a-f]*'),
    impact_sha256 TEXT NOT NULL
        CHECK (typeof(impact_sha256) = 'text' AND length(impact_sha256) = 64
            AND length(CAST(impact_sha256 AS BLOB)) = 64 AND impact_sha256 NOT GLOB '*[^0-9a-f]*'),
    payload_json TEXT NOT NULL
        CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 4096),
    payload_sha256 TEXT NOT NULL
        CHECK (typeof(payload_sha256) = 'text' AND length(payload_sha256) = 64
            AND length(CAST(payload_sha256 AS BLOB)) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    UNIQUE (job_id, approval_id),
    FOREIGN KEY (job_id, plan_sha256, impact_sha256)
        REFERENCES action_journal_plans(job_id, plan_sha256, impact_sha256)
);

CREATE INDEX ix_action_journal_approvals_job_sequence
    ON action_journal_approvals(job_id, approval_sequence);

CREATE INDEX ix_action_journal_approvals_plan_sequence
    ON action_journal_approvals(job_id, plan_sha256, impact_sha256, approval_sequence);

CREATE TABLE action_journal_entries (
    entry_sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    job_id TEXT NOT NULL,
    entry_id TEXT NOT NULL CHECK (
        typeof(entry_id) = 'text' AND length(entry_id) = 36
        AND length(CAST(entry_id AS BLOB)) = 36
        AND substr(entry_id, 9, 1) = '-' AND substr(entry_id, 14, 1) = '-'
        AND substr(entry_id, 19, 1) = '-' AND substr(entry_id, 24, 1) = '-'
        AND length(replace(entry_id, '-', '')) = 32
        AND replace(entry_id, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND entry_id <> '00000000-0000-0000-0000-000000000000'
    ),
    plan_sha256 TEXT NOT NULL
        CHECK (typeof(plan_sha256) = 'text' AND length(plan_sha256) = 64
            AND length(CAST(plan_sha256 AS BLOB)) = 64 AND plan_sha256 NOT GLOB '*[^0-9a-f]*'),
    impact_sha256 TEXT NOT NULL
        CHECK (typeof(impact_sha256) = 'text' AND length(impact_sha256) = 64
            AND length(CAST(impact_sha256 AS BLOB)) = 64 AND impact_sha256 NOT GLOB '*[^0-9a-f]*'),
    action_id TEXT NOT NULL CHECK (
        typeof(action_id) = 'text' AND length(action_id) = 36
        AND length(CAST(action_id AS BLOB)) = 36
        AND substr(action_id, 9, 1) = '-' AND substr(action_id, 14, 1) = '-'
        AND substr(action_id, 19, 1) = '-' AND substr(action_id, 24, 1) = '-'
        AND length(replace(action_id, '-', '')) = 32
        AND replace(action_id, '-', '') NOT GLOB '*[^0-9a-f]*'
        AND action_id <> '00000000-0000-0000-0000-000000000000'
    ),
    state INTEGER NOT NULL
        CHECK (typeof(state) = 'integer' AND state BETWEEN 1 AND 9),
    payload_json TEXT NOT NULL
        CHECK (typeof(payload_json) = 'text' AND length(CAST(payload_json AS BLOB)) BETWEEN 2 AND 4096),
    payload_sha256 TEXT NOT NULL
        CHECK (typeof(payload_sha256) = 'text' AND length(payload_sha256) = 64
            AND length(CAST(payload_sha256 AS BLOB)) = 64 AND payload_sha256 NOT GLOB '*[^0-9a-f]*'),
    UNIQUE (job_id, entry_id),
    FOREIGN KEY (job_id, plan_sha256, impact_sha256)
        REFERENCES action_journal_plans(job_id, plan_sha256, impact_sha256)
);

CREATE INDEX ix_action_journal_entries_plan_action_sequence
    ON action_journal_entries(job_id, plan_sha256, impact_sha256, action_id, entry_sequence);

CREATE INDEX ix_action_journal_entries_job_sequence
    ON action_journal_entries(job_id, entry_sequence);
