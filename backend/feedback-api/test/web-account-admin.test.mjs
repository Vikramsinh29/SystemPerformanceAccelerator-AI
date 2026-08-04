import test from "node:test";
import assert from "node:assert/strict";
import { handleRequest } from "../src/index.js";

function createEnvironment() {
  const state = {
    usersById: new Map(),
    usersByEmail: new Map(),
    sessionsByHash: new Map(),
    betaRequests: [],
    licensesById: new Map(),
    licensesByKeyHash: new Map(),
    activations: [],
    entitlementsByTokenHash: new Map(),
    auditLog: []
  };

  const adminUser = {
    user_id: "usr_admin01",
    email: "admin@example.test",
    password_hash: "pbkdf2$120000$salt$hash",
    display_name: "Admin User",
    created_utc: "2026-08-04T00:00:00.000Z"
  };
  state.usersById.set(adminUser.user_id, adminUser);
  state.usersByEmail.set(adminUser.email, adminUser);

  return {
    state,
    env: {
      FEEDBACK_HASH_SALT: "a-secure-feedback-salt-that-is-longer-than-32-characters",
      AUTH_HASH_SALT: "a-secure-auth-salt-that-is-longer-than-32-characters",
      BETA_ACCESS_HASH_SALT: "a-secure-license-salt-that-is-longer-than-32-characters",
      ADMIN_USER_IDS: adminUser.user_id,
      FEEDBACK_RATE_LIMITER: { async limit() { return { success: true }; } },
      BETA_ACCESS_RATE_LIMITER: { async limit() { return { success: true }; } },
      AUTH_RATE_LIMITER: { async limit() { return { success: true }; } },
      FEEDBACK_DB: {
        prepare(sql) {
          return statement(sql, state);
        }
      },
      __state: state
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
      if (sql.includes("FROM auth_users") && sql.includes("WHERE email")) {
        return state.usersByEmail.get(this.values[0]) ?? null;
      }
      if (sql.includes("FROM auth_sessions") && sql.includes("WHERE s.session_token_hash")) {
        const session = state.sessionsByHash.get(this.values[0]);
        if (!session) return null;
        const user = state.usersById.get(session.user_id);
        return {
          session_id: session.session_id,
          user_id: session.user_id,
          expires_utc: session.expires_utc,
          revoked_utc: session.revoked_utc,
          email: user.email,
          display_name: user.display_name
        };
      }
      if (sql.includes("FROM licenses") && sql.includes("WHERE license_key_hash")) {
        return state.licensesByKeyHash.get(this.values[0]) ?? null;
      }
      if (sql.includes("FROM license_activations") && sql.includes("WHERE license_id = ? AND device_hash = ?")) {
        return state.activations.find(item =>
          item.license_id === this.values[0] &&
          item.device_hash === this.values[1]) ?? null;
      }
      if (sql.includes("COUNT(*) AS active_count")) {
        return {
          active_count: state.activations.filter(item =>
            item.license_id === this.values[0] &&
            !item.revoked_utc).length
        };
      }
      if (sql.includes("FROM beta_entitlements b")) {
        const entitlement = state.entitlementsByTokenHash.get(this.values[0]);
        if (!entitlement) return null;
        const license = state.licensesById.get(entitlement.entitlement_reference);
        return {
          entitlement_reference: entitlement.entitlement_reference,
          installation_hash: entitlement.installation_hash,
          expires_utc: entitlement.expires_utc,
          revoked_utc: entitlement.revoked_utc,
          plan: license.plan,
          status: license.status
        };
      }
      if (sql.includes("FROM beta_entitlements") && sql.includes("WHERE entitlement_token_hash")) {
        return state.entitlementsByTokenHash.get(this.values[0]) ?? null;
      }
      return null;
    },
    async all() {
      if (sql.includes("FROM auth_users")) {
        return {
          results: [...state.usersById.values()].map(user => ({
            user_id: user.user_id,
            email: user.email,
            display_name: user.display_name,
            created_utc: user.created_utc
          }))
        };
      }
      if (sql.includes("FROM beta_requests")) {
        return {
          results: state.betaRequests.map(item => ({ ...item }))
        };
      }
      if (sql.includes("FROM licenses l") && sql.includes("WHERE l.user_id = ?")) {
        return {
          results: [...state.licensesById.values()]
            .filter(item => item.user_id === this.values[0])
            .map(item => ({
              license_id: item.license_id,
              label: item.label,
              plan: item.plan,
              status: item.status,
              expires_utc: item.expires_utc,
              activation_limit: item.activation_limit,
              activated_utc: item.activated_utc,
              active_device_count: state.activations.filter(activation =>
                activation.license_id === item.license_id &&
                !activation.revoked_utc).length
            }))
        };
      }
      if (sql.includes("FROM licenses l") && sql.includes("JOIN auth_users u")) {
        return {
          results: [...state.licensesById.values()].map(item => {
            const user = state.usersById.get(item.user_id);
            return {
              license_id: item.license_id,
              label: item.label,
              plan: item.plan,
              status: item.status,
              expires_utc: item.expires_utc,
              activation_limit: item.activation_limit,
              activated_utc: item.activated_utc,
              email: user.email,
              active_device_count: state.activations.filter(activation =>
                activation.license_id === item.license_id &&
                !activation.revoked_utc).length
            };
          })
        };
      }
      return { results: [] };
    },
    async run() {
      if (sql.includes("INSERT INTO auth_users")) {
        const [userId, email, passwordHash, displayName, createdUtc] = this.values;
        const user = {
          user_id: userId,
          email,
          password_hash: passwordHash,
          display_name: displayName,
          created_utc: createdUtc
        };
        state.usersById.set(userId, user);
        state.usersByEmail.set(email, user);
        return { meta: { changes: 1 } };
      }
      if (sql.includes("INSERT INTO beta_requests")) {
        const [requestId, email, displayName, notes, createdUtc] = this.values;
        state.betaRequests.push({
          request_id: requestId,
          email,
          display_name: displayName,
          notes,
          status: "pending",
          created_utc: createdUtc,
          reviewed_utc: null
        });
        return { meta: { changes: 1 } };
      }
      if (sql.includes("INSERT INTO auth_sessions")) {
        const [sessionId, sessionTokenHash, userId, createdUtc, expiresUtc, lastSeenUtc] = this.values;
        state.sessionsByHash.set(sessionTokenHash, {
          session_id: sessionId,
          session_token_hash: sessionTokenHash,
          user_id: userId,
          created_utc: createdUtc,
          expires_utc: expiresUtc,
          last_seen_utc: lastSeenUtc,
          revoked_utc: null
        });
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE auth_sessions SET last_seen_utc")) {
        for (const session of state.sessionsByHash.values()) {
          if (session.session_id === this.values[1]) {
            session.last_seen_utc = this.values[0];
          }
        }
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE auth_sessions SET revoked_utc")) {
        for (const session of state.sessionsByHash.values()) {
          if (session.session_id === this.values[1] && !session.revoked_utc) {
            session.revoked_utc = this.values[0];
            return { meta: { changes: 1 } };
          }
        }
        return { meta: { changes: 0 } };
      }
      if (sql.includes("INSERT INTO licenses")) {
        const [
          licenseId,
          userId,
          licenseKeyHash,
          licenseKeySuffix,
          label,
          plan,
          activationLimit,
          expiresUtc,
          createdUtc,
          issuedByUserId,
          lastIssuedKeyUtc
        ] = this.values;
        const license = {
          license_id: licenseId,
          user_id: userId,
          license_key_hash: licenseKeyHash,
          license_key_suffix: licenseKeySuffix,
          label,
          plan,
          status: "pending",
          activation_limit: activationLimit,
          expires_utc: expiresUtc,
          created_utc: createdUtc,
          issued_by_user_id: issuedByUserId,
          activated_utc: null,
          activated_by_user_id: null,
          revoked_utc: null,
          revoked_by_user_id: null,
          last_issued_key_utc: lastIssuedKeyUtc
        };
        state.licensesById.set(licenseId, license);
        state.licensesByKeyHash.set(licenseKeyHash, license);
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE licenses") && sql.includes("SET status = 'active'")) {
        const license = state.licensesById.get(this.values[2]);
        if (!license || license.status !== "pending") {
          return { meta: { changes: 0 } };
        }
        license.status = "active";
        license.activated_utc = this.values[0];
        license.activated_by_user_id = this.values[1];
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE licenses") && sql.includes("SET status = 'revoked'")) {
        const license = state.licensesById.get(this.values[2]);
        if (!license || license.status === "revoked") {
          return { meta: { changes: 0 } };
        }
        license.status = "revoked";
        license.revoked_utc = this.values[0];
        license.revoked_by_user_id = this.values[1];
        return { meta: { changes: 1 } };
      }
      if (sql.includes("INSERT INTO license_activations")) {
        const [activationId, licenseId, userId, deviceHash, deviceCountKey, activatedUtc, lastValidatedUtc] = this.values;
        state.activations.push({
          activation_id: activationId,
          license_id: licenseId,
          user_id: userId,
          device_hash: deviceHash,
          device_count_key: deviceCountKey,
          activated_utc: activatedUtc,
          last_validated_utc: lastValidatedUtc,
          revoked_utc: null
        });
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE license_activations SET revoked_utc = NULL")) {
        const activation = state.activations.find(item => item.activation_id === this.values[1]);
        if (activation) {
          activation.revoked_utc = null;
          activation.last_validated_utc = this.values[0];
        }
        return { meta: { changes: activation ? 1 : 0 } };
      }
      if (sql.includes("INSERT OR REPLACE INTO beta_entitlements")) {
        const [reference, tokenHash, invitationCodeHash, installationHash, applicationVersion, activatedUtc, expiresUtc, lastVerifiedUtc] = this.values;
        state.entitlementsByTokenHash.set(tokenHash, {
          entitlement_reference: reference,
          entitlement_token_hash: tokenHash,
          invitation_code_hash: invitationCodeHash,
          installation_hash: installationHash,
          application_version: applicationVersion,
          activated_utc: activatedUtc,
          expires_utc: expiresUtc,
          last_verified_utc: lastVerifiedUtc,
          revoked_utc: null
        });
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE beta_entitlements SET last_verified_utc")) {
        const entitlement = state.entitlementsByTokenHash.get(this.values[1]);
        if (entitlement) {
          entitlement.last_verified_utc = this.values[0];
        }
        return { meta: { changes: entitlement ? 1 : 0 } };
      }
      if (sql.includes("UPDATE beta_entitlements SET revoked_utc")) {
        if (sql.includes("WHERE entitlement_token_hash")) {
          const entitlement = state.entitlementsByTokenHash.get(this.values[1]);
          if (entitlement) {
            entitlement.revoked_utc = this.values[0];
            return { meta: { changes: 1 } };
          }
          return { meta: { changes: 0 } };
        }

        for (const entitlement of state.entitlementsByTokenHash.values()) {
          if (entitlement.entitlement_reference === this.values[1]) {
            entitlement.revoked_utc = this.values[0];
          }
        }
        return { meta: { changes: 1 } };
      }
      if (sql.includes("UPDATE license_activations SET revoked_utc")) {
        let changes = 0;
        for (const activation of state.activations) {
          if (activation.license_id === this.values[1] &&
              activation.device_hash === this.values[2]) {
            activation.revoked_utc = this.values[0];
            changes += 1;
          }
        }
        return { meta: { changes } };
      }
      if (sql.includes("INSERT INTO audit_log")) {
        const [auditId, actorUserId, action, targetType, targetId, detailsJson, createdUtc] = this.values;
        state.auditLog.push({
          audit_id: auditId,
          actor_user_id: actorUserId,
          action,
          target_type: targetType,
          target_id: targetId,
          details_json: detailsJson,
          created_utc: createdUtc
        });
        return { meta: { changes: 1 } };
      }
      return { meta: { changes: 0 } };
    }
  };
}

