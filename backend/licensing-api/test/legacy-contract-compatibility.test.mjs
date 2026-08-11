import assert from "node:assert/strict";
import test from "node:test";
import { LicensingCompatibilityService } from "../src/licensing-compatibility-service.js";
import { createLicensingHttpAdapter } from "../src/licensing-http-adapter.js";

const nowUtc = "2026-08-11T18:00:00Z";
const fingerprint = "b".repeat(64);

function entitlement(overrides = {}) {
  return {
    entitlement_id: "ent-legacy-1",
    account_id: "acct-legacy-1",
    product_id: "pcspa-pro",
    state: "active",
    seat_limit: 1,
    active_device_count: 0,
    period_ends_utc: "2027-08-11T18:00:00Z",
    payment_grace_ends_utc: null,
    offline_valid_until_utc: "2026-09-10T18:00:00Z",
    ...overrides
  };
}

class EventStore {
  constructor(value) { this.value = value; }
  async findEntitlement() { return this.value; }
}

class DeviceStore {
  constructor() {
    this.active = null;
    this.activateResult = { activated: true, entitlementReconciled: true };
    this.deactivateResult = { closed: true, entitlementReconciled: true };
    this.touchResult = { meta: { changes: 1 } };
  }
  async findActiveDevice() { return this.active; }
  async activateDevice(device) {
    if (this.activateResult.activated) {
      this.active = {
        activation_id: device.activationId,
        entitlement_id: device.entitlementId,
        device_fingerprint_hash: device.deviceFingerprintHash,
        status: "active",
        version: 0
      };
    }
    return this.activateResult;
  }
  async deactivateDevice() {
    if (this.deactivateResult.closed) this.active = null;
    return this.deactivateResult;
  }
  async touchValidation() { return this.touchResult; }
}

function stack(entitlementValue = entitlement(), devices = new DeviceStore()) {
  const service = new LicensingCompatibilityService({
    eventStore: new EventStore(entitlementValue),
    deviceStore: devices,
    idFactory: () => "act-legacy-1"
  });
  const adapter = createLicensingHttpAdapter({ service, clock: () => nowUtc });
  const identity = { accountId: "acct-legacy-1", productId: "pcspa-pro" };
  return { service, adapter, devices, identity };
}

function request(method, body) {
  return new Request("https://internal.invalid/licensing", {
    method,
    headers: body === undefined ? undefined : { "content-type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
}

test("legacy contract: account license read exposes activation limit and active device count", async () => {
  const { adapter, identity } = stack(entitlement({ seat_limit: 3, active_device_count: 2 }));
  const response = await adapter.readAccountLicense(request("GET"), identity);
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.license.activationLimit, 3);
  assert.equal(body.license.activeDeviceCount, 2);
  assert.equal(body.license.state, "active");
});

test("legacy contract: activation is idempotent for an already-active fingerprint", async () => {
  const devices = new DeviceStore();
  devices.active = { activation_id: "act-existing", status: "active", version: 2 };
  const { adapter, identity } = stack(entitlement({ active_device_count: 1 }), devices);
  const response = await adapter.activateDevice(
    request("POST", { deviceFingerprintHash: fingerprint, deviceLabel: "Primary PC" }),
    identity
  );
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.code, "already_active");
});

test("legacy contract: seat-capacity conflict maps to HTTP 409", async () => {
  const devices = new DeviceStore();
  devices.activateResult = { activated: false, entitlementReconciled: true };
  const { adapter, identity } = stack(entitlement({ seat_limit: 1, active_device_count: 1 }), devices);
  const response = await adapter.activateDevice(
    request("POST", { deviceFingerprintHash: fingerprint }),
    identity
  );
  assert.equal(response.status, 409);
  assert.equal((await response.json()).error, "seat_limit_or_activation_conflict");
});

test("legacy contract: revoked and expired licenses deny activation with HTTP 403", async () => {
  for (const value of [
    entitlement({ state: "revoked" }),
    entitlement({ state: "active", period_ends_utc: "2026-08-10T00:00:00Z" })
  ]) {
    const { adapter, identity } = stack(value);
    const response = await adapter.activateDevice(
      request("POST", { deviceFingerprintHash: fingerprint }),
      identity
    );
    assert.equal(response.status, 403);
  }
});

test("legacy contract: deactivation is idempotent when no active device exists", async () => {
  const { adapter, identity } = stack();
  const response = await adapter.deactivateDevice(
    request("POST", { deviceFingerprintHash: fingerprint }),
    identity
  );
  assert.equal(response.status, 200);
  assert.equal((await response.json()).code, "already_inactive");
});

test("legacy contract: validation requires the device fingerprint to be active", async () => {
  const { adapter, identity } = stack();
  const response = await adapter.validateDevice(
    request("POST", { deviceFingerprintHash: fingerprint }),
    identity
  );
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.valid, false);
  assert.equal(body.code, "device_not_active");
});

test("legacy contract: malformed device fingerprints never reach durable activation logic", async () => {
  const { adapter, identity } = stack();
  const response = await adapter.activateDevice(
    request("POST", { deviceFingerprintHash: "raw-device-id" }),
    identity
  );
  assert.equal(response.status, 400);
  assert.equal((await response.json()).error, "invalid_request");
});
