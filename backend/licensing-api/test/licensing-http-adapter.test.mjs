import assert from "node:assert/strict";
import test from "node:test";
import { createLicensingHttpAdapter } from "../src/licensing-http-adapter.js";

const nowUtc = "2026-08-11T18:00:00Z";
const identity = { accountId: "acct-1", productId: "pcspa-pro" };
const fingerprint = "a".repeat(64);

function request(method, body) {
  return new Request("https://internal.invalid/licensing", {
    method,
    headers: body === undefined ? undefined : { "content-type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

function service(overrides = {}) {
  return {
    async readAccountLicense(args) {
      return { found: true, license: { accountId: args.accountId, productId: args.productId } };
    },
    async activateDevice() {
      return { ok: true, code: "activated", activation: { activation_id: "act-1" }, license: { usable: true } };
    },
    async deactivateDevice() {
      return { ok: true, code: "deactivated", license: { usable: true } };
    },
    async validateDevice() {
      return { valid: true, code: "valid", license: { usable: true } };
    },
    ...overrides
  };
}

async function body(response) {
  return response.json();
}

test("account license read maps found license to 200", async () => {
  let captured;
  const adapter = createLicensingHttpAdapter({
    service: service({
      async readAccountLicense(args) {
        captured = args;
        return { found: true, license: { state: "active" } };
      }
    }),
    clock: () => nowUtc
  });

  const response = await adapter.readAccountLicense(request("GET"), identity);
  assert.equal(response.status, 200);
  assert.deepEqual(await body(response), { license: { state: "active" } });
  assert.deepEqual(captured, { ...identity, nowUtc });
  assert.equal(response.headers.get("cache-control"), "no-store");
});

test("missing account license maps to 404", async () => {
  const adapter = createLicensingHttpAdapter({
    service: service({ async readAccountLicense() { return { found: false, license: null }; } }),
    clock: () => nowUtc
  });
  const response = await adapter.readAccountLicense(request("GET"), identity);
  assert.equal(response.status, 404);
  assert.deepEqual(await body(response), { error: "license_not_found" });
});

test("activate forwards normalized identity, fingerprint, label, and time", async () => {
  let captured;
  const adapter = createLicensingHttpAdapter({
    service: service({
      async activateDevice(args) {
        captured = args;
        return { ok: true, code: "activated", activation: { activation_id: "act-1" }, license: null };
      }
    }),
    clock: () => nowUtc
  });

  const response = await adapter.activateDevice(
    request("POST", { deviceFingerprintHash: fingerprint, deviceLabel: "Primary PC" }),
    identity
  );
  assert.equal(response.status, 200);
  assert.equal((await body(response)).code, "activated");
  assert.deepEqual(captured, {
    ...identity,
    deviceFingerprintHash: fingerprint,
    deviceLabel: "Primary PC",
    nowUtc
  });
});

test("seat conflict maps to 409 without leaking implementation details", async () => {
  const adapter = createLicensingHttpAdapter({
    service: service({
      async activateDevice() {
        return { ok: false, code: "seat_limit_or_activation_conflict", license: { usable: true } };
      }
    }),
    clock: () => nowUtc
  });
  const response = await adapter.activateDevice(request("POST", { deviceFingerprintHash: fingerprint }), identity);
  assert.equal(response.status, 409);
  assert.deepEqual(await body(response), {
    error: "seat_limit_or_activation_conflict",
    license: { usable: true }
  });
});

test("license state failures map to 403", async () => {
  const adapter = createLicensingHttpAdapter({
    service: service({ async activateDevice() { return { ok: false, code: "license_revoked", license: null }; } }),
    clock: () => nowUtc
  });
  const response = await adapter.activateDevice(request("POST", { deviceFingerprintHash: fingerprint }), identity);
  assert.equal(response.status, 403);
  assert.equal((await body(response)).error, "license_revoked");
});

test("deactivate preserves idempotent success", async () => {
  const adapter = createLicensingHttpAdapter({
    service: service({ async deactivateDevice() { return { ok: true, code: "already_inactive", license: null }; } }),
    clock: () => nowUtc
  });
  const response = await adapter.deactivateDevice(request("POST", { deviceFingerprintHash: fingerprint }), identity);
  assert.equal(response.status, 200);
  assert.equal((await body(response)).code, "already_inactive");
});

test("validate returns domain result as 200", async () => {
  let captured;
  const adapter = createLicensingHttpAdapter({
    service: service({
      async validateDevice(args) {
        captured = args;
        return { valid: false, code: "device_not_active", license: { usable: true } };
      }
    }),
    clock: () => nowUtc
  });
  const response = await adapter.validateDevice(request("POST", { deviceFingerprintHash: fingerprint }), identity);
  assert.equal(response.status, 200);
  assert.deepEqual(await body(response), { valid: false, code: "device_not_active", license: { usable: true } });
  assert.deepEqual(captured, { ...identity, deviceFingerprintHash: fingerprint, nowUtc });
});

test("wrong method returns 405 with Allow", async () => {
  const adapter = createLicensingHttpAdapter({ service: service(), clock: () => nowUtc });
  const response = await adapter.activateDevice(request("GET"), identity);
  assert.equal(response.status, 405);
  assert.equal(response.headers.get("allow"), "POST");
});

test("malformed JSON returns sanitized 400", async () => {
  const adapter = createLicensingHttpAdapter({ service: service(), clock: () => nowUtc });
  const badRequest = new Request("https://internal.invalid/licensing", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: "{not-json"
  });
  const response = await adapter.activateDevice(badRequest, identity);
  assert.equal(response.status, 400);
  assert.deepEqual(await body(response), { error: "invalid_request" });
});

test("unexpected service error returns sanitized 500", async () => {
  const adapter = createLicensingHttpAdapter({
    service: service({ async validateDevice() { throw new Error("database internals"); } }),
    clock: () => nowUtc
  });
  const response = await adapter.validateDevice(request("POST", { deviceFingerprintHash: fingerprint }), identity);
  assert.equal(response.status, 500);
  assert.deepEqual(await body(response), { error: "internal_error" });
});
