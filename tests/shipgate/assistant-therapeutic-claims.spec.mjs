// =============================================================================
// tests/shipgate/assistant-therapeutic-claims.spec.mjs
//
// Run with: node --test tests/shipgate/assistant-therapeutic-claims.spec.mjs
//
// Asserts:
//   1. Every rule in assistant-therapeutic-claims.yml fires at least once
//      against the positive-test fixture.
//   2. A sanitised document (no banned patterns) produces zero rule matches.
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
const RULES_YAML = resolve(ROOT, "scripts/shipgate/assistant-therapeutic-claims.yml");
const PACK_NAME = "therapeutic-claims";

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

test("therapeutic-claims: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("therapeutic-claims: every rule fires at least once against the fixture", () => {
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

test("therapeutic-claims: all three locales represented", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const hasHe = ids.some((id) => id.startsWith("he-"));
  const hasAr = ids.some((id) => id.startsWith("ar-"));
  // English rules are the default (no locale prefix)
  const hasEn = ids.some((id) => !id.startsWith("he-") && !id.startsWith("ar-"));
  assert.ok(hasEn, "at least one English rule");
  assert.ok(hasHe, "at least one Hebrew rule");
  assert.ok(hasAr, "at least one Arabic rule");
});

test("therapeutic-claims: sanitised content produces zero fires", () => {
  // Synthetic neutral content: assistant-style messaging that stays on
  // math content and never asserts an emotional state or clinical label.
  const clean = [
    "Want to work through step 3 together? Let's look at the integral.",
    "This question is tricky — here is a worked example for the chain rule.",
    "You can take a break and come back; Cena will remember where you paused.",
    "If you have an approved accommodation, extended time is available on this session.",
    "We can try a smaller similar problem first, then come back to this one.",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, clean);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on neutral content`);
  }
});

// ---------------------------------------------------------------------------
// Helpers (duplicated from banned-mechanics.spec.mjs for standalone runs)
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
