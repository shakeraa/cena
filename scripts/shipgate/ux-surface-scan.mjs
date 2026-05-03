#!/usr/bin/env node
// =============================================================================
// Ship-gate UX-surface scanner v2 (prr-211, EPIC-PRR-E)
//
// Reads scripts/shipgate/shipgate-ux-surfaces.yml and scans the five named
// UX surfaces (HintLadder / StepSolverCard / Sidekick / MathInput /
// FreeBodyDiagramConstruct) plus any file under
// src/{student,admin}/full-version/src/{components,views}/**.vue that
// embeds one of them, for DOM-aware banned patterns that the existing
// string-only rulepack-scan.mjs cannot express.
//
// Rule kinds: string-ban | dom-coupled-class | emoji-in-rung | aria-required
// See the YAML header in shipgate-ux-surfaces.yml for the schema.
//
// Graceful missing-file handling: a surface whose canonical path does not
// yet exist logs a warning and exits 0 ("will be enforced on creation").
// Pass --strict-missing to flip this to a hard failure (exit 2).
//
// Exit codes:  0 clean | 1 violations | 2 YAML load error OR strict-missing
// Flags:       --fixture-mode --json --quiet --strict-missing
// =============================================================================

import { readFileSync, existsSync } from "fs";
import { resolve, relative, sep } from "path";
import { globSync } from "fs";

const ROOT = resolve(import.meta.dirname, "../..");
const RULES_YAML = resolve(ROOT, "scripts/shipgate/shipgate-ux-surfaces.yml");
const FIXTURE_PATH = resolve(ROOT, "shipgate/fixtures/ux-surfaces-sample.vue");

const argv = process.argv.slice(2);
const flag = (n) => argv.some((a) => a === n || a.startsWith(`${n}=`));
const FIXTURE_MODE = flag("--fixture-mode");
const JSON_OUT = flag("--json");
const QUIET = flag("--quiet");
const STRICT_MISSING = flag("--strict-missing");

// Minimal YAML loader — constrained schema. Lines are: comment, blank,
// top-level key, 2-space list-item opener (dash), or 4-space continuation
// (key: value). See scripts/shipgate/shipgate-ux-surfaces.yml header.

function unquote(raw) {
  const s = raw.trim();
  if (s.startsWith('"') && s.endsWith('"')) {
    return s.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, "\\");
  }
  if (s.startsWith("'") && s.endsWith("'")) {
    return s.slice(1, -1).replace(/''/g, "'");
  }
  return s;
}

function loadYaml(path) {
  const text = readFileSync(path, "utf8");
  const lines = text.split("\n");
  const result = { surfaces: [], rules: [] };
  let topKey = null;
  let curItem = null;

  const flush = () => {
    if (curItem && topKey) {
      result[topKey].push(curItem);
      curItem = null;
    }
  };

  for (const rawLine of lines) {
    const line = rawLine.replace(/\s+$/, "");
    if (!line.trim() || line.trim().startsWith("#")) continue;

    const topMatch = line.match(/^([A-Za-z_][A-Za-z0-9_-]*)\s*:\s*$/);
    if (topMatch) {
      flush();
      topKey = topMatch[1];
      if (!result[topKey]) result[topKey] = [];
      continue;
    }

    const itemMatch = line.match(/^  -\s+([A-Za-z_][A-Za-z0-9_-]*)\s*:\s*(.*)$/);
    if (itemMatch && topKey) {
      flush();
      curItem = {};
      curItem[itemMatch[1]] = unquote(itemMatch[2]);
      continue;
    }

    const contMatch = line.match(/^ {4}([A-Za-z_][A-Za-z0-9_-]*)\s*:\s*(.*)$/);
    if (contMatch && curItem) {
      curItem[contMatch[1]] = unquote(contMatch[2]);
      continue;
    }
  }
  flush();
  return result;
}

