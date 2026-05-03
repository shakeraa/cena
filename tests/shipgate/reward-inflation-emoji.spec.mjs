// =============================================================================
// tests/shipgate/reward-inflation-emoji.spec.mjs
//
// Run with: node --test tests/shipgate/reward-inflation-emoji.spec.mjs
//
// Asserts:
//   1. Every rule in reward-inflation-emoji.yml fires at least once against
//      the positive-test fixture.
//   2. A sanitised document (no banned emoji) produces zero rule matches.
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
const RULES_YAML = resolve(ROOT, "scripts/shipgate/reward-inflation-emoji.yml");
const PACK_NAME = "reward-emoji";

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

test("reward-inflation-emoji: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("reward-inflation-emoji: every rule fires at least once against the fixture", () => {
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

test("reward-inflation-emoji: sanitised content produces zero fires", () => {
  // Synthetic neutral content: progress celebration without variable-ratio
  // reward signalling. Text-only, no emoji.
  const clean = [
    "Unit 3 complete — 8 of 8 sub-skills mastered.",
    "You finished the derivative section. Want to review or move on?",
    "Progress recorded. Your teacher will see the updated mastery map.",
    "Good work today — session logged.",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, clean);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on neutral content`);
  }
});

test("reward-inflation-emoji: all seven banned emoji have a rule", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const expected = [
    "emoji-fire",
    "emoji-high-voltage",
    "emoji-hundred",
    "emoji-trophy",
    "emoji-party-popper",
    "emoji-glowing-star",
    "emoji-star",
  ];
  for (const id of expected) {
    assert.ok(ids.includes(id), `missing rule id '${id}' — every prr-153 banned emoji must have a rule`);
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
