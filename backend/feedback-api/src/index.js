const MAX_BODY_BYTES = 64 * 1024;
const RETENTION_DAYS = 45;
const MAX_DIAGNOSTIC_EVENTS = 10;
const BETA_ACCESS_DAYS = 30;
const MAX_ACCESS_CODE_LENGTH = 64;

const responseHeaders = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer"
};

export default {
  fetch: (request, env) => handleRequest(request, env),
  scheduled: (_controller, env, ctx) =>
    ctx.waitUntil(deleteExpiredReports(env))
};

export async function handleRequest(request, env) {
  const url = new URL(request.url);

  if (request.method === "GET" && url.pathname === "/health") {
    return json(200, { status: "ok", service: "pc-spa-feedback" });
  }

  if (request.method === "POST" && url.pathname === "/v1/beta/activate") {
    return handleBetaActivation(request, env);
  }

  if (request.method === "POST" && url.pathname === "/v1/beta/verify") {
    return handleBetaVerification(request, env);
  }

  if (request.method === "POST" && url.pathname === "/v1/admin/beta/invitations") {
    return handleInvitationCreation(request, env);
  }

  if (request.method === "POST" && url.pathname === "/v1/admin/beta/revoke") {
    return handleEntitlementRevocation(request, env);
  }

  if (request.method !== "POST" || url.pathname !== "/v1/feedback") {
    return json(404, { error: "not_found" });
  }

  if (!request.headers.get("content-type")?.toLowerCase()
    .startsWith("application/json")) {
    return json(415, { error: "application_json_required" });
  }

  const declaredLength = Number(request.headers.get("content-length") ?? 0);
  if (Number.isFinite(declaredLength) && declaredLength > MAX_BODY_BYTES) {
    return json(413, { error: "report_too_large" });
  }

  const rateKey = request.headers.get("cf-connecting-ip") ?? "unknown";
  const rateResult = await env.FEEDBACK_RATE_LIMITER.limit({ key: rateKey });
  if (!rateResult.success) {
    return json(429, { error: "rate_limited" }, { "retry-after": "60" });
  }

  const bodyText = await request.text();
  if (new TextEncoder().encode(bodyText).byteLength > MAX_BODY_BYTES) {
    return json(413, { error: "report_too_large" });
  }

  let input;
  try {
    input = JSON.parse(bodyText);
  } catch {
    return json(400, { error: "invalid_json" });
  }

  const validation = validateReport(input);
  if (!validation.ok) {
    return json(400, {
      error: "invalid_report",
      fields: validation.fields
    });
  }

  const report = sanitizeReport(input);
  const received = new Date();
  const expires = new Date(received);
  expires.setUTCDate(expires.getUTCDate() + RETENTION_DAYS);
  const reference = createReference(received);
  const installationHash = await hashInstallationId(
    report.installationId,
    env.FEEDBACK_HASH_SALT);

  await env.FEEDBACK_DB.prepare(`
    INSERT INTO feedback_reports (
      reference, received_utc, expires_utc, schema_version,
      application_version, build_identifier, error_reference,
      affected_area, description, expected_result, windows_version,
      runtime_version, is_elevated, installation_hash,
      diagnostic_events_json
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    reference,
    received.toISOString(),
    expires.toISOString(),
    report.schemaVersion,
    report.applicationVersion,
    report.buildIdentifier,
    report.errorReference,
    report.affectedArea,
    report.description,
    report.expectedResult,
    report.windowsVersion,
    report.runtimeVersion,
    report.isElevated ? 1 : 0,
    installationHash,
    JSON.stringify(report.diagnosticEvents)
  ).run();

  return json(201, {
    accepted: true,
    reference,
    retentionDays: RETENTION_DAYS,
    message: "Privacy-safe technical error report received."
  });
}

export function validateReport(value) {
  const fields = [];
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return { ok: false, fields: ["body"] };
  }

  if (value.schemaVersion !== 1) fields.push("schemaVersion");
  requireText(value, "applicationVersion", 1, 40, fields);
  requireText(value, "buildIdentifier", 1, 100, fields);
  requireText(value, "errorReference", 1, 64, fields);
  requireText(value, "affectedArea", 1, 80, fields);
  requireText(value, "description", 1, 2000, fields);
  optionalText(value, "expectedResult", 1000, fields);
  requireText(value, "windowsVersion", 1, 160, fields);
  requireText(value, "runtimeVersion", 1, 100, fields);
  requireText(value, "installationId", 16, 64, fields);
  if (typeof value.isElevated !== "boolean") fields.push("isElevated");

  if (!Array.isArray(value.diagnosticEvents) ||
      value.diagnosticEvents.length > MAX_DIAGNOSTIC_EVENTS) {
    fields.push("diagnosticEvents");
  } else {
    for (const event of value.diagnosticEvents) {
      if (!event || typeof event !== "object" || Array.isArray(event)) {
        fields.push("diagnosticEvents");
        break;
      }
      requireText(event, "reference", 1, 64, fields, "diagnosticEvents.reference");
      requireText(event, "type", 1, 160, fields, "diagnosticEvents.type");
      requireText(event, "message", 1, 2000, fields, "diagnosticEvents.message");
      optionalText(event, "stackTrace", 8000, fields, "diagnosticEvents.stackTrace");
    }
  }

  const allowed = new Set([
    "schemaVersion", "applicationVersion", "buildIdentifier",
    "errorReference", "affectedArea", "description", "expectedResult",
    "windowsVersion", "runtimeVersion", "isElevated", "installationId",
    "diagnosticEvents"
  ]);
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) fields.push(key);
  }

  return { ok: fields.length === 0, fields: [...new Set(fields)] };
}

export function sanitizeText(value) {
  return String(value ?? "")
    .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "<redacted-email>")
    .replace(
      /[A-Z]:\\Users\\[^\\\s]+(?:\\[^\s,;]+)*/gi,
      "%USERPROFILE%")
    .replace(/\\\\[^\r\n]+/g, "<redacted-path>")
    .replace(/[A-Z]:\\[^\r\n]+/gi, "<redacted-path>")
    .trim();
}

export function calculateBetaExpiry(activatedAt) {
  return new Date(activatedAt.getTime() + BETA_ACCESS_DAYS * 24 * 60 * 60 * 1000);
}

export function normalizeAccessCode(value) {
  return String(value ?? "").trim().toUpperCase();
}

export function isEntitlementActive(entitlement, now = new Date()) {
  return Boolean(entitlement) &&
    !entitlement.revoked_utc &&
    Date.parse(entitlement.expires_utc) > now.getTime();
}

export async function deleteExpiredReports(env, now = new Date()) {
  return env.FEEDBACK_DB.prepare(
    "DELETE FROM feedback_reports WHERE expires_utc <= ?"
  ).bind(now.toISOString()).run();
}

function sanitizeReport(value) {
  return {
    schemaVersion: 1,
    applicationVersion: sanitizeText(value.applicationVersion),
    buildIdentifier: sanitizeText(value.buildIdentifier),
    errorReference: sanitizeText(value.errorReference),
    affectedArea: sanitizeText(value.affectedArea),
    description: sanitizeText(value.description),
    expectedResult: sanitizeText(value.expectedResult),
    windowsVersion: sanitizeText(value.windowsVersion),
    runtimeVersion: sanitizeText(value.runtimeVersion),
    isElevated: value.isElevated,
    installationId: String(value.installationId),
    diagnosticEvents: value.diagnosticEvents.map(event => ({
      reference: sanitizeText(event.reference),
      type: sanitizeText(event.type),
      message: sanitizeText(event.message),
      stackTrace: sanitizeText(event.stackTrace)
    }))
  };
}

function requireText(object, key, minimum, maximum, fields, label = key) {
  const value = object[key];
  if (typeof value !== "string" ||
      value.trim().length < minimum || value.length > maximum) {
    fields.push(label);
  }
}

function optionalText(object, key, maximum, fields, label = key) {
  const value = object[key];
  if (typeof value !== "string" || value.length > maximum) fields.push(label);
}

function createReference(now) {
  const date = now.toISOString().slice(0, 10).replaceAll("-", "");
  const bytes = crypto.getRandomValues(new Uint8Array(5));
  const suffix = [...bytes]
    .map(value => value.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
  return `BETA-${date}-${suffix}`;
}

async function hashInstallationId(installationId, salt) {
  if (typeof salt !== "string" || salt.length < 32) {
    throw new Error("FEEDBACK_HASH_SALT is not configured securely.");
  }
  const data = new TextEncoder().encode(`${salt}:${installationId}`);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(digest)]
    .map(value => value.toString(16).padStart(2, "0"))
    .join("");
}

async function handleBetaActivation(request, env, now = new Date()) {
  const parsed = await readJsonRequest(request, env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return parsed.response;

  const validation = validateExactObject(parsed.value, {
    accessCode: value => validText(value, 12, MAX_ACCESS_CODE_LENGTH),
    installationId: value => validText(value, 16, 64),
    applicationVersion: value => validText(value, 1, 40)
  });
  if (!validation.ok) {
    return json(400, { error: "invalid_activation", fields: validation.fields });
  }

  const accessCode = normalizeAccessCode(parsed.value.accessCode);
  const codeHash = await hashValue(accessCode, env.BETA_ACCESS_HASH_SALT);
  const installationHash = await hashValue(
    String(parsed.value.installationId), env.BETA_ACCESS_HASH_SALT);

  const invitation = await env.FEEDBACK_DB.prepare(`
    SELECT code_hash, expires_utc, max_activations, activation_count, revoked_utc
    FROM beta_invitations
    WHERE code_hash = ?
  `).bind(codeHash).first();

  if (!invitation || invitation.revoked_utc ||
      Date.parse(invitation.expires_utc) <= now.getTime() ||
      invitation.activation_count >= invitation.max_activations) {
    return json(403, { error: "invalid_or_unavailable_access_code" });
  }

  const existing = await env.FEEDBACK_DB.prepare(`
    SELECT entitlement_reference
    FROM beta_entitlements
    WHERE invitation_code_hash = ? AND installation_hash = ?
  `).bind(codeHash, installationHash).first();
  if (existing) {
    return json(409, { error: "installation_already_activated" });
  }

  const entitlementToken = randomHex(32);
  const tokenHash = await hashValue(entitlementToken, env.BETA_ACCESS_HASH_SALT);
  const reference = `ENT-${randomHex(8).toUpperCase()}`;
  const expires = calculateBetaExpiry(now);

  const reservation = await env.FEEDBACK_DB.prepare(`
    UPDATE beta_invitations
    SET activation_count = activation_count + 1
    WHERE code_hash = ? AND revoked_utc IS NULL
      AND expires_utc > ? AND activation_count < max_activations
  `).bind(codeHash, now.toISOString()).run();
  if ((reservation.meta?.changes ?? 0) !== 1) {
    return json(409, { error: "access_code_capacity_reached" });
  }

  await env.FEEDBACK_DB.prepare(`
    INSERT INTO beta_entitlements (
      entitlement_reference, entitlement_token_hash, invitation_code_hash,
      installation_hash, application_version, activated_utc, expires_utc,
      last_verified_utc, revoked_utc
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)
  `).bind(
    reference,
    tokenHash,
    codeHash,
    installationHash,
    sanitizeText(parsed.value.applicationVersion),
    now.toISOString(),
    expires.toISOString(),
    now.toISOString()
  ).run();

  return json(201, {
    activated: true,
    entitlementReference: reference,
    entitlementToken,
    activatedUtc: now.toISOString(),
    expiresUtc: expires.toISOString(),
    accessDays: BETA_ACCESS_DAYS,
    gracePeriodDays: 0
  });
}

async function handleBetaVerification(request, env, now = new Date()) {
  const parsed = await readJsonRequest(request, env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return parsed.response;

  const validation = validateExactObject(parsed.value, {
    entitlementToken: value => validText(value, 64, 64),
    installationId: value => validText(value, 16, 64)
  });
  if (!validation.ok) {
    return json(400, { error: "invalid_verification", fields: validation.fields });
  }

  const tokenHash = await hashValue(
    parsed.value.entitlementToken, env.BETA_ACCESS_HASH_SALT);
  const installationHash = await hashValue(
    parsed.value.installationId, env.BETA_ACCESS_HASH_SALT);
  const entitlement = await env.FEEDBACK_DB.prepare(`
    SELECT entitlement_reference, installation_hash, activated_utc,
      expires_utc, revoked_utc
    FROM beta_entitlements
    WHERE entitlement_token_hash = ?
  `).bind(tokenHash).first();

  if (!entitlement || entitlement.installation_hash !== installationHash) {
    return json(403, { active: false, status: "not_valid" });
  }

  const active = isEntitlementActive(entitlement, now);
  const status = entitlement.revoked_utc
    ? "revoked"
    : active ? "active" : "expired";

  if (active) {
    await env.FEEDBACK_DB.prepare(`
      UPDATE beta_entitlements SET last_verified_utc = ?
      WHERE entitlement_token_hash = ?
    `).bind(now.toISOString(), tokenHash).run();
  }

  return json(200, {
    active,
    status,
    entitlementReference: entitlement.entitlement_reference,
    activatedUtc: entitlement.activated_utc,
    expiresUtc: entitlement.expires_utc,
    gracePeriodDays: 0
  });
}

async function handleInvitationCreation(request, env, now = new Date()) {
  if (!authorizedAdmin(request, env.BETA_ADMIN_KEY)) {
    return json(401, { error: "unauthorized" });
  }
  const parsed = await readJsonRequest(request);
  if (parsed.response) return parsed.response;

  const validation = validateExactObject(parsed.value, {
    label: value => validText(value, 1, 100),
    maxActivations: value => Number.isInteger(value) && value >= 1 && value <= 1000,
    invitationExpiresUtc: value =>
      typeof value === "string" && Number.isFinite(Date.parse(value)) &&
      Date.parse(value) > now.getTime()
  });
  if (!validation.ok) {
    return json(400, { error: "invalid_invitation", fields: validation.fields });
  }

  const accessCode = `PCSPA-${randomHex(8).toUpperCase()}`;
  const codeHash = await hashValue(accessCode, env.BETA_ACCESS_HASH_SALT);
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO beta_invitations (
      code_hash, label, created_utc, expires_utc,
      max_activations, activation_count, revoked_utc
    ) VALUES (?, ?, ?, ?, ?, 0, NULL)
  `).bind(
    codeHash,
    sanitizeText(parsed.value.label),
    now.toISOString(),
    new Date(parsed.value.invitationExpiresUtc).toISOString(),
    parsed.value.maxActivations
  ).run();

  return json(201, {
    created: true,
    accessCode,
    invitationExpiresUtc: new Date(parsed.value.invitationExpiresUtc).toISOString(),
    maxActivations: parsed.value.maxActivations
  });
}

