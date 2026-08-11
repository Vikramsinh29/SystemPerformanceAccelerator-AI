import assert from "node:assert/strict";
import test from "node:test";
import { LicensingCompatibilityService } from "../src/licensing-compatibility-service.js";

const nowUtc = "2026-08-11T18:00:00Z";
const fingerprint = "a".repeat(64);

function entitlement(overrides = {}) {
  return {
    entitlement_id: "ent-1",
    account_id: "acct-1",
    product_id: "pcspa-pro",
    state: "active",
    seat_limit: 2,
    active_device_count: 1,
    period_ends_utc: "2027-08-11T18:00:00Z",
    payment_grace_ends_utc: null,
    offline_valid_until_utc: "2026-09-10T18:00:00Z",
    ...overrides
  };
}

class FakeEventStore {
  constructor(value) { this.value = value; this.calls = []; }
  async findEntitlement(accountId, productId) {
    this.calls.push([accountId, productId]);
    return this.value;
  }
}

class FakeDeviceStore {
  constructor() {
    this.active = null;
    this.activations = [];
    this.deactivations = [];
    this.touches = [];
    this.activateResult = { activated: true, entitlementReconciled: true };
    this.deactivateResult = { closed: true, entitlementReconciled: true };
    this.touchResult = { meta: { changes: 1 } };
  }
  async findActiveDevice(entitlementId, deviceFingerprintHash) {
    this.lastFind = [entitlementId, deviceFingerprintHash];
    return this.active;
  }
  async activateDevice(device, at) {
    this.activations.push([device, at]);
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
  async deactivateDevice(activationId, version, at) {
    this.deactivations.push([activationId, version, at]);
    return this.deactivateResult;
  }
  async touchValidation(activationId, version, at) {
    this.touches.push([activationId, version, at]);
    return this.touchResult;
  }
}

function service(entitlementValue = entitlement(), deviceStore = new FakeDeviceStore()) {
  return {
    deviceStore,
    service: new LicensingCompatibilityService({
      eventStore: new FakeEventStore(entitlementValue),
      deviceStore,
      idFactory: () => "act-new"
    })
  };
}

test("account license read maps durable entitlement to V1-compatible fields", async () => {
  const { service: sut } = service();
  const result = await sut.readAccountLicense({
    accountId: "acct-1", productId: "pcspa-pro", nowUtc
  });
  assert.equal(result.found, true);
  assert.deepEqual(result.license, {
    entitlementId: "ent-1",
    accountId: "acct-1",
    productId: "pcspa-pro",
    state: "active",
    activationLimit: 2,
    activeDeviceCount: 1,
    periodEndsUtc: "2027-08-11T18:00:00Z",
    paymentGraceEndsUtc: null,
    offlineValidUntilUtc: "2026-09-10T18:00:00Z",
    usable: true
  });
});

test("activation is idempotent when the fingerprint is already active", async () => {
  const devices = new FakeDeviceStore();
  devices.active = { activation_id: "act-existing", version: 4, status: "active" };
  const { service: sut } = service(entitlement(), devices);
  const result = await sut.activateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.equal(result.ok, true);
  assert.equal(result.code, "already_active");
  assert.equal(devices.activations.length, 0);
});

test("activation delegates durable creation and returns the created activation", async () => {
  const devices = new FakeDeviceStore();
  const { service: sut } = service(entitlement(), devices);
  const result = await sut.activateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, deviceLabel: "Primary PC", nowUtc
  });
  assert.equal(result.ok, true);
  assert.equal(result.code, "activated");
  assert.equal(devices.activations.length, 1);
  assert.equal(devices.activations[0][0].activationId, "act-new");
  assert.equal(result.activation.activation_id, "act-new");
});

test("non-usable entitlement blocks activation before device writes", async () => {
  const devices = new FakeDeviceStore();
  const { service: sut } = service(entitlement({ state: "revoked" }), devices);
  const result = await sut.activateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.deepEqual(result.ok, false);
  assert.equal(result.code, "license_revoked");
  assert.equal(devices.activations.length, 0);
});

test("grace entitlement expires at its explicit payment grace deadline", async () => {
  const devices = new FakeDeviceStore();
  const { service: sut } = service(entitlement({
    state: "grace",
    period_ends_utc: "2026-08-01T00:00:00Z",
    payment_grace_ends_utc: "2026-08-10T00:00:00Z"
  }), devices);
  const result = await sut.validateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.equal(result.valid, false);
  assert.equal(result.code, "license_expired");
  assert.equal(devices.touches.length, 0);
});

test("deactivation is idempotent when the device is already inactive", async () => {
  const { service: sut, deviceStore } = service();
  const result = await sut.deactivateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.equal(result.ok, true);
  assert.equal(result.code, "already_inactive");
  assert.equal(deviceStore.deactivations.length, 0);
});

test("deactivation uses the durable activation version guard", async () => {
  const devices = new FakeDeviceStore();
  devices.active = { activation_id: "act-1", version: 6, status: "active" };
  const { service: sut } = service(entitlement(), devices);
  const result = await sut.deactivateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.equal(result.ok, true);
  assert.equal(result.code, "deactivated");
  assert.deepEqual(devices.deactivations[0], ["act-1", 6, nowUtc]);
});

test("validation requires an active device and touches its validation timestamp", async () => {
  const devices = new FakeDeviceStore();
  devices.active = { activation_id: "act-1", version: 2, status: "active" };
  const { service: sut } = service(entitlement(), devices);
  const result = await sut.validateDevice({
    accountId: "acct-1", productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint, nowUtc
  });
  assert.equal(result.valid, true);
  assert.equal(result.code, "valid");
  assert.deepEqual(devices.touches[0], ["act-1", 2, nowUtc]);
});
