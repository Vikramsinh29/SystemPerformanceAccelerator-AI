import assert from "node:assert/strict";
import test from "node:test";

import {
  D1InstallationAuthorizationStore
} from "../src/installation-authorization-store.js";

function createDatabase({
  runResult = { meta: { changes: 1 } },
  firstResult = null
} = {}) {
  const calls = [];

  return {
    calls,

    prepare(sql) {
      const call = {
        sql,
        bindings: []
      };

      calls.push(call);

      return {
        bind(...bindings) {
          call.bindings = bindings;

          return {
            async run() {
              return runResult;
            },

            async first() {
              return firstResult;
            }
          };
        }
      };
    }
  };
}

test(
  "store persists only the SHA-256 authorization digest",
  async () => {
    const database =
      createDatabase();

    const store =
      new D1InstallationAuthorizationStore(database);

    await store.createAuthorization({
      authorizationId:
        "ia_0123456789abcdef0123456789abcdef",
      codeSha256:
        "a".repeat(64),
      accountId:
        "acct-1",
      productId:
        "pcspa-pro",
      createdUtc:
        "2026-08-22T12:00:00.000Z",
      expiresUtc:
        "2026-08-22T12:05:00.000Z"
    });

    assert.equal(
      database.calls.length,
      1
    );

    assert.match(
      database.calls[0].sql,
      /INSERT INTO installation_authorizations/
    );

    assert.deepEqual(
      database.calls[0].bindings,
      [
        "ia_0123456789abcdef0123456789abcdef",
        "a".repeat(64),
        "acct-1",
        "pcspa-pro",
        "2026-08-22T12:00:00.000Z",
        "2026-08-22T12:05:00.000Z"
      ]
    );
  }
);

test(
  "consume is conditional on unused and unexpired authorization",
  async () => {
    const row = {
      authorization_id:
        "ia_0123456789abcdef0123456789abcdef",
      account_id:
        "acct-1",
      product_id:
        "pcspa-pro",
      created_utc:
        "2026-08-22T12:00:00.000Z",
      expires_utc:
        "2026-08-22T12:05:00.000Z",
      consumed_utc:
        "2026-08-22T12:01:00.000Z"
    };

    const database =
      createDatabase({
        firstResult: row
      });

    const store =
      new D1InstallationAuthorizationStore(database);

    const result =
      await store.consumeAuthorization(
        "b".repeat(64),
        "2026-08-22T12:01:00.000Z"
      );

    assert.deepEqual(
      result,
      row
    );

    assert.match(
      database.calls[0].sql,
      /consumed_utc IS NULL/
    );

    assert.match(
      database.calls[0].sql,
      /expires_utc > \?/
    );

    assert.match(
      database.calls[0].sql,
      /RETURNING/
    );

    assert.deepEqual(
      database.calls[0].bindings,
      [
        "2026-08-22T12:01:00.000Z",
        "b".repeat(64),
        "2026-08-22T12:01:00.000Z"
      ]
    );
  }
);

test(
  "store fails when an authorization insert changes no rows",
  async () => {
    const database =
      createDatabase({
        runResult: {
          meta: {
            changes: 0
          }
        }
      });

    const store =
      new D1InstallationAuthorizationStore(database);

    await assert.rejects(
      store.createAuthorization({
        authorizationId:
          "ia_0123456789abcdef0123456789abcdef",
        codeSha256:
          "c".repeat(64),
        accountId:
          "acct-1",
        productId:
          "pcspa-pro",
        createdUtc:
          "2026-08-22T12:00:00.000Z",
        expiresUtc:
          "2026-08-22T12:05:00.000Z"
      }),
      /not persisted/
    );
  }
);