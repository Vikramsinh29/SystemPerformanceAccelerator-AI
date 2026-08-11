const STATES = new Set([
  "pending", "active", "grace", "expired", "suspended", "revoked", "refunded"
]);
const OUTCOMES = new Set(["applied", "ignored_out_of_order", "rejected"]);

export class D1LicensingEventStore {
  constructor(database) {
    if (!database?.prepare || !database?.batch) {
      throw new TypeError("A D1-compatible database binding is required.");
    }
    this.database = database;
  }

  async findReceipt(provider, providerEventId) {
    return this.database.prepare(`
      SELECT provider, provider_event_id, processing_status, attempt_count,
             last_error_code, processed_utc
        FROM licensing_payment_event_receipts
       WHERE provider = ? AND provider_event_id = ?
    `).bind(provider, providerEventId).first();
  }

  async findEntitlement(accountId, productId) {
    return this.database.prepare(`
      SELECT * FROM licensing_entitlements
       WHERE account_id = ? AND product_id = ?
    `).bind(accountId, productId).first();
  }

  async recordFailure(event, errorCode, nowUtc) {
    validateEventIdentity(event);
    requireText(errorCode, "errorCode");
    requireIso(nowUtc, "nowUtc");
    return this.database.prepare(`
      INSERT INTO licensing_payment_event_receipts (
        provider, provider_event_id, account_id, product_id,
        provider_subscription_id, event_kind, occurred_utc,
        processing_status, attempt_count, last_error_code,
        first_received_utc, last_attempt_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, 'retryable_failure', 1, ?, ?, ?)
      ON CONFLICT(provider, provider_event_id) DO UPDATE SET
        attempt_count = attempt_count + 1,
        processing_status = 'retryable_failure',
        last_error_code = excluded.last_error_code,
        last_attempt_utc = excluded.last_attempt_utc
      WHERE processing_status <> 'processed'
    `).bind(
      event.provider, event.providerEventId, event.accountId, event.productId,
      event.providerSubscriptionId ?? null, event.kind, event.occurredUtc,
      errorCode, nowUtc, nowUtc
    ).run();
  }

  async commitTransition({ event, previousVersion, entitlement, audit, outcome, nowUtc }) {
    validateEvent(event);
    validateEntitlement(entitlement);
    validateAudit(audit);
    requireIso(nowUtc, "nowUtc");
    if (!OUTCOMES.has(outcome)) throw new TypeError("outcome is invalid.");
    if (!Number.isInteger(previousVersion) || previousVersion < -1) {
      throw new TypeError("previousVersion must be -1 or a non-negative integer.");
    }

    const receipt = this.database.prepare(`
      INSERT INTO licensing_payment_event_receipts (
        provider, provider_event_id, account_id, product_id,
        provider_subscription_id, event_kind, occurred_utc,
        processing_status, attempt_count, first_received_utc,
        last_attempt_utc, processed_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, 'processed', 1, ?, ?, ?)
      ON CONFLICT(provider, provider_event_id) DO NOTHING
    `).bind(
      event.provider, event.providerEventId, event.accountId, event.productId,
      event.providerSubscriptionId ?? null, event.kind, event.occurredUtc,
      nowUtc, nowUtc, nowUtc
    );

    const entitlementStatement = previousVersion === -1
      ? this.database.prepare(`
          INSERT INTO licensing_entitlements (
            entitlement_id, account_id, product_id, state, seat_limit,
            active_device_count, period_ends_utc, payment_grace_ends_utc,
            offline_valid_until_utc, transfers_used, transfer_window_started_utc,
            last_transfer_utc, last_commercial_event_utc, version, updated_utc
          )
          SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?
           WHERE EXISTS (
             SELECT 1 FROM licensing_payment_event_receipts
              WHERE provider = ? AND provider_event_id = ?
                AND processing_status = 'processed'
           )
          ON CONFLICT(account_id, product_id) DO NOTHING
        `).bind(...entitlementValues(entitlement), nowUtc,
          event.provider, event.providerEventId)
      : this.database.prepare(`
          UPDATE licensing_entitlements SET
            entitlement_id = ?, state = ?, seat_limit = ?, active_device_count = ?,
            period_ends_utc = ?, payment_grace_ends_utc = ?,
            offline_valid_until_utc = ?, transfers_used = ?,
            transfer_window_started_utc = ?, last_transfer_utc = ?,
            last_commercial_event_utc = ?, version = version + 1, updated_utc = ?
          WHERE account_id = ? AND product_id = ? AND version = ?
            AND EXISTS (
              SELECT 1 FROM licensing_payment_event_receipts
               WHERE provider = ? AND provider_event_id = ?
                 AND processing_status = 'processed'
            )
        `).bind(
          entitlement.entitlementId, entitlement.state, entitlement.seatLimit,
          entitlement.activeDeviceCount, entitlement.periodEndsUtc,
          entitlement.paymentGraceEndsUtc ?? null,
          entitlement.offlineValidUntilUtc ?? null, entitlement.transfersUsed,
          entitlement.transferWindowStartedUtc, entitlement.lastTransferUtc ?? null,
          entitlement.lastCommercialEventUtc ?? null, nowUtc,
          entitlement.accountId, entitlement.productId, previousVersion,
          event.provider, event.providerEventId
        );

    const auditStatement = this.database.prepare(`
      INSERT INTO licensing_audit_events (
        audit_id, occurred_utc, provider, provider_event_id, account_id,
        product_id, event_kind, processing_outcome, previous_state,
        current_state, message
      )
      SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
       WHERE EXISTS (
         SELECT 1 FROM licensing_payment_event_receipts
          WHERE provider = ? AND provider_event_id = ?
            AND processing_status = 'processed'
       )
    `).bind(
      audit.auditId, audit.occurredUtc, event.provider, event.providerEventId,
      event.accountId, event.productId, event.kind, outcome,
      audit.previousState ?? null, audit.currentState ?? null, audit.message,
      event.provider, event.providerEventId
    );

    const results = await this.database.batch([
      receipt, entitlementStatement, auditStatement
    ]);
    return {
      duplicate: changes(results[0]) === 0,
      entitlementChanged: changes(results[1]) === 1,
      auditWritten: changes(results[2]) === 1
    };
  }
}