function cookieFrom(response) {
  return response.headers.get("set-cookie")?.split(";")[0] ?? "";
}

test("register and login pages render accessible auth forms", async () => {
  const { env } = createEnvironment();

  const registerResponse = await handleRequest(
    new Request("https://example.test/register"),
    env
  );
  const registerHtml = await registerResponse.text();
  assert.equal(registerResponse.status, 200);
  assert.match(registerHtml, /<form method="post" action="\/register"/);
  assert.match(registerHtml, /type="email" name="email"/);
  assert.match(registerHtml, /type="password" name="password"/);

  const loginResponse = await handleRequest(
    new Request("https://example.test/login"),
    env
  );
  const loginHtml = await loginResponse.text();
  assert.equal(loginResponse.status, 200);
  assert.match(loginHtml, /<form method="post" action="\/login"/);
  assert.match(loginHtml, /autocomplete="current-password"/);
});

test("register and login UI create a session and redirect to account", async () => {
  const { env } = createEnvironment();

  const registerForm = new URLSearchParams({
    email: "tester@example.test",
    displayName: "Tester",
    password: "Password123!",
    betaRequestNotes: "Need beta access"
  });
  const registerResponse = await handleRequest(
    new Request("https://example.test/register", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: registerForm
    }),
    env
  );
  assert.equal(registerResponse.status, 302);
  assert.equal(registerResponse.headers.get("location"), "/account");
  assert.match(cookieFrom(registerResponse), /pcspa_session=/);

  const loginPayload = {
    email: "tester@example.test",
    password: "Password123!"
  };
  const loginResponse = await handleRequest(
    new Request("https://example.test/api/auth/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(loginPayload)
    }),
    env
  );
  assert.equal(loginResponse.status, 200);
  const loginBody = await loginResponse.json();
  assert.equal(loginBody.email, "tester@example.test");
  assert.equal(loginBody.authenticated, true);
  assert.ok(loginBody.sessionToken);
});

