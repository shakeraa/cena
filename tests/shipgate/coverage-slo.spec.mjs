// =============================================================================
// tests/shipgate/coverage-slo.spec.mjs
//
// Run with: node --test tests/shipgate/coverage-slo.spec.mjs
//
// Asserts the prr-210 coverage SLO ship-gate script behaves correctly across
// its three load-bearing paths:
//   1. Clean snapshot (every active cell meets N) → exit 0.
//   2. Crafted under-target snapshot → exit 1 and the failing cell shows up.
//   3. Missing snapshot without --allow-empty → exit 1 with LOUD error
//      (never a silent pass).
//   4. Missing snapshot with --allow-empty → exit 1 because required N > 0.
//   5. Malformed JSON snapshot → exit 3.
//   6. Report is written to the path passed via --report.
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { readFileSync, writeFileSync, mkdtempSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCRIPT = resolve(ROOT, "scripts/shipgate/coverage-slo.mjs");
const TARGETS = resolve(ROOT, "contracts/coverage/coverage-targets.yml");
const FIXTURE_UNDER = resolve(ROOT, "scripts/shipgate/fixtures/coverage-snapshot-under-target.json");
const FIXTURE_GREEN = resolve(ROOT, "scripts/shipgate/fixtures/coverage-snapshot-all-green.json");

function run(args) {
  try {
    const stdout = execFileSync(process.execPath, [SCRIPT, ...args], {
      cwd: ROOT,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { exitCode: 0, stdout, stderr: "" };
  } catch (err) {
    return {
      exitCode: err.status ?? 1,
      stdout: err.stdout?.toString() ?? "",
      stderr: err.stderr?.toString() ?? "",
    };
  }
}

test("coverage-slo: all-green fixture → exit 0", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", FIXTURE_GREEN,
    "--report", report,
  ]);
  assert.equal(r.exitCode, 0, `expected exit 0, got ${r.exitCode}. stderr=${r.stderr}`);
  assert.ok(existsSync(report), "report file must be written");
  const body = readFileSync(report, "utf8");
  assert.match(body, /Active failing\*\*: 0/);
});

test("coverage-slo: under-target fixture → exit 1 with gap listed", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", FIXTURE_UNDER,
    "--report", report,
  ]);
  assert.equal(r.exitCode, 1, "crafted under-target fixture MUST exit 1");
  assert.match(r.stderr, /below SLO/i);
  assert.match(r.stderr, /have=3\s+need=10\s+gap=7/);
  const body = readFileSync(report, "utf8");
  assert.match(body, /Failing \(gating\)/);
  assert.match(body, /algebra\.linear_equations.*Easy.*Halabi/);
});

test("coverage-slo: missing snapshot without --allow-empty → LOUD exit 1", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", resolve(tmp, "does-not-exist.json"),
    "--report", report,
  ]);
  assert.equal(r.exitCode, 1, "missing snapshot must fail (not silent-pass)");
  assert.match(r.stderr, /snapshot missing/i);
  assert.match(r.stderr, /loud/i);
});

test("coverage-slo: missing snapshot with --allow-empty still fails when N>0", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", resolve(tmp, "does-not-exist.json"),
    "--report", report,
    "--allow-empty",
  ]);
  assert.equal(r.exitCode, 1, "--allow-empty still fails because every active cell has required N > 0");
  // But the warning should say we're treating missing as zero.
  assert.match(r.stderr + r.stdout, /--allow-empty/);
});

test("coverage-slo: malformed JSON snapshot → exit 3", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const bad = resolve(tmp, "bad-snapshot.json");
  writeFileSync(bad, "{ not json", "utf8");
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", bad,
    "--report", report,
  ]);
  assert.equal(r.exitCode, 3, "malformed JSON must exit 3");
  assert.match(r.stderr, /parse error/i);
});

test("coverage-slo: --json emits machine-readable verdict", () => {
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", TARGETS,
    "--snapshot", FIXTURE_UNDER,
    "--report", report,
    "--json",
  ]);
  assert.equal(r.exitCode, 1);
  const doc = JSON.parse(r.stdout);
  assert.equal(doc.exitCode, 1);
  assert.equal(typeof doc.totalCells, "number");
  assert.ok(doc.failingActive.length >= 1);
  assert.equal(doc.failingActive[0].required, 10);
});

test("coverage-slo: defaults are applied when cell has no 'min' override", () => {
  // Craft targets on the fly with no per-cell min — default N=5 should apply.
  const tmp = mkdtempSync(resolve(tmpdir(), "prr210-"));
  const targets = resolve(tmp, "targets.yml");
  writeFileSync(
    targets,
    [
      "version: 1",
      "defaults:",
      "  global:",
      "    min: 5",
      "  methodology:",
      "    Halabi: 5",
      "  questionType:",
      "    step-solver: 5",
      "cells:",
      "  - topic: algebra.linear_equations",
      "    difficulty: Easy",
      "    methodology: Halabi",
      "    track: FourUnit",
      "    questionType: step-solver",
      "    active: true",
      "",
    ].join("\n"),
    "utf8",
  );
  const snapshot = resolve(tmp, "snap.json");
  writeFileSync(
    snapshot,
    JSON.stringify({
      schemaVersion: "1.0",
      source: "spec",
      cells: [{
        topic: "algebra.linear_equations",
        difficulty: "Easy",
        methodology: "Halabi",
        track: "FourUnit",
        questionType: "step-solver",
        language: "en",
        variantCount: 2,
        belowSlo: true,
        curatorTaskId: null,
      }],
    }),
    "utf8",
  );
  const report = resolve(tmp, "report.md");
  const r = run([
    "--targets", targets,
    "--snapshot", snapshot,
    "--report", report,
    "--json",
  ]);
  assert.equal(r.exitCode, 1);
  const doc = JSON.parse(r.stdout);
  // Default should have kicked in → required=5.
  assert.equal(doc.failingActive[0].required, 5);
  assert.equal(doc.failingActive[0].variantCount, 2);
  assert.equal(doc.failingActive[0].gap, 3);
});
