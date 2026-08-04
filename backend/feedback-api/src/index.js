import { z } from "zod";

const MAX_BODY_BYTES = 64 * 1024;
const RETENTION_DAYS = 45;
const MAX_DIAGNOSTIC_EVENTS = 10;
const BETA_ACCESS_DAYS = 30;
const MAX_ACCESS_CODE_LENGTH = 64;
const SESSION_DAYS = 14;
const COOKIE_NAME = "pcspa_session";

const responseHeaders = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer"
};

const pageHeaders = {
  "content-type": "text/html; charset=utf-8",
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
  "referrer-policy": "strict-origin-when-cross-origin"
};

const registerSchema = z.object({
  email: z.email().max(320),
  password: z.string().min(10).max(128),
  displayName: z.string().trim().min(1).max(120),
  betaRequestNotes: z.string().trim().max(1000).optional().default("")
}).strict();

const loginSchema = z.object({
  email: z.email().max(320),
  password: z.string().min(1).max(128)
}).strict();

const accountIssueLicenseSchema = z.object({
  userId: z.string().trim().min(8).max(64),
  label: z.string().trim().min(1).max(120),
  plan: z.string().trim().min(1).max(40),
  activationLimit: z.coerce.number().int().min(1).max(1000),
  expiresUtc: z.iso.datetime()
}).strict();

const activateLicenseSchema = z.object({
  activationKey: z.string().trim().min(12).max(128),
  deviceId: z.string().trim().min(16).max(128)
}).strict();

