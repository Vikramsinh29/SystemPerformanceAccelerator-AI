const ACTIVATABLE_STATES = new Set(["active", "grace"]);
const DEVICE_STATUSES = new Set(["active", "deactivated", "revoked"]);

export class D1DeviceActivationStore {
  constructor(database) {
    if (!database?.prepare || !database?.batch) {
      throw new TypeError("A D1-compatible database binding is required.");
    }
    this.database = database;
  }

  async findActiveDevice(entitlementId, deviceFingerprintHash) {
    requireText(entitlementId, "entitlementId");
    requireHash(deviceFingerprintHash, "deviceFingerprintHash");
    return this.database.prepare(`
      SELECT activation_id, entitlement_id, account_id, product_id,
             device_fingerprint_hash, device_label, status,
             activated_utc, last_validated_utc, version
        FROM licensing_device_activations
       WHERE entitlement_id = ? AND device_fingerprint_hash = ?
         AND status = 'active'
    `).bind(entitlementId, deviceFingerprintHash).first();
  }

  async listActiveDevices(entitlementId) {
    requireText(entitlementId, "entitlementId");
    return this.database.prepare(`
      SELECT activation_id, entitlement_id, account_id, product_id,
             device_fingerprint_hash, device_label, status,
             activated_utc, last_validated_utc, version
        FROM licensing_device_activations
       WHERE entitlement_id = ? AND status = 'active'
       ORDER BY activated_utc, activation_id
    `).bind(entitlementId).all();
  }

  async activateDevice(device, nowUtc) {
    validateDevice(device);
    requireIso(nowUtc, "nowUtc");

    const insert = this.database.prepare(`
      INSERT INTO licensing_device_activations (
        activation_id, entitlement_id, account_id, product_id,
        device_fingerprint_hash, device_label, status,
        activated_utc, last_validated_utc
      )
      SELECT ?, ?, ?, ?, ?, ?, 'active', ?, ?
       WHERE EXISTS (
         SELECT 1 FROM licensing_entitlements
          WHERE entitlement_id = ? AND account_id = ? AND product_id = ?
            AND state IN ('active', 'grace')
            AND active_device_count < seat_limit
       )
         AND NOT EXISTS (
           SELECT 1 FROM licensing_device_activations
            WHERE entitlement_id = ? AND device_fingerprint_hash = ?
              AND status = 'active'
         )
    `).bind(
      device.activationId, device.entitlementId, device.accountId, device.productId,
      device.deviceFingerprintHash, device.deviceLabel ?? null, nowUtc, nowUtc,
      device.entitlementId, device.accountId, device.productId,
      device.entitlementId, device.deviceFingerprintHash
    );

    const reconcile = this.database.prepare(`
      UPDATE licensing_entitlements
         SET active_device_count = (
               SELECT COUNT(*) FROM licensing_device_activations
                WHERE entitlement_id = ? AND status = 'active'
             ),
             version = version + 1,
             updated_utc = ?
       WHERE entitlement_id = ?
         AND active_device_count <> (
               SELECT COUNT(*) FROM licensing_device_activations
                WHERE entitlement_id = ? AND status = 'active'
             )
    `).bind(device.entitlementId, nowUtc, device.entitlementId, device.entitlementId);

    const results = await this.database.batch([insert, reconcile]);
    return {
      activated: changes(results[0]) === 1,
      entitlementReconciled: changes(results[1]) === 1
    };
  }

  async touchValidation(activationId, expectedVersion, nowUtc) {
    requireText(activationId, "activationId");
    requireVersion(expectedVersion);
    requireIso(nowUtc, "nowUtc");
    return this.database.prepare(`
      UPDATE licensing_device_activations
         SET last_validated_utc = ?, version = version + 1
       WHERE activation_id = ? AND status = 'active' AND version = ?
    `).bind(nowUtc, activationId, expectedVersion).run();
  }

  async deactivateDevice(activationId, expectedVersion, nowUtc) {
    return this.#closeDevice("deactivated", activationId, expectedVersion, nowUtc);
  }

  async revokeDevice(activationId, expectedVersion, nowUtc) {
    return this.#closeDevice("revoked", activationId, expectedVersion, nowUtc);
  }

  async #closeDevice(status, activationId, expectedVersion, nowUtc) {
    if (!DEVICE_STATUSES.has(status) || status === "active") {
      throw new TypeError("status is invalid.");
    }
    requireText(activationId, "activationId");
    requireVersion(expectedVersion);
    requireIso(nowUtc, "nowUtc");

    const timestampColumn = status === "revoked" ? "revoked_utc" : "deactivated_utc";
    const close = this.database.prepare(`
      UPDATE licensing_device_activations
         SET status = ?, ${timestampColumn} = ?, version = version + 1
       WHERE activation_id = ? AND status = 'active' AND version = ?
    `).bind(status, nowUtc, activationId, expectedVersion);

    const reconcile = this.database.prepare(`
      UPDATE licensing_entitlements
         SET active_device_count = (
               SELECT COUNT(*) FROM licensing_device_activations
                WHERE entitlement_id = licensing_entitlements.entitlement_id
                  AND status = 'active'
             ),
             version = version + 1,
             updated_utc = ?
       WHERE entitlement_id = (
               SELECT entitlement_id FROM licensing_device_activations
                WHERE activation_id = ?
             )
         AND active_device_count <> (
               SELECT COUNT(*) FROM licensing_device_activations
                WHERE entitlement_id = licensing_entitlements.entitlement_id
                  AND status = 'active'
             )
    `).bind(nowUtc, activationId);

    const results = await this.database.batch([close, reconcile]);
    return {
      closed: changes(results[0]) === 1,
      entitlementReconciled: changes(results[1]) === 1
    };
  }
}

function validateDevice(value) {
  if (!value || typeof value !== "object") throw new TypeError("device is required.");
  for (const key of ["activationId", "entitlementId", "accountId", "productId"]) {
    requireText(value[key], `device.${key}`);
  }
  requireHash(value.deviceFingerprintHash, "device.deviceFingerprintHash");
  if (value.deviceLabel != null && typeof value.deviceLabel !== "string") {
    throw new TypeError("device.deviceLabel must be a string when provided.");
  }
}

function requireHash(value, name) {
  requireText(value, name);
  if (!/^[a-f0-9]{64}$/i.test(value)) {
    throw new TypeError(`${name} must be a 64-character SHA-256 hex digest.`);
  }
}

function requireVersion(value) {
  if (!Number.isInteger(value) || value < 0) {
    throw new TypeError("expectedVersion must be a non-negative integer.");
  }
}

function requireText(value, name) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${name} is required.`);
  }
}

function requireIso(value, name) {
  requireText(value, name);
  if (Number.isNaN(Date.parse(value))) throw new TypeError(`${name} must be ISO-8601.`);
}

function changes(result) {
  return Number(result?.meta?.changes ?? 0);
}
