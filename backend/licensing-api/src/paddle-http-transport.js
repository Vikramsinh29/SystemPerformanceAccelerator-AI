const ALLOWED_HOSTS = new Set([
  "sandbox-api.paddle.com",
  "api.paddle.com"
]);

export class PaddleTransportError extends Error {
  constructor(code, message, options = {}) {
    super(message);

    this.name = "PaddleTransportError";
    this.code = code;
    this.status = options.status ?? null;
    this.requestId = options.requestId ?? null;
    this.retryable = options.retryable ?? false;
  }
}

export function createPaddleHttpTransport({
  fetchImpl
}) {
  if (typeof fetchImpl !== "function") {
    throw new PaddleTransportError(
      "invalid_transport",
      "A fetch implementation is required."
    );
  }

  return Object.freeze({
    async send(request) {
      validateRequest(request);

      let response;

      try {
        response = await fetchImpl(
          request.url,
          {
            method: request.method,
            headers: request.headers,
            body: request.body
          }
        );
      } catch {
        throw new PaddleTransportError(
          "transport_failure",
          "Paddle request could not be completed.",
          {
            retryable: false
          }
        );
      }

      return normalizeResponse(response);
    }
  });
}

function validateRequest(request) {
  if (
    !request ||
    typeof request !== "object"
  ) {
    throw new PaddleTransportError(
      "invalid_request",
      "Paddle request is invalid."
    );
  }

  if (request.method !== "POST") {
    throw new PaddleTransportError(
      "invalid_method",
      "Paddle transaction request must use POST."
    );
  }

  let parsed;

  try {
    parsed = new URL(request.url);
  } catch {
    throw new PaddleTransportError(
      "invalid_url",
      "Paddle request URL is invalid."
    );
  }

  if (
    parsed.protocol !== "https:" ||
    !ALLOWED_HOSTS.has(parsed.hostname)
  ) {
    throw new PaddleTransportError(
      "invalid_url",
      "Paddle request destination is not allowed."
    );
  }

  if (parsed.pathname !== "/transactions") {
    throw new PaddleTransportError(
      "invalid_path",
      "Paddle request path is not allowed."
    );
  }

  if (
    typeof request.headers?.Authorization !== "string" ||
    !request.headers.Authorization.startsWith("Bearer ")
  ) {
    throw new PaddleTransportError(
      "missing_authorization",
      "Paddle authorization is missing."
    );
  }

  if (typeof request.body !== "string") {
    throw new PaddleTransportError(
      "invalid_body",
      "Paddle request body must be serialized JSON."
    );
  }
}

async function normalizeResponse(response) {
  if (
    !response ||
    typeof response.status !== "number" ||
    typeof response.text !== "function"
  ) {
    throw new PaddleTransportError(
      "invalid_response",
      "Paddle returned an invalid response."
    );
  }

  let raw;

  try {
    raw = await response.text();
  } catch {
    throw new PaddleTransportError(
      "response_read_failure",
      "Paddle response could not be read."
    );
  }

  let json = null;

  if (raw.length > 0) {
    try {
      json = JSON.parse(raw);
    } catch {
      throw new PaddleTransportError(
        "invalid_json",
        "Paddle returned malformed JSON.",
        {
          status: response.status
        }
      );
    }
  }

  return Object.freeze({
    status: response.status,
    json
  });
}