const validateLicenseSchema = z.object({
  deviceId: z.string().trim().min(16).max(128)
}).strict();

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

  if (request.method === "GET" && url.pathname === "/register") {
    return renderRegisterPage(request, env, {});
  }

  if (request.method === "POST" && url.pathname === "/register") {
    return handleRegisterPageSubmission(request, env);
  }

  if (request.method === "GET" && url.pathname === "/login") {
    return renderLoginPage(request, env, {});
  }

  if (request.method === "POST" && url.pathname === "/login") {
    return handleLoginPageSubmission(request, env);
  }

  if (request.method === "GET" && url.pathname === "/account") {
    return renderAccountPage(request, env);
  }

  if (request.method === "GET" && url.pathname === "/admin") {
    return renderAdminOverviewPage(request, env);
  }

  if (request.method === "GET" && url.pathname === "/admin/users") {
    return renderAdminUsersPage(request, env);
  }

  if (request.method === "GET" && url.pathname === "/admin/beta-requests") {
    return renderAdminBetaRequestsPage(request, env);
  }

  if (request.method === "GET" && url.pathname === "/admin/licenses") {
    return renderAdminLicensesPage(request, env);
  }

  if (request.method === "POST" && url.pathname === "/admin/licenses/issue") {
    return handleIssueLicensePageSubmission(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/auth/register") {
    return handleRegisterApi(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/auth/login") {
    return handleLoginApi(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/auth/logout") {
    return handleLogoutApi(request, env);
  }

  if (request.method === "GET" && url.pathname === "/api/auth/session") {
    return handleSessionApi(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/licenses/activate") {
    return handleLicenseActivation(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/licenses/validate") {
    return handleLicenseValidation(request, env);
  }

  if (request.method === "POST" && url.pathname === "/api/licenses/deactivate") {
    return handleLicenseDeactivation(request, env);
  }

  if (request.method === "GET" && url.pathname === "/api/account/licenses") {
    return handleAccountLicenses(request, env);
  }

  if (request.method === "GET" && url.pathname === "/api/admin/users") {
    return handleAdminUsers(request, env);
  }

  if (request.method === "GET" && url.pathname === "/api/admin/beta-requests") {
    return handleAdminBetaRequests(request, env);
  }

  if (request.method === "GET" && url.pathname === "/api/admin/licenses") {
    return handleAdminLicenses(request, env);
  }

  if (request.method === "POST" &&
    url.pathname.match(/^\/api\/admin\/licenses\/[^/]+\/activate$/)) {
    const licenseId = url.pathname.split("/")[4];
    return handleAdminLicenseActivate(request, env, licenseId);
  }

  if (request.method === "POST" &&
    url.pathname.match(/^\/api\/admin\/licenses\/[^/]+\/revoke$/)) {
    const licenseId = url.pathname.split("/")[4];
    return handleAdminLicenseRevoke(request, env, licenseId);
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

async function handleRegisterPageSubmission(request, env) {
  const form = await request.formData();
  const input = {
    email: String(form.get("email") ?? ""),
    password: String(form.get("password") ?? ""),
    displayName: String(form.get("displayName") ?? ""),
    betaRequestNotes: String(form.get("betaRequestNotes") ?? "")
  };
  const result = registerSchema.safeParse(input);
  if (!result.success) {
    return renderRegisterPage(request, env, {
      error: "Enter a valid email, display name, and password with at least 10 characters.",
      values: { ...input, password: "" }
    });
  }

  const response = await registerUser(result.data, env, true);
  if (!response.success) {
    return renderRegisterPage(request, env, {
      error: mapAuthError(response.error),
      values: { ...input, password: "" }
    });
  }

  return htmlRedirect("/account", response.cookieHeader);
}

async function handleLoginPageSubmission(request, env) {
  const form = await request.formData();
  const input = {
    email: String(form.get("email") ?? ""),
    password: String(form.get("password") ?? "")
  };
  const result = loginSchema.safeParse(input);
  if (!result.success) {
    return renderLoginPage(request, env, {
      error: "Enter your email address and password.",
      values: { ...input, password: "" }
    });
  }

  const response = await loginUser(result.data, env, true);
  if (!response.success) {
    return renderLoginPage(request, env, {
      error: mapAuthError(response.error),
      values: { ...input, password: "" }
    });
  }

  return htmlRedirect("/account", response.cookieHeader);
}

async function handleRegisterApi(request, env) {
  const parsed = await readJsonRequest(request, env.AUTH_RATE_LIMITER ?? env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return withCors(parsed.response, request, env);
  const result = registerSchema.safeParse(parsed.value);
  if (!result.success) {
    return withCors(json(400, { error: "invalid_registration" }), request, env);
  }

  const response = await registerUser(result.data, env, false);
  if (!response.success) {
    return withCors(json(response.status, { error: response.error }), request, env);
  }

  return withCors(json(201, {
    registered: true,
    userId: response.user.userId,
    email: response.user.email,
    displayName: response.user.displayName
  }, response.cookieHeader ? { "set-cookie": response.cookieHeader } : {}), request, env);
}

async function handleLoginApi(request, env) {
  const parsed = await readJsonRequest(request, env.AUTH_RATE_LIMITER ?? env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return withCors(parsed.response, request, env);
  const result = loginSchema.safeParse(parsed.value);
  if (!result.success) {
    return withCors(json(400, { error: "invalid_login" }), request, env);
  }

  const response = await loginUser(result.data, env, false);
  if (!response.success) {
    return withCors(json(response.status, { error: response.error, message: mapAuthError(response.error) }), request, env);
  }

  return withCors(json(200, {
    sessionToken: response.sessionToken,
    userId: response.user.userId,
    email: response.user.email,
    displayName: response.user.displayName,
    authenticated: true,
    expiresUtc: response.expiresUtc
  }, response.cookieHeader ? { "set-cookie": response.cookieHeader } : {}), request, env);
}

async function handleLogoutApi(request, env) {
  const session = await requireSession(request, env, { allowBearer: true });
  if (!session.ok) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  await revokeSession(env, session.session.sessionId, new Date());
  return withCors(json(200, { success: true }, {
    "set-cookie": clearSessionCookie()
  }), request, env);
}

async function handleSessionApi(request, env) {
  const session = await requireSession(request, env, { allowBearer: true });
  if (!session.ok) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  return withCors(json(200, {
    userId: session.session.userId,
    email: session.session.email,
    displayName: session.session.displayName,
    isAuthenticated: true,
    expiresUtc: session.session.expiresUtc
  }), request, env);
}

async function handleLicenseActivation(request, env) {
  const session = await requireSession(request, env, { allowBearer: true });
  if (!session.ok) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  const parsed = await readJsonRequest(request, env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return withCors(parsed.response, request, env);
  const result = activateLicenseSchema.safeParse(parsed.value);
  if (!result.success) {
    return withCors(json(400, { error: "invalid_activation" }), request, env);
  }

  const activation = await activateLicenseForUser(
    env,
    session.session.userId,
    result.data.activationKey,
    result.data.deviceId,
    new Date());
  if (!activation.ok) {
    return withCors(json(activation.status, {
      error: activation.error,
      code: activation.error,
      message: mapLicenseError(activation.error)
    }), request, env);
  }

  return withCors(json(200, {
    licenseToken: activation.licenseToken,
    licenseId: activation.license.licenseId,
    plan: activation.license.plan,
    status: activation.license.status,
    deviceId: null,
    activatedUtc: activation.license.activatedUtc,
    expiresUtc: activation.license.expiresUtc,
    validatedUtc: activation.license.validatedUtc
  }), request, env);
}

async function handleLicenseValidation(request, env) {
  const parsed = await readJsonRequest(request, env.BETA_ACCESS_RATE_LIMITER);
  if (parsed.response) return withCors(parsed.response, request, env);
  const result = validateLicenseSchema.safeParse(parsed.value);
  if (!result.success) {
    return withCors(json(400, { error: "invalid_validation" }), request, env);
  }

  const token = bearerToken(request);
  if (!token) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  const validation = await validateLicenseToken(
    env,
    token,
    result.data.deviceId,
    new Date());
  if (!validation.ok) {
    return withCors(json(validation.status, {
      error: validation.error,
      code: validation.error,
      message: mapLicenseError(validation.error)
    }), request, env);
  }

  return withCors(json(200, {
    licenseId: validation.license.licenseId,
    plan: validation.license.plan,
    status: validation.license.status,
    deviceId: null,
    activatedUtc: validation.license.activatedUtc,
    expiresUtc: validation.license.expiresUtc,
    validatedUtc: validation.license.validatedUtc,
    isValid: validation.license.status === "active"
  }), request, env);
}

async function handleLicenseDeactivation(request, env) {
  const token = bearerToken(request);
  if (!token) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  const deactivation = await deactivateLicenseToken(env, token, new Date());
  if (!deactivation.ok) {
    return withCors(json(deactivation.status, {
      error: deactivation.error,
      code: deactivation.error,
      message: mapLicenseError(deactivation.error)
    }), request, env);
  }

  return withCors(json(200, { success: true }), request, env);
}

async function handleAccountLicenses(request, env) {
  const session = await requireSession(request, env);
  if (!session.ok) {
    return withCors(json(401, { error: "unauthorized" }), request, env);
  }

  const licenses = await listLicensesForUser(env, session.session.userId);
  return withCors(json(200, {
    licenses: licenses.map(serializeLicense)
  }), request, env);
}

async function handleAdminUsers(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return withCors(json(admin.status, { error: admin.error }), request, env);
  }

  const users = await listUsers(env);
  return withCors(json(200, {
    users: users.map(user => ({
      userId: user.userId,
      email: user.email,
      displayName: user.displayName,
      createdUtc: user.createdUtc
    }))
  }), request, env);
}

async function handleAdminBetaRequests(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return withCors(json(admin.status, { error: admin.error }), request, env);
  }

  const requests = await listBetaRequests(env);
  return withCors(json(200, { betaRequests: requests }), request, env);
}

async function handleAdminLicenses(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return withCors(json(admin.status, { error: admin.error }), request, env);
  }

  const licenses = await listAllLicenses(env);
  return withCors(json(200, {
    licenses: licenses.map(serializeLicense)
  }), request, env);
}

async function handleAdminLicenseActivate(request, env, licenseId) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return withCors(json(admin.status, { error: admin.error }), request, env);
  }

  const result = await updateLicenseStatus(
    env,
    licenseId,
    "active",
    admin.session.userId,
    new Date());
  if (!result.ok) {
    return withCors(json(result.status, { error: result.error }), request, env);
  }

  await writeAuditLog(env, {
    actorUserId: admin.session.userId,
    action: "license.activate",
    targetType: "license",
    targetId: licenseId,
    details: { nextStatus: "active" }
  });
  return withCors(json(200, { activated: true }), request, env);
}

async function handleAdminLicenseRevoke(request, env, licenseId) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return withCors(json(admin.status, { error: admin.error }), request, env);
  }

  const result = await updateLicenseStatus(
    env,
    licenseId,
    "revoked",
    admin.session.userId,
    new Date());
  if (!result.ok) {
    return withCors(json(result.status, { error: result.error }), request, env);
  }

  await writeAuditLog(env, {
    actorUserId: admin.session.userId,
    action: "license.revoke",
    targetType: "license",
    targetId: licenseId,
    details: { nextStatus: "revoked" }
  });
  return withCors(json(200, { revoked: true }), request, env);
}

async function renderRegisterPage(request, env, options) {
  const existing = await requireSession(request, env);
  if (existing.ok) {
    return htmlRedirect("/account");
  }

  return html(200, renderPageLayout("Create PC-SPA account", `
    <section class="panel">
      <p class="eyebrow">Account Access</p>
      <h1>Create your tester account</h1>
      <p class="lede">Register for PC-SPA access without changing the locked public landing page.</p>
      ${options.error ? `<p class="status error" role="alert">${escapeHtml(options.error)}</p>` : ""}
      <form method="post" action="/register" class="stack" novalidate>
        <label>Email
          <input type="email" name="email" autocomplete="email" required value="${escapeHtml(options.values?.email ?? "")}">
        </label>
        <label>Display name
          <input type="text" name="displayName" autocomplete="name" required value="${escapeHtml(options.values?.displayName ?? "")}">
        </label>
        <label>Password
          <input type="password" name="password" autocomplete="new-password" required minlength="10">
        </label>
        <label>Tester request note
          <textarea name="betaRequestNotes" rows="4">${escapeHtml(options.values?.betaRequestNotes ?? "")}</textarea>
        </label>
        <button type="submit">Create account</button>
      </form>
      <p class="subtle">Passwords are verified securely and are never displayed or logged.</p>
      <p class="subtle"><a href="/login">Already registered? Sign in.</a></p>
    </section>
  `));
}

async function renderLoginPage(request, env, options) {
  const existing = await requireSession(request, env);
  if (existing.ok) {
    return htmlRedirect("/account");
  }

  return html(200, renderPageLayout("PC-SPA sign in", `
    <section class="panel">
      <p class="eyebrow">Account Access</p>
      <h1>Sign in</h1>
      <p class="lede">Access your PC-SPA account and license status.</p>
      ${options.error ? `<p class="status error" role="alert">${escapeHtml(options.error)}</p>` : ""}
      <form method="post" action="/login" class="stack" novalidate>
        <label>Email
          <input type="email" name="email" autocomplete="email" required value="${escapeHtml(options.values?.email ?? "")}">
        </label>
        <label>Password
          <input type="password" name="password" autocomplete="current-password" required>
        </label>
        <button type="submit">Sign in</button>
      </form>
      <p class="subtle"><a href="/register">Need a tester account? Register.</a></p>
    </section>
  `));
}

async function renderAccountPage(request, env) {
  const session = await requireSession(request, env);
  if (!session.ok) {
    return htmlRedirect("/login");
  }

  const licenses = await listLicensesForUser(env, session.session.userId);
  const rows = licenses.map(license => `
    <tr>
      <td>${escapeHtml(license.label)}</td>
      <td>${escapeHtml(license.status)}</td>
      <td>${escapeHtml(license.expiresUtc)}</td>
      <td>${license.activationLimit}</td>
      <td>${license.activeDeviceCount}</td>
    </tr>
  `).join("");

  return html(200, renderPageLayout("Your PC-SPA account", `
    <section class="panel">
      <p class="eyebrow">Account</p>
      <h1>${escapeHtml(session.session.displayName ?? session.session.email)}</h1>
      <p class="lede">${escapeHtml(session.session.email)}</p>
      <p class="status success">Authenticated session active until ${escapeHtml(session.session.expiresUtc)}.</p>
      <form method="post" action="/api/auth/logout" class="inline-form" onsubmit="return submitLogout(event)">
        <button type="submit">Log out</button>
      </form>
    </section>
    <section class="panel">
      <p class="eyebrow">Licenses</p>
      <h2>Your licenses</h2>
      <table>
        <thead>
          <tr><th>Label</th><th>Status</th><th>Expiry</th><th>Activation limit</th><th>Active devices</th></tr>
        </thead>
        <tbody>${rows || "<tr><td colspan='5'>No licenses assigned yet.</td></tr>"}</tbody>
      </table>
    </section>
  `, logoutScript()));
}

async function renderAdminOverviewPage(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) {
    return admin.status === 401
      ? htmlRedirect("/login")
      : html(403, renderPageLayout("Admin access denied", "<section class='panel'><h1>Admin access required</h1></section>"));
  }

  return html(200, renderPageLayout("PC-SPA admin", `
    <section class="panel">
      <p class="eyebrow">Admin</p>
      <h1>License and onboarding administration</h1>
      <nav class="admin-nav">
        <a href="/admin/users">Users</a>
        <a href="/admin/beta-requests">Beta requests</a>
        <a href="/admin/licenses">Licenses</a>
      </nav>
    </section>
  `));
}

async function renderAdminUsersPage(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) return adminPageFailure(admin);
  const users = await listUsers(env);
  return html(200, renderPageLayout("Admin users", `
    <section class="panel">
      <p class="eyebrow">Admin</p>
      <h1>Users</h1>
      <table>
        <thead><tr><th>User ID</th><th>Email</th><th>Display name</th><th>Created</th></tr></thead>
        <tbody>${users.map(user => `<tr><td>${escapeHtml(user.userId)}</td><td>${escapeHtml(user.email)}</td><td>${escapeHtml(user.displayName)}</td><td>${escapeHtml(user.createdUtc)}</td></tr>`).join("") || "<tr><td colspan='4'>No users found.</td></tr>"}</tbody>
      </table>
    </section>
  `));
}

