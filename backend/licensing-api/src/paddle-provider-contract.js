const ENVIRONMENTS = Object.freeze({
  sandbox: {
    apiBaseUrl: "https://sandbox-api.paddle.com"
  },
  production: {
    apiBaseUrl: "https://api.paddle.com"
  }
});

const PADDLE_API_VERSION = "1";

export class PaddleProviderContract {
  constructor({
    environment,
    apiKey,
    fetchImpl = globalThis.fetch
  }) {
    if (!Object.hasOwn(ENVIRONMENTS, environment)) {
      throw new PaddleContractError(
        "invalid_environment",
        "Paddle environment must be sandbox or production."
      );
    }

    validateApiKey(environment, apiKey);

    if (typeof fetchImpl !== "function") {
      throw new PaddleContractError(
        "invalid_fetch",
        "A fetch implementation is required."
      );
    }

    this.environment = environment;
    this.apiKey = apiKey;
    this.fetchImpl = fetchImpl;
  }

  get apiBaseUrl() {
    return ENVIRONMENTS[this.environment].apiBaseUrl;
  }

  buildRequest(path, {
    method = "GET",
    body = undefined,
    headers = {}
  } = {}) {
    const normalizedPath = normalizePath(path);

    const requestHeaders = {
      Authorization: `Bearer ${this.apiKey}`,
      "Paddle-Version": PADDLE_API_VERSION,
      Accept: "application/json",
      ...headers
    };

    let encodedBody;

    if (body !== undefined) {
      requestHeaders["Content-Type"] = "application/json";
      encodedBody = JSON.stringify(body);
    }

    return Object.freeze({
      url: `${this.apiBaseUrl}${normalizedPath}`,
      method: method.toUpperCase(),
      headers: Object.freeze(requestHeaders),
      body: encodedBody
    });
  }

  async request(path, options = {}) {
    const request = this.buildRequest(path, options);

    let response;

    try {
      response = await this.fetchImpl(
        request.url,
        {
          method: request.method,
          headers: request.headers,
          body: request.body
        }
      );
    } catch {
      throw new PaddleContractError(
        "network_failure",
        "Paddle request could not be completed."
      );
    }

    return normalizeResponse(response);
  }
}

export function createPaddleProviderContract({
  environment,
  apiKey,
  fetchImpl
}) {
  return new PaddleProviderContract({
    environment,
    apiKey,
    fetchImpl
  });
}

export function sanitizePaddleError(error) {
  if (error instanceof PaddleContractError) {
    return Object.freeze({
      code: error.code,
      message: error.message,
      retryable: error.retryable
    });
  }

  return Object.freeze({
    code: "provider_failure",
    message: "Payment provider request failed.",
    retryable: false
  });
}

async function normalizeResponse(response) {
  if (
    !response ||
    typeof response.ok !== "boolean" ||
    typeof response.status !== "number"
  ) {
    throw new PaddleContractError(
      "invalid_provider_response",
      "Paddle returned an invalid response."
    );
  }

  const payload = await readJsonSafely(response);

  if (response.ok) {
    return Object.freeze({
      ok: true,
      status: response.status,
      data: payload?.data ?? null,
      meta: payload?.meta ?? null
    });
  }

  const providerCode =
    readProviderErrorCode(payload);

  throw new PaddleContractError(
    mapStatusToCode(response.status),
    "Paddle request failed.",
    {
      status: response.status,
      providerCode,
      retryable: isRetryableStatus(response.status)
    }
  );
}

async function readJsonSafely(response) {
  if (typeof response.text !== "function") {
    throw new PaddleContractError(
      "invalid_provider_response",
      "Paddle returned an invalid response."
    );
  }

  const text = await response.text();

  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    throw new PaddleContractError(
      "invalid_provider_response",
      "Paddle returned malformed JSON."
    );
  }
}

function readProviderErrorCode(payload) {
  const value =
    payload?.error?.code ??
    payload?.code ??
    null;

  return typeof value === "string"
    ? value
    : null;
}

function mapStatusToCode(status) {
  if (status === 400 || status === 422) {
    return "provider_request_rejected";
  }

  if (status === 401 || status === 403) {
    return "provider_authentication_failed";
  }

  if (status === 404) {
    return "provider_resource_not_found";
  }

  if (status === 409) {
    return "provider_conflict";
  }

  if (status === 429) {
    return "provider_rate_limited";
  }

  if (status >= 500) {
    return "provider_unavailable";
  }

  return "provider_failure";
}

function isRetryableStatus(status) {
  return status === 429 || status >= 500;
}

function normalizePath(path) {
  if (
    typeof path !== "string" ||
    path.trim().length === 0
  ) {
    throw new PaddleContractError(
      "invalid_path",
      "Paddle API path is required."
    );
  }

  const value = path.trim();

  if (
    value.startsWith("http://") ||
    value.startsWith("https://")
  ) {
    throw new PaddleContractError(
      "absolute_url_rejected",
      "Absolute Paddle URLs are not accepted."
    );
  }

  if (value.includes("..")) {
    throw new PaddleContractError(
      "unsafe_path",
      "Unsafe Paddle API path was rejected."
    );
  }

  return value.startsWith("/")
    ? value
    : `/${value}`;
}

function validateApiKey(environment, apiKey) {
  if (
    typeof apiKey !== "string" ||
    apiKey.trim().length < 16
  ) {
    throw new PaddleContractError(
      "invalid_api_key",
      "A valid Paddle API key is required."
    );
  }

  const value = apiKey.trim();

  if (
    environment === "sandbox" &&
    !value.includes("_sdbx")
  ) {
    throw new PaddleContractError(
      "environment_key_mismatch",
      "Sandbox requires a sandbox Paddle API key."
    );
  }

  if (
    environment === "production" &&
    value.includes("_sdbx")
  ) {
    throw new PaddleContractError(
      "environment_key_mismatch",
      "Sandbox credentials cannot be used in production."
    );
  }
}

export class PaddleContractError extends Error {
  constructor(
    code,
    message,
    {
      status = null,
      providerCode = null,
      retryable = false
    } = {}
  ) {
    super(message);

    this.name = "PaddleContractError";
    this.code = code;
    this.status = status;
    this.providerCode = providerCode;
    this.retryable = retryable;
  }
}