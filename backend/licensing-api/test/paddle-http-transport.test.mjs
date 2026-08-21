import assert from "node:assert/strict";
import test from "node:test";

import {
  PaddleTransportError,
  createPaddleHttpTransport
} from "../src/paddle-http-transport.js";

function request() {
  return {
    method: "POST",
    url:
      "https://sandbox-api.paddle.com/transactions",
    headers: {
      Authorization:
        "Bearer fake-secret"
    },
    body: "{}"
  };
}

test("transport delegates valid Paddle request to injected fetch", async () => {
  let observed = null;

  const transport =
    createPaddleHttpTransport({
      fetchImpl:
        async (url, options) => {
          observed = {
            url,
            options
          };

          return {
            status: 201,
            async text() {
              return JSON.stringify({
                data: {}
              });
            }
          };
        }
    });

  const response =
    await transport.send(
      request()
    );

  assert.equal(
    observed.url,
    "https://sandbox-api.paddle.com/transactions"
  );

  assert.equal(
    observed.options.method,
    "POST"
  );

  assert.equal(
    response.status,
    201
  );
});

test("transport refuses non-Paddle destination", async () => {
  const transport =
    createPaddleHttpTransport({
      fetchImpl:
        async () => {
          throw new Error(
            "must not execute"
          );
        }
    });

  await assert.rejects(
    transport.send({
      ...request(),
      url:
        "https://example.com/transactions"
    }),

    (error) =>
      error instanceof
        PaddleTransportError &&
      error.code ===
        "invalid_url"
  );
});

test("transport refuses HTTP destination", async () => {
  const transport =
    createPaddleHttpTransport({
      fetchImpl: async () => {}
    });

  await assert.rejects(
    transport.send({
      ...request(),
      url:
        "http://sandbox-api.paddle.com/transactions"
    }),

    (error) =>
      error.code ===
        "invalid_url"
  );
});

test("transport refuses unexpected Paddle path", async () => {
  const transport =
    createPaddleHttpTransport({
      fetchImpl: async () => {}
    });

  await assert.rejects(
    transport.send({
      ...request(),
      url:
        "https://sandbox-api.paddle.com/customers"
    }),

    (error) =>
      error.code ===
        "invalid_path"
  );
});

test("transport converts network exception into sanitized failure", async () => {
  const transport =
    createPaddleHttpTransport({
      fetchImpl:
        async () => {
          throw new Error(
            "SECRET INTERNAL DETAIL"
          );
        }
    });

  await assert.rejects(
    transport.send(
      request()
    ),

    (error) => {
      assert.equal(
        error.code,
        "transport_failure"
      );

      assert.equal(
        error.retryable,
        false
      );

      assert.doesNotMatch(
        error.message,
        /SECRET/
      );

      return true;
    }
  );
});

test("transport rejects malformed provider JSON", async () => {
  const transport =
    createPaddleHttpTransport({
      fetchImpl:
        async () => ({
          status: 201,

          async text() {
            return "{broken";
          }
        })
    });

  await assert.rejects(
    transport.send(
      request()
    ),

    (error) =>
      error.code ===
        "invalid_json"
  );
});