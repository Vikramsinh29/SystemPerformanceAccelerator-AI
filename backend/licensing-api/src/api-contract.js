export const LICENSING_API_VERSION = "v1";

export const LICENSING_ROUTES = Object.freeze({
  entitlement: "/v1/internal/entitlements/:accountId/:productId",
  paymentEvent: "/v1/internal/payment-events",
  activate: "/v1/desktop/activate",
  validate: "/v1/desktop/validate",
  transfer: "/v1/desktop/transfer"
});

export const LICENSING_ENVIRONMENTS = new Set(["local", "staging", "production"]);

export function requireEnvironment(value) {
  if (typeof value !== "string" || !LICENSING_ENVIRONMENTS.has(value)) {
    throw new TypeError("LICENSING_ENVIRONMENT must be local, staging, or production.");
  }
  return value;
}

export function requireInternalAuthHeader(request) {
  const value = request?.headers?.get?.("authorization");
  if (typeof value !== "string" || !value.startsWith("Bearer ") || value.length <= 7) {
    return false;
  }
  return true;
}

export function jsonResponse(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store"
    }
  });
}
