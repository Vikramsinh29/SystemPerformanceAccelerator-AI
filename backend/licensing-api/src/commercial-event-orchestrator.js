import {
  deriveEntitlementProjection,
  transitionBillingState
} from "./commercial-billing-domain.js";

export class CommercialEventOrchestrator {
  constructor({
    billingStore,
    licensingStore,
    idFactory
  }) {
    if (!billingStore) {
      throw new TypeError("billingStore is required.");
    }

    if (!licensingStore) {
      throw new TypeError("licensingStore is required.");
    }

    if (typeof idFactory !== "function") {
      throw new TypeError("idFactory is required.");
    }

    this.billingStore = billingStore;
    this.licensingStore = licensingStore;
    this.idFactory = idFactory;
  }

  async process(input, nowUtc) {
    validateInput(input);
    requireIso(nowUtc, "nowUtc");

    const received = await this.billingStore.recordProviderEvent(
      input.providerEvent,
      nowUtc
    );

    if (received.duplicate) {
      const existing =
        await this.billingStore.findProviderEvent(
          input.providerEvent.provider,
          input.providerEvent.providerEventId
        );

      if (
        existing?.processing_status === "processed" ||
        existing?.processing_status === "ignored"
      ) {
        return {
          duplicate: true,
          billingChanged: false,
          licensingChanged: false,
          ignoredOutOfOrder: false
        };
      }
    }

    const subscription =
      await this.billingStore.findSubscription(
        input.subscriptionId
      );

    if (!subscription) {
      await this.billingStore.markProviderEventRetryableFailure({
        provider: input.providerEvent.provider,
        providerEventId: input.providerEvent.providerEventId,
        errorCode: "subscription_not_found",
        attemptedUtc: nowUtc
      });

      throw new CommercialEventError(
        "subscription_not_found",
        "Billing subscription was not found."
      );
    }

    const previousEventUtc =
      subscription.last_provider_event_utc
        ? Date.parse(subscription.last_provider_event_utc)
        : null;

    const incomingEventUtc =
      Date.parse(input.providerEvent.occurredUtc);

    if (
      previousEventUtc !== null &&
      incomingEventUtc < previousEventUtc
    ) {
      await this.billingStore.markProviderEventIgnored({
        provider: input.providerEvent.provider,
        providerEventId: input.providerEvent.providerEventId,
        processedUtc: nowUtc
      });

      return {
        duplicate: false,
        billingChanged: false,
        licensingChanged: false,
        ignoredOutOfOrder: true
      };
    }

    const nextBillingState =
      resolveBillingState(
        subscription.state,
        input.commercialAction
      );

    const transition =
      transitionBillingState(
        subscription.state,
        nextBillingState
      );

    const nextSubscription = {
      subscriptionId: subscription.subscription_id,
      customerId: subscription.customer_id,
      productId: subscription.product_id,
      planId: subscription.plan_id,
      priceId: subscription.price_id,
      state: transition.state,
      periodStartsUtc:
        input.periodStartsUtc ??
        subscription.period_starts_utc ??
        null,
      periodEndsUtc:
        input.periodEndsUtc ??
        subscription.period_ends_utc ??
        null,
      paymentGraceEndsUtc:
        input.paymentGraceEndsUtc ??
        subscription.payment_grace_ends_utc ??
        null,
      lastProviderEventUtc:
        input.providerEvent.occurredUtc,
      cancelAtPeriodEnd:
        input.cancelAtPeriodEnd ??
        Boolean(subscription.cancel_at_period_end),
      canceledUtc:
        input.canceledUtc ??
        subscription.canceled_utc ??
        null
    };

    const billingResult =
      await this.billingStore.commitProviderSubscriptionTransition({
        provider: input.providerEvent.provider,
        providerEventId:
          input.providerEvent.providerEventId,
        previousVersion: subscription.version,
        subscription: nextSubscription,
        processedUtc: nowUtc
      });

    if (
      !billingResult.subscriptionChanged ||
      !billingResult.eventProcessed
    ) {
      throw new CommercialEventError(
        "billing_transition_conflict",
        "Billing transition could not be committed atomically."
      );
    }

    const projection =
      deriveEntitlementProjection(
        nextSubscription,
        nowUtc
      );

    const existingEntitlement =
      await this.licensingStore.findEntitlement(
        subscription.customer_id,
        subscription.product_id
      );

    const entitlement =
      buildEntitlement({
        existingEntitlement,
        subscription,
        projection,
        input,
        nowUtc,
        idFactory: this.idFactory
      });

    const previousVersion =
      existingEntitlement
        ? existingEntitlement.version
        : -1;

    const licensingEvent = {
      provider: input.providerEvent.provider,
      providerEventId:
        input.providerEvent.providerEventId,
      accountId: subscription.customer_id,
      productId: subscription.product_id,
      providerSubscriptionId:
        input.providerSubscriptionRef ?? null,
      kind: input.commercialAction,
      occurredUtc: input.providerEvent.occurredUtc
    };

    const audit = {
      auditId: this.idFactory("audit"),
      occurredUtc: nowUtc,
      previousState:
        existingEntitlement?.state ?? null,
      currentState: entitlement.state,
      message:
        `Commercial event ${input.commercialAction} projected to ${entitlement.state}.`
    };

    const licensingResult =
      await this.licensingStore.commitTransition({
        event: licensingEvent,
        previousVersion,
        entitlement,
        audit,
        outcome: "applied",
        nowUtc
      });

    return {
      duplicate: false,
      billingChanged: true,
      licensingChanged:
        licensingResult.entitlementChanged === true,
      ignoredOutOfOrder: false
    };
  }
}

