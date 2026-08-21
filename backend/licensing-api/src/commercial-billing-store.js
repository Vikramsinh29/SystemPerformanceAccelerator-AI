export class CommercialBillingStore {
  constructor(db) {
    if (!db || typeof db.prepare !== "function" || typeof db.batch !== "function") {
      throw new TypeError("A D1-compatible database binding is required.");
    }

    this.db = db;
  }

  async findCustomerByAccountId(accountId) {
    requireText(accountId, "accountId");

    return this.db.prepare(`
      SELECT *
      FROM billing_customers
      WHERE account_id = ?
    `).bind(accountId).first();
  }

  async findSubscription(subscriptionId) {
    requireText(subscriptionId, "subscriptionId");

    return this.db.prepare(`
      SELECT *
      FROM billing_subscriptions
      WHERE subscription_id = ?
    `).bind(subscriptionId).first();
  }

  async findProviderMapping(provider, entityType, internalEntityId) {
    requireText(provider, "provider");
    requireText(entityType, "entityType");
    requireText(internalEntityId, "internalEntityId");

    return this.db.prepare(`
      SELECT *
      FROM billing_provider_mappings
      WHERE provider = ?
        AND entity_type = ?
        AND internal_entity_id = ?
    `).bind(
      provider.toLowerCase(),
      entityType,
      internalEntityId
    ).first();
  }

  async recordProviderEvent(event, nowUtc) {
    validateProviderEvent(event);
    requireIso(nowUtc, "nowUtc");

    const insert = this.db.prepare(`
      INSERT OR IGNORE INTO billing_provider_events (
        provider,
        provider_event_id,
        event_type,
        occurred_utc,
        received_utc,
        payload_sha256,
        processing_status,
        attempt_count
      )
      VALUES (?, ?, ?, ?, ?, ?, 'received', 1)
    `).bind(
      event.provider.toLowerCase(),
      event.providerEventId,
      event.eventType,
      event.occurredUtc,
      nowUtc,
      event.payloadSha256.toLowerCase()
    );

    const result = await insert.run();

    return {
      duplicate: changes(result) === 0
    };
  }

  async markProviderEventProcessed({
    provider,
    providerEventId,
    processedUtc
  }) {
    requireText(provider, "provider");
    requireText(providerEventId, "providerEventId");
    requireIso(processedUtc, "processedUtc");

    const result = await this.db.prepare(`
      UPDATE billing_provider_events
      SET processing_status = 'processed',
          processed_utc = ?,
          last_error_code = NULL
      WHERE provider = ?
        AND provider_event_id = ?
        AND processing_status <> 'processed'
    `).bind(
      processedUtc,
      provider.toLowerCase(),
      providerEventId
    ).run();

    return {
      changed: changes(result) === 1
    };
  }

  async markProviderEventRetryableFailure({
    provider,
    providerEventId,
    errorCode,
    attemptedUtc
  }) {
    requireText(provider, "provider");
    requireText(providerEventId, "providerEventId");
    requireText(errorCode, "errorCode");
    requireIso(attemptedUtc, "attemptedUtc");

    const result = await this.db.prepare(`
      UPDATE billing_provider_events
      SET processing_status = 'retryable_failure',
          attempt_count = attempt_count + 1,
          last_error_code = ?,
          processed_utc = NULL
      WHERE provider = ?
        AND provider_event_id = ?
        AND processing_status <> 'processed'
    `).bind(
      errorCode,
      provider.toLowerCase(),
      providerEventId
    ).run();

    return {
      changed: changes(result) === 1
    };
  }

  async findProviderEvent(provider, providerEventId) {
    requireText(provider, "provider");
    requireText(providerEventId, "providerEventId");

    return this.db.prepare(`
      SELECT *
      FROM billing_provider_events
      WHERE provider = ?
        AND provider_event_id = ?
    `).bind(
      provider.toLowerCase(),
      providerEventId
    ).first();
  }

  async commitProviderSubscriptionTransition({
    provider,
    providerEventId,
    previousVersion,
    subscription,
    processedUtc
  }) {
    requireText(provider, "provider");
    requireText(providerEventId, "providerEventId");
    validateSubscription(subscription);
    requireIso(processedUtc, "processedUtc");

    if (!Number.isSafeInteger(previousVersion) || previousVersion < 0) {
      throw new TypeError("previousVersion must be non-negative.");
    }

    const subscriptionStatement = this.db.prepare(`
      UPDATE billing_subscriptions
      SET state = ?,
          period_starts_utc = ?,
          period_ends_utc = ?,
          payment_grace_ends_utc = ?,
          last_provider_event_utc = ?,
          cancel_at_period_end = ?,
          canceled_utc = ?,
          version = version + 1,
          updated_utc = ?
      WHERE subscription_id = ?
        AND version = ?
    `).bind(
      subscription.state,
      subscription.periodStartsUtc ?? null,
      subscription.periodEndsUtc ?? null,
      subscription.paymentGraceEndsUtc ?? null,
      subscription.lastProviderEventUtc,
      subscription.cancelAtPeriodEnd ? 1 : 0,
      subscription.canceledUtc ?? null,
      processedUtc,
      subscription.subscriptionId,
      previousVersion
    );

    const eventStatement = this.db.prepare(`
      UPDATE billing_provider_events
      SET processing_status = 'processed',
          processed_utc = ?,
          last_error_code = NULL
      WHERE provider = ?
        AND provider_event_id = ?
        AND processing_status <> 'processed'
    `).bind(
      processedUtc,
      provider.toLowerCase(),
      providerEventId
    );

    const results = await this.db.batch([
      subscriptionStatement,
      eventStatement
    ]);

    return {
      subscriptionChanged: changes(results[0]) === 1,
      eventProcessed: changes(results[1]) === 1
    };
  }

  async markProviderEventIgnored({
    provider,
    providerEventId,
    processedUtc
  }) {
    requireText(provider, "provider");
    requireText(providerEventId, "providerEventId");
    requireIso(processedUtc, "processedUtc");

    const result = await this.db.prepare(`
      UPDATE billing_provider_events
      SET processing_status = 'ignored',
          processed_utc = ?,
          last_error_code = NULL
      WHERE provider = ?
        AND provider_event_id = ?
        AND processing_status <> 'processed'
    `).bind(
      processedUtc,
      provider.toLowerCase(),
      providerEventId
    ).run();

    return {
      changed: changes(result) === 1
    };
  }
  async upsertProviderMapping(mapping) {
    validateProviderMapping(mapping);

    const result = await this.db.prepare(`
      INSERT INTO billing_provider_mappings (
        mapping_id,
        provider,
        entity_type,
        internal_entity_id,
        provider_ref,
        created_utc,
        updated_utc
      )
      VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(provider, entity_type, internal_entity_id)
      DO UPDATE SET
        provider_ref = excluded.provider_ref,
        updated_utc = excluded.updated_utc
    `).bind(
      mapping.mappingId,
      mapping.provider.toLowerCase(),
      mapping.entityType,
      mapping.internalEntityId,
      mapping.providerRef,
      mapping.createdUtc,
      mapping.updatedUtc
    ).run();

    return {
      changed: changes(result) >= 1
    };
  }

  async commitSubscriptionTransition({
    previousVersion,
    subscription,
    transaction,
    ledgerEntries,
    nowUtc
  }) {
    validateSubscription(subscription);
    validateTransaction(transaction);
    validateLedgerEntries(ledgerEntries, transaction.transactionId);
    requireIso(nowUtc, "nowUtc");

    if (!Number.isSafeInteger(previousVersion) || previousVersion < -1) {
      throw new TypeError("previousVersion is invalid.");
    }

    const statements = [];

    if (previousVersion === -1) {
      statements.push(
        this.db.prepare(`
          INSERT INTO billing_subscriptions (
            subscription_id,
            customer_id,
            product_id,
            plan_id,
            price_id,
            state,
            period_starts_utc,
            period_ends_utc,
            payment_grace_ends_utc,
            last_provider_event_utc,
            cancel_at_period_end,
            canceled_utc,
            version,
            created_utc,
            updated_utc
          )
          VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
        `).bind(
          subscription.subscriptionId,
          subscription.customerId,
          subscription.productId,
          subscription.planId,
          subscription.priceId,
          subscription.state,
          subscription.periodStartsUtc ?? null,
          subscription.periodEndsUtc ?? null,
          subscription.paymentGraceEndsUtc ?? null,
          subscription.lastProviderEventUtc,
          subscription.cancelAtPeriodEnd ? 1 : 0,
          subscription.canceledUtc ?? null,
          nowUtc,
          nowUtc
        )
      );
    } else {
      statements.push(
        this.db.prepare(`
          UPDATE billing_subscriptions
          SET state = ?,
              period_starts_utc = ?,
              period_ends_utc = ?,
              payment_grace_ends_utc = ?,
              last_provider_event_utc = ?,
              cancel_at_period_end = ?,
              canceled_utc = ?,
              version = version + 1,
              updated_utc = ?
          WHERE subscription_id = ?
            AND version = ?
        `).bind(
          subscription.state,
          subscription.periodStartsUtc ?? null,
          subscription.periodEndsUtc ?? null,
          subscription.paymentGraceEndsUtc ?? null,
          subscription.lastProviderEventUtc,
          subscription.cancelAtPeriodEnd ? 1 : 0,
          subscription.canceledUtc ?? null,
          nowUtc,
          subscription.subscriptionId,
          previousVersion
        )
      );
    }

    statements.push(
      this.db.prepare(`
        INSERT INTO billing_transactions (
          transaction_id,
          customer_id,
          subscription_id,
          transaction_kind,
          status,
          currency,
          list_amount_minor,
          discount_amount_minor,
          subtotal_minor,
          tax_amount_minor,
          gross_amount_minor,
          processor_fee_minor,
          processor_fee_tax_minor,
          refund_amount_minor,
          chargeback_amount_minor,
          net_receivable_minor,
          occurred_utc,
          settled_utc,
          created_utc,
          updated_utc
        )
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      `).bind(
        transaction.transactionId,
        transaction.customerId,
        transaction.subscriptionId ?? null,
        transaction.transactionKind,
        transaction.status,
        transaction.currency,
        transaction.listAmountMinor,
        transaction.discountAmountMinor,
        transaction.subtotalMinor,
        transaction.taxAmountMinor,
        transaction.grossAmountMinor,
        transaction.processorFeeMinor,
        transaction.processorFeeTaxMinor,
        transaction.refundAmountMinor,
        transaction.chargebackAmountMinor,
        transaction.netReceivableMinor,
        transaction.occurredUtc,
        transaction.settledUtc ?? null,
        nowUtc,
        nowUtc
      )
    );

    for (const entry of ledgerEntries) {
      statements.push(
        this.db.prepare(`
          INSERT INTO billing_ledger_entries (
            ledger_entry_id,
            transaction_id,
            entry_type,
            currency,
            amount_minor,
            occurred_utc,
            created_utc
          )
          VALUES (?, ?, ?, ?, ?, ?, ?)
        `).bind(
          entry.ledgerEntryId,
          entry.transactionId,
          entry.entryType,
          entry.currency,
          entry.amountMinor,
          entry.occurredUtc,
          nowUtc
        )
      );
    }

    const results = await this.db.batch(statements);

    return {
      subscriptionChanged: changes(results[0]) === 1,
      transactionWritten: changes(results[1]) === 1,
      ledgerEntriesWritten: results
        .slice(2)
        .reduce((sum, result) => sum + changes(result), 0)
    };
  }
}

