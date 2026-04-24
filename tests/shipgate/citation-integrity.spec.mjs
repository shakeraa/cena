// =============================================================================
// tests/shipgate/citation-integrity.spec.mjs
//
// Run with: node --test tests/shipgate/citation-integrity.spec.mjs
//
// Asserts:
//   1. The citation-integrity cross-reference scanner detects unknown
//      citation_id= references against the approved-citations manifest.
//   2. The scanner detects claims whose numeric effect-size exceeds the
//      manifest's max_cited_es bound.
//   3. Valid citation_id references with in-bound numeric claims produce
//      zero violations.
//   4. The full-repo scan remains clean (prevents regression from an
//      engineer accidentally introducing an unknown citation_id).
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/citation-integrity-scan.mjs");
const MANIFEST = resolve(ROOT, "contracts/citations/approved-citations.yml");

function runScanner(extraArgs = []) {
  try {
    const out = execFileSync(process.execPath, [SCANNER, ...extraArgs, "--json"], {
      encoding: "utf8",
      cwd: ROOT,
    });
    return { exitCode: 0, stdout: out };
  } catch (err) {
    return { exitCode: err.status ?? 1, stdout: err.stdout?.toString() || "" };
  }
}

test("citation-integrity: fixture triggers unknown-citation violation", () => {
  const result = runScanner(["--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const unknown = report.violations.filter((v) => v.kind === "unknown-citation");
  assert.ok(unknown.length >= 1, "at least one unknown-citation violation must fire");
  assert.equal(unknown[0].citation_id, "fabricated-et-al-3000");
});

test("citation-integrity: fixture triggers exceeds-max-es violation", () => {
  const result = runScanner(["--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  const over = report.violations.filter((v) => v.kind === "exceeds-max-es");
  assert.ok(over.length >= 1, "at least one exceeds-max-es violation must fire");
  assert.equal(over[0].citation_id, "brummair-richter-2019-interleaving");
  assert.ok(over[0].claimValue > over[0].max, "claimValue must exceed max");
});

test("citation-integrity: manifest loads and contains required entries", () => {
  assert.ok(existsSync(MANIFEST), "approved-citations.yml must exist");
  const result = runScanner(["--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  assert.ok(report.manifestEntries >= 5, "manifest must seed at least 5 entries");
});

test("citation-integrity: valid citations in fixture do NOT produce violations for Case 3 lines", () => {
  // Case 3 of the fixture uses two valid citations with in-bound claims.
  // Those lines must not appear in the violations list.
  const result = runScanner(["--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  const case3Line28 = report.violations.find((v) => v.line === 28); // meta-analytic mean line
  const case3Line31 = report.violations.find((v) => v.line === 31); // formative range line
  assert.equal(case3Line28, undefined, "valid citation on line 28 must not violate");
  assert.equal(case3Line31, undefined, "valid citation on line 31 must not violate");
});

test("citation-integrity: full-repo scan exits clean", () => {
  const result = runScanner([]);
  assert.equal(result.exitCode, 0, "full-repo scan must exit 0 (no violations)");
  const report = JSON.parse(result.stdout);
  assert.equal(report.totalViolations, 0, "no violations across repo");
});
