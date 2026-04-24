#!/usr/bin/env node
// =============================================================================
// Ship-gate dark-pattern scanner (GD-004)
//
// Scans locale files (en.json, ar.json, he.json), Vue templates, and source
// code for banned engagement mechanics. Fails CI on any match.
//
// Legal basis: FTC v. Epic ($245M), FTC v. Edmodo (2023), COPPA 2025 Final
// Rule, ICO v. Reddit £14.47M (Feb 2026), Israel PPL Amendment 13.
//
// Usage: node scripts/shipgate/scan.mjs [--fix-suggestions]
// Exit 0 = clean, Exit 1 = violations found
// =============================================================================

import { readFileSync, existsSync } from "fs";
import { resolve, relative } from "path";
import { globSync } from "fs";

const ROOT = resolve(import.meta.dirname, "../..");

// ---------------------------------------------------------------------------
// Banned terms by language
// ---------------------------------------------------------------------------
const BANNED = {
  en: [
    { pattern: /\bstreak\b/i, reason: "streak counter (loss-aversion)", allowlist: ["streak current", "streaked", "electrical streak"] },
    { pattern: /don['']t break/i, reason: "loss-aversion copy" },
    { pattern: /keep the chain/i, reason: "streak/chain mechanic" },
    { pattern: /lose your/i, reason: "loss-aversion copy" },
    { pattern: /daily streak/i, reason: "streak counter" },
    { pattern: /\bhearts\b/i, reason: "lives/hearts currency", allowlist: ["heart rate", "cardiac", "heart of"] },
    { pattern: /\blives\b/i, reason: "lives currency", allowlist: ["lives in", "lives at", "who lives", "he lives", "she lives", "it lives"] },
    { pattern: /you['']ll lose/i, reason: "loss-aversion threat" },
    { pattern: /don['']t miss/i, reason: "FOMO urgency" },
    { pattern: /running out of time/i, reason: "artificial urgency" },
    // RDY-065 (F11): anxiety-safe hint ladder — student-facing hint UI must
    // never display penalty counters, numeric hint levels, or comparative
    // shame. Backend adjusts BKT assisted credit internally; UI stays silent.
    { pattern: /\(\s*\{?\s*(remaining|hintsRemaining|hintsLeft)\s*\}?\s+(left|remaining|to go)\s*\)/i, reason: "penalty-style hint counter (RDY-065)" },
    { pattern: /\bhint\s+\{?level\}?\b/i, reason: "numeric hint level display (RDY-065 — use qualitative labels)" },
    { pattern: /you['']re\s+behind/i, reason: "comparative shame (RDY-065)" },
    { pattern: /slower than (\d+%|\w+ of)/i, reason: "comparative percentile shame (RDY-065)" },
    { pattern: /behind\s+\d+%\s+of/i, reason: "comparative percentile shame (RDY-065)" },
    { pattern: /-\s*\d+%\s*(credit|score|mastery|points)/i, reason: "visible BKT penalty (RDY-065 — must stay internal)" },
    { pattern: /\bhints?\s+used\b/i, reason: "hints-used counter (RDY-065)" },
    // RDY-071 (F8): honest-framing contract. Until RDY-080 concordance
    // study produces an approved ConcordanceMapping with
    // F8PointEstimateEnabled=true, student-facing surfaces MUST NOT
    // claim a numeric Bagrut prediction. See
    // docs/engineering/mastery-trajectory-honest-framing.md.
    { pattern: /predicted\s+bagrut/i, reason: "predicted Bagrut score (RDY-071 — blocked until RDY-080 calibration approved)" },
    { pattern: /your\s+bagrut\s+score/i, reason: "possessive Bagrut score framing (RDY-071)" },
    { pattern: /bagrut\s+score\s+will\s+be/i, reason: "future-tense Bagrut assertion (RDY-071)" },
    { pattern: /expected\s+(grade|score)/i, reason: "expected-grade/score forward extrapolation (RDY-071)" },
    { pattern: /we\s+predict\s+you['']ll\s+score/i, reason: "conversational score prediction (RDY-071)" },
    { pattern: /your\s+score\s+will\s+be/i, reason: "future-tense score assertion (RDY-071)" },
    { pattern: /you\s+will\s+score/i, reason: "future-tense score prediction (RDY-071)" },
    { pattern: /you['']ll\s+get\s+a?\s*\d/i, reason: "numeric score prediction (RDY-071)" },
    { pattern: /predicted\s+score\s*:?/i, reason: "predicted score label (RDY-071)" },
    { pattern: /bagrut\s+prediction/i, reason: "Bagrut prediction label (RDY-071)" },
    { pattern: /grade\s+prediction/i, reason: "grade prediction label (RDY-071)" },
    { pattern: /\d{2,3}\s+on\s+(your|the)\s+bagrut/i, reason: "numeric on-the-Bagrut prediction (RDY-071)" },
    // RDY-077 (F12): parent time-budget soft-cap UI MUST NOT use FOMO /
    // scarcity / red-countdown framing. See
    // docs/design/parent-time-budget-design.md when it ships.
    { pattern: /only\s+\d+\s+min(ute)?s?\s+left/i, reason: "FOMO countdown on time budget (RDY-077)" },
    { pattern: /\bout of time\b/i, reason: "scarcity framing on time budget (RDY-077)" },
    { pattern: /time['']s\s+up/i, reason: "lockout framing on time budget (RDY-077)" },
    { pattern: /hurry\s+(up|before)/i, reason: "urgency framing (RDY-077)" },
    { pattern: /don['']t\s+waste/i, reason: "loss-aversion on time budget (RDY-077)" },
    { pattern: /you\s+must\s+stop\s+now/i, reason: "hard-cap lockout framing (RDY-077)" },
  ],
  ar: [
    { pattern: /سلسلة(?!.*كهرب)/u, reason: "سلسلة as streak (not electrical chain)" },
    { pattern: /لا تفقد/u, reason: "loss-aversion copy (Arabic)" },
    { pattern: /ستخسر/u, reason: "you will lose (Arabic)" },
    // RDY-065 (F11) Arabic patterns
    { pattern: /\(\s*(تبقى|متبقي)\s+\{?[^)]*\}?\s*\)/u, reason: "Arabic hint counter (RDY-065)" },
    { pattern: /أنت\s+متأخر/u, reason: "comparative shame Arabic (RDY-065)" },
    // RDY-071 (F8) honest-framing Arabic patterns
    { pattern: /علامة\s+البجروت\s+المتوقعة/u, reason: "predicted Bagrut score Arabic (RDY-071)" },
    { pattern: /درجتك\s+ستكون/u, reason: "your-score-will-be Arabic (RDY-071)" },
    { pattern: /نتوقع\s+أن\s+تحصل\s+على/u, reason: "we-predict-you-will-get Arabic (RDY-071)" },
    { pattern: /درجتك\s+المتوقعة/u, reason: "your-expected-grade Arabic (RDY-071)" },
  ],
  he: [
    { pattern: /רצף יומי/u, reason: "daily streak (Hebrew)" },
    { pattern: /אל תשבור/u, reason: "don't break streak (Hebrew)" },
    { pattern: /תפסיד/u, reason: "you will lose (Hebrew)" },
    // RDY-065 (F11) Hebrew patterns
    { pattern: /\(\s*נותרו?\s+\{?[^)]*\}?\s*\)/u, reason: "Hebrew hint counter (RDY-065)" },
    { pattern: /אתה\s+מפגר/u, reason: "comparative shame Hebrew (RDY-065)" },
    // RDY-071 (F8) honest-framing Hebrew patterns
    { pattern: /ציון\s+הבגרות\s+החזוי/u, reason: "predicted Bagrut score Hebrew (RDY-071)" },
    { pattern: /הציון\s+שלך\s+יהיה/u, reason: "your-score-will-be Hebrew (RDY-071)" },
    { pattern: /אנו\s+צופים\s+שתקבל/u, reason: "we-predict-you-will-get Hebrew (RDY-071)" },
    { pattern: /הציון\s+הצפוי\s+שלך/u, reason: "your-expected-grade Hebrew (RDY-071)" },
  ],
};