function validateProviderEvent(value) {
  if (!value || typeof value !== "object") {
    throw new TypeError("provider event is required.");
  }

  requireText(value.provider, "provider");
  requireText(value.providerEventId, "providerEventId");
  requireText(value.eventType, "eventType");
  requireIso(value.occurredUtc, "occurredUtc");

  if (
    typeof value.payloadSha256 !== "string" ||
    !/^[a-fA-F0-9]{64}$/.test(value.payloadSha256)
  ) {
    throw new TypeError("payloadSha256 is invalid.");
  }
}

function validateProviderMapping(value) {
  if (!value || typeof value !== "object") {
    throw new TypeError("provider mapping is required.");
  }

  for (const field of [
    "mappingId",
    "provider",
    "entityType",
    "internalEntityId",
    "providerRef"
  ]) {
    requireText(value[field], field);
  }

  requireIso(value.createdUtc, "createdUtc");
  requireIso(value.updatedUtc, "updatedUtc");
}

function validateSubscription(value) {
  if (!value || typeof value !== "object") {
    throw new TypeError("subscription is required.");
  }

  for (const field of [
    "subscriptionId",
    "customerId",
    "productId",
    "planId",
    "priceId",
    "state"
  ]) {
    requireText(value[field], field);
  }

  for (const field of [
    "periodStartsUtc",
    "periodEndsUtc",
    "paymentGraceEndsUtc",
    "lastProviderEventUtc",
    "canceledUtc"
  ]) {
    if (value[field] !== null && value[field] !== undefined) {
      requireIso(value[field], field);
    }
  }

  if (typeof value.cancelAtPeriodEnd !== "boolean") {
    throw new TypeError("cancelAtPeriodEnd must be boolean.");
  }
}

