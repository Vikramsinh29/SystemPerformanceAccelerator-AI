import assert from "node:assert/strict";
import test from "node:test";

import productionEntrypoint from "../src/production-entrypoint.js";

const STRONG_SECRET =
  "0123456789abcdef0123456789abcdef";

function createD1() {
  const authorizations =
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

                authorizations.set(
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

              return {
                meta: {
                  changes: 0
                }
              };
            },

            async first() {
              if (
                sql.includes(
                  "UPDATE installation_authorizations"
                )
              ) {
                const [
                  nowUtc,
                  codeSha256
                ] = bindings;

                const row =
                  authorizations.get(
                    codeSha256
                  );

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

              return null;
            },

            async all() {
              return {
                results: []
              };
            }
          };
        }
      };
    },

    async batch() {
      return [];
    }
  };
}

function environment(
  database = createD1()
) {
  return {
    PRODUCTION_LICENSING_ENABLED:
      "enabled",

    LICENSING_IDENTITY_SECRET:
      STRONG_SECRET,

    LICENSING_DB:
      database
  };
}

function internalIssueRequest(
  body,
  url =
    "https://licensing-v2.internal/internal/installation-authorization"
) {
  return new Request(
    url,
    {
      method: "POST",
      headers: {
        "content-type":
          "application/json"
      },
      body:
        JSON.stringify(body)
    }
  );
}

function publicExchangeRequest(
  body
) {
  return new Request(
    "https://pc-spa-licensing-v2-production.example.test/installation-authorization/exchange",
    {
      method: "POST",
      headers: {
        "content-type":
          "application/json"
      },
      body:
        JSON.stringify(body)
    }
  );
}

test(
  "production entrypoint keeps installation authorization behind production gate",
  async () => {
    const response =
      await productionEntrypoint.fetch(
        internalIssueRequest({
          accountId:
            "acct-1",
          productId:
            "pcspa-pro"
        }),
        {
          PRODUCTION_LICENSING_ENABLED:
            "disabled"
        }
      );

    assert.equal(
      response.status,
      503
    );

    assert.equal(
      (await response.json()).error,
      "production_not_enabled"
    );
  }
);

test(
  "production entrypoint permits exact private service-binding issuance",
  async () => {
    const database =
      createD1();

    const response =
      await productionEntrypoint.fetch(
        internalIssueRequest({
          accountId:
            "acct-service-binding",
          productId:
            "pcspa-pro"
        }),
        environment(database)
      );

    assert.equal(
      response.status,
      200
    );

    const body =
      await response.json();

    assert.match(
      body.authorizationCode,
      /^[A-Za-z0-9_-]{43}$/
    );

    assert.equal(
      body.expiresInSeconds,
      300
    );

    assert.equal(
      Object.hasOwn(
        body,
        "accountId"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(
        body,
        "productId"
      ),
      false
    );
  }
);

test(
  "public Worker host cannot reach trusted installation authorization issuance",
  async () => {
    const response =
      await productionEntrypoint.fetch(
        internalIssueRequest(
          {
            accountId:
              "acct-attacker",
            productId:
              "pcspa-pro"
          },
          "https://pc-spa-licensing-v2-production.example.test/internal/installation-authorization"
        ),
        environment()
      );

    assert.equal(
      response.status,
      404
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "not_found"
      }
    );
  }
);

test(
  "production entrypoint exchanges private-issued authorization on public endpoint",
  async () => {
    const database =
      createD1();

    const env =
      environment(database);

    const issueResponse =
      await productionEntrypoint.fetch(
        internalIssueRequest({
          accountId:
            "acct-runtime-bound",
          productId:
            "pcspa-pro"
        }),
        env
      );

    assert.equal(
      issueResponse.status,
      200
    );

    const issued =
      await issueResponse.json();

    const exchangeResponse =
      await productionEntrypoint.fetch(
        publicExchangeRequest({
          authorizationCode:
            issued.authorizationCode,

          accountId:
            "acct-attacker",

          productId:
            "attacker-product"
        }),
        env
      );

    assert.equal(
      exchangeResponse.status,
      200
    );

    const exchanged =
      await exchangeResponse.json();

    assert.equal(
      exchanged.tokenType,
      "Bearer"
    );

    assert.equal(
      exchanged.expiresInSeconds,
      300
    );

    assert.match(
      exchanged.token,
      /^pcspa1\./
    );

    assert.equal(
      Object.hasOwn(
        exchanged,
        "accountId"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(
        exchanged,
        "productId"
      ),
      false
    );
  }
);

test(
  "production entrypoint enforces one-time public exchange",
  async () => {
    const database =
      createD1();

    const env =
      environment(database);

    const issueResponse =
      await productionEntrypoint.fetch(
        internalIssueRequest({
          accountId:
            "acct-single-use",
          productId:
            "pcspa-pro"
        }),
        env
      );

    const issued =
      await issueResponse.json();

    const makeExchange =
      () =>
        publicExchangeRequest({
          authorizationCode:
            issued.authorizationCode
        });

    const first =
      await productionEntrypoint.fetch(
        makeExchange(),
        env
      );

    const second =
      await productionEntrypoint.fetch(
        makeExchange(),
        env
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
  "production entrypoint rejects unknown authorization without token",
  async () => {
    const response =
      await productionEntrypoint.fetch(
        publicExchangeRequest({
          authorizationCode:
            "A".repeat(43)
        }),
        environment()
      );

    assert.equal(
      response.status,
      401
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "invalid_authorization"
      }
    );
  }
);

test(
  "existing public unknown route behavior remains unchanged",
  async () => {
    const response =
      await productionEntrypoint.fetch(
        new Request(
          "https://pc-spa-licensing-v2-production.example.test/not-a-route"
        ),
        environment()
      );

    assert.equal(
      response.status,
      404
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "not_found"
      }
    );
  }
);