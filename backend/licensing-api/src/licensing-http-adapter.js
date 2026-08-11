const JSON_HEADERS = Object.freeze({
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store"
});

export function createLicensingHttpAdapter({ service, clock = () => new Date().toISOString() }) {
  requireService(service);
  if (typeof clock !== "function") throw new TypeError("clock must be a function.");

  return {
    readAccountLicense(request, identity) {
      return handleRead(service, clock, request, identity);
    },
    activateDevice(request, identity) {
      return handleDeviceMutation(service.activateDevice.bind(service), "POST", clock, request, identity);
    },
    deactivateDevice(request, identity) {
      return handleDeviceMutation(service.deactivateDevice.bind(service), "POST", clock, request, identity);
    },
    validateDevice(request, identity) {
      return handleValidation(service, clock, request, identity);
    }
  };
}

async function handleRead(service, clock, request, identity) {
  const methodError = requireMethod(request, "GET");
  if (methodError) return methodError;

  try {
    const normalized = requireIdentity(identity);
    const result = await service.readAccountLicense({
      ...normalized,
      nowUtc: currentUtc(clock)
    });
    if (!result.found) return json(404, { error: "license_not_found" });
    return json(200, { license: result.license });
  } catch (error) {
    return mapException(error);
  }
}

async function handleDeviceMutation(operation, expectedMethod, clock, request, identity) {
  const methodError = requireMethod(request, expectedMethod);
  if (methodError) return methodError;

  try {
    const normalized = requireIdentity(identity);
    const payload = await readJsonObject(request);
    const result = await operation({
      ...normalized,
      deviceFingerprintHash: requireText(payload.deviceFingerprintHash, "deviceFingerprintHash"),
      ...(payload.deviceLabel == null ? {} : { deviceLabel: payload.deviceLabel }),
      nowUtc: currentUtc(clock)
    });
    return mapMutationResult(result);
  } catch (error) {
    return mapException(error);
  }
}

async function handleValidation(service, clock, request, identity) {
  const methodError = requireMethod(request, "POST");
  if (methodError) return methodError;

  try {
    const normalized = requireIdentity(identity);
    const payload = await readJsonObject(request);
    const result = await service.validateDevice({
      ...normalized,
      deviceFingerprintHash: requireText(payload.deviceFingerprintHash, "deviceFingerprintHash"),
      nowUtc: currentUtc(clock)
    });
    return json(200, result);
  } catch (error) {
    return mapException(error);
  }
}

function mapMutationResult(result) {
  if (result?.ok) return json(200, result);
  const status = statusForDomainCode(result?.code);
  return json(status, {
    error: result?.code ?? "licensing_failure",
    license: result?.license ?? null
  });
}

function statusForDomainCode(code) {
  if (code === "license_not_found") return 404;
  if (code === "seat_limit_or_activation_conflict" || code === "activation_conflict") return 409;
  if (typeof code === "string" && code.startsWith("license_")) return 403;
  return 400;
}

function requireMethod(request, expected) {
  if (!request || typeof request.method !== "string") {
    return json(400, { error: "invalid_request" });
  }
  if (request.method.toUpperCase() !== expected) {
    return new Response(JSON.stringify({ error: "method_not_allowed" }), {
      status: 405,
      headers: { ...JSON_HEADERS, allow: expected }
    });
  }
  return null;
}

async function readJsonObject(request) {
  let payload;
  try {
    payload = await request.json();
  } catch {
    throw new TypeError("request body must be valid JSON.");
  }
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    throw new TypeError("request body must be a JSON object.");
  }
  return payload;
}

function requireIdentity(identity) {
  if (!identity || typeof identity !== "object") throw new TypeError("identity is required.");
  return {
    accountId: requireText(identity.accountId, "accountId"),
    productId: requireText(identity.productId, "productId")
  };
}

function currentUtc(clock) {
  const value = clock();
  if (typeof value !== "string" || Number.isNaN(Date.parse(value))) {
    throw new TypeError("clock must return an ISO-8601 string.");
  }
  return value;
}

function requireText(value, name) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${name} is required.`);
  }
  return value;
}

function requireService(service) {
  for (const name of ["readAccountLicense", "activateDevice", "deactivateDevice", "validateDevice"]) {
    if (typeof service?.[name] !== "function") {
      throw new TypeError(`service.${name} is required.`);
    }
  }
}

function mapException(error) {
  if (error instanceof TypeError) return json(400, { error: "invalid_request" });
  return json(500, { error: "internal_error" });
}

function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: JSON_HEADERS
  });
}
