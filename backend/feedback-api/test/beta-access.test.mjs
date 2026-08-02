import test from "node:test";
import assert from "node:assert/strict";
import {
  calculateBetaExpiry,
  handleRequest,
  isEntitlementActive,
  normalizeAccessCode
} from "../src/index.js";

function betaEnvironment() {
  const state = {
    invitations: new Map(),
    entitlementsByToken: new Map(),
    entitlementsByReference: new Map()
  };

  return {
    state,
    env: {
      BETA_ADMIN_KEY: "a-secure-admin-key-that-is-longer-than-32-characters",
      BETA_ACCESS_HASH_SALT: "a-separate-beta-access-salt-longer-than-32-characters",
      BETA_ACCESS_RATE_LIMITER: {
        async limit() { return { success: true }; }
      },
      FEEDBACK_DB: {
        prepare(sql) {
          return statement(sql, state);
        }
      }
    }
  };
}

function statement(sql, state) {
  return {
    values: [],
    bind(...values) {
      this.values = values;
      return this;
    },
    async first() {
      if (sql.includes("FROM beta_invitations")) {
        return state.invitations.get(this.values[0]) ?? null;
      }
      if (sql.includes("WHERE invitation_code_hash")) {
        return [...state.entitlementsByToken.values()].find(item =>
          item.invitation_code_hash === this.values[0] &&
          item.installation_hash === this.values[1]) ?? null;
      }
      if (sql.includes("WHERE entitlement_token_hash")) {
        return state.entitlementsByToken.get(this.values[0]) ?? null;
      }
      throw new Error(`Unexpected first query: ${sql}`);
    },
    async run() {
      if (sql.includes("INSERT INTO beta_invitations")) {
        const [codeHash, label, createdUtc, expiresUtc, maxActivations] = this.values;
        state.invitations.set(codeHash, {
          code_hash: codeHash,
          label,
          created_utc: createdUtc,
          expires_utc: expiresUtc,
          max_activations: maxActivations,
          activation_count: 0,
          revoked_utc: null
        });
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE beta_invitations")) {
        const invitation = state.invitations.get(this.values[0]);
        if (!invitation || invitation.revoked_utc ||
            invitation.expires_utc <= this.values[1] ||
            invitation.activation_count >= invitation.max_activations) {
          return { meta: { changes: 0 } };
        }
        invitation.activation_count += 1;
        return { meta: { changes: 1 } };
      }
      if (sql.includes("INSERT INTO beta_entitlements")) {
        const [reference, tokenHash, invitationHash, installationHash,
          applicationVersion, activatedUtc, expiresUtc, lastVerifiedUtc] = this.values;
        const entitlement = {
          entitlement_reference: reference,
          entitlement_token_hash: tokenHash,
          invitation_code_hash: invitationHash,
          installation_hash: installationHash,
          application_version: applicationVersion,
          activated_utc: activatedUtc,
          expires_utc: expiresUtc,
          last_verified_utc: lastVerifiedUtc,
          revoked_utc: null
        };
        state.entitlementsByToken.set(tokenHash, entitlement);
        state.entitlementsByReference.set(reference, entitlement);
        return { meta: { changes: 1 } };
      }
      if (sql.includes("SET last_verified_utc")) {
        const entitlement = state.entitlementsByToken.get(this.values[1]);
        if (entitlement) entitlement.last_verified_utc = this.values[0];
        return { meta: { changes: entitlement ? 1 : 0 } };
      }
      if (sql.includes("SET revoked_utc")) {
        const entitlement = state.entitlementsByReference.get(this.values[1]);
        if (!entitlement || entitlement.revoked_utc) return { meta: { changes: 0 } };
        entitlement.revoked_utc = this.values[0];
        return { meta: { changes: 1 } };
      }
      throw new Error(`Unexpected run query: ${sql}`);
    }
  };
}

function post(path, body, adminKey) {
  const headers = { "content-type": "application/json" };
  if (adminKey) headers.authorization = `Bearer ${adminKey}`;
  return new Request(`https://example.test${path}`, {
    method: "POST",
    headers,
    body: JSON.stringify(body)
  });
}

test("beta expiry is exactly 30 days with no grace period", () => {
  const activated = new Date("2026-08-02T12:00:00.000Z");
  const expires = calculateBetaExpiry(activated);
  assert.equal(expires.toISOString(), "2026-09-01T12:00:00.000Z");
  const entitlement = { expires_utc: expires.toISOString(), revoked_utc: null };
  assert.equal(isEntitlementActive(entitlement, new Date(expires.getTime() - 1)), true);
  assert.equal(isEntitlementActive(entitlement, expires), false);
});

test("access codes are normalized consistently", () => {
  assert.equal(normalizeAccessCode("  pcspa-abcd1234  "), "PCSPA-ABCD1234");
});

test("admin creates invitation and installation activates for 30 days", async () => {
  const { env, state } = betaEnvironment();
  const invitationResponse = await handleRequest(post(
    "/v1/admin/beta/invitations",
    {
      label: "Invited tester",
      maxActivations: 1,
      invitationExpiresUtc: "2099-01-01T00:00:00.000Z"
    },
    env.BETA_ADMIN_KEY), env);
  assert.equal(invitationResponse.status, 201);
  const invitation = await invitationResponse.json();
  assert.match(invitation.accessCode, /^PCSPA-[A-F0-9]{16}$/);
  assert.equal([...state.invitations.keys()].includes(invitation.accessCode), false);
  assert.match([...state.invitations.keys()][0], /^[a-f0-9]{64}$/);

  const activationResponse = await handleRequest(post("/v1/beta/activate", {
    accessCode: invitation.accessCode,
    installationId: "0123456789abcdef0123456789abcdef",
    applicationVersion: "1.0.0"
  }), env);
  assert.equal(activationResponse.status, 201);
  const activation = await activationResponse.json();
  assert.equal(activation.accessDays, 30);
  assert.equal(activation.gracePeriodDays, 0);
  assert.equal(
    Date.parse(activation.expiresUtc) - Date.parse(activation.activatedUtc),
    30 * 24 * 60 * 60 * 1000);
  assert.match(activation.entitlementToken, /^[a-f0-9]{64}$/);
  assert.equal(state.entitlementsByToken.has(activation.entitlementToken), false);

  const verificationResponse = await handleRequest(post("/v1/beta/verify", {
    entitlementToken: activation.entitlementToken,
    installationId: "0123456789abcdef0123456789abcdef"
  }), env);
  assert.equal(verificationResponse.status, 200);
  const verification = await verificationResponse.json();
  assert.equal(verification.active, true);
  assert.equal(verification.status, "active");
  assert.equal(verification.gracePeriodDays, 0);
});

test("admin endpoints fail closed without the configured key", async () => {
  const { env } = betaEnvironment();
  const response = await handleRequest(post("/v1/admin/beta/invitations", {
    label: "Unauthorized",
    maxActivations: 1,
    invitationExpiresUtc: "2099-01-01T00:00:00.000Z"
  }), env);
  assert.equal(response.status, 401);
});