async function handleEntitlementRevocation(request, env, now = new Date()) {
  if (!authorizedAdmin(request, env.BETA_ADMIN_KEY)) {
    return json(401, { error: "unauthorized" });
  }
  const parsed = await readJsonRequest(request);
  if (parsed.response) return parsed.response;
  const validation = validateExactObject(parsed.value, {
    entitlementReference: value => validText(value, 12, 32)
  });
  if (!validation.ok) {
    return json(400, { error: "invalid_revocation", fields: validation.fields });
  }
  const result = await env.FEEDBACK_DB.prepare(`
    UPDATE beta_entitlements SET revoked_utc = ?
    WHERE entitlement_reference = ? AND revoked_utc IS NULL
  `).bind(now.toISOString(), parsed.value.entitlementReference).run();
  if ((result.meta?.changes ?? 0) !== 1) {
    return json(404, { error: "entitlement_not_found" });
  }
  return json(200, { revoked: true });
}

async function readJsonRequest(request, limiter) {
  if (!request.headers.get("content-type")?.toLowerCase()
    .startsWith("application/json")) {
    return { response: json(415, { error: "application_json_required" }) };
  }
  if (limiter) {
    const key = request.headers.get("cf-connecting-ip") ?? "unknown";
    const rateResult = await limiter.limit({ key });
    if (!rateResult.success) {
      return { response: json(429, { error: "rate_limited" }, { "retry-after": "60" }) };
    }
  }
  const text = await request.text();
  if (new TextEncoder().encode(text).byteLength > MAX_BODY_BYTES) {
    return { response: json(413, { error: "request_too_large" }) };
  }
  try {
    return { value: JSON.parse(text) };
  } catch {
    return { response: json(400, { error: "invalid_json" }) };
  }
}