// Rule compilation — translate a YAML rule row into compiled regexes.
function compileRule(rule) {
  const flags = rule.flags || "i";
  const compiled = { ...rule };

  if (rule.kind === "string-ban" && rule.pattern) {
    compiled.patternRx = new RegExp(rule.pattern, flags);
  }
  if (rule.kind === "dom-coupled-class") {
    if (!rule["dom-anchor"] || !rule["banned-classes"]) {
      throw new Error(`rule ${rule.id}: dom-coupled-class requires dom-anchor and banned-classes`);
    }
    compiled.anchorRx = new RegExp(rule["dom-anchor"], flags);
    compiled.bannedTokens = rule["banned-classes"].split(/\s+/).filter(Boolean);
  }
  if (rule.kind === "aria-required") {
    if (!rule["aria-target"] || !rule["aria-required"]) {
      throw new Error(`rule ${rule.id}: aria-required requires aria-target and aria-required`);
    }
    compiled.ariaTargetRx = new RegExp(rule["aria-target"], flags);
    compiled.ariaMarkerRx = new RegExp(rule["aria-required"], flags);
  }
  if (rule.kind === "emoji-in-rung") {
    compiled.rung = Number(rule.rung) || null;
  }
  return compiled;
}

// Emoji detection — conservative classifier. Math operators (U+2200..),
// arrows (U+2190..21FF), technical symbols (U+2300..23FF) are INTENTIONALLY
// excluded — they appear legitimately in physics/math content.

const EMOJI_RX = new RegExp(
  "[" +
    "\\u{1F300}-\\u{1F6FF}" + // Misc pictographs + transport
    "\\u{1F900}-\\u{1F9FF}" + // Supplemental symbols
    "\\u{1FA70}-\\u{1FAFF}" + // Extended pictographs
    "\\u{1F600}-\\u{1F64F}" + // Emoticons
    "\\u{2600}-\\u{26FF}" +   // Misc symbols (weather, stars, warning triangle)
    "\\u{2700}-\\u{27BF}" +   // Dingbats (check, cross, sparkles)
    "]",
  "u"
);

// Target file collection.
const MONITORED_GLOBS = [
  "src/student/full-version/src/components/**/*.vue",
  "src/student/full-version/src/views/**/*.vue",
  "src/admin/full-version/src/components/**/*.vue",
  "src/admin/full-version/src/views/**/*.vue",
];

const EXCLUDE_REGEXES = [
  /[\\/]node_modules[\\/]/,
  /[\\/]dist[\\/]/,
  /[\\/]fixtures[\\/]/,
];

function collectCandidateFiles() {
  if (FIXTURE_MODE) {
    return existsSync(FIXTURE_PATH) ? [FIXTURE_PATH] : [];
  }
  const seen = new Set();
  for (const g of MONITORED_GLOBS) {
    for (const m of globSync(resolve(ROOT, g), { nodir: true })) {
      if (EXCLUDE_REGEXES.some((rx) => rx.test(m))) continue;
      seen.add(m);
    }
  }
  return [...seen];
}

// Which surface(s) does this file represent? Canonical path match OR
// embedding via <HintLadder />, <StepSolverCard />, <Sidekick />, etc.
function surfacesPresentIn(filepath, content, surfaces) {
  const rel = relative(ROOT, filepath).split(sep).join("/");
  const hits = new Set();
  for (const s of surfaces) {
    if (rel === s.path) hits.add(s.name);
    const embedRx = new RegExp(`<${s.name}[\\s/>]`);
    if (embedRx.test(content)) hits.add(s.name);
  }
  return hits;
}

