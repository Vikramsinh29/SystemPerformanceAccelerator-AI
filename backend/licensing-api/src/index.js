export { D1LicensingEventStore } from "./licensing-event-store.js";
export { D1DeviceActivationStore } from "./device-activation-store.js";
export { LicensingCompatibilityService } from "./licensing-compatibility-service.js";
export { createLicensingHttpAdapter } from "./licensing-http-adapter.js";
export { createLicensingComposition } from "./licensing-composition.js";
export { LicensingIdentityError, createLicensingIdentityBridge } from "./licensing-identity-bridge.js";
export { LICENSING_RUNTIME_OPERATIONS, createLicensingWorkerRuntime } from "./licensing-worker-runtime.js";
export { createLicensingStagingRouter } from "./licensing-staging-router.js";
export { ProductionIdentityError, createProductionIdentityResolver } from "./production-identity-verifier.js";
export { createProductionTokenIssuer } from "./production-token-issuer.js";
export { createProductionLicensingRuntime } from "./production-licensing-runtime.js";
export { createProductionLicensingRouter } from "./production-licensing-router.js";

export default {
  async fetch() {
    return new Response(JSON.stringify({
      error: "not_deployed",
      message: "Licensing V2 durable storage is not an HTTP API yet."
    }), {
      status: 503,
      headers: {
        "content-type": "application/json; charset=utf-8",
        "cache-control": "no-store"
      }
    });
  }
};
