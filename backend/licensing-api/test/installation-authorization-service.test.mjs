import assert from "node:assert/strict";
import test from "node:test";

import {
  createInstallationAuthorizationService
} from "../src/installation-authorization-service.js";

function deterministicEntropy(length) {
  return Uint8Array.from(
    { length },
    (_, index) => index + 1
  );
}

test(
  "issuance creates a 5-minute opaque code without persisting plaintext",
  async () => {
    let persisted;

    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization(record) {
            persisted = record;
          },

          async consumeAuthorization() {
            return null;
          }
        },

        clock: () =>
          new Date(
            "2026-08-22T12:00:00.000Z"
          ),

        randomBytes:
          deterministicEntropy
      });

    const issued =
      await service.issue({
        accountId: "acct-123",
        productId: "pcspa-pro"
      });

    assert.match(
      issued.code,
      /^[A-Za-z0-9_-]{43}$/
    );

    assert.equal(
      issued.expiresInSeconds,
      300
    );

    assert.equal(
      persisted.accountId,
      "acct-123"
    );

    assert.equal(
      persisted.productId,
      "pcspa-pro"
    );

    assert.equal(
      persisted.createdUtc,
      "2026-08-22T12:00:00.000Z"
    );

    assert.equal(
      persisted.expiresUtc,
      "2026-08-22T12:05:00.000Z"
    );

    assert.match(
      persisted.codeSha256,
      /^[a-f0-9]{64}$/
    );

    assert.equal(
      Object.hasOwn(persisted, "code"),
      false
    );

    assert.equal(
      JSON.stringify(persisted).includes(
        issued.code
      ),
      false
    );
  }
);

test(
  "exchange returns only identity bound to consumed server record",
  async () => {
    let persisted;
    let consumedHash;

    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization(record) {
            persisted = record;
          },

          async consumeAuthorization(hash) {
            consumedHash = hash;

            return {
              account_id:
                persisted.accountId,
              product_id:
                persisted.productId
            };
          }
        },

        clock: () =>
          new Date(
            "2026-08-22T12:00:00.000Z"
          ),

        randomBytes:
          deterministicEntropy
      });

    const issued =
      await service.issue({
        accountId: "acct-server-derived",
        productId: "pcspa-pro"
      });

    const result =
      await service.exchange(
        issued.code
      );

    assert.equal(
      consumedHash,
      persisted.codeSha256
    );

    assert.deepEqual(
      result,
      {
        authorized: true,
        accountId:
          "acct-server-derived",
        productId:
          "pcspa-pro"
      }
    );
  }
);

test(
  "malformed installation code fails closed without store access",
  async () => {
    let consumeCalls = 0;

    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization() {},

          async consumeAuthorization() {
            consumeCalls += 1;
            return null;
          }
        }
      });

    const result =
      await service.exchange(
        "not-valid"
      );

    assert.deepEqual(
      result,
      {
        authorized: false
      }
    );

    assert.equal(
      consumeCalls,
      0
    );
  }
);

test(
  "unknown expired or already-consumed code fails closed",
  async () => {
    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization() {},

          async consumeAuthorization() {
            return null;
          }
        }
      });

    const code =
      "A".repeat(43);

    assert.deepEqual(
      await service.exchange(code),
      {
        authorized: false
      }
    );
  }
);

test(
  "store failure during exchange fails closed",
  async () => {
    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization() {},

          async consumeAuthorization() {
            throw new Error(
              "database unavailable"
            );
          }
        }
      });

    assert.deepEqual(
      await service.exchange(
        "B".repeat(43)
      ),
      {
        authorized: false
      }
    );
  }
);

test(
  "issue rejects absent trusted account or product identity",
  async () => {
    const service =
      createInstallationAuthorizationService({
        store: {
          async createAuthorization() {},
          async consumeAuthorization() {
            return null;
          }
        },

        randomBytes:
          deterministicEntropy
      });

    await assert.rejects(
      service.issue({
        accountId: "",
        productId: "pcspa-pro"
      }),
      /accountId/
    );

    await assert.rejects(
      service.issue({
        accountId: "acct-1",
        productId: ""
      }),
      /productId/
    );
  }
);