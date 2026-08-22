import test from "node:test";
import assert from "node:assert/strict";

import {
  buildPaddleTransactionRequest
} from "../src/paddle-transaction-request.js";

const PRICE =
  "pri_01m0k4ycnmg10qe9455hwdx0vr";

const CUSTOMER =
  "ctm_01m0kbea1yqw8bfrfskhr11e2x";

const ADDRESS =
  "add_01m0kbh51wyj0x0nbkv7bxdnd6";

function base() {
  return {
    priceId: PRICE,
    quantity: 1,
    internalAccountId:
      "sandbox-account-001",
    internalSubscriptionId:
      "sandbox-sub-001",
    productCode:
      "pcspa-pro"
  };
}

test(
  "trusted Paddle customer and address enter transaction body",
  () => {
    const body =
      buildPaddleTransactionRequest({
        ...base(),
        providerCustomerId:
          CUSTOMER,
        providerAddressId:
          ADDRESS
      });

    assert.equal(
      body.customer_id,
      CUSTOMER
    );

    assert.equal(
      body.address_id,
      ADDRESS
    );
  }
);

test(
  "customer and address must be supplied together",
  () => {
    assert.throws(
      () =>
        buildPaddleTransactionRequest({
          ...base(),
          providerCustomerId:
            CUSTOMER
        }),
      /supplied together/
    );
  }
);

test(
  "invalid provider identifiers fail closed",
  () => {
    assert.throws(
      () =>
        buildPaddleTransactionRequest({
          ...base(),
          providerCustomerId:
            "customer-controlled",
          providerAddressId:
            ADDRESS
        }),
      /providerCustomerId is invalid/
    );
  }
);
