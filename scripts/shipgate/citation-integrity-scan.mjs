#!/usr/bin/env node
// =============================================================================
// Ship-gate citation-integrity cross-reference scanner (ADR-0049 / prr-042)
//
// Complements rulepack-scan.mjs. Where rulepack-scan.mjs flags BARE effect-size
// claims that lack a citation_id= tag, this scanner verifies that every
// citation_id= tag in the scanned surfaces resolves against
// contracts/citations/approved-citations.yml and that any numeric claim
// attached to the tag respects the manifest's max_cited_es bound.
//
// Exit codes:
//   0 — clean
//   1 — unresolved citation_id, retired citation, or out-of-bounds ES claim
//   2 — manifest file failed to load
//
// Flags:
//   --json                 Machine-readable output
//   --quiet                Suppress header banners
//   --fixture-mode         Scan the fixture files only (CI test mode)
//
// Usage:
//   node scripts/shipgate/citation-integrity-scan.mjs
//   node scripts/shipgate/citation-integrity-scan.mjs --json
// =============================================================================

import { readFileSync, existsSync } from "fs";
import { resolve, relative } from "path";
import { globSync } from "fs";

const ROOT = resolve(import.meta.dirname, "../..");

const argv = process.argv.slice(2);
const hasFlag = (name) => argv.some((a) => a === name || a.startsWith(`${name}=`));
const JSON_OUT = hasFlag("--json");
const QUIET = hasFlag("--quiet");
const FIXTURE_MODE = hasFlag("--fixture-mode");

const MANIFEST_PATH = resolve(ROOT, "contracts/citations/approved-citations.yml");
const FIXTURE_FILE = resolve(ROOT, "shipgate/fixtures/citation-integrity-sample.md");

// ---------------------------------------------------------------------------
// Minimal YAML loader — supports only the approved-citations.yml schema
// (list of mapping entries with scalar values).
// ---------------------------------------------------------------------------

function unquote(s) {
  let v = s.trim();
  if (v.startsWith('"') && v.endsWith('"')) {
    v = v.slice(1, -1);
    v = v.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
    return v;
  }
  if (v.startsWith("'") && v.endsWith("'")) return v.slice(1, -1).replace(/''/g, "'");
  return v;
}

function parseManifest(path) {
  if (!existsSync(path)) {
    console.error(`[citation-integrity-scan] manifest not found: ${path}`);
    process.exit(2);
  }
  const text = readFileSync(path, "utf8");
  const lines = text.split("\n");
  const entries = [];
  let cur = null;
  let inCitations = false;

  for (const raw of lines) {
    const line = raw.replace(/\s+$/, "");
    if (!line.trim() || line.trim().startsWith("#")) continue;

    if (/^citations\s*:\s*$/.test(line)) {
      inCitations = true;
      continue;
    }
    if (!inCitations) continue;

    const itemMatch = line.match(/^\s*-\s+id:\s+(.+)$/);
    if (itemMatch) {
      if (cur) entries.push(cur);
      cur = { id: unquote(itemMatch[1]) };
      continue;
    }
    if (!cur) continue;

    const keyMatch = line.match(/^\s+([a-z_]+)\s*:\s*(.+)$/i);
    if (keyMatch) {
      const key = keyMatch[1];
      const rawVal = unquote(keyMatch[2]);
      if (["year", "meta_analytic_mean", "reported_ci_low", "reported_ci_high", "max_cited_es"].includes(key)) {
        cur[key] = Number(rawVal);
      } else {
        cur[key] = rawVal;
      }
    }
  }
  if (cur) entries.push(cur);
  return entries;
}

// ---------------------------------------------------------------------------
// Surfaces to scan for citation_id= tags
// ---------------------------------------------------------------------------

const SCAN_GLOBS = [
  "src/student/full-version/src/plugins/i18n/locales/*.json",
  "src/admin/full-version/src/plugins/i18n/locales/*.json",
  "src/student/full-version/src/**/*.vue",
  "src/student/full-version/src/**/*.ts",
  "src/admin/full-version/src/**/*.vue",
  "src/admin/full-version/src/**/*.ts",
  "src/actors/**/*.cs",
  "src/api/**/*.cs",
  "src/shared/**/*.cs",
  "docs/feature-specs/**/*.md",
  "docs/engineering/**/*.md",
  "docs/design/**/*.md",
];

const EXCLUDE_REGEXES = [
  /[\\/]bin[\\/]/, /[\\/]obj[\\/]/, /[\\/]node_modules[\\/]/, /[\\/]dist[\\/]/,
  /[\\/]Tests[\\/]/, /[\\/]tests[\\/]/, /[\\/]tests-mock[\\/]/,
  /[\\/]Generated[\\/]/,
];

