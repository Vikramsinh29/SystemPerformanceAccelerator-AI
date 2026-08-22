import test from "node:test";
import assert from "node:assert/strict";

import {
  CommercialCheckoutOrchestrator
} from "../src/commercial-checkout-orchestrator.js";

const PRICE_ID =
  "pri_01m0k4ycnmg10qe9455hwdx0vr";

const CUSTOMER_ID =
  "ctm_01m0kbea1yqw8bfrfskhr11e2x";

const ADDRESS_ID =
  "add_01m0kbh51wyj0x0nbkv7bxdnd6";

test(
  "orchestrator forwards trusted Paddle billing context",
  async () => {
    let capturedBody = null;

    const orchestrator =
      new CommercialCheckoutOrchestrator({
        priceCatalog: {
          resolve(plan, seats) {
            return {
              productId:
                "pcspa-pro",
              billingInterval:
                plan,
              seats,
              providerPriceId:
                PRICE_ID
            };
          }
        },

        paddleClient: {
          async createTransaction(body) {
            capturedBody = body;

            return {
              transactionId:
                "txn_01m0kbh67p71v5129qcf3m0672",

              checkoutUrl:
                "https://localhost/?_ptxn=test",

              requestId:
                "req_test"
            };
          }
        },

        idFactory() {
          return "sandbox-sub-001";
        }
      });

    const result =
      await orchestrator.createCheckout({
        accountId:
          "sandbox-account-001",

        plan:
          "monthly",

        seats:
          1,

        providerCustomerId:
          CUSTOMER_ID,

        providerAddressId:
          ADDRESS_ID
      });

    assert.equal(
      capturedBody.customer_id,
      CUSTOMER_ID
    );

    assert.equal(
      capturedBody.address_id,
      ADDRESS_ID
    );

    assert.equal(
      result.entitlementActivated,
      false
    );
  }
);
