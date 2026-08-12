import { createProductionTokenAcquisitionHandler } from "./production-token-acquisition.js";

const INTERNAL_HOST = "licensing-v2.internal";
const INTERNAL_PATH = "/internal/token-acquisition";
const MAX_BODY_BYTES = 4096;

export function isProductionInternalTokenAcquisitionRequest(request) {
  if (!request || typeof request.url !== "string") return false;

  let url;
  try {
    url = new URL(request.url);
  } catch {
    return false;
  }

  return url.protocol === "https:" &&
    url.hostname === INTERNAL_HOST &&
    url.pathname === INTERNAL_PATH;
}

export function createProductionInternalTokenAcquisitionHandler({
  identitySecret,
  clock,
  lifetimeSeconds
} = {}) {
  const resolveAuthenticatedAccount = async (request) => {
    if (!isProductionInternalTokenAcquisitionRequest(request)) return null;

    const contentType = request.headers?.get?.("content-type") ?? "";
    if (!contentType.toLowerCase().startsWith("application/json")) {
      throw new TypeError("Internal token acquisition requires JSON.");
    }

    const contentLength = Number(request.headers?.get?.("content-length") ?? "0");
    if (Number.isFinite(contentLength) && contentLength > MAX_BODY_BYTES) {
      throw new TypeError("Internal token acquisition body is too large.");
    }

    const text = await request.text();
    if (text.length === 0 || text.length > MAX_BODY_BYTES) {
      throw new TypeError("Internal token acquisition body is invalid.");
    }

    let body;
    try {
      body = JSON.parse(text);
    } catch {
      throw new TypeError("Internal token acquisition body must be valid JSON.");
    }

    if (!body || typeof body !== "object" || Array.isArray(body)) {
      throw new TypeError("Internal token acquisition identity must be an object.");
    }

    return Object.freeze({
      accountId: requireClaim(body.accountId, "accountId"),
      productId: requireClaim(body.productId, "productId")
    });
  };

  return createProductionTokenAcquisitionHandler({
    resolveAuthenticatedAccount,
    identitySecret,
    ...(clock === undefined ? {} : { clock }),
    ...(lifetimeSeconds === undefined ? {} : { lifetimeSeconds })
  });
}

function requireClaim(value, name) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > 128) {
    throw new TypeError(`${name} must be a non-empty string no longer than 128 characters.`);
  }
  return value.trim();
}
