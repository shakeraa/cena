// =============================================================================
// tests/shipgate/positive-framing-extended.spec.mjs
//
// Run with: node --test tests/shipgate/positive-framing-extended.spec.mjs
//
// EPIC-PRR-D P2 tail bundle (prr-163/166/167/168/170/171/172/177/178).
//
// Asserts:
//   1. Every rule in positive-framing-extended.yml fires at least once against
//      the positive-test fixture.
//   2. A sanitised document (no banned patterns) produces zero rule matches.
//   3. All three locales (en/he/ar) are represented in the rule set.
//   4. The pack is registered in the scanner's PACKS array and FIXTURE_FILES
//      map (guards against drift between the YAML pack and the scanner config).
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
const RULES_YAML = resolve(ROOT, "scripts/shipgate/positive-framing-extended.yml");
const PACK_NAME = "positive-framing-extended";

function runScanner(extraArgs = []) {
  try {
    const out = execFileSync(process.execPath, [SCANNER, ...extraArgs, "--json"], {
      encoding: "utf8",
      cwd: ROOT,
      maxBuffer: 16 * 1024 * 1024,
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

test("positive-framing-extended: fixture triggers violations", () => {
  const result = runScanner([`--pack=${PACK_NAME}`, "--fixture-mode"]);
  assert.equal(result.exitCode, 1, "fixture scan must exit 1 (violations expected)");

  const report = JSON.parse(result.stdout);
  const pack = report.packs.find((p) => p.name === PACK_NAME);
  assert.ok(pack, `${PACK_NAME} pack present in report`);
  assert.ok(pack.violations.length > 0, "fixture produced violations");
});

test("positive-framing-extended: every rule fires at least once against the fixture", () => {
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

test("positive-framing-extended: all three locales represented", () => {
  const yaml = readFileSync(RULES_YAML, "utf8");
  const ids = extractRuleIds(yaml);
  const hasHe = ids.some((id) => id.startsWith("he-"));
  const hasAr = ids.some((id) => id.startsWith("ar-"));
  const hasEn = ids.some((id) => !id.startsWith("he-") && !id.startsWith("ar-"));
  assert.ok(hasEn, "at least one English rule");
  assert.ok(hasHe, "at least one Hebrew rule");
  assert.ok(hasAr, "at least one Arabic rule");
});

test("positive-framing-extended: every prr-ID in scope has at least one rule", () => {
  // The P2 tail bundles nine prr-IDs; each must be represented by at least
  // one rule whose `reason` cites the corresponding prr-ID so a reviewer can
  // grep by prr-ID and find the enforcing rule.
  const yaml = readFileSync(RULES_YAML, "utf8");
  const EXPECTED_PRR_IDS = [
    "prr-163", "prr-166", "prr-167", "prr-168", "prr-170",
    "prr-171", "prr-172", "prr-177", "prr-178",
  ];
  const missing = EXPECTED_PRR_IDS.filter((id) => !yaml.includes(id));
  assert.deepEqual(missing, [], `prr-IDs without a cited rule: ${missing.join(", ")}`);
});

test("positive-framing-extended: scanner registers the pack and its fixture", () => {
  const scannerSource = readFileSync(SCANNER, "utf8");
  assert.ok(
    scannerSource.includes(`name: "${PACK_NAME}"`),
    `rulepack-scan.mjs must register pack '${PACK_NAME}' in the PACKS array`,
  );
  assert.ok(
    scannerSource.includes(`"${PACK_NAME}":`),
    `rulepack-scan.mjs must map pack '${PACK_NAME}' to a FIXTURE_FILES entry`,
  );
});

test("positive-framing-extended: sanitised content produces zero fires", () => {
  // Synthetic neutral content: uses the positive alternatives mandated by
  // ADR-0048 (time-awareness, honest-number posture), ADR-0049 (citation-
  // tagged effect sizes), and the design-doc rewrites.
  const clean = [
    "Step 3 scored 2 of 4 because the rule for negative exponents was applied before distribution.",
    "Here's a familiar pattern — derivatives of polynomials. A good warm-up for the integration step.",
    "Let's look at your thinking on step 2. The chain rule was applied, but to the wrong expression.",
    "Interleaving shows a moderate effect on cumulative retention (d=0.34, 95% CI 0.20-0.48; citation_id=brummair-richter-2019-interleaving).",
    "Your class covered Unit 3 last week. Catch up when you're ready.",
    "Practice Set: 5 problems on integration by parts.",
    ".journey-path { color: var(--brand-primary); }",
    ".stuck-ask { background: var(--surface-raised); }",
  ].join("\n");

  const yaml = readFileSync(RULES_YAML, "utf8");
  const rules = parseRulesFromYaml(yaml);

  for (const rule of rules) {
    const hit = ruleMatches(rule, clean);
    assert.equal(hit, false, `rule ${rule.id} should NOT fire on neutral content`);
  }
});

// ---------------------------------------------------------------------------
// Helpers (mirrors progress-framing.spec.mjs — keep in sync)
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