test("account page requires an authenticated session", async () => {
  const { env } = createEnvironment();

  const anonymousResponse = await handleRequest(
    new Request("https://example.test/account"),
    env
  );
  assert.equal(anonymousResponse.status, 302);
  assert.equal(anonymousResponse.headers.get("location"), "/login");
});

test("admin endpoints deny unauthenticated and non-admin access", async () => {
  const { env } = createEnvironment();

  const unauthenticated = await handleRequest(
    new Request("https://example.test/api/admin/users"),
    env
  );
  assert.equal(unauthenticated.status, 401);

  const sessionCookie = await registerAndLogin(env, "tester@example.test");
  const nonAdmin = await handleRequest(
    new Request("https://example.test/api/admin/users", {
      headers: { cookie: sessionCookie }
    }),
    env
  );
  assert.equal(nonAdmin.status, 403);
});

test("account license listing and admin lists exclude raw key hashes", async () => {
  const { env, state } = createEnvironment();
  const sessionCookie = await registerAndLogin(env, "owner@example.test");
  const ownerId = state.usersByEmail.get("owner@example.test").user_id;

  const adminCookie = await createAdminSessionCookie(env);
  const issueForm = new URLSearchParams({
    userId: ownerId,
    label: "Owner Beta",
    plan: "beta",
    activationLimit: "2",
    expiresUtc: "2026-09-01T10:00"
  });
  const issueResponse = await handleRequest(
    new Request("https://example.test/admin/licenses/issue", {
      method: "POST",
      headers: {
        "content-type": "application/x-www-form-urlencoded",
        cookie: adminCookie
      },
      body: issueForm
    }),
    env
  );
  const issueHtml = await issueResponse.text();
  assert.equal(issueResponse.status, 200);
  assert.match(issueHtml, /Activation key shown once:/);
  assert.doesNotMatch(issueHtml, /license_key_hash/i);

  const accountResponse = await handleRequest(
    new Request("https://example.test/api/account/licenses", {
      headers: { cookie: sessionCookie }
    }),
    env
  );
  const accountBody = await accountResponse.json();
  assert.equal(accountResponse.status, 200);
  assert.equal(accountBody.licenses.length, 1);
  assert.equal(accountBody.licenses[0].label, "Owner Beta");
  assert.equal(accountBody.licenses[0].activationLimit, 2);
  assert.equal(accountBody.licenses[0].activeDeviceCount, 0);
  assert.equal(Object.hasOwn(accountBody.licenses[0], "licenseKeyHash"), false);

  const adminListResponse = await handleRequest(
    new Request("https://example.test/api/admin/licenses", {
      headers: { cookie: adminCookie }
    }),
    env
  );
  const adminListBody = await adminListResponse.json();
  assert.equal(adminListResponse.status, 200);
  assert.equal(adminListBody.licenses.length, 1);
  assert.equal(Object.hasOwn(adminListBody.licenses[0], "licenseKeyHash"), false);
});

