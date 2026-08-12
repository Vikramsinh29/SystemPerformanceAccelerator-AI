const USABLE_STATES = new Set(["active", "grace"]);

export class LicensingCompatibilityService {
  constructor({ eventStore, deviceStore, idFactory = () => crypto.randomUUID() }) {
    if (!eventStore?.findEntitlement) {
      throw new TypeError("eventStore.findEntitlement is required.");
    }
    if (!deviceStore?.findActiveDevice || !deviceStore?.activateDevice ||
        !deviceStore?.deactivateDevice || !deviceStore?.touchValidation) {
      throw new TypeError("A compatible deviceStore is required.");
    }
    if (typeof idFactory !== "function") {
      throw new TypeError("idFactory must be a function.");
    }
    this.eventStore = eventStore;
    this.deviceStore = deviceStore;
    this.idFactory = idFactory;
  }

  async readAccountLicense({ accountId, productId, nowUtc }) {
    validateIdentity(accountId, productId);
    requireIso(nowUtc, "nowUtc");
    const entitlement = await this.eventStore.findEntitlement(accountId, productId);
    if (!entitlement) return { found: false, license: null };
    return {
      found: true,
      license: toCompatibilityLicense(entitlement, nowUtc)
    };
  }

  async activateDevice({
    accountId, productId, deviceFingerprintHash, deviceLabel = null, nowUtc
  }) {
    validateIdentity(accountId, productId);
    requireHash(deviceFingerprintHash, "deviceFingerprintHash");
    requireIso(nowUtc, "nowUtc");
    if (deviceLabel != null && typeof deviceLabel !== "string") {
      throw new TypeError("deviceLabel must be a string when provided.");
    }

    const entitlement = await this.eventStore.findEntitlement(accountId, productId);
    const eligibility = evaluateEntitlement(entitlement, nowUtc);
    if (!eligibility.usable) return failure(eligibility.code, entitlement, nowUtc);

    const existing = await this.deviceStore.findActiveDevice(
      entitlement.entitlement_id,
      deviceFingerprintHash
    );
    if (existing) {
      return {
        ok: true,
        code: "already_active",
        activation: existing,
        license: toCompatibilityLicense(entitlement, nowUtc)
      };
    }

    const activationId = this.idFactory();
    requireText(activationId, "activationId");
    const result = await this.deviceStore.activateDevice({
      activationId,
      entitlementId: entitlement.entitlement_id,
      accountId,
      productId,
      deviceFingerprintHash,
      deviceLabel
    }, nowUtc);

    if (!result.activated) {
      return failure("seat_limit_or_activation_conflict", entitlement, nowUtc);
    }

    const activation = await this.deviceStore.findActiveDevice(
      entitlement.entitlement_id,
      deviceFingerprintHash
    );
    const refreshedEntitlement = await this.eventStore.findEntitlement(accountId, productId);
    if (!refreshedEntitlement) {
      return failure("license_not_found", null, nowUtc);
    }
    return {
      ok: true,
      code: "activated",
      activation,
      license: toCompatibilityLicense(refreshedEntitlement, nowUtc)
    };
  }

  async deactivateDevice({ accountId, productId, deviceFingerprintHash, nowUtc }) {
    validateIdentity(accountId, productId);
    requireHash(deviceFingerprintHash, "deviceFingerprintHash");
    requireIso(nowUtc, "nowUtc");

    const entitlement = await this.eventStore.findEntitlement(accountId, productId);
    if (!entitlement) return failure("license_not_found", null, nowUtc);

    const activation = await this.deviceStore.findActiveDevice(
      entitlement.entitlement_id,
      deviceFingerprintHash
    );
    if (!activation) {
      return {
        ok: true,
        code: "already_inactive",
        license: toCompatibilityLicense(entitlement, nowUtc)
      };
    }

    const result = await this.deviceStore.deactivateDevice(
      activation.activation_id,
      activation.version,
      nowUtc
    );
    if (!result.closed) {
      return failure("activation_conflict", entitlement, nowUtc);
    }
    const refreshedEntitlement = await this.eventStore.findEntitlement(accountId, productId);
    if (!refreshedEntitlement) {
      return failure("license_not_found", null, nowUtc);
    }
    return {
      ok: true,
      code: "deactivated",
      license: toCompatibilityLicense(refreshedEntitlement, nowUtc)
    };
  }

  async validateDevice({ accountId, productId, deviceFingerprintHash, nowUtc }) {
    validateIdentity(accountId, productId);
    requireHash(deviceFingerprintHash, "deviceFingerprintHash");
    requireIso(nowUtc, "nowUtc");

    const entitlement = await this.eventStore.findEntitlement(accountId, productId);
    const eligibility = evaluateEntitlement(entitlement, nowUtc);
    if (!eligibility.usable) {
      return {
        valid: false,
        code: eligibility.code,
        license: entitlement ? toCompatibilityLicense(entitlement, nowUtc) : null
      };
    }

    const activation = await this.deviceStore.findActiveDevice(
      entitlement.entitlement_id,
      deviceFingerprintHash
    );
    if (!activation) {
      return {
        valid: false,
        code: "device_not_active",
        license: toCompatibilityLicense(entitlement, nowUtc)
      };
    }

    const touched = await this.deviceStore.touchValidation(
      activation.activation_id,
      activation.version,
      nowUtc
    );
    if (changes(touched) !== 1) {
      return {
        valid: false,
        code: "activation_conflict",
        license: toCompatibilityLicense(entitlement, nowUtc)
      };
    }

    return {
      valid: true,
      code: "valid",
      activation,
      license: toCompatibilityLicense(entitlement, nowUtc)
    };
  }
}

function evaluateEntitlement(entitlement, nowUtc) {
  if (!entitlement) return { usable: false, code: "license_not_found" };
  if (!USABLE_STATES.has(entitlement.state)) {
    return { usable: false, code: `license_${entitlement.state}` };
  }
  const now = Date.parse(nowUtc);
  const periodEnd = Date.parse(entitlement.period_ends_utc);
  if (!Number.isNaN(periodEnd) && now > periodEnd && entitlement.state !== "grace") {
    return { usable: false, code: "license_expired" };
  }
  if (entitlement.state === "grace") {
    const graceEnd = Date.parse(entitlement.payment_grace_ends_utc ?? "");
    if (Number.isNaN(graceEnd) || now > graceEnd) {
      return { usable: false, code: "license_expired" };
    }
  }
  return { usable: true, code: "usable" };
}

function toCompatibilityLicense(entitlement, nowUtc) {
  const eligibility = evaluateEntitlement(entitlement, nowUtc);
  return {
    entitlementId: entitlement.entitlement_id,
    accountId: entitlement.account_id,
    productId: entitlement.product_id,
    state: entitlement.state,
    activationLimit: entitlement.seat_limit,
    activeDeviceCount: entitlement.active_device_count,
    periodEndsUtc: entitlement.period_ends_utc,
    paymentGraceEndsUtc: entitlement.payment_grace_ends_utc ?? null,
    offlineValidUntilUtc: entitlement.offline_valid_until_utc ?? null,
    usable: eligibility.usable
  };
}

function failure(code, entitlement, nowUtc) {
  return {
    ok: false,
    code,
    license: entitlement ? toCompatibilityLicense(entitlement, nowUtc) : null
  };
}

function validateIdentity(accountId, productId) {
  requireText(accountId, "accountId");
  requireText(productId, "productId");
}

function requireHash(value, name) {
  requireText(value, name);
  if (!/^[a-f0-9]{64}$/i.test(value)) {
    throw new TypeError(`${name} must be a 64-character SHA-256 hex digest.`);
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