function entitlementValues(value) {
  return [
    value.entitlementId, value.accountId, value.productId, value.state,
    value.seatLimit, value.activeDeviceCount, value.periodEndsUtc,
    value.paymentGraceEndsUtc ?? null, value.offlineValidUntilUtc ?? null,
    value.transfersUsed, value.transferWindowStartedUtc,
    value.lastTransferUtc ?? null, value.lastCommercialEventUtc ?? null
  ];
}

function changes(result) {
  return Number(result?.meta?.changes ?? 0);
}

function validateEvent(value) {
  validateEventIdentity(value);
  requireText(value.kind, "event.kind");
  requireIso(value.occurredUtc, "event.occurredUtc");
}

function validateEventIdentity(value) {
  if (!value || typeof value !== "object") throw new TypeError("event is required.");
  for (const key of ["provider", "providerEventId", "accountId", "productId"]) {
    requireText(value[key], `event.${key}`);
  }
}

function validateEntitlement(value) {
  if (!value || typeof value !== "object") throw new TypeError("entitlement is required.");
  for (const key of ["entitlementId", "accountId", "productId"]) {
    requireText(value[key], `entitlement.${key}`);
  }
  if (!STATES.has(value.state)) throw new TypeError("entitlement.state is invalid.");
  for (const key of ["seatLimit", "activeDeviceCount", "transfersUsed"]) {
    if (!Number.isInteger(value[key]) || value[key] < 0) {
      throw new TypeError(`entitlement.${key} is invalid.`);
    }
  }
  if (value.seatLimit < 1 || value.activeDeviceCount > value.seatLimit) {
    throw new TypeError("entitlement seat counts are invalid.");
  }
  requireIso(value.periodEndsUtc, "entitlement.periodEndsUtc");
  requireIso(value.transferWindowStartedUtc, "entitlement.transferWindowStartedUtc");
}

function validateAudit(value) {
  if (!value || typeof value !== "object") throw new TypeError("audit is required.");
  requireText(value.auditId, "audit.auditId");
  requireText(value.message, "audit.message");
  requireIso(value.occurredUtc, "audit.occurredUtc");
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
