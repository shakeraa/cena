// =============================================================================
// tests/shipgate/progress-framing.spec.mjs
//
// Run with: node --test tests/shipgate/progress-framing.spec.mjs
//
// Asserts:
//   1. Every rule in progress-framing.yml fires at least once against the
//      positive-test fixture.
//   2. A sanitised document (no banned patterns) produces zero rule matches.
//   3. All three locales (en/he/ar) are represented in the rule set.
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
const RULES_YAML = resolve(ROOT, "scripts/shipgate/progress-framing.yml");
const PACK_NAME = "progress-framing";

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

test("progress-framing: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("progress-framing: every rule fires at least once against the fixture", () => {
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

test("progress-framing: all three locales represented", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const hasHe = ids.some((id) => id.startsWith("he-"));
  const hasAr = ids.some((id) => id.startsWith("ar-"));
  const hasEn = ids.some((id) => !id.startsWith("he-") && !id.startsWith("ar-"));
  assert.ok(hasEn, "at least one English rule");
  assert.ok(hasHe, "at least one Hebrew rule");
  assert.ok(hasAr, "at least one Arabic rule");
});

test("progress-framing: sanitised content produces zero fires", () => {
  // Synthetic neutral content: progress framing that stays on
  // internal-reference-point language with CI-bounded numbers.
  const clean = [
    "Your current accuracy on Unit 3 is 72% (95% CI 68-76%) over 180 attempts.",
    "You have mastered 2 of 8 sub-skills in this unit.",
    "Your weekly cadence is 4 of 5 planned sessions.",
    "Progress on the derivative unit is steady — want to try a harder item?",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, clean);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on neutral content`);
  }
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
