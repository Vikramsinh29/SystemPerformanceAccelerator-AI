CREATE TABLE IF NOT EXISTS beta_invitations (
    code_hash TEXT PRIMARY KEY NOT NULL,
    label TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    max_activations INTEGER NOT NULL CHECK (max_activations BETWEEN 1 AND 1000),
    activation_count INTEGER NOT NULL DEFAULT 0 CHECK (activation_count >= 0),
    revoked_utc TEXT NULL,
    CHECK (length(code_hash) = 64),
    CHECK (length(label) BETWEEN 1 AND 100),
    CHECK (activation_count <= max_activations)
);

CREATE TABLE IF NOT EXISTS beta_entitlements (
    entitlement_reference TEXT PRIMARY KEY NOT NULL,
    entitlement_token_hash TEXT UNIQUE NOT NULL,
    invitation_code_hash TEXT NOT NULL,
    installation_hash TEXT NOT NULL,
    application_version TEXT NOT NULL,
    activated_utc TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    last_verified_utc TEXT NOT NULL,
    revoked_utc TEXT NULL,
    FOREIGN KEY (invitation_code_hash) REFERENCES beta_invitations(code_hash),
    UNIQUE (invitation_code_hash, installation_hash),
    CHECK (length(entitlement_token_hash) = 64),
    CHECK (length(invitation_code_hash) = 64),
    CHECK (length(installation_hash) = 64),
    CHECK (length(application_version) BETWEEN 1 AND 40)
);

CREATE INDEX IF NOT EXISTS ix_beta_invitations_expires_utc
    ON beta_invitations(expires_utc);

CREATE INDEX IF NOT EXISTS ix_beta_entitlements_expires_utc
    ON beta_entitlements(expires_utc);

CREATE INDEX IF NOT EXISTS ix_beta_entitlements_installation_hash
    ON beta_entitlements(installation_hash);
