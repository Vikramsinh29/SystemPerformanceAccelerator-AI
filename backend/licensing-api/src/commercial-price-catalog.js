const PADDLE_PRICE_ID =
  /^pri_[a-z\d]{26}$/;

const PLAN_CODES =
  Object.freeze({
    monthly: "PCSPA_PRO_MONTHLY",
    annual: "PCSPA_PRO_ANNUAL"
  });

export function createCommercialPriceCatalog({
  productId = "pcspa-pro",
  monthlyPriceId,
  annualPriceId,
  maxSeats = 100
}) {
  requireInternalId(
    productId,
    "productId"
  );

  requirePaddlePriceId(
    monthlyPriceId,
    "monthlyPriceId"
  );

  requirePaddlePriceId(
    annualPriceId,
    "annualPriceId"
  );

  if (
    !Number.isSafeInteger(maxSeats) ||
    maxSeats < 1 ||
    maxSeats > 100000
  ) {
    throw new CommercialPriceCatalogError(
      "invalid_max_seats",
      "maxSeats is invalid."
    );
  }

  const entries =
    Object.freeze({
      monthly:
        Object.freeze({
          planId:
            "pcspa-pro-monthly",

          productId,

          planCode:
            PLAN_CODES.monthly,

          billingInterval:
            "monthly",

          providerPriceId:
            monthlyPriceId,

          maxSeats
        }),

      annual:
        Object.freeze({
          planId:
            "pcspa-pro-annual",

          productId,

          planCode:
            PLAN_CODES.annual,

          billingInterval:
            "annual",

          providerPriceId:
            annualPriceId,

          maxSeats
        })
    });

  return Object.freeze({
    resolve(plan, seats) {
      if (
        plan !== "monthly" &&
        plan !== "annual"
      ) {
        throw new CommercialPriceCatalogError(
          "unsupported_plan",
          "Commercial plan is not supported."
        );
      }

      if (
        !Number.isSafeInteger(seats) ||
        seats < 1 ||
        seats > entries[plan].maxSeats
      ) {
        throw new CommercialPriceCatalogError(
          "invalid_seat_quantity",
          "Seat quantity is outside the approved range."
        );
      }

      return Object.freeze({
        ...entries[plan],
        seats
      });
    }
  });
}

function requirePaddlePriceId(
  value,
  field
) {
  if (
    typeof value !== "string" ||
    !PADDLE_PRICE_ID.test(value)
  ) {
    throw new CommercialPriceCatalogError(
      "invalid_provider_price",
      `${field} is invalid.`
    );
  }
}

function requireInternalId(
  value,
  field
) {
  if (
    typeof value !== "string" ||
    value.trim().length === 0 ||
    value.length > 128
  ) {
    throw new CommercialPriceCatalogError(
      "invalid_internal_id",
      `${field} is invalid.`
    );
  }
}

export class CommercialPriceCatalogError
  extends Error {
  constructor(code, message) {
    super(message);

    this.name =
      "CommercialPriceCatalogError";

    this.code = code;
  }
}