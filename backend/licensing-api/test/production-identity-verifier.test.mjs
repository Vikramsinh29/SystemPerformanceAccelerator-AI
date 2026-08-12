import assert from "node:assert/strict";
import test from "node:test";
import {
  ProductionIdentityError,
  createProductionIdentityResolver
} from "../src/production-identity-verifier.js";

const secret = "production-identity-test-secret-0123456789abcdef";
const nowMs = Date.parse("2026-08-12T06:00:00Z");

async function issue(payload, signingSecret = secret) {
  const prefix = "pcspa1";
  const payloadPart = base64url(new TextEncoder().encode(JSON.stringify(payload)));
  const signingInput = `${prefix}.${payloadPart}`;
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(signingSecret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = new Uint8Array(
    await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(signingInput))
  );
  return `${signingInput}.${base64url(signature)}`;
}

function base64url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

function request(token) {
  const headers = token ? { authorization: `Bearer ${token}` } : {};
  return new Request("https://licensing.invalid/account/license", { headers });
}

test("production identity token resolves trusted account and optional product claims", async () => {
  const resolver = createProductionIdentityResolver({ secret, clock: () => nowMs });
  const token = await issue({
    v: 1,
    aud: "pc-spa-licensing-v2",
    sub: "acct-production-1",
    product: "pcspa-pro",
    exp: Math.floor(nowMs / 1000) + 300
  });

  assert.deepEqual(await resolver(request(token)), {
    accountId: "acct-production-1",
    productId: "pcspa-pro"
  });
});

test("production identity boundary rejects missing and tampered bearer tokens", async () => {
  const resolver = createProductionIdentityResolver({ secret, clock: () => nowMs });

  await assert.rejects(() => resolver(request()), error =>
    error instanceof ProductionIdentityError && error.code === "missing_identity_token");

  const token = await issue({
    v: 1,
    aud: "pc-spa-licensing-v2",
    sub: "acct-production-1",
    exp: Math.floor(nowMs / 1000) + 300
  });
  const tampered = `${token.slice(0, -1)}${token.endsWith("A") ? "B" : "A"}`;

  await assert.rejects(() => resolver(request(tampered)), error =>
    error instanceof ProductionIdentityError && error.code === "invalid_identity_token");
});

test("production identity boundary rejects expired or wrong-audience tokens", async () => {
  const resolver = createProductionIdentityResolver({ secret, clock: () => nowMs });

  const expired = await issue({
    v: 1,
    aud: "pc-spa-licensing-v2",
    sub: "acct-production-1",
    exp: Math.floor(nowMs / 1000) - 1
  });
  await assert.rejects(() => resolver(request(expired)), error =>
    error instanceof ProductionIdentityError && error.code === "expired_identity_token");

  const wrongAudience = await issue({
    v: 1,
    aud: "pc-spa-web",
    sub: "acct-production-1",
    exp: Math.floor(nowMs / 1000) + 300
  });
  await assert.rejects(() => resolver(request(wrongAudience)), error =>
    error instanceof ProductionIdentityError && error.code === "invalid_identity_token");
});

test("production identity resolver refuses weak secrets before handling requests", () => {
  assert.throws(
    () => createProductionIdentityResolver({ secret: "too-short" }),
    /at least 32 characters/
  );
});
