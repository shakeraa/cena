#!/usr/bin/env node
// =============================================================================
// Cena Platform — Lexicon Lock Gate (RDY-068b)
//
// Student-facing production code MUST NOT contain Arabic lexicon terms whose
// review status is still DRAFT or PROF_AMJAD_REVIEW. LOCKED terms are safe;
// anything else is a potential curriculum-fidelity regression.
//
// The lexicon source of truth is docs/content/arabic-math-lexicon.md.
// This script parses that file (same Markdown format the .NET
// TerminologyLexicon parser reads), extracts each term's status, then
// scans production code paths for any occurrence of a non-LOCKED Arabic
// form.
//
// Ship modes:
//   Advisory (default, pre-lock) — while the lexicon has ZERO LOCKED terms,
//     the gate runs but exits 0 regardless of violations. This is the
//     initial state: every term is DRAFT until Prof. Amjad's first review.
//     The gate still prints the violation list so engineering can see what
//     would be rejected once terms start landing LOCKED.
//   Enforcing (post-lock) — as soon as the lexicon contains ≥ 1 LOCKED
//     term, the gate flips to enforcing and any non-LOCKED term in
//     production code exits 1. Override with --advisory to suppress.
//   --strict forces enforcing mode regardless of lexicon state; use in
//     unit tests.
//
// Exit codes:
//   0 — clean (or advisory mode with zero LOCKED terms)
//   1 — enforcing mode + one or more non-LOCKED terms found
//   2 — missing lexicon file at startup
//
// Usage:
//   node scripts/shipgate/lexicon-lock-gate.mjs              # auto mode
//   node scripts/shipgate/lexicon-lock-gate.mjs --advisory   # never fail
//   node scripts/shipgate/lexicon-lock-gate.mjs --strict     # always fail on violation
//   node scripts/shipgate/lexicon-lock-gate.mjs --json       # machine-readable
//   node scripts/shipgate/lexicon-lock-gate.mjs --quiet      # CI noise control
// =============================================================================

import { readFileSync, existsSync } from "fs";
import { resolve, relative } from "path";
import { globSync } from "fs";

const ROOT = resolve(import.meta.dirname, "../..");
const LEXICON_PATH = resolve(ROOT, "docs/content/arabic-math-lexicon.md");

const args = new Set(process.argv.slice(2));
const JSON_OUT = args.has("--json");
const QUIET = args.has("--quiet");
const FORCE_ADVISORY = args.has("--advisory");
const FORCE_STRICT = args.has("--strict");

// ---------------------------------------------------------------------------
// Parse docs/content/arabic-math-lexicon.md
// ---------------------------------------------------------------------------
// Row shape: `| <arabic> | <hebrew> | <english> | <STATUS> | <notes> |`
// Matching status tokens: DRAFT / PROF_AMJAD_REVIEW / LOCKED

const STATUS_TOKEN = /^\s*(DRAFT|PROF_AMJAD_REVIEW|LOCKED)\s*$/i;

function parseLexicon(path) {
  if (!existsSync(path)) {
    console.error(`[lexicon-gate] Missing lexicon at ${path}`);
    process.exit(2);
  }
  const text = readFileSync(path, "utf8");
  const lines = text.split("\n");
  const terms = [];
  for (const line of lines) {
    const m = line.match(/^\|(?<cells>.+)\|\s*$/);
    if (!m) continue;
    const cells = m.groups.cells.split("|").map(c => c.trim());
    if (cells.length < 4) continue;
    const statusCell = cells[3];
    if (!STATUS_TOKEN.test(statusCell)) continue;

    const arabic = cells[0];
    const hebrew = cells[1];
    const english = cells[2].replace(/\(.+?\)/, "").trim();
    const status = statusCell.trim().toUpperCase();
    if (!arabic || !hebrew || !english) continue;
    terms.push({ arabic, hebrew, english, status });
  }
  return terms;
}

// ---------------------------------------------------------------------------
// Production code paths to scan
// ---------------------------------------------------------------------------
// Student-facing SPAs + backend prompt templates are the hot paths. We
// explicitly exclude:
//   - docs/**          (the lexicon itself + research docs)
//   - tasks/**         (the task backlog)
//   - **/bin/**, **/obj/**, **/node_modules/**
//   - **/tests/**, **/*Tests/**, **/*.Tests/**, **/tests-mock/** (test fixtures may quote non-LOCKED terms)
//   - **/fixtures/**   (test payloads)
//   - any file in a done-lexicon allowlist path (below)

