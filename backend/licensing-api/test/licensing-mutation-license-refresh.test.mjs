import assert from "node:assert/strict";
import test from "node:test";

import { LicensingCompatibilityService } from "../src/licensing-compatibility-service.js";

const nowUtc = "2026-08-12T06:00:00Z";
const fingerprint = "a".repeat(64);

function entitlement(activeDeviceCount) {
  return {
    entitlement_id: "ent-1",
    account_id: "acct-1",
    product_id: "pcspa-pro",
    state: "active",
    seat_limit: 1,
    active_device_count: activeDeviceCount,
    period_ends_utc: "2027-08-12T06:00:00Z",
    payment_grace_ends_utc: null,
    offline_valid_until_utc: "2026-09-12T06:00:00Z"
  };
}

class SequencedEventStore {
  constructor(values) {
    this.values = values;
    this.calls = 0;
  }

  async findEntitlement() {
    const index = Math.min(this.calls, this.values.length - 1);
    this.calls += 1;
    return this.values[index];
  }
}

class MutationDeviceStore {
  constructor({ active = null } = {}) {
    this.active = active;
  }

  async findActiveDevice() {
    return this.active;
  }

  async activateDevice(device) {
    this.active = {
      activation_id: device.activationId,
      entitlement_id: device.entitlementId,
      account_id: device.accountId,
      product_id: device.productId,
      device_fingerprint_hash: device.deviceFingerprintHash,
      status: "active",
      version: 0
    };
    return { activated: true, entitlementReconciled: true };
  }

  async deactivateDevice() {
    return { closed: true, entitlementReconciled: true };
  }

  async touchValidation() {
    return { meta: { changes: 1 } };
  }
}

test("activation response uses refreshed entitlement after durable reconciliation", async () => {
  const eventStore = new SequencedEventStore([
    entitlement(0),
    entitlement(1)
  ]);
  const deviceStore = new MutationDeviceStore();
  const service = new LicensingCompatibilityService({
    eventStore,
    deviceStore,
    idFactory: () => "act-1"
  });

  const result = await service.activateDevice({
    accountId: "acct-1",
    productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint,
    nowUtc
  });

  assert.equal(result.ok, true);
  assert.equal(result.code, "activated");
  assert.equal(result.license.state, "active");
  assert.equal(result.license.activeDeviceCount, 1);
  assert.equal(eventStore.calls, 2);
});

test("deactivation response uses refreshed entitlement after durable reconciliation", async () => {
  const eventStore = new SequencedEventStore([
    entitlement(1),
    entitlement(0)
  ]);
  const deviceStore = new MutationDeviceStore({
    active: {
      activation_id: "act-1",
      entitlement_id: "ent-1",
      device_fingerprint_hash: fingerprint,
      status: "active",
      version: 4
    }
  });
  const service = new LicensingCompatibilityService({
    eventStore,
    deviceStore,
    idFactory: () => "unused"
  });

  const result = await service.deactivateDevice({
    accountId: "acct-1",
    productId: "pcspa-pro",
    deviceFingerprintHash: fingerprint,
    nowUtc
  });

  assert.equal(result.ok, true);
  assert.equal(result.code, "deactivated");
  assert.equal(result.license.state, "active");
  assert.equal(result.license.activeDeviceCount, 0);
  assert.equal(eventStore.calls, 2);
});
