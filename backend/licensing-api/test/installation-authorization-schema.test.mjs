import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here =
  path.dirname(fileURLToPath(import.meta.url));

const migrationPath =
  path.resolve(
    here,
    "../migrations/0005_installation_authorizations.sql"
  );

const sql =
  fs.readFileSync(migrationPath, "utf8");

test(
  "installation authorization migration stores only code hashes",
  () => {
    assert.match(
      sql,
      /code_sha256 TEXT NOT NULL UNIQUE/
    );

    assert.doesNotMatch(
      sql,
      /\bcode_plaintext\b/i
    );

    assert.doesNotMatch(
      sql,
      /\bauthorization_code TEXT\b/i
    );
  }
);

test(
  "installation authorization migration binds trusted identity and expiry",
  () => {
    assert.match(
      sql,
      /account_id TEXT NOT NULL/
    );

    assert.match(
      sql,
      /product_id TEXT NOT NULL/
    );

    assert.match(
      sql,
      /created_utc TEXT NOT NULL/
    );

    assert.match(
      sql,
      /expires_utc TEXT NOT NULL/
    );

    assert.match(
      sql,
      /consumed_utc TEXT/
    );
  }
);

test(
  "installation authorization migration supports single-use consumption",
  () => {
    assert.match(
      sql,
      /consumed_utc/
    );

    assert.match(
      sql,
      /UNIQUE/
    );
  }
);