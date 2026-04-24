// =============================================================================
// tests/shipgate/cheating-alert-framing.spec.mjs
//
// Run with: node --test tests/shipgate/cheating-alert-framing.spec.mjs
//
// Asserts:
//   1. Every rule in cheating-alert-framing.yml fires at least once against
//      the positive-test fixture.
//   2. A sanitised document (no banned patterns) produces zero rule matches.
//   3. All three locales (en/he/ar) are represented.
//   4. The IAcademicIntegrityReviewService interface file exists at the
//      expected path (this is the architectural seam the arch-test
//      companion in src/actors/Cena.Actors.Tests/Architecture/ asserts
//      against).
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { readFileSync, existsSync } from "node:fs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");
const RULES_YAML = resolve(ROOT, "scripts/shipgate/cheating-alert-framing.yml");
const INTEGRITY_SEAM = resolve(ROOT, "src/actors/Cena.Actors/AcademicIntegrity/IAcademicIntegrityReviewService.cs");
const PACK_NAME = "cheating-alert";

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

test("cheating-alert-framing: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("cheating-alert-framing: every rule fires at least once against the fixture", () => {
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

test("cheating-alert-framing: all three locales represented", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const hasHe = ids.some((id) => id.startsWith("he-"));
  const hasAr = ids.some((id) => id.startsWith("ar-"));
  const hasEn = ids.some((id) => !id.startsWith("he-") && !id.startsWith("ar-"));
  assert.ok(hasEn, "at least one English rule");
  assert.ok(hasHe, "at least one Hebrew rule");
  assert.ok(hasAr, "at least one Arabic rule");
});

test("cheating-alert-framing: sanitised content produces zero fires", () => {
  // Synthetic neutral content: the kinds of neutral signals the review
  // service might emit — framed as review-needed invitations, never as
  // accusations.
  const clean = [
    "Potential collaboration detected — review needed by an educator.",
    "Unusual session-sharing pattern flagged for teacher review.",
    "Session-overlap signal logged; no action taken automatically.",
    "A teacher will review this session before any response is sent.",
    "If you studied with a friend today, please let your teacher know.",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, clean);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on neutral content`);
  }
});

test("cheating-alert-framing: IAcademicIntegrityReviewService seam exists", () => {
  assert.ok(
    existsSync(INTEGRITY_SEAM),
    `expected IAcademicIntegrityReviewService stub interface at ${INTEGRITY_SEAM} — this is the architectural seam any integrity signal must route through`,
  );
  const src = readFileSync(INTEGRITY_SEAM, "utf8");
  assert.ok(
    /interface\s+IAcademicIntegrityReviewService/.test(src),
    "seam file must declare 'interface IAcademicIntegrityReviewService'",
  );
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseRulesFromYaml(yaml) {
  const rules = [];
  const lines = yaml.split("\n");
  let cur = null;
  for (const raw of lines) {
    const line = raw.replace(/\s+$/, "");
    if (!line.trim() || line.trim().startsWith("#")) continue;

    const idMatch = line.match(/^\s*-\s+id:\s+(\S+)\s*$/);
    if (idMatch) {
      if (cur) rules.push(cur);
      cur = { id: idMatch[1], flags: "i" };
      continue;
    }
    if (!cur) continue;
    const patternMatch = line.match(/^\s+pattern:\s+"(.+)"\s*$/);
    if (patternMatch) { cur.pattern = patternMatch[1]; continue; }
    const flagsMatch = line.match(/^\s+flags:\s+(\S+)\s*$/);
    if (flagsMatch) { cur.flags = flagsMatch[1]; continue; }
    const localeMatch = line.match(/^\s+locale:\s+(\S+)\s*$/);
    if (localeMatch) { cur.locale = localeMatch[1]; continue; }
    const nearAMatch = line.match(/^\s+a:\s+"(.+)"\s*$/);
    if (nearAMatch) { cur.near = cur.near || {}; cur.near.a = nearAMatch[1]; continue; }
    const nearBMatch = line.match(/^\s+b:\s+"(.+)"\s*$/);
    if (nearBMatch) { cur.near = cur.near || {}; cur.near.b = nearBMatch[1]; continue; }
  }
  if (cur) rules.push(cur);
  return rules;
}

function ruleMatches(rule, text) {
  const lines = text.split("\n");
  for (const line of lines) {
    if (rule.pattern) {
      try {
        if (new RegExp(rule.pattern, rule.flags).test(line)) return true;
      } catch { return false; }
    } else if (rule.near) {
      try {
        if (new RegExp(rule.near.a, rule.flags).test(line)
          && new RegExp(rule.near.b, rule.flags).test(line)) return true;
      } catch { return false; }
    }
  }
  return false;
}