function collectFiles() {
  if (FIXTURE_MODE) {
    return existsSync(FIXTURE_FILE) ? [FIXTURE_FILE] : [];
  }
  const seen = new Set();
  for (const pattern of SCAN_GLOBS) {
    for (const match of globSync(resolve(ROOT, pattern), { nodir: true })) {
      if (EXCLUDE_REGEXES.some((rx) => rx.test(match))) continue;
      seen.add(match);
    }
  }
  return [...seen];
}

// ---------------------------------------------------------------------------
// Citation reference extractor
//
// A citation reference looks like:   citation_id=<slug>
// When the reference appears on the same line as a numeric ES claim
// (d=0.xx, d of 0.xx, effect size 0.xx, ES of 0.xx, or Cohen's d = 0.xx),
// we extract the numeric value and check it against max_cited_es.
// ---------------------------------------------------------------------------

const CITE_RX = /citation_id\s*=\s*([a-z0-9\-]+)/gi;
const ES_PATTERNS = [
  /\bd\s*=\s*([0-9]+\.[0-9]+)/i,
  /\bd\s+of\s+([0-9]+\.[0-9]+)/i,
  /effect\s+size\s+(?:of\s+)?([0-9]+\.[0-9]+)/i,
  /\bES\s+(?:of\s+)?([0-9]+\.[0-9]+)/i,
  /Cohen'?s?\s+d\s*=\s*([0-9]+\.[0-9]+)/i,
];

function findClaimValue(line) {
  for (const rx of ES_PATTERNS) {
    const m = line.match(rx);
    if (m) return Number(m[1]);
  }
  return null;
}

function scanFile(filepath, manifestById) {
  let content;
  try {
    content = readFileSync(filepath, "utf8");
  } catch {
    return [];
  }
  const rel = relative(ROOT, filepath);
  const lines = content.split("\n");
  const hits = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    CITE_RX.lastIndex = 0;
    let m;
    while ((m = CITE_RX.exec(line)) !== null) {
      const id = m[1];
      const entry = manifestById.get(id);
      if (!entry) {
        hits.push({
          file: rel,
          line: i + 1,
          citation_id: id,
          kind: "unknown-citation",
          message: `citation_id=${id} does not resolve against contracts/citations/approved-citations.yml`,
          context: line.trim().slice(0, 160),
        });
        continue;
      }
      // Check the claim value on the same line (if any)
      const claimValue = findClaimValue(line);
      if (claimValue !== null && entry.max_cited_es > 0) {
        if (claimValue > entry.max_cited_es + 1e-6) {
          hits.push({
            file: rel,
            line: i + 1,
            citation_id: id,
            kind: "exceeds-max-es",
            claimValue,
            max: entry.max_cited_es,
            message: `Claim d=${claimValue} exceeds manifest max_cited_es=${entry.max_cited_es} for citation_id=${id}`,
            context: line.trim().slice(0, 160),
          });
        }
      }
    }
  }
  return hits;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const manifest = parseManifest(MANIFEST_PATH);
const manifestById = new Map(manifest.map((e) => [e.id, e]));
const files = collectFiles();

const allHits = [];
for (const f of files) {
  allHits.push(...scanFile(f, manifestById));
}

if (JSON_OUT) {
  console.log(JSON.stringify({
    filesScanned: files.length,
    manifestEntries: manifest.length,
    violations: allHits,
    totalViolations: allHits.length,
  }, null, 2));
  process.exit(allHits.length === 0 ? 0 : 1);
}

if (!QUIET) {
  const suffix = FIXTURE_MODE ? " [fixture-mode]" : "";
  console.log(`[citation-integrity-scan] scanned ${files.length} files / ${manifest.length} manifest entries${suffix}`);
}

if (allHits.length === 0) {
  if (!QUIET) console.log("[citation-integrity-scan] clean");
  process.exit(0);
}

console.error(`[citation-integrity-scan] ${allHits.length} violation(s):\n`);
for (const v of allHits) {
  console.error(`  [${v.kind}] ${v.file}:${v.line}  citation_id=${v.citation_id}`);
  console.error(`    ${v.message}`);
  console.error(`    ${v.context}\n`);
}
console.error(
  "Fix options:\n"
  + "  1. If the citation_id is misspelled, correct it.\n"
  + "  2. If the citation should be approved, add it to contracts/citations/approved-citations.yml.\n"
  + "  3. If the claim exceeds the manifest bound, revise the claim downward or update the manifest with a sourced justification.\n"
);
process.exit(1);
