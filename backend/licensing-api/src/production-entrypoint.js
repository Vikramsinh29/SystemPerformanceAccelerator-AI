const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

export default {
  async fetch() {
    return new Response(JSON.stringify({
      error: "production_not_enabled",
      message: "Licensing V2 production runtime is configured but not enabled."
    }), {
      status: 503,
      headers: JSON_HEADERS
    });
  }
};
