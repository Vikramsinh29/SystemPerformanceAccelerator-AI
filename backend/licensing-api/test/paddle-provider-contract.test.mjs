import assert from "node:assert/strict";
import test from "node:test";

import {
  PaddleContractError,
  PaddleProviderContract,
  sanitizePaddleError
} from "../src/paddle-provider-contract.js";

const sandboxKey =
  "pdl_sdbx_apikey_12345678901234567890";

const productionKey =
  "pdl_live_apikey_12345678901234567890";

function response({
  ok = true,
  status = 200,
  body = {}
} = {}) {
  return {
    ok,
    status,
    async text() {
      return JSON.stringify(body);
    }
  };
}

test("sandbox contract is pinned to sandbox API", () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      fetchImpl() {}
    });

  assert.equal(
    contract.apiBaseUrl,
    "https://sandbox-api.paddle.com"
  );
});

test("production contract is pinned to production API", () => {
  const contract =
    new PaddleProviderContract({
      environment: "production",
      apiKey: productionKey,
      fetchImpl() {}
    });

  assert.equal(
    contract.apiBaseUrl,
    "https://api.paddle.com"
  );
});

test("sandbox credentials cannot cross into production", () => {
  assert.throws(
    () => new PaddleProviderContract({
      environment: "production",
      apiKey: sandboxKey,
      fetchImpl() {}
    }),
    (error) =>
      error instanceof PaddleContractError &&
      error.code === "environment_key_mismatch"
  );
});

test("production-style credentials cannot enter sandbox", () => {
  assert.throws(
    () => new PaddleProviderContract({
      environment: "sandbox",
      apiKey: productionKey,
      fetchImpl() {}
    }),
    (error) =>
      error instanceof PaddleContractError &&
      error.code === "environment_key_mismatch"
  );
});

test("request builder pins authorization and Paddle API version", () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      fetchImpl() {}
    });

  const request =
    contract.buildRequest(
      "/transactions",
      {
        method: "POST",
        body: {
          items: []
        }
      }
    );

  assert.equal(
    request.url,
    "https://sandbox-api.paddle.com/transactions"
  );

  assert.equal(
    request.method,
    "POST"
  );

  assert.equal(
    request.headers.Authorization,
    `Bearer ${sandboxKey}`
  );

  assert.equal(
    request.headers["Paddle-Version"],
    "1"
  );

  assert.equal(
    request.headers["Content-Type"],
    "application/json"
  );
});

test("absolute URLs cannot bypass environment isolation", () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      fetchImpl() {}
    });

  assert.throws(
    () => contract.buildRequest(
      "https://api.paddle.com/transactions"
    ),
    (error) =>
      error instanceof PaddleContractError &&
      error.code === "absolute_url_rejected"
  );
});

test("successful provider response exposes only normalized data", async () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      async fetchImpl() {
        return response({
          body: {
            data: {
              id: "txn_123"
            },
            meta: {
              request_id: "req_1"
            }
          }
        });
      }
    });

  const result =
    await contract.request("/transactions");

  assert.deepEqual(result, {
    ok: true,
    status: 200,
    data: {
      id: "txn_123"
    },
    meta: {
      request_id: "req_1"
    }
  });
});

test("provider authentication errors are sanitized", async () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      async fetchImpl() {
        return response({
          ok: false,
          status: 401,
          body: {
            error: {
              code: "authentication_failed",
              detail: "Sensitive provider detail"
            }
          }
        });
      }
    });

  await assert.rejects(
    () => contract.request("/transactions"),
    (error) => {
      assert.equal(
        error.code,
        "provider_authentication_failed"
      );

      assert.equal(
        error.status,
        401
      );

      assert.equal(
        error.providerCode,
        "authentication_failed"
      );

      assert.doesNotMatch(
        error.message,
        /Sensitive provider detail/
      );

      return true;
    }
  );
});

test("rate limiting is classified retryable", async () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      async fetchImpl() {
        return response({
          ok: false,
          status: 429,
          body: {
            error: {
              code: "too_many_requests"
            }
          }
        });
      }
    });

  await assert.rejects(
    () => contract.request("/transactions"),
    (error) => {
      assert.equal(
        error.code,
        "provider_rate_limited"
      );

      assert.equal(
        error.retryable,
        true
      );

      return true;
    }
  );
});

test("provider 5xx errors are classified retryable", async () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      async fetchImpl() {
        return response({
          ok: false,
          status: 503,
          body: {}
        });
      }
    });

  await assert.rejects(
    () => contract.request("/transactions"),
    (error) => {
      assert.equal(
        error.code,
        "provider_unavailable"
      );

      assert.equal(
        error.retryable,
        true
      );

      return true;
    }
  );
});

test("network failures do not expose underlying exception details", async () => {
  const contract =
    new PaddleProviderContract({
      environment: "sandbox",
      apiKey: sandboxKey,
      async fetchImpl() {
        throw new Error(
          "socket failure with secret detail"
        );
      }
    });

  await assert.rejects(
    () => contract.request("/transactions"),
    (error) => {
      assert.equal(
        error.code,
        "network_failure"
      );

      assert.doesNotMatch(
        error.message,
        /secret detail/
      );

      return true;
    }
  );
});

test("sanitized provider errors never include API credentials", () => {
  const error =
    new PaddleContractError(
      "provider_failure",
      "Payment provider request failed."
    );

  const safe =
    sanitizePaddleError(error);

  const serialized =
    JSON.stringify(safe);

  assert.doesNotMatch(
    serialized,
    /pdl_sdbx/
  );

  assert.deepEqual(safe, {
    code: "provider_failure",
    message: "Payment provider request failed.",
    retryable: false
  });
});