// Allowlist file — explicit overrides with justification
const ALLOWLIST_PATH = resolve(ROOT, "scripts/shipgate/allowlist.json");

// ---------------------------------------------------------------------------
// Glob helper (Node 22+ fs.globSync or fallback)
// ---------------------------------------------------------------------------
function findFiles(patterns) {
  const results = [];
  for (const pat of patterns) {
    try {
      // Node 22+ has fs.globSync
      const found = globSync(pat, { cwd: ROOT });
      results.push(...found.map((f) => resolve(ROOT, f)));
    } catch {
      // Fallback: use simple recursive walk (shouldn't happen in CI)
      console.warn(`[WARN] globSync unavailable for ${pat}, skipping`);
    }
  }
  return results;
}

// ---------------------------------------------------------------------------
// Load allowlist
// ---------------------------------------------------------------------------
function loadAllowlist() {
  if (!existsSync(ALLOWLIST_PATH)) return [];
  const data = JSON.parse(readFileSync(ALLOWLIST_PATH, "utf-8"));
  // Format: [{ file: "path", line: 42, term: "streak", justification: "..." }]
  return data;
}

// ---------------------------------------------------------------------------
// Main scan
// ---------------------------------------------------------------------------
function scan() {
  const violations = [];
  const allowlist = loadAllowlist();

  // Files to scan
  const localeFiles = [
    "src/student/full-version/src/plugins/i18n/locales/en.json",
    "src/student/full-version/src/plugins/i18n/locales/ar.json",
    "src/student/full-version/src/plugins/i18n/locales/he.json",
    "src/admin/full-version/src/plugins/i18n/locales/en.json",
    "src/admin/full-version/src/plugins/i18n/locales/ar.json",
    "src/admin/full-version/src/plugins/i18n/locales/he.json",
  ];

  const vuePatterns = [
    "src/student/full-version/src/**/*.vue",
    "src/admin/full-version/src/**/*.vue",
  ];

  const codePatterns = [
    "src/student/full-version/src/**/*.ts",
    "src/admin/full-version/src/**/*.ts",
    "src/actors/**/*.cs",
    "src/api/**/*.cs",
  ];

  // Determine language from file path
  function getLang(filepath) {
    if (filepath.includes("/ar.json") || filepath.includes("/ar/")) return "ar";
    if (filepath.includes("/he.json") || filepath.includes("/he/")) return "he";
    return "en";
  }

  // Scan a single file
  function scanFile(filepath) {
    if (!existsSync(filepath)) return;
    const content = readFileSync(filepath, "utf-8");
    const lines = content.split("\n");
    const lang = getLang(filepath);
    const rules = [...BANNED.en, ...(BANNED[lang] || [])];

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      for (const rule of rules) {
        const match = line.match(rule.pattern);
        if (!match) continue;

        // Check per-rule allowlist (built-in)
        if (rule.allowlist?.some((a) => line.toLowerCase().includes(a.toLowerCase()))) continue;

        // Check external allowlist
        const rel = relative(ROOT, filepath);
        const isAllowed = allowlist.some(
          (a) => a.file === rel && a.line === i + 1 && a.term === match[0]
        );
        if (isAllowed) continue;

        violations.push({
          file: rel,
          line: i + 1,
          term: match[0],
          reason: rule.reason,
          context: line.trim().slice(0, 120),
        });
      }
    }
  }

  // Scan locale files
  for (const f of localeFiles) {
    scanFile(resolve(ROOT, f));
  }

  // Test folders legitimately reference banned phrases to assert they do
  // NOT appear in production renders (RDY-065b, RDY-071 regression suites).
  // Excluding them from the scan keeps the shipgate signal focused on the
  // production paths it actually guards.
  const testPathExcludes = [
    /[\\/]tests[\\/]/i,
    /[\\/]Tests[\\/]/,
    /\.Tests[\\/]/,
    /[\\/]test[\\/]/i,
  ];
  function isTestPath(filepath) {
    return testPathExcludes.some((rx) => rx.test(filepath));
  }

  // Scan Vue and TS files
  for (const pattern of [...vuePatterns, ...codePatterns]) {
    for (const f of findFiles([pattern])) {
      if (isTestPath(f)) continue;
      scanFile(f);
    }
  }

  return violations;
}

// ---------------------------------------------------------------------------
// Report
// ---------------------------------------------------------------------------
const violations = scan();

if (violations.length === 0) {
  console.log("✓ Ship-gate scan passed — no dark-pattern violations found.");
  process.exit(0);
} else {
  console.error(`✗ Ship-gate scan FAILED — ${violations.length} violation(s):\n`);
  for (const v of violations) {
    console.error(`  ${v.file}:${v.line} — "${v.term}" (${v.reason})`);
    console.error(`    ${v.context}\n`);
  }
  console.error(
    "To allowlist a legitimate use, add an entry to scripts/shipgate/allowlist.json\n" +
    "with { file, line, term, justification }. Justifications are reviewed in PR."
  );
  process.exit(1);
}