test("pending-to-active transition and revocation are enforced", async () => {
  const { env, state } = createEnvironment();
  await registerAndLogin(env, "owner@example.test");
  const ownerId = state.usersByEmail.get("owner@example.test").user_id;
  const adminCookie = await createAdminSessionCookie(env);
  const { activationKey, licenseId } = await issueLicenseThroughPage(
    env,
    adminCookie,
    ownerId,
    "Owner Beta");

  const pendingActivation = await handleRequest(
    new Request("https://example.test/api/licenses/activate", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        authorization: `Bearer ${(await loginApi(env, "owner@example.test")).sessionToken}`
      },
      body: JSON.stringify({
        activationKey,
        deviceId: "device-0123456789abcdef"
      })
    }),
    env
  );
  assert.equal(pendingActivation.status, 409);

  const activatePending = await handleRequest(
    new Request(`https://example.test/api/admin/licenses/${licenseId}/activate`, {
      method: "POST",
      headers: { cookie: adminCookie }
    }),
    env
  );
  assert.equal(activatePending.status, 200);

  const activeActivation = await handleRequest(
    new Request("https://example.test/api/licenses/activate", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        authorization: `Bearer ${(await loginApi(env, "owner@example.test")).sessionToken}`
      },
      body: JSON.stringify({
        activationKey,
        deviceId: "device-0123456789abcdef"
      })
    }),
    env
  );
  assert.equal(activeActivation.status, 200);
  const activeBody = await activeActivation.json();
  assert.ok(activeBody.licenseToken);

  const revokeResponse = await handleRequest(
    new Request(`https://example.test/api/admin/licenses/${licenseId}/revoke`, {
      method: "POST",
      headers: { cookie: adminCookie }
    }),
    env
  );
  assert.equal(revokeResponse.status, 200);

  const validateResponse = await handleRequest(
    new Request("https://example.test/api/licenses/validate", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        authorization: `Bearer ${activeBody.licenseToken}`
      },
      body: JSON.stringify({
        deviceId: "device-0123456789abcdef"
      })
    }),
    env
  );
  assert.equal(validateResponse.status, 403);
});