async function renderAdminBetaRequestsPage(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) return adminPageFailure(admin);
  const requests = await listBetaRequests(env);
  return html(200, renderPageLayout("Admin beta requests", `
    <section class="panel">
      <p class="eyebrow">Admin</p>
      <h1>Beta requests</h1>
      <table>
        <thead><tr><th>Email</th><th>Name</th><th>Status</th><th>Created</th></tr></thead>
        <tbody>${requests.map(item => `<tr><td>${escapeHtml(item.email)}</td><td>${escapeHtml(item.displayName)}</td><td>${escapeHtml(item.status)}</td><td>${escapeHtml(item.createdUtc)}</td></tr>`).join("") || "<tr><td colspan='4'>No requests found.</td></tr>"}</tbody>
      </table>
    </section>
  `));
}

async function renderAdminLicensesPage(request, env, flash = {}) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) return adminPageFailure(admin);
  const users = await listUsers(env);
  const licenses = await listAllLicenses(env);
  return html(200, renderPageLayout("Admin licenses", `
    <section class="panel">
      <p class="eyebrow">Admin</p>
      <h1>Issue and manage licenses</h1>
      ${flash.error ? `<p class="status error" role="alert">${escapeHtml(flash.error)}</p>` : ""}
      ${flash.activationKey ? `
        <div class="status warning" role="status">
          <strong>Activation key shown once:</strong>
          <code>${escapeHtml(flash.activationKey)}</code>
          <p>This raw activation key cannot be recovered later. Store it securely before leaving this page.</p>
        </div>
      ` : ""}
      <form method="post" action="/admin/licenses/issue" class="stack" novalidate>
        <label>User
          <select name="userId" required>
            ${users.map(user => `<option value="${escapeHtml(user.userId)}">${escapeHtml(user.displayName)} (${escapeHtml(user.email)})</option>`).join("")}
          </select>
        </label>
        <label>Label
          <input type="text" name="label" required>
        </label>
        <label>Plan
          <input type="text" name="plan" required value="beta">
        </label>
        <label>Activation limit
          <input type="number" name="activationLimit" min="1" max="1000" required value="1">
        </label>
        <label>Expiry (UTC)
          <input type="datetime-local" name="expiresUtc" required>
        </label>
        <button type="submit">Issue license</button>
      </form>
    </section>
    <section class="panel">
      <table>
        <thead><tr><th>License</th><th>User</th><th>Status</th><th>Expiry</th><th>Limit</th><th>Active devices</th><th>Actions</th></tr></thead>
        <tbody>${licenses.map(license => `
          <tr>
            <td>${escapeHtml(license.label)}<div class="subtle">${escapeHtml(license.licenseId)}</div></td>
            <td>${escapeHtml(license.userEmail)}</td>
            <td>${escapeHtml(license.status)}</td>
            <td>${escapeHtml(license.expiresUtc)}</td>
            <td>${license.activationLimit}</td>
            <td>${license.activeDeviceCount}</td>
            <td>
              <form method="post" action="/api/admin/licenses/${encodeURIComponent(license.licenseId)}/activate" class="inline-form" onsubmit="return submitAdminAction(event, 'activate')"><button type="submit">Activate pending</button></form>
              <form method="post" action="/api/admin/licenses/${encodeURIComponent(license.licenseId)}/revoke" class="inline-form" onsubmit="return submitAdminAction(event, 'revoke')"><button type="submit">Revoke</button></form>
            </td>
          </tr>
        `).join("") || "<tr><td colspan='7'>No licenses issued yet.</td></tr>"}</tbody>
      </table>
    </section>
  `, adminActionScript()));
}

