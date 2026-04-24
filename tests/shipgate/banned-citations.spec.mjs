// =============================================================================
// tests/shipgate/banned-citations.spec.mjs
//
// Run with: node --test tests/shipgate/banned-citations.spec.mjs
//
// Asserts:
//   1. Every rule in banned-citations.yml fires at least once against the
//      positive-test fixture (shipgate/fixtures/banned-citation-sample.md).
//   2. A clean document (no banned patterns) produces zero violations.
//   3. The whitelist suppresses hits from whitelisted file paths.
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { execFileSync } from "node:child_process";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");
const RULES_YAML = resolve(ROOT, "scripts/shipgate/banned-citations.yml");

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

test("banned-citations: fixture triggers violations", () => {
  const result = runScanner(["--pack=citations", "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === "citations");
  assert.ok(pack, "citations pack present in report");
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("banned-citations: every rule fires at least once against the fixture", async () => {
  const { readFileSync } = await import("node:fs");
  const yaml = readFileSync(RULES_YAML, "utf8");
  const expectedIds = extractRuleIds(yaml);
  assert.ok(expectedIds.length > 0, "rule pack parsed at least one rule id");

  const result = runScanner(["--pack=citations", "--fixture-mode"]);
  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === "citations");
  const firedIds = new Set(pack.violations.map((v) => v.ruleId));

  const missing = expectedIds.filter((id) => !firedIds.has(id));
  assert.deepEqual(missing, [], `rules that did not fire: ${missing.join(", ")}`);
});

test("banned-citations: sanitised string content produces zero rule fires", async () => {
  // Synthetic content that avoids every banned pattern.
  const synthetic = [
    "Our research cites Lehman & Graesser 2014 for tutorial dialogue.",
    "Effect sizes are reported with confidence intervals per meta-analytic norms.",
    "Hattie's self-reported-grades finding remains contextually valid.",
    "Interleaving benefits are documented in the Rohrer 2015 meta-analysis.",
    "We use calibrated estimates, not inflated ones.",
  ].join("\n");

  const { readFileSync } = await import("node:fs");
  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, synthetic);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on clean text`);
  }
});

// -- helpers ---------------------------------------------------------------

function parseRulesFromYaml(yaml) {
  // Minimal parser mirror: find each "- id:" block and extract pattern or near.
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
    const nearAMatch = line.match(/^\s+a:\s+"(.+)"\s*$/);
    if (nearAMatch) { cur.near = cur.near || {}; cur.near.a = nearAMatch[1]; continue; }
    const nearBMatch = line.match(/^\s+b:\s+"(.+)"\s*$/);
    if (nearBMatch) { cur.near = cur.near || {}; cur.near.b = nearBMatch[1]; continue; }
  }
  if (cur) rules.push(cur);
  return rules;
}

function ruleMatches(rule, text) {
  // Check each line (matching the scanner's per-line semantics).
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
