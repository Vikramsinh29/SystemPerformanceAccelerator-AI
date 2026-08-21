import assert from "node:assert/strict";
import test from "node:test";

import {
  CommercialPriceCatalogError,
  createCommercialPriceCatalog
} from "../src/commercial-price-catalog.js";

const monthly =
  "pri_01h00000000000000000000000";

const annual =
  "pri_01h11111111111111111111111";

function catalog() {
  return createCommercialPriceCatalog({
    monthlyPriceId:
      monthly,

    annualPriceId:
      annual,

    maxSeats:
      25
  });
}

test("monthly plan resolves trusted provider price", () => {
  const result =
    catalog().resolve(
      "monthly",
      3
    );

  assert.equal(
    result.providerPriceId,
    monthly
  );

  assert.equal(
    result.billingInterval,
    "monthly"
  );

  assert.equal(
    result.seats,
    3
  );
});

test("annual plan resolves independent provider price", () => {
  const result =
    catalog().resolve(
      "annual",
      5
    );

  assert.equal(
    result.providerPriceId,
    annual
  );

  assert.equal(
    result.billingInterval,
    "annual"
  );
});

test("unsupported plan fails closed", () => {
  assert.throws(
    () =>
      catalog().resolve(
        "lifetime",
        1
      ),

    (error) =>
      error instanceof
        CommercialPriceCatalogError &&
      error.code ===
        "unsupported_plan"
  );
});

test("zero seats are rejected", () => {
  assert.throws(
    () =>
      catalog().resolve(
        "monthly",
        0
      ),

    (error) =>
      error.code ===
        "invalid_seat_quantity"
  );
});

test("seat quantity cannot exceed server policy", () => {
  assert.throws(
    () =>
      catalog().resolve(
        "annual",
        26
      ),

    (error) =>
      error.code ===
        "invalid_seat_quantity"
  );
});

test("provider price IDs are configuration only", () => {
  assert.throws(
    () =>
      createCommercialPriceCatalog({
        monthlyPriceId:
          "client-controlled",

        annualPriceId:
          annual
      }),

    (error) =>
      error.code ===
        "invalid_provider_price"
  );
});