async function handleIssueLicensePageSubmission(request, env) {
  const admin = await requireAdminSession(request, env);
  if (!admin.ok) return adminPageFailure(admin);
  const form = await request.formData();
  const isoValue = normalizeDateTimeLocal(String(form.get("expiresUtc") ?? ""));
  const parsed = accountIssueLicenseSchema.safeParse({
    userId: String(form.get("userId") ?? ""),
    label: String(form.get("label") ?? ""),
    plan: String(form.get("plan") ?? ""),
    activationLimit: String(form.get("activationLimit") ?? ""),
    expiresUtc: isoValue
  });
  if (!parsed.success) {
    return renderAdminLicensesPage(request, env, {
      error: "Enter a valid user, label, plan, activation limit, and future expiry."
    });
  }

  const issued = await issueLicense(env, parsed.data, admin.session.userId, new Date());
  return renderAdminLicensesPage(request, env, {
    activationKey: issued.activationKey
  });
}

async function registerUser(input, env, setCookie) {
  const existing = await getUserByEmail(env, input.email);
  if (existing) {
    return { success: false, status: 409, error: "email_already_registered" };
  }

  const now = new Date();
  const userId = `usr_${randomHex(8)}`;
  const passwordHash = await createPasswordHash(input.password);
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO auth_users (
      user_id, email, password_hash, display_name, created_utc
    ) VALUES (?, ?, ?, ?, ?)
  `).bind(
    userId,
    normalizeEmail(input.email),
    passwordHash,
    sanitizeText(input.displayName),
    now.toISOString()
  ).run();
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO beta_requests (
      request_id, email, display_name, notes, status, created_utc, reviewed_utc, reviewed_by_user_id
    ) VALUES (?, ?, ?, ?, 'pending', ?, NULL, NULL)
  `).bind(
    `req_${randomHex(8)}`,
    normalizeEmail(input.email),
    sanitizeText(input.displayName),
    sanitizeText(input.betaRequestNotes),
    now.toISOString()
  ).run();

  const session = await createSession(env, {
    userId,
    email: normalizeEmail(input.email),
    displayName: sanitizeText(input.displayName)
  }, now, setCookie);

  return {
    success: true,
    user: {
      userId,
      email: normalizeEmail(input.email),
      displayName: sanitizeText(input.displayName)
    },
    sessionToken: session.sessionToken,
    expiresUtc: session.expiresUtc,
    cookieHeader: session.cookieHeader
  };
}

