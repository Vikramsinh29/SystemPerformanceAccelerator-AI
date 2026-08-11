export { D1LicensingEventStore } from "./licensing-event-store.js";
export {
  LICENSING_API_VERSION,
  LICENSING_ENVIRONMENTS,
  LICENSING_ROUTES,
  jsonResponse,
  requireEnvironment
} from "./api-contract.js";

export default {
  async fetch() {
    return new Response(JSON.stringify({
      error: "not_deployed",
      message: "Licensing V2 staging boundary is defined, but the HTTP API is intentionally disabled."
    }), {
      status: 503,
      headers: {
        "content-type": "application/json; charset=utf-8",
        "cache-control": "no-store"
      }
    });
  }
};
