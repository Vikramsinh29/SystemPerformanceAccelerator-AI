import fs from "node:fs";

import {
  createPaddleSandboxCheckoutRuntime
} from "../src/paddle-sandbox-checkout-runtime.js";

function loadVars(path) {
  const raw = fs.readFileSync(path, "utf8").replace(/^\uFEFF/, "");
  const env = {};

  for (const line of raw.split(/\r?\n/)) {
    const match =
      line.match(/^([A-Z0-9_]+)="(.*)"$/);

    if (match) {
      env[match[1]] = match[2];
    }
  }

  return env;
}

const env =
  loadVars(".dev.vars");

const runtime =
  createPaddleSandboxCheckoutRuntime({
    env,

    fetchImpl: fetch,

    resolveAuthenticatedAccount:
      async () => ({
        accountId:
          "sandbox-checkout-test-001",
        productId:
          "pcspa-pro"
      }),

    resolveTrustedBillingProfile:
      async () => ({
        email:
          "sandbox-checkout@example.com",
        name:
          "PC-SPA Sandbox Checkout",
        countryCode:
          "IN",
        postalCode:
          "110001"
      }),

    idFactory: () =>
      `sandbox-sub-${crypto.randomUUID()}`
  });

const request =
  new Request(
    "https://local.pcspa.test/checkout",
    {
      method: "POST",
      headers: {
        "content-type":
          "application/json"
      },
      body: JSON.stringify({
        billingInterval: "monthly",
        seats: 1
      })
    }
  );

const response =
  await runtime(request);

const body =
  await response.json();

console.log(
  JSON.stringify(
    {
      status: response.status,
      checkoutUrl:
        body.checkoutUrl ?? null,
      error:
        body.error ?? null
    },
    null,
    2
  )
);