function resolveBillingState(currentState, action) {
  const transitions = {
    payment_activated: "active",
    renewal_succeeded: "active",
    payment_failed: "past_due",
    grace_started: "grace",
    canceled: "canceled",
    expired: "expired",
    suspended: "suspended"
  };

  const next = transitions[action];

  if (!next) {
    throw new CommercialEventError(
      "unsupported_commercial_action",
      "Commercial action is not supported."
    );
  }

  if (
    currentState === "canceled" &&
    action === "renewal_succeeded"
  ) {
    return "active";
  }

  return next;
}

function buildEntitlement({
  existingEntitlement,
  subscription,
  projection,
  input,
  nowUtc,
  idFactory
}) {
  const entitlementId =
    existingEntitlement?.entitlement_id ??
    idFactory("entitlement");

  const activeDeviceCount =
    existingEntitlement?.active_device_count ?? 0;

  const transfersUsed =
    existingEntitlement?.transfers_used ?? 0;

  const transferWindowStartedUtc =
    existingEntitlement?.transfer_window_started_utc ??
    nowUtc;

  const periodEndsUtc =
    input.periodEndsUtc ??
    subscription.period_ends_utc;

  if (!periodEndsUtc) {
    throw new CommercialEventError(
      "missing_period_end",
      "A licensing period end is required."
    );
  }

  return {
    entitlementId,
    accountId: subscription.customer_id,
    productId: subscription.product_id,
    state: projection.state,
    seatLimit: input.seatLimit,
    activeDeviceCount,
    periodEndsUtc,
    paymentGraceEndsUtc:
      input.paymentGraceEndsUtc ??
      subscription.payment_grace_ends_utc ??
      null,
    offlineValidUntilUtc:
      existingEntitlement?.offline_valid_until_utc ??
      null,
    transfersUsed,
    transferWindowStartedUtc,
    lastTransferUtc:
      existingEntitlement?.last_transfer_utc ??
      null,
    lastCommercialEventUtc:
      input.providerEvent.occurredUtc
  };
}

function validateInput(value) {
  if (!value || typeof value !== "object") {
    throw new TypeError("commercial event input is required.");
  }

  if (!value.providerEvent) {
    throw new TypeError("providerEvent is required.");
  }

  requireText(value.subscriptionId, "subscriptionId");
  requireText(value.commercialAction, "commercialAction");

  if (
    !Number.isSafeInteger(value.seatLimit) ||
    value.seatLimit < 1
  ) {
    throw new TypeError("seatLimit must be a positive integer.");
  }

  for (const field of [
    "periodStartsUtc",
    "periodEndsUtc",
    "paymentGraceEndsUtc",
    "canceledUtc"
  ]) {
    if (
      value[field] !== null &&
      value[field] !== undefined
    ) {
      requireIso(value[field], field);
    }
  }
}

function requireText(value, field) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0
  ) {
    throw new TypeError(`${field} is required.`);
  }
}

function requireIso(value, field) {
  requireText(value, field);

  if (Number.isNaN(Date.parse(value))) {
    throw new TypeError(`${field} must be ISO-8601.`);
  }
}

export class CommercialEventError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "CommercialEventError";
    this.code = code;
  }
}