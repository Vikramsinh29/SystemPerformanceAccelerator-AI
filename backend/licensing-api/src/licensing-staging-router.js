import { LICENSING_RUNTIME_OPERATIONS } from "./licensing-worker-runtime.js";

const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

const ROUTES = Object.freeze(new Map([
  ["/account/license", LICENSING_RUNTIME_OPERATIONS.READ_ACCOUNT_LICENSE],
  ["/activate", LICENSING_RUNTIME_OPERATIONS.ACTIVATE_DEVICE],
  ["/deactivate", LICENSING_RUNTIME_OPERATIONS.DEACTIVATE_DEVICE],
  ["/validate", LICENSING_RUNTIME_OPERATIONS.VALIDATE_DEVICE]
]));

export function createLicensingStagingRouter({ runtime } = {}) {
  requireRuntime(runtime);

  return Object.freeze({
    async fetch(request) {
      if (!request || typeof request.url !== "string") {
        return json(400, { error: "invalid_request" });
      }

      let url;
      try {
        url = new URL(request.url);
      } catch {
        return json(400, { error: "invalid_request" });
      }

      const operation = ROUTES.get(url.pathname);
      if (!operation) {
        return json(404, { error: "not_found" });
      }

      return runtime.handle(operation, request);
    }
  });
}

function requireRuntime(runtime) {
  if (!runtime || typeof runtime.handle !== "function") {
    throw new TypeError("runtime.handle is required.");
  }
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
