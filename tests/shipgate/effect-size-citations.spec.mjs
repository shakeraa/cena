// =============================================================================
// tests/shipgate/effect-size-citations.spec.mjs
//
// Run with: node --test tests/shipgate/effect-size-citations.spec.mjs
//
// Asserts:
//   1. Every rule in effect-size-citations.yml fires at least once against
//      the positive-test fixture (shipgate/fixtures/effect-size-citations-sample.md).
//   2. A synthetic clean document (every ES claim carries a citation_id tag)
//      produces zero rule fires.
//   3. The scanner exits 1 with violations in fixture mode.
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");
const RULES_YAML = resolve(ROOT, "scripts/shipgate/effect-size-citations.yml");

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

test("effect-size-citations: fixture triggers violations", () => {
  const result = runScanner(["--pack=effect-size", "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === "effect-size");
  assert.ok(pack, "effect-size pack present in report");
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("effect-size-citations: every rule fires at least once against the fixture", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const expectedIds = extractRuleIds(yaml);
  assert.ok(expectedIds.length > 0, "rule pack parsed at least one rule id");

  const result = runScanner(["--pack=effect-size", "--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === "effect-size");
  const firedIds = new Set(pack.violations.map((v) => v.ruleId));

  const missing = expectedIds.filter((id) => !firedIds.has(id));
  assert.deepEqual(missing, [], `rules that did not fire: ${missing.join(", ")}`);
});

test("effect-size-citations: claims with citation_id= tag do NOT fire", () => {
  // Synthetic content where every ES claim carries a citation tag. All rules
  // use "near" with a negative-lookahead B pattern that fails on lines
  // containing citation_id=, so these lines must produce zero hits.
  const synthetic = [
    "Interleaved practice shows d=0.34 (95% CI 0.20-0.48; citation_id=brummair-richter-2019-interleaving).",
    "Formative assessment effect size of 0.55 (citation_id=black-wiliam-1998-formative).",
    "Cohen's d = 0.37 for immediate feedback (citation_id=kehrer-2021-immediate-feedback).",
    "ES of 0.40 for study-techniques review (citation_id=dunlosky-2013-study-techniques).",
    "The 25% improvement in retention (citation_id=brummair-richter-2019-interleaving) is meta-analytic.",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, synthetic);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on tagged text`);
  }
});

test("effect-size-citations: plain text without any ES claim produces zero fires", () => {
  const synthetic = [
    "Cena renders math notation left-to-right inside RTL layouts.",
    "The session-scoped misconception tag persists for 30 days.",
    "Parent surface displays weekly study cadence as an Apple-Fitness-style ring.",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, synthetic);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on benign text`);
  }
});

// -- helpers ---------------------------------------------------------------

function parseRulesFromYaml(yaml) {
  const rules = [];
  const lines = yaml.split("\n");
  let cur = null;
  let inNear = false;
  for (const raw of lines) {
    const line = raw.replace(/\s+$/, "");
    if (!line.trim() || line.trim().startsWith("#")) continue;

    const idMatch = line.match(/^\s*-\s+id:\s+(\S+)\s*$/);
    if (idMatch) {
      if (cur) rules.push(cur);
      cur = { id: idMatch[1], flags: "i" };
      inNear = false;
      continue;
    }
    if (!cur) continue;
    if (/^\s+near:\s*$/.test(line)) {
      inNear = true;
      cur.near = {};
      continue;
    }
    const patternMatch = line.match(/^\s+pattern:\s+"(.+)"\s*$/);
    if (patternMatch) { cur.pattern = patternMatch[1]; inNear = false; continue; }
    const flagsMatch = line.match(/^\s+flags:\s+(\S+)\s*$/);
    if (flagsMatch) {
      const raw = flagsMatch[1];
      cur.flags = raw.replace(/^"|"$/g, "");
      continue;
    }
    if (inNear) {
      const nearAMatch = line.match(/^\s+a:\s+"(.+)"\s*$/);
      if (nearAMatch) { cur.near.a = nearAMatch[1]; continue; }
      const nearBMatch = line.match(/^\s+b:\s+"(.+)"\s*$/);
      if (nearBMatch) { cur.near.b = nearBMatch[1]; continue; }
    }
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
        const flags = rule.flags || "";
        if (new RegExp(rule.near.a, flags).test(line)
          && new RegExp(rule.near.b, flags).test(line)) return true;
      } catch { return false; }
    }
  }
  return false;
}
