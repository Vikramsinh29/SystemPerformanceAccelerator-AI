export { D1LicensingEventStore } from "./licensing-event-store.js";
export { D1DeviceActivationStore } from "./device-activation-store.js";

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
