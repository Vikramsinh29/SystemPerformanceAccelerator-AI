import test from "node:test";
import assert from "node:assert/strict";
import {
  handleRequest,
  sanitizeText,
  validateReport
} from "../src/index.js";

function validReport() {
  return {
    schemaVersion: 1,
    applicationVersion: "1.0.0",
    buildIdentifier: "test-build",
    errorReference: "ERR-20260802120000-ABCDEF",
    affectedArea: "Cleaner",
    description: "The scan stopped.",
    expectedResult: "The scan completes.",
    windowsVersion: "Microsoft Windows 10.0.19045",
    runtimeVersion: ".NET 10.0.9",
    isElevated: true,
    installationId: "0123456789abcdef0123456789abcdef",
    diagnosticEvents: []
  };
}

function environment({ rateAllowed = true } = {}) {
  const executed = [];
  const statement = {
    bind(...values) {
      this.values = values;
      return this;
    },
    async run() {
      executed.push(this.values);
      return { success: true };
    }
  };
  return {
    env: {
      FEEDBACK_HASH_SALT: "a-secure-test-salt-that-is-longer-than-32-characters",
      FEEDBACK_RATE_LIMITER: {
        async limit() { return { success: rateAllowed }; }
      },
      FEEDBACK_DB: {
        prepare() { return Object.create(statement); }
      }
    },
    executed
  };
}

test("valid report is accepted and stored without raw installation id", async () => {
  const { env, executed } = environment();
  const report = validReport();
  const response = await handleRequest(new Request("https://example.test/v1/feedback", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(report)
  }), env);

  assert.equal(response.status, 201);
  const body = await response.json();
  assert.match(body.reference, /^PCSPA-\d{8}-[A-F0-9]{10}$/);
  assert.equal(executed.length, 1);
  assert.notEqual(executed[0][13], report.installationId);
  assert.match(executed[0][13], /^[a-f0-9]{64}$/);
});

test("unknown properties and oversized fields are rejected", () => {
  const report = validReport();
  report.description = "x".repeat(2001);
  report.personalFiles = ["secret.txt"];
  const result = validateReport(report);
  assert.equal(result.ok, false);
  assert.deepEqual(result.fields.sort(), ["description", "personalFiles"]);
});

test("personal paths and email addresses are redacted", () => {
  const result = sanitizeText(
    "alice@example.com opened C:\\Users\\Alice\\Documents\\private.txt");
  assert.equal(result, "<redacted-email> opened %USERPROFILE%");
});

test("rate-limited report is not stored", async () => {
  const { env, executed } = environment({ rateAllowed: false });
  const response = await handleRequest(new Request("https://example.test/v1/feedback", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(validReport())
  }), env);
  assert.equal(response.status, 429);
  assert.equal(executed.length, 0);
});

test("non-json and malformed requests fail closed", async () => {
  const { env } = environment();
  const wrongType = await handleRequest(new Request("https://example.test/v1/feedback", {
    method: "POST",
    body: "text"
  }), env);
  assert.equal(wrongType.status, 415);

  const malformed = await handleRequest(new Request("https://example.test/v1/feedback", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: "{invalid"
  }), env);
  assert.equal(malformed.status, 400);
});
