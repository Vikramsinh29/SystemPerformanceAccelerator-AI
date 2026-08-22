import assert from "node:assert/strict";
import test from "node:test";

import {
  createInstallationAuthorizationHttpAdapter
} from "../src/installation-authorization-http-adapter.js";

function request(
  url = "https://licensing.example.test/install",
  init = {}
) {
  return new Request(
    url,
    init
  );
}

async function jsonBody(response) {
  return JSON.parse(
    await response.text()
  );
}

test(
  "browser issuance uses trusted resolver identity and ignores request-controlled identity",
  async () => {
    let issuedCommand;

    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => ({
            accountId:
              "acct-trusted",
            productId:
              "pcspa-pro"
          }),

        authorizationService: {
          async issue(command) {
            issuedCommand =
              command;

            return {
              authorizationCode:
                undefined,
              code:
                "A".repeat(43),
              expiresInSeconds:
                300
            };
          },

          async exchange() {
            return {
              authorized: false
            };
          }
        },

        issueProductionToken:
          async () => "unused"
      });

    const response =
      await adapter.issue(
        request(
          "https://licensing.example.test/install/issue",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              accountId:
                "acct-attacker",
              productId:
                "attacker-product"
            })
          }
        )
      );

    assert.equal(
      response.status,
      200
    );

    assert.deepEqual(
      issuedCommand,
      {
        accountId:
          "acct-trusted",
        productId:
          "pcspa-pro"
      }
    );

    const body =
      await jsonBody(response);

    assert.equal(
      body.authorizationCode,
      "A".repeat(43)
    );

    assert.equal(
      body.expiresInSeconds,
      300
    );

    assert.equal(
      Object.hasOwn(body, "accountId"),
      false
    );

    assert.equal(
      Object.hasOwn(body, "productId"),
      false
    );
  }
);

test(
  "browser issuance requires authenticated server identity",
  async () => {
    let issueCalls = 0;

    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {
            issueCalls += 1;
          },

          async exchange() {
            return {
              authorized: false
            };
          }
        },

        issueProductionToken:
          async () => "unused"
      });

    const response =
      await adapter.issue(
        request(
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
      await jsonBody(response),
      {
        error:
          "unauthenticated"
      }
    );

    assert.equal(
      issueCalls,
      0
    );
  }
);

test(
  "browser identity resolver failure fails closed",
  async () => {
    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => {
            throw new Error(
              "session provider unavailable"
            );
          },

        authorizationService: {
          async issue() {
            throw new Error(
              "should not execute"
            );
          },

          async exchange() {
            return {
              authorized: false
            };
          }
        },

        issueProductionToken:
          async () => "unused"
      });

    const response =
      await adapter.issue(
        request(
          "https://licensing.example.test/install/issue",
          {
            method: "POST"
          }
        )
      );

    assert.equal(
      response.status,
      503
    );

    assert.deepEqual(
      await jsonBody(response),
      {
        error:
          "identity_unavailable"
      }
    );
  }
);

test(
  "desktop exchange accepts only opaque code identity and ignores body account overrides",
  async () => {
    let exchangedCode;
    let issuedIdentity;

    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {
            throw new Error(
              "unused"
            );
          },

          async exchange(code) {
            exchangedCode =
              code;

            return {
              authorized: true,
              accountId:
                "acct-bound-in-d1",
              productId:
                "pcspa-pro"
            };
          }
        },

        issueProductionToken:
          async identity => {
            issuedIdentity =
              identity;

            return "pcspa1.payload.signature";
          }
      });

    const code =
      "B".repeat(43);

    const response =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              authorizationCode:
                code,
              accountId:
                "acct-attacker",
              productId:
                "attacker-product"
            })
          }
        )
      );

    assert.equal(
      response.status,
      200
    );

    assert.equal(
      exchangedCode,
      code
    );

    assert.deepEqual(
      issuedIdentity,
      {
        accountId:
          "acct-bound-in-d1",
        productId:
          "pcspa-pro"
      }
    );

    assert.deepEqual(
      await jsonBody(response),
      {
        token:
          "pcspa1.payload.signature",
        tokenType:
          "Bearer",
        expiresInSeconds:
          300
      }
    );
  }
);

test(
  "unknown expired or consumed desktop authorization fails closed without token",
  async () => {
    let tokenCalls = 0;

    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {
            throw new Error(
              "unused"
            );
          },

          async exchange() {
            return {
              authorized: false
            };
          }
        },

        issueProductionToken:
          async () => {
            tokenCalls += 1;
            return "should-not-exist";
          }
      });

    const response =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              authorizationCode:
                "C".repeat(43)
            })
          }
        )
      );

    assert.equal(
      response.status,
      401
    );

    assert.deepEqual(
      await jsonBody(response),
      {
        error:
          "invalid_authorization"
      }
    );

    assert.equal(
      tokenCalls,
      0
    );
  }
);

test(
  "exchange rejects malformed JSON and missing authorization code",
  async () => {
    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {},

          async exchange() {
            throw new Error(
              "should not execute"
            );
          }
        },

        issueProductionToken:
          async () => "unused"
      });

    const malformed =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: "{"
          }
        )
      );

    assert.equal(
      malformed.status,
      400
    );

    assert.deepEqual(
      await jsonBody(malformed),
      {
        error:
          "invalid_json"
      }
    );

    const missing =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              accountId:
                "attacker"
            })
          }
        )
      );

    assert.equal(
      missing.status,
      400
    );
  }
);

test(
  "issuance and exchange are POST-only",
  async () => {
    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {},
          async exchange() {}
        },

        issueProductionToken:
          async () => "unused"
      });

    const issueResponse =
      await adapter.issue(
        request(
          "https://licensing.example.test/install/issue"
        )
      );

    const exchangeResponse =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange"
        )
      );

    assert.equal(
      issueResponse.status,
      405
    );

    assert.equal(
      exchangeResponse.status,
      405
    );

    assert.equal(
      issueResponse.headers.get("allow"),
      "POST"
    );

    assert.equal(
      exchangeResponse.headers.get("allow"),
      "POST"
    );
  }
);

test(
  "token issuer failure fails closed without implementation details",
  async () => {
    const adapter =
      createInstallationAuthorizationHttpAdapter({
        resolveAuthenticatedAccount:
          async () => null,

        authorizationService: {
          async issue() {},

          async exchange() {
            return {
              authorized: true,
              accountId:
                "acct-1",
              productId:
                "pcspa-pro"
            };
          }
        },

        issueProductionToken:
          async () => {
            throw new Error(
              "secret signing internals"
            );
          }
      });

    const response =
      await adapter.exchange(
        request(
          "https://licensing.example.test/install/exchange",
          {
            method: "POST",
            headers: {
              "content-type":
                "application/json"
            },
            body: JSON.stringify({
              authorizationCode:
                "D".repeat(43)
            })
          }
        )
      );

    assert.equal(
      response.status,
      503
    );

    const rawBody =
      await response.text();

    assert.deepEqual(
      JSON.parse(rawBody),
      {
        error:
          "token_unavailable"
      }
    );

    assert.equal(
      rawBody.includes(
        "secret signing internals"
      ),
      false
    );
  }
);