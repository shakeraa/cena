// =============================================================================
// tests/shipgate/epic-prr-d-combo-full-repo-scan.spec.mjs
//
// Run with: node --test tests/shipgate/epic-prr-d-combo-full-repo-scan.spec.mjs
//
// EPIC-PRR-D 2nd-wave combo integration test (prr-073/091/142/144/153).
//
// Asserts:
//   1. Full-repo scan (no --pack filter, no --fixture-mode) exits 0 on
//      the current repository. Pre-existing hits that predate this epic
//      must be whitelisted; any NEW violation fails CI.
//   2. Fixture-mode scan across ALL packs exits 1 and produces violations
//      in every one of the five new packs. This is the belt-and-braces
//      check against a whitelist that accidentally masks the fixture
//      itself (which would hide the "every rule fires" assertion).
//   3. Each of the five new packs is registered in the scanner's PACKS
//      array.
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { readFileSync, writeFileSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");
const SCANNER_SOURCE = readFileSync(SCANNER, "utf8");

const NEW_PACKS = [
  "therapeutic-claims",
  "progress-framing",
  "error-blame",
  "cheating-alert",
  "reward-emoji",
];

function runScanner(extraArgs = []) {
  // Route stdout to a temp file because spawnSync's default pipe buffer
  // truncates large JSON outputs (~64KB) on macOS even when maxBuffer is
  // raised. The fixture-mode full-repo report is ~150KB so we must spill
  // through a real file descriptor.
  const dir = mkdtempSync(resolve(tmpdir(), "shipgate-scan-"));
  const outPath = resolve(dir, "out.json");
  try {
    const res = spawnSync(
      "sh",
      ["-c", `${JSON.stringify(process.execPath)} ${JSON.stringify(SCANNER)} ${extraArgs.concat(["--json"]).map((a) => JSON.stringify(a)).join(" ")} > ${JSON.stringify(outPath)}`],
      { encoding: "utf8", cwd: ROOT },
    );
    const stdout = (() => {
      try { return readFileSync(outPath, "utf8"); } catch { return ""; }
    })();
    return { exitCode: res.status ?? 1, stdout };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test("epic-prr-d-combo: scanner registers all five new packs", () => {
  for (const pack of NEW_PACKS) {
    assert.ok(
      SCANNER_SOURCE.includes(`name: "${pack}"`),
      `scripts/shipgate/rulepack-scan.mjs must register pack '${pack}' in the PACKS array`,
    );
  }
});

test("epic-prr-d-combo: scanner registers a fixture for every new pack", () => {
  for (const pack of NEW_PACKS) {
    assert.ok(
      SCANNER_SOURCE.includes(`"${pack}":`),
      `scripts/shipgate/rulepack-scan.mjs must map pack '${pack}' to a FIXTURE_FILES entry`,
    );
  }
});

test("epic-prr-d-combo: full-repo scan exits 0 on the current repository", () => {
  const result = runScanner([]);
  if (result.exitCode !== 0) {
    // Include a short summary for debugging — failures here indicate a
    // NEW violation (non-whitelisted) has landed, which is the intended
    // failure mode of the scanner.
    const report = tryParseJson(result.stdout);
    const newHits = report?.packs
      ?.filter((p) => NEW_PACKS.includes(p.name))
      .flatMap((p) => p.violations.map((v) => ({
        pack: p.name,
        rule: v.ruleId,
        file: v.file,
        line: v.line,
      }))) ?? [];
    assert.fail(
      `Full-repo scan exited ${result.exitCode}. New-pack violations (new-pack only, not pre-existing packs):\n` +
      newHits.slice(0, 25).map((h) => `  [${h.pack}/${h.rule}] ${h.file}:${h.line}`).join("\n"),
    );
  }
});

test("epic-prr-d-combo: fixture-mode across all packs produces violations for every new pack", () => {
  const result = runScanner(["--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture-mode full scan must exit 1 (all fixtures are traps)");

  const report = JSON.parse(result.stdout);
  for (const packName of NEW_PACKS) {
    const pack = report.packs.find((p) => p.name === packName);
    assert.ok(pack, `pack '${packName}' is present in the scanner report`);
    assert.ok(
      pack.violations.length > 0,
      `pack '${packName}' produced zero fixture violations — the fixture may be misnamed or whitelisted. Check shipgate/fixtures/ and the FIXTURE_FILES map in rulepack-scan.mjs.`,
    );
  }
});

test("epic-prr-d-combo: fixture-mode coverage — every rule in every new pack fires at least once", () => {
  const result = runScanner(["--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  for (const packName of NEW_PACKS) {
    const pack = report.packs.find((p) => p.name === packName);
    const firedIds = new Set(pack.violations.map((v) => v.ruleId));
    assert.equal(
      firedIds.size,
      pack.ruleCount,
      `pack '${packName}': ${firedIds.size} of ${pack.ruleCount} rules fired in fixture mode. ` +
      `Every rule must have a trap line in its fixture.`,
    );
  }
});

// ---------------------------------------------------------------------------

function tryParseJson(s) {
  try { return JSON.parse(s); } catch { return null; }
}