// Strip same-line HTML/Vue comments, /* block */ fragments, and JS/TS //
// line comments (only when // is outside a string literal on that line).
// Fixture files must never use these forms to hide a banned pattern —
// ship-visible copy is what we scan, not prose.
function stripCodeAndMarkupComments(line) {
  let s = line.replace(/<!--[\s\S]*?-->/g, "");
  s = s.replace(/\/\*[\s\S]*?\*\//g, "");
  // Heuristic JS/TS // comment stripping: if the // is NOT inside a pair of
  // matching quotes on the same line, drop it and everything after.
  const quoteChars = ['"', "'", "`"];
  let i = 0;
  let inQuote = null;
  while (i < s.length) {
    const ch = s[i];
    if (inQuote) {
      if (ch === "\\") { i += 2; continue; }
      if (ch === inQuote) inQuote = null;
      i++; continue;
    }
    if (quoteChars.includes(ch)) { inQuote = ch; i++; continue; }
    if (ch === "/" && s[i + 1] === "/") { s = s.slice(0, i); break; }
    i++;
  }
  return s;
}

// Replace multi-line HTML comments and JS/TS /* block */ comments with
// same-line blanking so line numbers are preserved. Each commented line
// becomes effectively empty for the scanner but keeps its position.
function blankMultiLineComments(content) {
  return content
    .replace(/<!--[\s\S]*?-->/g, (m) => m.replace(/[^\n]/g, " "))
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "));
}

function scanStringBan(rule, content, rel) {
  const hits = [];
  const prepared = blankMultiLineComments(content);
  const scanLines = prepared.split("\n");
  const origLines = content.split("\n");
  for (let i = 0; i < scanLines.length; i++) {
    // Strip single-line comments too ( // ... ), so the ban policy's own
    // explanations don't trip the scanner. Visible rendered copy is what
    // we care about.
    const scan = stripCodeAndMarkupComments(scanLines[i]);
    const m = scan.match(rule.patternRx);
    if (m) {
      hits.push({
        ruleId: rule.id,
        kind: rule.kind,
        surface: rule.surface,
        file: rel,
        line: i + 1,
        match: m[0],
        reason: rule.reason,
        context: (origLines[i] || "").trim().slice(0, 180),
      });
    }
  }
  return hits;
}

// DOM-coupled: anchor hit on line N + banned token in N-1..N+2 window.
// Tight enough to stay structural; tolerant enough for multi-line attr
// wrapping in long Vue SFCs.
function scanDomCoupledClass(rule, content, rel) {
  const hits = [];
  const lines = content.split("\n");
  for (let i = 0; i < lines.length; i++) {
    if (!rule.anchorRx.test(lines[i])) continue;
    const windowStart = Math.max(0, i - 1);
    const windowEnd = Math.min(lines.length - 1, i + 2);
    for (let j = windowStart; j <= windowEnd; j++) {
      for (const tok of rule.bannedTokens) {
        // Word-boundary for plain identifiers, literal for attr= tokens.
        const needsWordBoundary = /^[A-Za-z0-9_-]+$/.test(tok);
        const escaped = tok.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
        const rx = needsWordBoundary
          ? new RegExp(`\\b${escaped}\\b`)
          : new RegExp(escaped);
        if (rx.test(lines[j])) {
          hits.push({
            ruleId: rule.id,
            kind: rule.kind,
            surface: rule.surface,
            file: rel,
            line: j + 1,
            match: tok,
            anchor: lines[i].trim().slice(0, 80),
            reason: rule.reason,
            context: lines[j].trim().slice(0, 180),
          });
        }
      }
    }
  }
  return hits;
}

// Aria check — anchor line triggers a 60-line window for a marker match.
// Strip <!-- ... --> on each scanned line so commented-out aria references
// don't falsely satisfy the requirement.
function stripHtmlComments(line) {
  return line.replace(/<!--[\s\S]*?-->/g, "");
}

function scanAriaRequired(rule, content, rel) {
  const hits = [];
  const lines = content.split("\n");
  const WINDOW = 60;
  for (let i = 0; i < lines.length; i++) {
    if (!rule.ariaTargetRx.test(lines[i])) continue;
    const windowStart = Math.max(0, i - WINDOW);
    const windowEnd = Math.min(lines.length - 1, i + WINDOW);
    let ariaFound = false;
    for (let j = windowStart; j <= windowEnd && !ariaFound; j++) {
      if (rule.ariaMarkerRx.test(stripHtmlComments(lines[j]))) ariaFound = true;
    }
    if (!ariaFound) {
      hits.push({
        ruleId: rule.id,
        kind: rule.kind,
        surface: rule.surface,
        file: rel,
        line: i + 1,
        match: "(no aria markers in window)",
        reason: rule.reason,
        context: lines[i].trim().slice(0, 180),
      });
    }
  }
  return hits;
}

// Emoji-in-rung — opening marker (rung=N prop OR data-testid="hint-rung-N")
// triggers a forward scan, capped at 40 lines, until the next <VAlert
// boundary. Emoji codepoint in the block = violation.
function scanEmojiInRung(rule, content, rel) {
  const hits = [];
  const lines = content.split("\n");
  const rung = rule.rung;
  const openRungRx = rung
    ? new RegExp(`data-testid="hint-rung-${rung}"|:rung="${rung}"|rung="${rung}"`)
    : new RegExp("data-testid=\"hint-rung-\\d+\"");
  for (let i = 0; i < lines.length; i++) {
    if (!openRungRx.test(lines[i])) continue;
    const end = Math.min(lines.length, i + 40);
    for (let j = i; j < end; j++) {
      if (j > i && /<\s*VAlert[\s>]|<\/\s*VAlert\s*>/.test(lines[j]) && !openRungRx.test(lines[j])) {
        break;
      }
      const m = lines[j].match(EMOJI_RX);
      if (m) {
        hits.push({
          ruleId: rule.id,
          kind: rule.kind,
          surface: rule.surface,
          file: rel,
          line: j + 1,
          match: m[0],
          reason: rule.reason,
          context: lines[j].trim().slice(0, 180),
        });
      }
    }
  }
  return hits;
}

function scanFile(filepath, content, applicableRules) {
  const rel = relative(ROOT, filepath).split(sep).join("/");
  const hits = [];
  for (const rule of applicableRules) {
    if (rule.kind === "string-ban") hits.push(...scanStringBan(rule, content, rel));
    else if (rule.kind === "dom-coupled-class") hits.push(...scanDomCoupledClass(rule, content, rel));
    else if (rule.kind === "aria-required") hits.push(...scanAriaRequired(rule, content, rel));
    else if (rule.kind === "emoji-in-rung") hits.push(...scanEmojiInRung(rule, content, rel));
  }
  return hits;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

if (!existsSync(RULES_YAML)) {
  console.error(`[ux-surface-scan] missing rule pack: ${RULES_YAML}`);
  process.exit(2);
}

const doc = loadYaml(RULES_YAML);
const surfaces = (doc.surfaces || []).map((s) => ({
  name: s.name,
  path: s.path,
  "locale-prefix": s["locale-prefix"],
}));
const rules = (doc.rules || []).map(compileRule);

// Surface-presence audit: report missing surfaces up front.
const surfaceStatus = surfaces.map((s) => ({
  ...s,
  present: existsSync(resolve(ROOT, s.path)),
}));

const missing = surfaceStatus.filter((s) => !s.present);
if (!FIXTURE_MODE && missing.length > 0) {
  if (!QUIET) {
    for (const s of missing) {
      console.warn(
        `[ux-surface-scan] surface not found: ${s.name} (${s.path}) — will be enforced on creation.`
      );
    }
  }
  if (STRICT_MISSING) {
    console.error(`[ux-surface-scan] --strict-missing: ${missing.length} surface(s) absent.`);
    process.exit(2);
  }
}

// Collect files and scan.
const files = collectCandidateFiles();
const allHits = [];

for (const f of files) {
  let content;
  try {
    content = readFileSync(f, "utf8");
  } catch {
    continue;
  }
  // Figure out which surfaces this file represents. Rules that target a
  // surface only run against files that represent that surface (either as
  // the canonical file or as an embedding view).
  const surfacesInFile = FIXTURE_MODE
    ? new Set(surfaces.map((s) => s.name))
    : surfacesPresentIn(f, content, surfaces);
  if (surfacesInFile.size === 0 && !FIXTURE_MODE) continue;

  const applicable = rules.filter(
    (r) => r.surface === "any" || surfacesInFile.has(r.surface)
  );
  allHits.push(...scanFile(f, content, applicable));
}

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------

if (JSON_OUT) {
  console.log(
    JSON.stringify(
      {
        filesScanned: files.length,
        surfaces: surfaceStatus,
        ruleCount: rules.length,
        violations: allHits,
        totalViolations: allHits.length,
      },
      null,
      2
    )
  );
  process.exit(allHits.length === 0 ? 0 : 1);
}

if (!QUIET) {
  const tag = FIXTURE_MODE ? " [fixture-mode]" : "";
  console.log(
    `[ux-surface-scan] scanned ${files.length} file(s) against ${rules.length} rule(s)${tag}`
  );
  for (const s of surfaceStatus) {
    const mark = s.present ? "present" : "MISSING";
    console.log(`  surface=${s.name} (${mark}): ${s.path}`);
  }
}

if (allHits.length === 0) {
  if (!QUIET) console.log("[ux-surface-scan] clean");
  process.exit(0);
}

console.error(`[ux-surface-scan] ${allHits.length} violation(s):\n`);
for (const v of allHits) {
  console.error(
    `  [${v.kind}/${v.ruleId}] ${v.file}:${v.line}  surface=${v.surface}  "${v.match}"`
  );
  console.error(`    ${v.reason}`);
  if (v.anchor) console.error(`    anchor: ${v.anchor}`);
  console.error(`    ${v.context}\n`);
}
console.error(
  "Fix by: rewording the UX copy, removing warning-color classes from rung/step-solver elements,\n" +
  "or adding the appropriate aria markers to hint-ladder regions. See docs/engineering/shipgate.md.\n"
);
process.exit(1);
