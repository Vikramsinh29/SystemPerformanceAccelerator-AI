import { createProductionLicensingRuntime } from "./production-licensing-runtime.js";
import { createProductionLicensingRouter } from "./production-licensing-router.js";
import {
  createProductionInternalTokenAcquisitionHandler,
  isProductionInternalTokenAcquisitionRequest
} from "./production-internal-token-acquisition.js";
import {
  createProductionInstallationAuthorizationBoundary,
  isProductionInstallationAuthorizationExchangeRequest,
  isProductionInternalInstallationAuthorizationRequest
} from "./production-installation-authorization-boundary.js";

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

    /*
     * Existing trusted internal token acquisition remains
     * unchanged and continues to use its exact private
     * service-binding URL.
     */
    if (isProductionInternalTokenAcquisitionRequest(request)) {
      let internalHandler;
      try {
        internalHandler =
          createProductionInternalTokenAcquisitionHandler({
            identitySecret:
              env?.LICENSING_IDENTITY_SECRET
          });
      } catch {
        return json(503, {
          error: "production_not_ready",
          message: "Licensing V2 production runtime is not ready."
        });
      }

      return internalHandler.fetch(request);
    }

    /*
     * Installation authorization has two deliberately
     * different trust boundaries:
     *
     * 1. issuance is accepted ONLY on the exact private
     *    service-binding hostname;
     *
     * 2. exchange is accepted ONLY on the public HTTPS
     *    exchange path.
     *
     * A public request can therefore never supply trusted
     * accountId/productId values to the issuance handler.
     */
    if (
      isProductionInternalInstallationAuthorizationRequest(request) ||
      isProductionInstallationAuthorizationExchangeRequest(request)
    ) {
      let installationAuthorization;

      try {
        installationAuthorization =
          createProductionInstallationAuthorizationBoundary({
            database:
              env?.LICENSING_DB,
            identitySecret:
              env?.LICENSING_IDENTITY_SECRET
          });
      } catch {
        return json(503, {
          error: "production_not_ready",
          message: "Licensing V2 production runtime is not ready."
        });
      }

      if (
        isProductionInternalInstallationAuthorizationRequest(
          request
        )
      ) {
        return installationAuthorization.issueInternal(
          request
        );
      }

      return installationAuthorization.exchangePublic(
        request
      );
    }

    /*
     * Existing public licensing operations remain on the
     * unchanged production router.
     */
    const router =
      createProductionLicensingRouter({
        runtime
      });

    return router.fetch(request);
  }
};

function json(status, body) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers: JSON_HEADERS
    }
  );
}