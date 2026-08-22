import assert from "node:assert/strict";
import test from "node:test";

import {
  createProductionInstallationAuthorizationComposition
} from "../src/production-installation-authorization-composition.js";

function createD1() {
  const rows =
    new Map();

  return {
    prepare(sql) {
      return {
        bind(...bindings) {
          return {
            async run() {
              if (
                sql.includes(
                  "INSERT INTO installation_authorizations"
                )
              ) {
                const [
                  authorizationId,
                  codeSha256,
                  accountId,
                  productId,
                  createdUtc,
                  expiresUtc
                ] = bindings;

                rows.set(
                  codeSha256,
                  {
                    authorization_id:
                      authorizationId,
                    code_sha256:
                      codeSha256,
                    account_id:
                      accountId,
                    product_id:
                      productId,
                    created_utc:
                      createdUtc,
                    expires_utc:
                      expiresUtc,
                    consumed_utc:
                      null
                  }
                );

                return {
                  meta: {
                    changes: 1
                  }
                };
              }

              throw new Error(
                "unexpected run query"
              );
            },

            async first() {
              if (
                !sql.includes(
                  "UPDATE installation_authorizations"
                )
              ) {
                throw new Error(
                  "unexpected first query"
                );
              }

              const [
                nowUtc,
                codeSha256
              ] = bindings;

              const row =
                rows.get(codeSha256);

              if (
                !row ||
                row.consumed_utc !== null ||
                row.expires_utc <= nowUtc
              ) {
                return null;
              }

              row.consumed_utc =
                nowUtc;

              return {
                authorization_id:
                  row.authorization_id,
                account_id:
                  row.account_id,
                product_id:
                  row.product_id,
                created_utc:
                  row.created_utc,
                expires_utc:
                  row.expires_utc,
                consumed_utc:
                  row.consumed_utc
              };
            }
          };
        }
      };
    }
  };
}

function entropy(length) {
  return Uint8Array.from(
    { length },
    (_, index) =>
      (index + 17) % 256
  );
}

test(
  "production composition carries verified browser identity through one-time exchange into production token",
  async () => {
    const database =
      createD1();

    const secret =
      "0123456789abcdef0123456789abcdef";

    const authorizationClock =
      () =>
        new Date(
          "2026-08-22T12:00:00.000Z"
        );

    const tokenClock =
      () =>
        Date.parse(
          "2026-08-22T12:01:00.000Z"
        );

    const handlers =
      createProductionInstallationAuthorizationComposition({
        database,

        verifySession:
          async () => ({
            accountId:
              "acct-session-trusted",
            productId:
              "pcspa-pro"
          }),

        identitySecret:
          secret,

        authorizationClock,
        tokenClock,
        randomBytes:
          entropy
      });

    const issueResponse =
      await handlers.issue(
        new Request(
          "https://licensing.example.test/install/issue",
          {
            method: "POST"
          }
        )
      );

    assert.equal(
      issueResponse.status,
      200
    );

    const issued =
      await issueResponse.json();

    assert.match(
      issued.authorizationCode,
      /^[A-Za-z0-9_-]{43}$/
    );

    const exchangeResponse =
      await handlers.exchange(
        new Request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              authorizationCode:
                issued.authorizationCode,

              accountId:
                "acct-attacker",

              productId:
                "attacker-product"
            })
          }
        )
      );

    assert.equal(
      exchangeResponse.status,
      200
    );

    const tokenResult =
      await exchangeResponse.json();

    assert.equal(
      tokenResult.tokenType,
      "Bearer"
    );

    assert.equal(
      tokenResult.expiresInSeconds,
      300
    );

    assert.match(
      tokenResult.token,
      /^pcspa1\./
    );
  }
);

test(
  "production composition makes authorization genuinely single-use",
  async () => {
    const handlers =
      createProductionInstallationAuthorizationComposition({
        database:
          createD1(),

        verifySession:
          async () => ({
            accountId:
              "acct-1",
            productId:
              "pcspa-pro"
          }),

        identitySecret:
          "0123456789abcdef0123456789abcdef",

        authorizationClock:
          () =>
            new Date(
              "2026-08-22T12:00:00.000Z"
            ),

        tokenClock:
          () =>
            Date.parse(
              "2026-08-22T12:01:00.000Z"
            ),

        randomBytes:
          entropy
      });

    const issueResponse =
      await handlers.issue(
        new Request(
          "https://licensing.example.test/install/issue",
          {
            method: "POST"
          }
        )
      );

    const issued =
      await issueResponse.json();

    const makeExchange =
      () =>
        new Request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              authorizationCode:
                issued.authorizationCode
            })
          }
        );

    const first =
      await handlers.exchange(
        makeExchange()
      );

    const second =
      await handlers.exchange(
        makeExchange()
      );

    assert.equal(
      first.status,
      200
    );

    assert.equal(
      second.status,
      401
    );

    assert.deepEqual(
      await second.json(),
      {
        error:
          "invalid_authorization"
      }
    );
  }
);

test(
  "production composition rejects unauthenticated issuance",
  async () => {
    const handlers =
      createProductionInstallationAuthorizationComposition({
        database:
          createD1(),

        verifySession:
          async () => null,

        identitySecret:
          "0123456789abcdef0123456789abcdef",

        randomBytes:
          entropy
      });

    const response =
      await handlers.issue(
        new Request(
          "https://licensing.example.test/install/issue",
          {
            method: "POST"
          }
        )
      );

    assert.equal(
      response.status,
      401
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "unauthenticated"
      }
    );
  }
);

test(
  "production composition validates database session verifier and signing secret at construction",
  () => {
    assert.throws(
      () =>
        createProductionInstallationAuthorizationComposition({
          database: {},
          verifySession:
            async () => ({
              accountId:
                "acct-1"
            }),
          identitySecret:
            "0123456789abcdef0123456789abcdef"
        }),
      /D1-compatible/
    );

    assert.throws(
      () =>
        createProductionInstallationAuthorizationComposition({
          database:
            createD1(),
          verifySession:
            null,
          identitySecret:
            "0123456789abcdef0123456789abcdef"
        }),
      /verifySession/
    );

    assert.throws(
      () =>
        createProductionInstallationAuthorizationComposition({
          database:
            createD1(),
          verifySession:
            async () => ({
              accountId:
                "acct-1"
            }),
          identitySecret:
            "too-short"
        }),
      /identity secret/
    );
  }
);

test(
  "production composition is not a router and exposes only issue and exchange handlers",
  () => {
    const handlers =
      createProductionInstallationAuthorizationComposition({
        database:
          createD1(),

        verifySession:
          async () => ({
            accountId:
              "acct-1",
            productId:
              "pcspa-pro"
          }),

        identitySecret:
          "0123456789abcdef0123456789abcdef",

        randomBytes:
          entropy
      });

    assert.deepEqual(
      Object.keys(handlers).sort(),
      [
        "exchange",
        "issue"
      ]
    );

    assert.equal(
      Object.hasOwn(
        handlers,
        "fetch"
      ),
      false
    );
  }
);