async function loginUser(input, env, setCookie) {
  const user = await getUserByEmail(env, input.email);
  if (!user) {
    return { success: false, status: 401, error: "invalid_credentials" };
  }

  const matches = await verifyPassword(
    user.passwordHash,
    input.password);
  if (!matches) {
    return { success: false, status: 401, error: "invalid_credentials" };
  }

  const session = await createSession(env, user, new Date(), setCookie);
  return {
    success: true,
    user,
    sessionToken: session.sessionToken,
    expiresUtc: session.expiresUtc,
    cookieHeader: session.cookieHeader
  };
}

async function createSession(env, user, now, includeCookie) {
  const sessionId = `ses_${randomHex(8)}`;
  const sessionToken = randomHex(32);
  const sessionTokenHash = await hashAuthValue(sessionToken, env);
  const expires = new Date(now.getTime() + SESSION_DAYS * 24 * 60 * 60 * 1000);
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO auth_sessions (
      session_id, session_token_hash, user_id, created_utc, expires_utc, last_seen_utc, revoked_utc
    ) VALUES (?, ?, ?, ?, ?, ?, NULL)
  `).bind(
    sessionId,
    sessionTokenHash,
    user.userId,
    now.toISOString(),
    expires.toISOString(),
    now.toISOString()
  ).run();
  return {
    sessionToken,
    expiresUtc: expires.toISOString(),
    cookieHeader: includeCookie
      ? createSessionCookie(sessionToken, expires)
      : null
  };
}

async function requireSession(request, env, options = {}) {
  const token = options.allowBearer
    ? bearerToken(request) ?? readSessionCookie(request)
    : readSessionCookie(request);
  if (!token) {
    return { ok: false, status: 401, error: "unauthorized" };
  }

  const sessionTokenHash = await hashAuthValue(token, env);
  const record = await env.FEEDBACK_DB.prepare(`
    SELECT s.session_id, s.user_id, s.expires_utc, s.revoked_utc,
      u.email, u.display_name
    FROM auth_sessions s
    JOIN auth_users u ON u.user_id = s.user_id
    WHERE s.session_token_hash = ?
  `).bind(sessionTokenHash).first();
  if (!record || record.revoked_utc || Date.parse(record.expires_utc) <= Date.now()) {
    return { ok: false, status: 401, error: "unauthorized" };
  }

  await env.FEEDBACK_DB.prepare(`
    UPDATE auth_sessions SET last_seen_utc = ?
    WHERE session_id = ?
  `).bind(new Date().toISOString(), record.session_id).run();
  return {
    ok: true,
    session: {
      sessionId: record.session_id,
      userId: record.user_id,
      email: record.email,
      displayName: record.display_name,
      expiresUtc: record.expires_utc
    }
  };
}

async function requireAdminSession(request, env) {
  const session = await requireSession(request, env);
  if (!session.ok) {
    return session;
  }

  const adminIds = String(env.ADMIN_USER_IDS ?? "")
    .split(",")
    .map(value => value.trim())
    .filter(Boolean);
  if (!adminIds.includes(session.session.userId)) {
    return { ok: false, status: 403, error: "admin_required" };
  }
  return session;
}

async function revokeSession(env, sessionId, now) {
  await env.FEEDBACK_DB.prepare(`
    UPDATE auth_sessions SET revoked_utc = ?
    WHERE session_id = ? AND revoked_utc IS NULL
  `).bind(now.toISOString(), sessionId).run();
}

async function issueLicense(env, input, actorUserId, now) {
  const activationKey = `PCSPA-${randomHex(10).toUpperCase()}`;
  const keyHash = await hashAuthValue(activationKey, env);
  const licenseId = `lic_${randomHex(8)}`;
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO licenses (
      license_id, user_id, license_key_hash, license_key_suffix, label, plan,
      status, activation_limit, expires_utc, created_utc, issued_by_user_id,
      activated_utc, activated_by_user_id, revoked_utc, revoked_by_user_id,
      last_issued_key_utc
    ) VALUES (?, ?, ?, ?, ?, ?, 'pending', ?, ?, ?, ?, NULL, NULL, NULL, NULL, ?)
  `).bind(
    licenseId,
    input.userId,
    keyHash,
    activationKey.slice(-6),
    sanitizeText(input.label),
    sanitizeText(input.plan),
    input.activationLimit,
    new Date(input.expiresUtc).toISOString(),
    now.toISOString(),
    actorUserId,
    now.toISOString()
  ).run();
  await writeAuditLog(env, {
    actorUserId,
    action: "license.issue",
    targetType: "license",
    targetId: licenseId,
    details: {
      userId: input.userId,
      activationLimit: input.activationLimit,
      expiresUtc: new Date(input.expiresUtc).toISOString()
    }
  });
  return { licenseId, activationKey };
}

