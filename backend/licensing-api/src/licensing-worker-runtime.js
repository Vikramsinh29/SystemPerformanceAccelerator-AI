import { createLicensingComposition } from "./licensing-composition.js";
import { LicensingIdentityError, createLicensingIdentityBridge } from "./licensing-identity-bridge.js";

const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

export const LICENSING_RUNTIME_OPERATIONS = Object.freeze({
  READ_ACCOUNT_LICENSE: "readAccountLicense",
  ACTIVATE_DEVICE: "activateDevice",
  DEACTIVATE_DEVICE: "deactivateDevice",
  VALIDATE_DEVICE: "validateDevice"
});

export function createLicensingWorkerRuntime({
  env,
  resolveAuthenticatedAccount,
  productId,
  idFactory,
  clock
} = {}) {
  const database = requireDatabaseBinding(env);
  const composition = createLicensingComposition({
    database,
    ...(idFactory === undefined ? {} : { idFactory }),
    ...(clock === undefined ? {} : { clock })
  });
  const identityBridge = createLicensingIdentityBridge({
    resolveAuthenticatedAccount,
    ...(productId === undefined ? {} : { productId })
  });

  return Object.freeze({
    composition,
    identityBridge,
    async handle(operation, request) {
      const handler = operationHandler(composition.adapter, operation);
      let identity;
      try {
        identity = await identityBridge.resolve(request);
      } catch (error) {
        if (error instanceof LicensingIdentityError && error.code === "unauthenticated") {
          return json(401, { error: "unauthenticated" });
        }
        return json(500, { error: "identity_resolution_failed" });
      }
      return handler(request, identity);
    }
  });
}

function requireDatabaseBinding(env) {
  if (!env || typeof env !== "object") {
    throw new TypeError("env is required.");
  }
  if (!env.LICENSING_DB?.prepare || !env.LICENSING_DB?.batch) {
    throw new TypeError("env.LICENSING_DB must be a D1-compatible database binding.");
  }
  return env.LICENSING_DB;
}

function operationHandler(adapter, operation) {
  if (!Object.values(LICENSING_RUNTIME_OPERATIONS).includes(operation)) {
    throw new TypeError("Unsupported licensing runtime operation.");
  }
  return adapter[operation].bind(adapter);
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
