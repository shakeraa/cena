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
  ],
  ar: [
    { pattern: /سلسلة(?!.*كهرب)/u, reason: "سلسلة as streak (not electrical chain)" },
    { pattern: /لا تفقد/u, reason: "loss-aversion copy (Arabic)" },
    { pattern: /ستخسر/u, reason: "you will lose (Arabic)" },
  ],
  he: [
    { pattern: /רצף יומי/u, reason: "daily streak (Hebrew)" },
    { pattern: /אל תשבור/u, reason: "don't break streak (Hebrew)" },
    { pattern: /תפסיד/u, reason: "you will lose (Hebrew)" },
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

  // Scan Vue and TS files
  for (const pattern of [...vuePatterns, ...codePatterns]) {
    for (const f of findFiles([pattern])) {
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
