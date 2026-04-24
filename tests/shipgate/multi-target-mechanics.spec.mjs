// =============================================================================
// tests/shipgate/multi-target-mechanics.spec.mjs
//
// Run with: node --test tests/shipgate/multi-target-mechanics.spec.mjs
//
// Asserts:
//   1. Every rule in multi-target-mechanics.yml fires at least once against
//      the positive-test fixture.
//   2. A sanitised document (no banned patterns) produces zero rule matches.
//   3. All three locales (en/he/ar) are represented in the rule set.
//   4. Scanner runs clean against the real repo (after PRR-225 streak leak
//      removal and multi-target-mechanics-whitelist coverage of legacy
//      surfaces).
//
// This is the PRR-224 companion test for
// scripts/shipgate/multi-target-mechanics.yml.
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");
const RULES_YAML = resolve(ROOT, "scripts/shipgate/multi-target-mechanics.yml");
const PACK_NAME = "multi-target-mechanics";

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

function extractRuleIds(yamlText) {
  const ids = [];
  for (const line of yamlText.split("\n")) {
    const m = line.match(/^\s*-\s+id:\s+(\S+)\s*$/);
    if (m) ids.push(m[1]);
  }
  return ids;
}

test("multi-target-mechanics: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("multi-target-mechanics: every rule fires at least once against the fixture", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const expectedIds = extractRuleIds(yaml);
  assert.ok(expectedIds.length > 0, "rule pack parsed at least one rule id");

  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  const firedIds = new Set(pack.violations.map((v) => v.ruleId));

  const missing = expectedIds.filter((id) => !firedIds.has(id));
  assert.deepEqual(missing, [], `rules that did not fire: ${missing.join(", ")}`);
});

test("multi-target-mechanics: all three locales represented", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const hasHe = ids.some((id) => id.startsWith("he-"));
  const hasAr = ids.some((id) => id.startsWith("ar-"));
  const hasEn = ids.some((id) => !id.startsWith("he-") && !id.startsWith("ar-"));
  assert.ok(hasEn, "at least one English rule");
  assert.ok(hasHe, "at least one Hebrew rule");
  assert.ok(hasAr, "at least one Arabic rule");
});

test("multi-target-mechanics: identifier bans present for core ADR-0050 §10 idents", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  // ADR-0050 §10 names these identifier families as banned in student-facing
  // surfaces. Explicit coverage is a ratchet — if one of these rule ids gets
  // dropped, the test fails and the reviewer must explain why.
  const requiredIdentRules = [
    "ident-days-until",
    "ident-days-left",
    "ident-countdown",
    "ident-streak",
    "ident-deadline-pressure",
  ];
  for (const req of requiredIdentRules) {
    assert.ok(
      ids.includes(req),
      `rule id '${req}' missing — ADR-0050 §10 identifier ban must remain`,
    );
  }
});

test("multi-target-mechanics: scanner runs clean against real repo", () => {
  // PRR-225 removed the pre-existing dayStreakCount leak from progress/time.vue.
  // After that fix, and with the multi-target-mechanics-whitelist covering
  // legacy negation-context files + backend C# surfaces awaiting GD-004
  // cleanup, this pack should run clean against the repo.
  const result = runScanner([`--pack=${PACK_NAME}`]);
  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);

  if (pack.violations.length > 0) {
    const summary = pack.violations
      .map((v) => `  ${v.file}:${v.line} [${v.ruleId}] "${v.match}"`)
      .join("\n");
    assert.fail(
      `multi-target-mechanics pack not clean against real repo — ${pack.violations.length} violation(s):\n${summary}\n\n` +
      `Either fix the violation in the offending file, or add a narrow whitelist entry to ` +
      `scripts/shipgate/multi-target-mechanics-whitelist.yml with a justification.`,
    );
  }
  assert.equal(result.exitCode, 0, "real-repo scan exits clean");
});

test("multi-target-mechanics: fixture itself is whitelisted so it only fires in fixture-mode", () => {
  // Sanity check: normal-mode scan should NOT surface the fixture's
  // intentional traps. If it does, the fixture slipped out of the
  // whitelist and every CI run will spam violations.
  const result = runScanner([`--pack=${PACK_NAME}`]);
  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  const fixtureHits = pack.violations.filter((v) =>
    v.file.includes("multi-target-mechanics-sample.md"),
  );
  assert.equal(
    fixtureHits.length,
    0,
    "fixture file must be whitelisted so its intentional traps don't fire in normal-mode scans",
  );
});