async function activateLicenseForUser(env, userId, activationKey, deviceId, now) {
  const keyHash = await hashAuthValue(activationKey.trim().toUpperCase(), env);
  const license = await env.FEEDBACK_DB.prepare(`
    SELECT license_id, user_id, label, plan, status, activation_limit, expires_utc
    FROM licenses
    WHERE license_key_hash = ?
  `).bind(keyHash).first();
  if (!license || license.user_id !== userId) {
    return { ok: false, status: 403, error: "invalid_activation_key" };
  }
  if (license.status === "revoked") {
    return { ok: false, status: 403, error: "license_revoked" };
  }
  if (license.status === "expired" || Date.parse(license.expires_utc) <= now.getTime()) {
    return { ok: false, status: 403, error: "license_expired" };
  }
  if (license.status !== "active") {
    return { ok: false, status: 409, error: "license_pending" };
  }

  const deviceHash = await hashAuthValue(deviceId, env);
  const activeDevices = await countLicenseActivations(env, license.license_id);
  const existing = await env.FEEDBACK_DB.prepare(`
    SELECT activation_id, revoked_utc
    FROM license_activations
    WHERE license_id = ? AND device_hash = ?
  `).bind(license.license_id, deviceHash).first();
  if (!existing && activeDevices >= license.activation_limit) {
    return { ok: false, status: 409, error: "activation_limit_reached" };
  }

  if (!existing) {
    await env.FEEDBACK_DB.prepare(`
      INSERT INTO license_activations (
        activation_id, license_id, user_id, device_hash, device_count_key,
        activated_utc, last_validated_utc, revoked_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, NULL)
    `).bind(
      `act_${randomHex(8)}`,
      license.license_id,
      userId,
      deviceHash,
      `dev_${randomHex(6)}`,
      now.toISOString(),
      now.toISOString()
    ).run();
  } else {
    await env.FEEDBACK_DB.prepare(`
      UPDATE license_activations SET revoked_utc = NULL, last_validated_utc = ?
      WHERE activation_id = ?
    `).bind(now.toISOString(), existing.activation_id).run();
  }

  const licenseToken = randomHex(32);
  const tokenHash = await hashAuthValue(licenseToken, env);
  await env.FEEDBACK_DB.prepare(`
    INSERT OR REPLACE INTO beta_entitlements (
      entitlement_reference, entitlement_token_hash, invitation_code_hash,
      installation_hash, application_version, activated_utc, expires_utc,
      last_verified_utc, revoked_utc
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)
  `).bind(
    license.license_id,
    tokenHash,
    keyHash,
    deviceHash,
    "pc-spa-web",
    now.toISOString(),
    license.expires_utc,
    now.toISOString()
  ).run();

  return {
    ok: true,
    licenseToken,
    license: {
      licenseId: license.license_id,
      plan: license.plan,
      status: "active",
      activatedUtc: now.toISOString(),
      expiresUtc: license.expires_utc,
      validatedUtc: now.toISOString()
    }
  };
}

async function validateLicenseToken(env, token, deviceId, now) {
  const tokenHash = await hashAuthValue(token, env);
  const deviceHash = await hashAuthValue(deviceId, env);
  const entitlement = await env.FEEDBACK_DB.prepare(`
    SELECT b.entitlement_reference, b.installation_hash, b.expires_utc, b.revoked_utc,
      l.plan, l.status
    FROM beta_entitlements b
    JOIN licenses l ON l.license_id = b.entitlement_reference
    WHERE b.entitlement_token_hash = ?
  `).bind(tokenHash).first();
  if (!entitlement) {
    return { ok: false, status: 403, error: "invalid_license" };
  }
  if (entitlement.installation_hash !== deviceHash) {
    return { ok: false, status: 422, error: "invalid_device" };
  }
  if (entitlement.revoked_utc || entitlement.status === "revoked") {
    return { ok: false, status: 403, error: "license_revoked" };
  }
  if (Date.parse(entitlement.expires_utc) <= now.getTime()) {
    return { ok: false, status: 403, error: "license_expired" };
  }
  if (entitlement.status === "pending") {
    return { ok: false, status: 409, error: "license_pending" };
  }

  await env.FEEDBACK_DB.prepare(`
    UPDATE beta_entitlements SET last_verified_utc = ?
    WHERE entitlement_token_hash = ?
  `).bind(now.toISOString(), tokenHash).run();
  return {
    ok: true,
    license: {
      licenseId: entitlement.entitlement_reference,
      plan: entitlement.plan,
      status: "active",
      activatedUtc: null,
      expiresUtc: entitlement.expires_utc,
      validatedUtc: now.toISOString()
    }
  };
}

async function deactivateLicenseToken(env, token, now) {
  const tokenHash = await hashAuthValue(token, env);
  const entitlement = await env.FEEDBACK_DB.prepare(`
    SELECT entitlement_reference, installation_hash
    FROM beta_entitlements
    WHERE entitlement_token_hash = ?
  `).bind(tokenHash).first();
  if (!entitlement) {
    return { ok: false, status: 403, error: "invalid_license" };
  }
  await env.FEEDBACK_DB.prepare(`
    UPDATE beta_entitlements SET revoked_utc = ?
    WHERE entitlement_token_hash = ?
  `).bind(now.toISOString(), tokenHash).run();
  await env.FEEDBACK_DB.prepare(`
    UPDATE license_activations SET revoked_utc = ?
    WHERE license_id = ? AND device_hash = ?
  `).bind(now.toISOString(), entitlement.entitlement_reference, entitlement.installation_hash).run();
  return { ok: true };
}

async function updateLicenseStatus(env, licenseId, nextStatus, actorUserId, now) {
  const result = nextStatus === "active"
    ? await env.FEEDBACK_DB.prepare(`
      UPDATE licenses
      SET status = 'active', activated_utc = ?, activated_by_user_id = ?
      WHERE license_id = ? AND status = 'pending'
    `).bind(now.toISOString(), actorUserId, licenseId).run()
    : await env.FEEDBACK_DB.prepare(`
      UPDATE licenses
      SET status = 'revoked', revoked_utc = ?, revoked_by_user_id = ?
      WHERE license_id = ? AND status != 'revoked'
    `).bind(now.toISOString(), actorUserId, licenseId).run();
  if ((result.meta?.changes ?? 0) !== 1) {
    return { ok: false, status: 404, error: "license_not_found" };
  }
  if (nextStatus === "revoked") {
    await env.FEEDBACK_DB.prepare(`
      UPDATE beta_entitlements SET revoked_utc = ?
      WHERE entitlement_reference = ?
    `).bind(now.toISOString(), licenseId).run();
  }
  return { ok: true };
}

