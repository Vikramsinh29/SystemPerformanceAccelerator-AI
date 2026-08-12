import { createProductionLicensingRuntime } from "./production-licensing-runtime.js";
import { createProductionLicensingRouter } from "./production-licensing-router.js";
import {
  createProductionInternalTokenAcquisitionHandler,
  isProductionInternalTokenAcquisitionRequest
} from "./production-internal-token-acquisition.js";

const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

const ENABLED_VALUE = "enabled";

export default {
  async fetch(request, env) {
    if (env?.PRODUCTION_LICENSING_ENABLED !== ENABLED_VALUE) {
      return json(503, {
        error: "production_not_enabled",
        message: "Licensing V2 production runtime is configured but not enabled."
      });
    }

    let runtime;
    try {
      runtime = createProductionLicensingRuntime({
        env,
        identitySecret: env?.LICENSING_IDENTITY_SECRET
      });
    } catch {
      return json(503, {
        error: "production_not_ready",
        message: "Licensing V2 production runtime is not ready."
      });
    }

    if (isProductionInternalTokenAcquisitionRequest(request)) {
      let internalHandler;
      try {
        internalHandler = createProductionInternalTokenAcquisitionHandler({
          identitySecret: env?.LICENSING_IDENTITY_SECRET
        });
      } catch {
        return json(503, {
          error: "production_not_ready",
          message: "Licensing V2 production runtime is not ready."
        });
      }
      return internalHandler.fetch(request);
    }

    const router = createProductionLicensingRouter({ runtime });
    return router.fetch(request);
  }
};

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
