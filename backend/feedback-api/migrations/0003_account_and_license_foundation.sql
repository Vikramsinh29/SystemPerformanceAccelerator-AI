CREATE TABLE IF NOT EXISTS auth_users (
    user_id TEXT PRIMARY KEY NOT NULL,
    email TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    display_name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    CHECK (length(user_id) BETWEEN 8 AND 64),
    CHECK (length(email) BETWEEN 3 AND 320),
    CHECK (length(display_name) BETWEEN 1 AND 120)
);

CREATE TABLE IF NOT EXISTS auth_sessions (
    session_id TEXT PRIMARY KEY NOT NULL,
    session_token_hash TEXT UNIQUE NOT NULL,
    user_id TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    revoked_utc TEXT NULL,
    FOREIGN KEY (user_id) REFERENCES auth_users(user_id),
    CHECK (length(session_id) BETWEEN 8 AND 64),
    CHECK (length(session_token_hash) = 64)
);

CREATE INDEX IF NOT EXISTS ix_auth_sessions_user_id
    ON auth_sessions(user_id);

CREATE INDEX IF NOT EXISTS ix_auth_sessions_expires_utc
    ON auth_sessions(expires_utc);

CREATE TABLE IF NOT EXISTS beta_requests (
    request_id TEXT PRIMARY KEY NOT NULL,
    email TEXT NOT NULL,
    display_name TEXT NOT NULL,
    notes TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('pending', 'approved', 'rejected')),
    created_utc TEXT NOT NULL,
    reviewed_utc TEXT NULL,
    reviewed_by_user_id TEXT NULL,
    CHECK (length(request_id) BETWEEN 8 AND 64),
    CHECK (length(email) BETWEEN 3 AND 320),
    CHECK (length(display_name) BETWEEN 1 AND 120),
    CHECK (length(notes) <= 1000)
);

CREATE INDEX IF NOT EXISTS ix_beta_requests_status
    ON beta_requests(status);

CREATE TABLE IF NOT EXISTS licenses (
    license_id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    license_key_hash TEXT UNIQUE NOT NULL,
    license_key_suffix TEXT NOT NULL,
    label TEXT NOT NULL,
    plan TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('pending', 'active', 'revoked', 'expired')),
    activation_limit INTEGER NOT NULL CHECK (activation_limit BETWEEN 1 AND 1000),
    expires_utc TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    issued_by_user_id TEXT NOT NULL,
    activated_utc TEXT NULL,
    activated_by_user_id TEXT NULL,
    revoked_utc TEXT NULL,
    revoked_by_user_id TEXT NULL,
    last_issued_key_utc TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES auth_users(user_id),
    FOREIGN KEY (issued_by_user_id) REFERENCES auth_users(user_id),
    FOREIGN KEY (activated_by_user_id) REFERENCES auth_users(user_id),
    FOREIGN KEY (revoked_by_user_id) REFERENCES auth_users(user_id),
    CHECK (length(license_id) BETWEEN 8 AND 64),
    CHECK (length(license_key_hash) = 64),
    CHECK (length(license_key_suffix) BETWEEN 4 AND 16),
    CHECK (length(label) BETWEEN 1 AND 120),
    CHECK (length(plan) BETWEEN 1 AND 40)
);

CREATE INDEX IF NOT EXISTS ix_licenses_user_id
    ON licenses(user_id);

CREATE INDEX IF NOT EXISTS ix_licenses_status
    ON licenses(status);

CREATE TABLE IF NOT EXISTS license_activations (
    activation_id TEXT PRIMARY KEY NOT NULL,
    license_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    device_hash TEXT NOT NULL,
    device_count_key TEXT NOT NULL,
    activated_utc TEXT NOT NULL,
    last_validated_utc TEXT NOT NULL,
    revoked_utc TEXT NULL,
    FOREIGN KEY (license_id) REFERENCES licenses(license_id),
    FOREIGN KEY (user_id) REFERENCES auth_users(user_id),
    UNIQUE (license_id, device_hash),
    CHECK (length(activation_id) BETWEEN 8 AND 64),
    CHECK (length(device_hash) = 64),
    CHECK (length(device_count_key) BETWEEN 8 AND 64)
);

CREATE INDEX IF NOT EXISTS ix_license_activations_license_id
    ON license_activations(license_id);

CREATE INDEX IF NOT EXISTS ix_license_activations_device_count_key
    ON license_activations(device_count_key);

CREATE TABLE IF NOT EXISTS audit_log (
    audit_id TEXT PRIMARY KEY NOT NULL,
    actor_user_id TEXT NULL,
    action TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    details_json TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (actor_user_id) REFERENCES auth_users(user_id),
    CHECK (length(audit_id) BETWEEN 8 AND 64),
    CHECK (length(action) BETWEEN 1 AND 80),
    CHECK (length(target_type) BETWEEN 1 AND 40),
    CHECK (length(target_id) BETWEEN 1 AND 80),
    CHECK (length(details_json) <= 4000)
);

CREATE INDEX IF NOT EXISTS ix_audit_log_created_utc
    ON audit_log(created_utc);
