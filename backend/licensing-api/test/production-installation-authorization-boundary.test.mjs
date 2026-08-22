import assert from "node:assert/strict";
import test from "node:test";

import {
  createProductionInstallationAuthorizationBoundary,
  isProductionInstallationAuthorizationExchangeRequest,
  isProductionInternalInstallationAuthorizationRequest
} from "../src/production-installation-authorization-boundary.js";

const SECRET =
  "0123456789abcdef0123456789abcdef";

function entropy(length) {
  return Uint8Array.from(
    {
      length
    },
    (_, index) =>
      (index + 41) % 256
  );
}

function createD1() {
  const rows =
    new Map();

  return {
    rows,

    prepare(sql) {
      return {
        bind(...bindings) {
          return {
            async run() {
              if (
                !sql.includes(
                  "INSERT INTO installation_authorizations"
                )
              ) {
                throw new Error(
                  "unexpected run query"
                );
              }

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
                  changes:
                    1
                }
              };
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
                rows.get(
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
          };
        }
      };
    }
  };
}

function createBoundary(
  overrides = {}
) {
  return createProductionInstallationAuthorizationBoundary({
    database:
      createD1(),

    identitySecret:
      SECRET,

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
      entropy,

    ...overrides
  });
}

function internalRequest(
  body,
  url =
    "https://licensing-v2.internal/internal/installation-authorization"
) {
  return new Request(
    url,
    {
      method:
        "POST",
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
  body,
  url =
    "https://pc-spa-licensing-v2-production.example.test/installation-authorization/exchange"
) {
  return new Request(
    url,
    {
      method:
        "POST",
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
  "internal issuance matcher accepts only exact service-binding URL",
  () => {
    assert.equal(
      isProductionInternalInstallationAuthorizationRequest(
        new Request(
          "https://licensing-v2.internal/internal/installation-authorization"
        )
      ),
      true
    );

    assert.equal(
      isProductionInternalInstallationAuthorizationRequest(
        new Request(
          "https://public.example.test/internal/installation-authorization"
        )
      ),
      false
    );

    assert.equal(
      isProductionInternalInstallationAuthorizationRequest(
        new Request(
          "https://licensing-v2.internal/installation-authorization/exchange"
        )
      ),
      false
    );
  }
);

test(
  "public exchange matcher refuses internal host and accepts public HTTPS path",
  () => {
    assert.equal(
      isProductionInstallationAuthorizationExchangeRequest(
        new Request(
          "https://public.example.test/installation-authorization/exchange"
        )
      ),
      true
    );

    assert.equal(
      isProductionInstallationAuthorizationExchangeRequest(
        new Request(
          "https://licensing-v2.internal/installation-authorization/exchange"
        )
      ),
      false
    );

    assert.equal(
      isProductionInstallationAuthorizationExchangeRequest(
        new Request(
          "http://public.example.test/installation-authorization/exchange"
        )
      ),
      false
    );
  }
);

test(
  "trusted internal service-binding identity receives opaque five-minute authorization",
  async () => {
    const boundary =
      createBoundary();

    const response =
      await boundary.issueInternal(
        internalRequest({
          accountId:
            "acct-trusted-service",
          productId:
            "pcspa-pro"
        })
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
  "public host cannot invoke trusted internal issuance",
  async () => {
    const boundary =
      createBoundary();

    const response =
      await boundary.issueInternal(
        internalRequest(
          {
            accountId:
              "acct-attacker",
            productId:
              "pcspa-pro"
          },
          "https://public.example.test/internal/installation-authorization"
        )
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
  "malformed trusted internal identity fails closed",
  async () => {
    const boundary =
      createBoundary();

    const response =
      await boundary.issueInternal(
        internalRequest({
          accountId:
            "",
          productId:
            "pcspa-pro"
        })
      );

    assert.equal(
      response.status,
      400
    );

    assert.deepEqual(
      await response.json(),
      {
        error:
          "invalid_identity"
      }
    );
  }
);

test(
  "issued code exchanges publicly into production bearer using server-bound identity",
  async () => {
    const boundary =
      createBoundary();

    const issue =
      await boundary.issueInternal(
        internalRequest({
          accountId:
            "acct-server-bound",
          productId:
            "pcspa-pro"
        })
      );

    assert.equal(
      issue.status,
      200
    );

    const issued =
      await issue.json();

    const exchange =
      await boundary.exchangePublic(
        publicExchangeRequest({
          authorizationCode:
            issued.authorizationCode,

          accountId:
            "acct-attacker",

          productId:
            "attacker-product"
        })
      );

    assert.equal(
      exchange.status,
      200
    );

    const result =
      await exchange.json();

    assert.equal(
      result.tokenType,
      "Bearer"
    );

    assert.equal(
      result.expiresInSeconds,
      300
    );

    assert.match(
      result.token,
      /^pcspa1\./
    );

    assert.equal(
      Object.hasOwn(
        result,
        "accountId"
      ),
      false
    );

    assert.equal(
      Object.hasOwn(
        result,
        "productId"
      ),
      false
    );
  }
);

test(
  "public installation authorization is single-use",
  async () => {
    const boundary =
      createBoundary();

    const issue =
      await boundary.issueInternal(
        internalRequest({
          accountId:
            "acct-single-use",
          productId:
            "pcspa-pro"
        })
      );

    const issued =
      await issue.json();

    const makeExchange =
      () =>
        publicExchangeRequest({
          authorizationCode:
            issued.authorizationCode
        });

    const first =
      await boundary.exchangePublic(
        makeExchange()
      );

    const second =
      await boundary.exchangePublic(
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
  "internal issuance and public exchange are POST-only",
  async () => {
    const boundary =
      createBoundary();

    const internal =
      await boundary.issueInternal(
        new Request(
          "https://licensing-v2.internal/internal/installation-authorization"
        )
      );

    const exchange =
      await boundary.exchangePublic(
        new Request(
          "https://public.example.test/installation-authorization/exchange"
        )
      );

    assert.equal(
      internal.status,
      405
    );

    assert.equal(
      exchange.status,
      405
    );

    assert.equal(
      internal.headers.get(
        "allow"
      ),
      "POST"
    );

    assert.equal(
      exchange.headers.get(
        "allow"
      ),
      "POST"
    );
  }
);

test(
  "invalid public authorization never produces a bearer token",
  async () => {
    const boundary =
      createBoundary();

    const response =
      await boundary.exchangePublic(
        publicExchangeRequest({
          authorizationCode:
            "Z".repeat(43)
        })
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
  "boundary construction fails closed for invalid database or signing secret",
  () => {
    assert.throws(
      () =>
        createProductionInstallationAuthorizationBoundary({
          database:
            {},
          identitySecret:
            SECRET
        }),
      /D1-compatible/
    );

    assert.throws(
      () =>
        createProductionInstallationAuthorizationBoundary({
          database:
            createD1(),
          identitySecret:
            "too-short"
        }),
      /identity secret/
    );
  }
);