async function registerAndLogin(env, email) {
  const response = await handleRequest(
    new Request("https://example.test/register", {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        email,
        displayName: "Tester",
        password: "Password123!",
        betaRequestNotes: "Need access"
      })
    }),
    env
  );
  return cookieFrom(response);
}

async function loginApi(env, email) {
  const response = await handleRequest(
    new Request("https://example.test/api/auth/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        email,
        password: "Password123!"
      })
    }),
    env
  );
  return response.json();
}

async function createAdminSessionCookie(env) {
  const token = "admin-session-token";
  const tokenHash = await hashForTests(
    token,
    env.AUTH_HASH_SALT
  );
  env.__state.sessionsByHash.set(tokenHash, {
    session_id: "ses_admin01",
    session_token_hash: tokenHash,
    user_id: "usr_admin01",
    created_utc: "2026-08-04T00:00:00.000Z",
    expires_utc: "2026-09-04T00:00:00.000Z",
    last_seen_utc: "2026-08-04T00:00:00.000Z",
    revoked_utc: null
  });
  return "pcspa_session=admin-session-token";
}

async function issueLicenseThroughPage(env, adminCookie, userId, label) {
  const issueResponse = await handleRequest(
    new Request("https://example.test/admin/licenses/issue", {
      method: "POST",
      headers: {
        "content-type": "application/x-www-form-urlencoded",
        cookie: adminCookie
      },
      body: new URLSearchParams({
        userId,
        label,
        plan: "beta",
        activationLimit: "1",
        expiresUtc: "2026-09-01T10:00"
      })
    }),
    env
  );
  const html = await issueResponse.text();
  const match = html.match(/<code>(PCSPA-[A-F0-9]+)<\/code>/);
  assert.ok(match);
  const lastLicense = [...env.__state.licensesById.values()].at(-1);
  return {
    activationKey: match[1],
    licenseId: lastLicense.license_id
  };
}

async function hashForTests(value, salt) {
  const data = new TextEncoder().encode(`${salt}:${value}`);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(digest)]
    .map(item => item.toString(16).padStart(2, "0"))
    .join("");
}
