const MAX_BODY_BYTES = 64 * 1024;
const RETENTION_DAYS = 45;
const MAX_DIAGNOSTIC_EVENTS = 10;

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
  return `PCSPA-${date}-${suffix}`;
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

function json(status, body, additionalHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...responseHeaders, ...additionalHeaders }
  });
}