function validateExactObject(value, rules) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return { ok: false, fields: ["body"] };
  }
  const fields = [];
  for (const [key, rule] of Object.entries(rules)) {
    if (!rule(value[key])) fields.push(key);
  }
  for (const key of Object.keys(value)) {
    if (!(key in rules)) fields.push(key);
  }
  return { ok: fields.length === 0, fields: [...new Set(fields)] };
}

function validText(value, minimum, maximum) {
  return typeof value === "string" &&
    value.trim().length >= minimum && value.length <= maximum;
}

function authorizedAdmin(request, configuredKey) {
  if (typeof configuredKey !== "string" || configuredKey.length < 32) return false;
  const supplied = request.headers.get("authorization") ?? "";
  return constantTimeEqual(supplied, `Bearer ${configuredKey}`);
}

function constantTimeEqual(left, right) {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

function randomHex(byteCount) {
  const bytes = crypto.getRandomValues(new Uint8Array(byteCount));
  return [...bytes]
    .map(value => value.toString(16).padStart(2, "0"))
    .join("");
}

async function hashValue(value, salt) {
  if (typeof salt !== "string" || salt.length < 32) {
    throw new Error("BETA_ACCESS_HASH_SALT is not configured securely.");
  }
  const data = new TextEncoder().encode(`${salt}:${value}`);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(digest)]
    .map(value => value.toString(16).padStart(2, "0"))
    .join("");
}

function json(status, body, additionalHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...responseHeaders, ...additionalHeaders }
  });
}
