import { createLicensingWorkerRuntime } from "./licensing-worker-runtime.js";
import { createLicensingStagingRouter } from "./licensing-staging-router.js";

const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

export default {
  async fetch(request, env) {
    if (!isAuthorized(request, env)) {
      return json(401, { error: "staging_unauthorized" });
    }

    const stagingAccountId = requireText(env?.STAGING_ACCOUNT_ID, "STAGING_ACCOUNT_ID");
    const runtime = createLicensingWorkerRuntime({
      env,
      resolveAuthenticatedAccount: async () => ({ accountId: stagingAccountId })
    });
    const router = createLicensingStagingRouter({ runtime });
    return router.fetch(request);
  }
};

function isAuthorized(request, env) {
  const expected = env?.STAGING_ACCESS_TOKEN;
  if (typeof expected !== "string" || expected.length < 16) return false;
  const provided = request?.headers?.get?.("x-pcspa-staging-token");
  return typeof provided === "string" && provided === expected;
}

function requireText(value, name) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${name} is required.`);
  }
  return value.trim();
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
