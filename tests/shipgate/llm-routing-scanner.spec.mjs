// =============================================================================
// tests/shipgate/llm-routing-scanner.spec.mjs
//
// Run with: node --test tests/shipgate/llm-routing-scanner.spec.mjs
//
// Asserts (ADR-0026, prr-004):
//   1. The positive-test fixture (shipgate/fixtures/llm-routing/) produces at
//      least one violation — i.e. the untagged sample fires.
//   2. The tagged sample in the same fixture dir does NOT fire.
//   3. Advisory mode never exits non-zero, even when violations are present.
//   4. Strict mode exits 1 when violations are present.
//   5. The allowlist YAML loads cleanly (no config error exit 2).
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/llm-routing-scanner.mjs");

function run(extra = []) {
  try {
    const out = execFileSync(process.execPath, [SCANNER, ...extra], {
      encoding: "utf8",
      cwd: ROOT,
    });
    return { exitCode: 0, stdout: out };
  } catch (err) {
    return { exitCode: err.status ?? 1, stdout: err.stdout?.toString() || "" };
  }
}

test("llm-routing-scanner: strict fixture scan exits 1 when untagged sample present", () => {
  const result = run(["--fixture-mode", "--strict", "--json"]);
  assert.equal(result.exitCode, 1, "fixture with untagged sample must fail strict");
  const report = JSON.parse(result.stdout);
  assert.ok(report.violations.length >= 1, "at least one violation expected");
  const v = report.violations.find((x) => /violation-sample/.test(x.file));
  assert.ok(v, "untagged fixture file surfaced as a violation");
});

test("llm-routing-scanner: strict fixture scan does NOT flag the tagged sample", () => {
  const result = run(["--fixture-mode", "--strict", "--json"]);
  const report = JSON.parse(result.stdout);
  const tagged = report.violations.find((x) => /tagged-sample/.test(x.file));
  assert.equal(tagged, undefined, "tagged fixture file must not produce a violation");
});

test("llm-routing-scanner: advisory mode exits 0 even with violations", () => {
  const result = run(["--fixture-mode", "--advisory", "--json"]);
  assert.equal(result.exitCode, 0, "advisory mode must never non-zero exit");
});

test("llm-routing-scanner: allowlist loads without config error", () => {
  // Run against real src/ — any exit code is fine except 2 (config error).
  const result = run(["--advisory", "--quiet"]);
  assert.notEqual(result.exitCode, 2, "allowlist YAML must parse cleanly");
});