function validateTransaction(value) {
  if (!value || typeof value !== "object") {
    throw new TypeError("transaction is required.");
  }

  for (const field of [
    "transactionId",
    "customerId",
    "transactionKind",
    "status",
    "currency"
  ]) {
    requireText(value[field], field);
  }

  for (const field of [
    "listAmountMinor",
    "discountAmountMinor",
    "subtotalMinor",
    "taxAmountMinor",
    "grossAmountMinor",
    "processorFeeMinor",
    "processorFeeTaxMinor",
    "refundAmountMinor",
    "chargebackAmountMinor",
    "netReceivableMinor"
  ]) {
    if (!Number.isSafeInteger(value[field])) {
      throw new TypeError(`${field} must be an integer.`);
    }
  }

  requireIso(value.occurredUtc, "occurredUtc");

  if (value.settledUtc !== null && value.settledUtc !== undefined) {
    requireIso(value.settledUtc, "settledUtc");
  }
}

function validateLedgerEntries(values, transactionId) {
  if (!Array.isArray(values) || values.length === 0) {
    throw new TypeError("ledgerEntries are required.");
  }

  for (const value of values) {
    if (!value || typeof value !== "object") {
      throw new TypeError("ledger entry is invalid.");
    }

    for (const field of [
      "ledgerEntryId",
      "transactionId",
      "entryType",
      "currency"
    ]) {
      requireText(value[field], field);
    }

    if (value.transactionId !== transactionId) {
      throw new TypeError("ledger entry transactionId mismatch.");
    }

    if (!Number.isSafeInteger(value.amountMinor)) {
      throw new TypeError("ledger amountMinor must be an integer.");
    }

    requireIso(value.occurredUtc, "occurredUtc");
  }
}

function requireText(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.length > 256
  ) {
    throw new TypeError(`${field} is invalid.`);
  }

  return value.trim();
}

function requireIso(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    Number.isNaN(Date.parse(value))
  ) {
    throw new TypeError(`${field} is invalid.`);
  }
}

function changes(result) {
  return Number(
    result?.meta?.changes ??
    result?.changes ??
    0
  );
}