CREATE TABLE IF NOT EXISTS feedback_reports (
    reference TEXT PRIMARY KEY NOT NULL,
    received_utc TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    schema_version INTEGER NOT NULL CHECK (schema_version = 1),
    application_version TEXT NOT NULL,
    build_identifier TEXT NOT NULL,
    error_reference TEXT NOT NULL,
    affected_area TEXT NOT NULL,
    description TEXT NOT NULL,
    expected_result TEXT NOT NULL,
    windows_version TEXT NOT NULL,
    runtime_version TEXT NOT NULL,
    is_elevated INTEGER NOT NULL CHECK (is_elevated IN (0, 1)),
    installation_hash TEXT NOT NULL,
    diagnostic_events_json TEXT NOT NULL,
    CHECK (length(description) <= 2000),
    CHECK (length(expected_result) <= 1000),
    CHECK (length(diagnostic_events_json) <= 40000)
);

CREATE INDEX IF NOT EXISTS ix_feedback_reports_received_utc
    ON feedback_reports(received_utc);

CREATE INDEX IF NOT EXISTS ix_feedback_reports_expires_utc
    ON feedback_reports(expires_utc);