async function listLicensesForUser(env, userId) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT l.license_id, l.label, l.plan, l.status, l.expires_utc, l.activation_limit,
      l.activated_utc, COUNT(a.activation_id) AS active_device_count
    FROM licenses l
    LEFT JOIN license_activations a
      ON a.license_id = l.license_id AND a.revoked_utc IS NULL
    WHERE l.user_id = ?
    GROUP BY l.license_id
    ORDER BY l.created_utc DESC
  `).bind(userId).all();
  return (result.results ?? []).map(row => ({
    licenseId: row.license_id,
    label: row.label,
    plan: row.plan,
    status: row.status,
    expiresUtc: row.expires_utc,
    activationLimit: row.activation_limit,
    activeDeviceCount: Number(row.active_device_count ?? 0),
    activatedUtc: row.activated_utc,
    userEmail: null
  }));
}

async function listAllLicenses(env) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT l.license_id, l.label, l.plan, l.status, l.expires_utc, l.activation_limit,
      l.activated_utc, u.email,
      COUNT(a.activation_id) AS active_device_count
    FROM licenses l
    JOIN auth_users u ON u.user_id = l.user_id
    LEFT JOIN license_activations a
      ON a.license_id = l.license_id AND a.revoked_utc IS NULL
    GROUP BY l.license_id
    ORDER BY l.created_utc DESC
  `).all();
  return (result.results ?? []).map(row => ({
    licenseId: row.license_id,
    label: row.label,
    plan: row.plan,
    status: row.status,
    expiresUtc: row.expires_utc,
    activationLimit: row.activation_limit,
    activeDeviceCount: Number(row.active_device_count ?? 0),
    activatedUtc: row.activated_utc,
    userEmail: row.email
  }));
}

async function listUsers(env) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT user_id, email, display_name, created_utc
    FROM auth_users
    ORDER BY created_utc DESC
  `).all();
  return (result.results ?? []).map(row => ({
    userId: row.user_id,
    email: row.email,
    displayName: row.display_name,
    createdUtc: row.created_utc
  }));
}

async function listBetaRequests(env) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT request_id, email, display_name, notes, status, created_utc, reviewed_utc
    FROM beta_requests
    ORDER BY created_utc DESC
  `).all();
  return (result.results ?? []).map(row => ({
    requestId: row.request_id,
    email: row.email,
    displayName: row.display_name,
    notes: row.notes,
    status: row.status,
    createdUtc: row.created_utc,
    reviewedUtc: row.reviewed_utc
  }));
}