const INCLUDES = [
  "src/student/full-version/src/**/*.{vue,ts,js}",
  "src/admin/full-version/src/**/*.{vue,ts,js}",
  "src/api/**/*.cs",
  "src/actors/**/*.cs",
  "src/shared/**/*.cs",
];

const EXCLUDE_REGEXES = [
  /\/bin\//,
  /\/obj\//,
  /\/node_modules\//,
  /\/dist\//,
  /\.Tests\//,
  /\/tests\//,
  /\/tests-mock\//,
  /\/fixtures\//,
  /\/Generated\//,
];

// ---------------------------------------------------------------------------
// Scan
// ---------------------------------------------------------------------------

function collectFiles() {
  const files = new Set();
  for (const pattern of INCLUDES) {
    for (const match of globSync(resolve(ROOT, pattern), { nodir: true })) {
      files.add(match);
    }
  }
  // Apply exclusions.
  const kept = [];
  for (const f of files) {
    if (!EXCLUDE_REGEXES.some(rx => rx.test(f))) kept.push(f);
  }
  return kept;
}

function scanFile(filePath, nonLockedTerms) {
  let content;
  try {
    content = readFileSync(filePath, "utf8");
  } catch {
    return [];
  }
  const hits = [];
  for (const term of nonLockedTerms) {
    if (!term.arabic) continue;
    // Locate each occurrence and report file:line.
    let idx = 0;
    while ((idx = content.indexOf(term.arabic, idx)) !== -1) {
      const line = content.slice(0, idx).split("\n").length;
      hits.push({
        file: relative(ROOT, filePath),
        line,
        term: term.arabic,
        english: term.english,
        status: term.status,
      });
      idx += term.arabic.length;
    }
  }
  return hits;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const allTerms = parseLexicon(LEXICON_PATH);
const nonLocked = allTerms.filter(t => t.status !== "LOCKED");
const locked = allTerms.filter(t => t.status === "LOCKED");

if (!QUIET && !JSON_OUT) {
  console.log(`[lexicon-gate] loaded ${allTerms.length} terms `
    + `(${locked.length} LOCKED, ${nonLocked.length} non-LOCKED)`);
}

const files = collectFiles();
const violations = [];
for (const f of files) {
  violations.push(...scanFile(f, nonLocked));
}

// Mode selection
const advisory = FORCE_ADVISORY
  || (!FORCE_STRICT && locked.length === 0);
const mode = advisory ? "advisory" : "enforcing";

if (JSON_OUT) {
  console.log(JSON.stringify({
    mode,
    totalTerms: allTerms.length,
    lockedCount: locked.length,
    nonLockedCount: nonLocked.length,
    filesScanned: files.length,
    violations,
  }, null, 2));
} else {
  if (violations.length === 0) {
    if (!QUIET)
      console.log(`[lexicon-gate] ✅ clean — ${files.length} files scanned, `
        + `no non-LOCKED lexicon terms in production code (mode=${mode})`);
    process.exit(0);
  }
  const symbol = advisory ? "⚠️ " : "❌";
  const header = advisory
    ? `[lexicon-gate] ${symbol} ADVISORY — ${violations.length} non-LOCKED term(s) found `
      + `(zero LOCKED terms in lexicon; gate is in pre-review mode):`
    : `[lexicon-gate] ${symbol} ${violations.length} violation(s):`;
  console.error(header);
  for (const v of violations) {
    console.error(`  ${v.file}:${v.line}  term="${v.term}" english="${v.english}" status=${v.status}`);
  }
  if (advisory) {
    console.error(
      "\nAdvisory mode: this run exits 0 so CI doesn't block the build. "
      + "Once Prof. Amjad signs off on the first batch of LOCKED terms in "
      + "docs/content/arabic-math-lexicon.md, the gate will automatically "
      + "flip to enforcing.\n"
    );
    process.exit(0);
  }
  console.error(
    "\nEvery lexicon term used in student-facing production code must be "
    + "LOCKED in docs/content/arabic-math-lexicon.md. Either:\n"
    + "  (a) ship the Prof. Amjad review and promote the term to LOCKED, or\n"
    + "  (b) remove the non-LOCKED term from production code paths.\n"
  );
}

process.exit(violations.length === 0 || advisory ? 0 : 1);
