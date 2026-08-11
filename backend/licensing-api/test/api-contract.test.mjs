import assert from "node:assert/strict";
import test from "node:test";
import {
  LICENSING_API_VERSION,
  LICENSING_ROUTES,
  requireEnvironment
} from "../src/api-contract.js";

test("licensing API contract is versioned and bounded", () => {
  assert.equal(LICENSING_API_VERSION, "v1");
  assert.equal(LICENSING_ROUTES.paymentEvent, "/v1/internal/payment-events");
  assert.equal(LICENSING_ROUTES.activate, "/v1/desktop/activate");
  assert.equal(LICENSING_ROUTES.validate, "/v1/desktop/validate");
  assert.equal(LICENSING_ROUTES.transfer, "/v1/desktop/transfer");
});

test("licensing environment accepts only explicit environments", () => {
  for (const value of ["local", "staging", "production"]) {
    assert.equal(requireEnvironment(value), value);
  }
  assert.throws(() => requireEnvironment("preview"), TypeError);
  assert.throws(() => requireEnvironment(""), TypeError);
});