async function getUserByEmail(env, email) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT user_id, email, password_hash, display_name, created_utc
    FROM auth_users
    WHERE email = ?
  `).bind(normalizeEmail(email)).first();
  return result
    ? {
      userId: result.user_id,
      email: result.email,
      passwordHash: result.password_hash,
      displayName: result.display_name,
      createdUtc: result.created_utc
    }
    : null;
}

async function countLicenseActivations(env, licenseId) {
  const result = await env.FEEDBACK_DB.prepare(`
    SELECT COUNT(*) AS active_count
    FROM license_activations
    WHERE license_id = ? AND revoked_utc IS NULL
  `).bind(licenseId).first();
  return Number(result?.active_count ?? 0);
}

async function writeAuditLog(env, event) {
  await env.FEEDBACK_DB.prepare(`
    INSERT INTO audit_log (
      audit_id, actor_user_id, action, target_type, target_id, details_json, created_utc
    ) VALUES (?, ?, ?, ?, ?, ?, ?)
  `).bind(
    `aud_${randomHex(8)}`,
    event.actorUserId ?? null,
    event.action,
    event.targetType,
    event.targetId,
    JSON.stringify(event.details ?? {}),
    new Date().toISOString()
  ).run();
}

function serializeLicense(license) {
  return {
    licenseId: license.licenseId,
    label: license.label,
    plan: license.plan,
    status: license.status,
    expiresUtc: license.expiresUtc,
    activationLimit: license.activationLimit,
    activeDeviceCount: license.activeDeviceCount,
    activatedUtc: license.activatedUtc,
    userEmail: license.userEmail
  };
}

function mapAuthError(error) {
  return error === "invalid_credentials"
    ? "The email address or password is incorrect."
    : error === "email_already_registered"
      ? "An account with this email address already exists."
      : "The request could not be completed.";
}

function mapLicenseError(error) {
  switch (error) {
    case "invalid_activation_key":
      return "The activation key is invalid for this account.";
    case "license_pending":
      return "This license is pending admin activation.";
    case "license_revoked":
      return "This license has been revoked.";
    case "license_expired":
      return "This license has expired.";
    case "activation_limit_reached":
      return "This license has reached its activation limit.";
    case "invalid_device":
      return "This activation does not match the current device.";
    case "invalid_license":
      return "The saved license token is no longer valid.";
    default:
      return "The license request could not be completed.";
  }
}

function readSessionCookie(request) {
  const cookieHeader = request.headers.get("cookie") ?? "";
  for (const part of cookieHeader.split(";")) {
    const [name, ...rest] = part.trim().split("=");
    if (name === COOKIE_NAME) {
      return decodeURIComponent(rest.join("="));
    }
  }
  return null;
}

function bearerToken(request) {
  const header = request.headers.get("authorization") ?? "";
  return header.startsWith("Bearer ")
    ? header.slice("Bearer ".length)
    : null;
}

function createSessionCookie(token, expires) {
  return `${COOKIE_NAME}=${encodeURIComponent(token)}; Path=/; HttpOnly; Secure; SameSite=Lax; Expires=${expires.toUTCString()}`;
}

function clearSessionCookie() {
  return `${COOKIE_NAME}=; Path=/; HttpOnly; Secure; SameSite=Lax; Expires=Thu, 01 Jan 1970 00:00:00 GMT`;
}

function normalizeEmail(value) {
  return String(value ?? "").trim().toLowerCase();
}

function normalizeDateTimeLocal(value) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? ""
    : date.toISOString();
}

async function createPasswordHash(password) {
  const salt = randomHex(16);
  const iterations = 120000;
  const digest = await derivePasswordHash(password, salt, iterations);
  return `pbkdf2$${iterations}$${salt}$${digest}`;
}

async function verifyPassword(storedHash, password) {
  const [scheme, iterationText, salt, digest] =
    String(storedHash).split("$");
  if (scheme !== "pbkdf2") return false;
  const iterations = Number(iterationText);
  const candidate = await derivePasswordHash(password, salt, iterations);
  return constantTimeEqual(candidate, digest);
}

async function derivePasswordHash(password, salt, iterations) {
  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    { name: "PBKDF2" },
    false,
    ["deriveBits"]
  );
  const bits = await crypto.subtle.deriveBits({
    name: "PBKDF2",
    hash: "SHA-256",
    salt: new TextEncoder().encode(salt),
    iterations
  }, keyMaterial, 256);
  return toHex(new Uint8Array(bits));
}

async function hashAuthValue(value, env) {
  return hashValue(value, env.AUTH_HASH_SALT ?? env.BETA_ACCESS_HASH_SALT ?? env.FEEDBACK_HASH_SALT);
}

function renderPageLayout(title, content, script = "") {
  return `<!doctype html>
  <html lang="en">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>${escapeHtml(title)}</title>
      <style>
        :root { color-scheme: light; --bg:#f5f1e8; --panel:#fffaf2; --border:#d8cfbf; --text:#211b14; --muted:#6e6256; --accent:#7b5a2f; --accent-soft:#efe3cf; --danger:#8f2d2d; --success:#2f6b45; }
        *{box-sizing:border-box} body{margin:0;font-family:Segoe UI,system-ui,sans-serif;background:linear-gradient(180deg,#f6f1e7 0%,#efe5d4 100%);color:var(--text)} a{color:var(--accent)} main{max-width:980px;margin:0 auto;padding:40px 20px 64px}.panel{background:rgba(255,250,242,.92);border:1px solid var(--border);border-radius:18px;padding:24px 24px 28px;box-shadow:0 18px 40px rgba(73,49,24,.08);margin-bottom:20px}.eyebrow{text-transform:uppercase;letter-spacing:.14em;font-size:.74rem;color:var(--muted);font-weight:700}.lede,.subtle{color:var(--muted)} .stack{display:grid;gap:14px}.admin-nav{display:flex;gap:14px;flex-wrap:wrap}.admin-nav a{display:inline-block;padding:10px 14px;border-radius:999px;background:var(--accent-soft);text-decoration:none;color:var(--text)} label{display:grid;gap:6px;font-weight:600} input,select,textarea{width:100%;padding:12px 14px;border-radius:12px;border:1px solid var(--border);background:white;font:inherit} button{padding:12px 16px;border:0;border-radius:12px;background:var(--accent);color:white;font:inherit;font-weight:700;cursor:pointer} table{width:100%;border-collapse:collapse;margin-top:12px} th,td{text-align:left;padding:12px 10px;border-bottom:1px solid var(--border);vertical-align:top} .status{padding:12px 14px;border-radius:12px;margin:14px 0}.status.error{background:#fce9e7;color:var(--danger)} .status.success{background:#e5f4e8;color:var(--success)} .status.warning{background:#fff1d8;color:#7d5800} .inline-form{display:inline-block;margin:0 8px 8px 0} code{display:inline-block;padding:4px 8px;border-radius:8px;background:#f0e8da} h1,h2{margin:.2rem 0 1rem}
      </style>
    </head>
    <body>
      <main>${content}</main>
      ${script}
    </body>
  </html>`;
}

function logoutScript() {
  return `<script>
    async function submitLogout(event) {
      event.preventDefault();
      const response = await fetch('/api/auth/logout', { method:'POST', credentials:'same-origin' });
      if (response.ok) window.location.href = '/login';
      return false;
    }
  </script>`;
}

function adminActionScript() {
  return `<script>
    async function submitAdminAction(event, actionName) {
      event.preventDefault();
      const response = await fetch(event.target.action, { method:'POST', credentials:'same-origin' });
      if (response.ok) { window.location.reload(); return false; }
      const payload = await response.json().catch(() => ({ error: 'request_failed' }));
      alert(actionName + ' failed: ' + (payload.error || 'request_failed'));
      return false;
    }
  </script>`;
}

function adminPageFailure(admin) {
  return admin.status === 401
    ? htmlRedirect("/login")
    : html(403, renderPageLayout("Admin access denied", "<section class='panel'><h1>Admin access required</h1></section>"));
}

function html(status, content, additionalHeaders = {}) {
  return new Response(content, {
    status,
    headers: { ...pageHeaders, ...additionalHeaders }
  });
}

function htmlRedirect(location, cookieHeader) {
  const headers = { location };
  if (cookieHeader) headers["set-cookie"] = cookieHeader;
  return html(302, "", headers);
}

function withCors(response, request, env) {
  const origin = request.headers.get("origin");
  const allowedOrigin = env.CORS_ORIGIN ?? origin ?? "*";
  if (!origin) {
    return response;
  }
  const headers = new Headers(response.headers);
  headers.set("access-control-allow-origin", allowedOrigin);
  headers.set("access-control-allow-credentials", "true");
  headers.set("vary", "origin");
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers
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
  return toHex(new Uint8Array(digest));
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
  return toHex(bytes);
}

function toHex(bytes) {
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
  return toHex(new Uint8Array(digest));
}

function json(status, body, additionalHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...responseHeaders, ...additionalHeaders }
  });
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}
