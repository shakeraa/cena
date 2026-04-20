#!/usr/bin/env node
// =============================================================================
// Ship-gate rule-pack scanner (EPIC-PRR-D D1+D2)
//
// Reads YAML rule packs from scripts/shipgate/*.yml, scans doc + locale + UI
// source paths for matches, and reports violations. Complements the baked-in
// rules in scan.mjs by externalising two larger rule packs (banned-citations,
// banned-mechanics) so they can evolve via YAML edits without touching the
// JS scanner.
//
// The YAML parser is intentionally minimal — the rule packs use a
// deliberately constrained subset (top-level `rules:` or `whitelist:` list,
// scalar values, double-quoted strings, two-space indent). Do not stretch
// the parser to handle arbitrary YAML; if you need more, add js-yaml.
//
// Exit codes:
//   0 — clean
//   1 — one or more violations found
//   2 — a rule-pack file failed to load
//
// Flags:
//   --pack=<name>         Only run one pack (citations | mechanics)
//   --fixture-mode        Scan ONLY the fixture files; used in CI tests.
//   --json                Machine-readable output
//   --quiet               Suppress header banners
//
// Usage:
//   node scripts/shipgate/rulepack-scan.mjs
//   node scripts/shipgate/rulepack-scan.mjs --pack=citations --fixture-mode
// =============================================================================

import { readFileSync, existsSync, statSync } from "fs";
import { resolve, relative, sep } from "path";
import { globSync } from "fs";

const ROOT = resolve(import.meta.dirname, "../..");

const args = process.argv.slice(2);
const flag = (name) => args.some((a) => a === name || a.startsWith(`${name}=`));
const flagValue = (name) => {
  const a = args.find((a) => a.startsWith(`${name}=`));
  return a ? a.slice(name.length + 1) : null;
};

const PACK_FILTER = flagValue("--pack");
const FIXTURE_MODE = flag("--fixture-mode");
const JSON_OUT = flag("--json");
const QUIET = flag("--quiet");
const ADVISORY = flag("--advisory");
const STRICT = flag("--strict");

// ---------------------------------------------------------------------------
// Minimal YAML loader (constrained subset — see header)
// ---------------------------------------------------------------------------
// Supports:
//   top-level key: (list-of-mappings | list-of-scalars)
//   list items: "- key: value" with following "  key: value" indented by 4
//   quoted string values: "..." unescaped literally (YAML-ish: \\ -> \, \" -> ")
//   unquoted scalars: stripped
// Rejects: anchors, aliases, flow-style, multiline folded, block literals, etc.
// ---------------------------------------------------------------------------

function loadYaml(path) {
  const text = readFileSync(path, "utf8");
  const lines = text.split("\n");
  const result = {};
  let topKey = null;
  let curItem = null;
  const list = [];

  const unquote = (v) => {
    let s = v.trim();
    if (s.startsWith('"') && s.endsWith('"')) {
      s = s.slice(1, -1);
      // Unescape \" and \\ (enough for our rule packs)
      s = s.replace(/\\"/g, '"').replace(/\\\\/g, "\\");
      return s;
    }
    if (s.startsWith("'") && s.endsWith("'")) {
      return s.slice(1, -1).replace(/''/g, "'");
    }
    return s;
  };

  let i = 0;
  while (i < lines.length) {
    const raw = lines[i];
    const stripped = raw.replace(/\s+$/, "");
    i++;

    // Skip comments / blanks
    if (!stripped.trim() || stripped.trim().startsWith("#")) continue;

    // Top-level key: `rules:` or `whitelist:` etc.
    const topMatch = stripped.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*$/);
    if (topMatch) {
      // Flush previous list
      if (topKey && list.length) {
        if (curItem) list.push(curItem);
        result[topKey] = list.slice();
        list.length = 0;
        curItem = null;
      }
      topKey = topMatch[1];
      curItem = null;
      continue;
    }

    // New list item at 2-space indent: "  - key: value"
    const itemMatch = stripped.match(/^  -\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$/);
    if (itemMatch && topKey) {
      if (curItem) list.push(curItem);
      curItem = {};
      curItem[itemMatch[1]] = unquote(itemMatch[2]);
      continue;
    }

    // Nested key inside a list item: "      key: value" (6 spaces)
    const nestedMatch = stripped.match(/^ {6,}([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$/);
    if (nestedMatch && curItem) {
      const key = nestedMatch[1];
      const value = nestedMatch[2];
      if (value === "") {
        // Begin a nested mapping (e.g. `near:`)
        curItem[key] = {};
        curItem.__nested = key;
      } else if (curItem.__nested) {
        // Inside a nested mapping one level deeper — still nestedMatch applies;
        // the nested mapping receives this key/value.
        curItem[curItem.__nested][key] = unquote(value);
      } else {
        curItem[key] = unquote(value);
      }
      continue;
    }

    // Continuation at 4-space indent: "    key: value" — top-level key of list item
    const flatMatch = stripped.match(/^ {4}([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$/);
    if (flatMatch && curItem) {
      const key = flatMatch[1];
      const value = flatMatch[2];
      // Leaving any nested mapping context when returning to 4-space indent
      delete curItem.__nested;
      if (value === "") {
        curItem[key] = {};
        curItem.__nested = key;
      } else {
        curItem[key] = unquote(value);
      }
      continue;
    }
  }
  if (curItem) list.push(curItem);
  if (topKey) result[topKey] = list;

  // Strip __nested markers
  for (const key of Object.keys(result)) {
    if (Array.isArray(result[key])) {
      for (const item of result[key]) delete item.__nested;
    }
  }
  return result;
}

// ---------------------------------------------------------------------------
// Rule compilation
// ---------------------------------------------------------------------------

function compileRule(rule, packName) {
  const flags = rule.flags || "i";
  const compiled = { id: rule.id, reason: rule.reason, finding: rule.finding, locale: rule.locale, pack: packName };

  if (rule.pattern) {
    try {
      compiled.pattern = new RegExp(rule.pattern, flags);
    } catch (e) {
      throw new Error(`${packName}/${rule.id}: bad pattern ${rule.pattern}: ${e.message}`);
    }
  } else if (rule.near) {
    const nearFlags = rule.near.flags || flags;
    try {
      compiled.nearA = new RegExp(rule.near.a, nearFlags);
      compiled.nearB = new RegExp(rule.near.b, nearFlags);
    } catch (e) {
      throw new Error(`${packName}/${rule.id}: bad near regex: ${e.message}`);
    }
  } else {
    throw new Error(`${packName}/${rule.id}: rule must have 'pattern' or 'near'`);
  }
  return compiled;
}

// ---------------------------------------------------------------------------
// Whitelist matching (glob-ish, supports ** and *)
// ---------------------------------------------------------------------------

function globToRegex(glob) {
  // Convert simple globs to regex: ** -> .*, * -> [^/]*, literal . -> \.
  let re = "";
  let i = 0;
  while (i < glob.length) {
    const c = glob[i];
    if (c === "*") {
      if (glob[i + 1] === "*") {
        re += ".*";
        i += 2;
        if (glob[i] === "/") i++;
      } else {
        re += "[^/]*";
        i++;
      }
    } else if (c === "?") {
      re += ".";
      i++;
    } else if ("+.()|{}[]^$\\".includes(c)) {
      re += "\\" + c;
      i++;
    } else {
      re += c;
      i++;
    }
  }
  return new RegExp("^" + re + "$");
}

function isWhitelisted(relPath, whitelist) {
  const normalized = relPath.split(sep).join("/");
  for (const entry of whitelist) {
    if (globToRegex(entry.path).test(normalized)) return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// Locale detection (for locale-scoped rules)
// ---------------------------------------------------------------------------

function detectLocale(filepath) {
  if (/[\\/]he\.json$/.test(filepath) || /[\\/]he[\\/]/.test(filepath)) return "he";
  if (/[\\/]ar\.json$/.test(filepath) || /[\\/]ar[\\/]/.test(filepath)) return "ar";
  if (/[\\/]en\.json$/.test(filepath) || /[\\/]en[\\/]/.test(filepath)) return "en";
  return null;
}

// ---------------------------------------------------------------------------
// Target file patterns
// ---------------------------------------------------------------------------

// Production surfaces scanned by BOTH packs.
const PRODUCTION_GLOBS = [
  // Student + admin i18n bundles (all locales)
  "src/student/full-version/src/plugins/i18n/locales/*.json",
  "src/admin/full-version/src/plugins/i18n/locales/*.json",
  // Vue templates and TS source
  "src/student/full-version/src/**/*.vue",
  "src/student/full-version/src/**/*.ts",
  "src/admin/full-version/src/**/*.vue",
  "src/admin/full-version/src/**/*.ts",
  // C# backend (prompts, hard-coded strings)
  "src/actors/**/*.cs",
  "src/api/**/*.cs",
  "src/shared/**/*.cs",
  // Feature specs that feed production copy
  "docs/feature-specs/**/*.md",
  "docs/engineering/**/*.md",
  "docs/design/**/*.md",
];

const EXCLUDE_REGEXES = [
  /[\\/]bin[\\/]/, /[\\/]obj[\\/]/, /[\\/]node_modules[\\/]/, /[\\/]dist[\\/]/,
  /[\\/]Tests[\\/]/, /[\\/]tests[\\/]/, /[\\/]tests-mock[\\/]/,
  /[\\/]fixtures[\\/]/, /[\\/]Generated[\\/]/,
];

// Fixture-mode: scan ONLY the positive-test fixture files.
const FIXTURE_FILES = {
  citations: "shipgate/fixtures/banned-citation-sample.md",
  mechanics: "shipgate/fixtures/banned-mechanics-sample.md",
  "effect-size": "shipgate/fixtures/effect-size-citations-sample.md",
};

function collectFiles() {
  if (FIXTURE_MODE) {
    const out = [];
    for (const key of Object.keys(FIXTURE_FILES)) {
      if (PACK_FILTER && PACK_FILTER !== key) continue;
      const p = resolve(ROOT, FIXTURE_FILES[key]);
      if (existsSync(p)) out.push(p);
    }
    return out;
  }
  const seen = new Set();
  for (const pattern of PRODUCTION_GLOBS) {
    for (const match of globSync(resolve(ROOT, pattern), { nodir: true })) {
      if (EXCLUDE_REGEXES.some((rx) => rx.test(match))) continue;
      seen.add(match);
    }
  }
  return [...seen];
}

// ---------------------------------------------------------------------------
// Rule-pack registry
// ---------------------------------------------------------------------------

const PACKS = [
  {
    name: "citations",
    rulesFile: "scripts/shipgate/banned-citations.yml",
    whitelistFile: "scripts/shipgate/banned-citations-whitelist.yml",
  },
  {
    name: "mechanics",
    rulesFile: "scripts/shipgate/banned-mechanics.yml",
    whitelistFile: "scripts/shipgate/banned-mechanics-whitelist.yml",
  },
  {
    name: "effect-size",
    rulesFile: "scripts/shipgate/effect-size-citations.yml",
    whitelistFile: "scripts/shipgate/effect-size-citations-whitelist.yml",
  },
];

function loadPack(pack) {
  const rulesPath = resolve(ROOT, pack.rulesFile);
  const wlPath = resolve(ROOT, pack.whitelistFile);
  if (!existsSync(rulesPath)) {
    console.error(`[rulepack-scan] missing rule pack: ${pack.rulesFile}`);
    process.exit(2);
  }
  const rulesDoc = loadYaml(rulesPath);
  const rules = (rulesDoc.rules || []).map((r) => compileRule(r, pack.name));
  let whitelist = [];
  if (existsSync(wlPath)) {
    const wlDoc = loadYaml(wlPath);
    whitelist = wlDoc.whitelist || [];
  }
  return { name: pack.name, rules, whitelist };
}

// ---------------------------------------------------------------------------
// Scan
// ---------------------------------------------------------------------------

function scanFile(filepath, pack) {
  let content;
  try {
    content = readFileSync(filepath, "utf8");
  } catch {
    return [];
  }
  const rel = relative(ROOT, filepath);
  // In fixture-mode we INTENTIONALLY scan the whitelisted fixture files to
  // prove every rule fires — whitelist is bypassed.
  if (!FIXTURE_MODE && isWhitelisted(rel, pack.whitelist)) return [];
  const fileLocale = detectLocale(filepath);
  const lines = content.split("\n");
  const hits = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    for (const rule of pack.rules) {
      // Locale-scoped rules only apply to matching locale files (or any file
      // when no locale is detected — e.g. Vue templates quoting Hebrew copy).
      if (rule.locale && fileLocale && rule.locale !== fileLocale) continue;

      if (rule.pattern) {
        const m = line.match(rule.pattern);
        if (m) {
          hits.push({
            pack: pack.name,
            ruleId: rule.id,
            file: rel,
            line: i + 1,
            match: m[0],
            reason: rule.reason,
            context: line.trim().slice(0, 160),
          });
        }
      } else if (rule.nearA && rule.nearB) {
        if (rule.nearA.test(line) && rule.nearB.test(line)) {
          hits.push({
            pack: pack.name,
            ruleId: rule.id,
            file: rel,
            line: i + 1,
            match: line.match(rule.nearA)?.[0] ?? "",
            reason: rule.reason,
            context: line.trim().slice(0, 160),
          });
        }
      }
    }
  }
  return hits;
}

function runPack(pack, files) {
  const allHits = [];
  for (const f of files) {
    allHits.push(...scanFile(f, pack));
  }
  return allHits;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const files = collectFiles();
const results = [];
for (const packDef of PACKS) {
  if (PACK_FILTER && PACK_FILTER !== packDef.name) continue;
  const pack = loadPack(packDef);
  const hits = runPack(pack, files);
  results.push({ pack, hits });
}

const total = results.reduce((n, r) => n + r.hits.length, 0);

if (JSON_OUT) {
  console.log(JSON.stringify({
    filesScanned: files.length,
    packs: results.map((r) => ({
      name: r.pack.name,
      ruleCount: r.pack.rules.length,
      violations: r.hits,
    })),
    totalViolations: total,
  }, null, 2));
  process.exit(total === 0 ? 0 : 1);
}

if (!QUIET) {
  const fixtureNote = FIXTURE_MODE ? " [fixture-mode]" : "";
  console.log(`[rulepack-scan] scanned ${files.length} files across ${results.length} pack(s)${fixtureNote}`);
  for (const r of results) {
    console.log(`  pack=${r.pack.name}  rules=${r.pack.rules.length}  violations=${r.hits.length}`);
  }
}

if (total === 0) {
  if (!QUIET) console.log("[rulepack-scan] clean");
  process.exit(0);
}

console.error(`[rulepack-scan] ${total} violation(s):\n`);
for (const r of results) {
  for (const v of r.hits) {
    console.error(`  [${v.pack}/${v.ruleId}] ${v.file}:${v.line}  "${v.match}"`);
    console.error(`    ${v.reason}`);
    console.error(`    ${v.context}\n`);
  }
}
console.error(
  "If a legitimate use was flagged, add the file's path to the relevant\n"
  + "whitelist file (banned-citations-whitelist.yml or\n"
  + "banned-mechanics-whitelist.yml) with a one-line reason.\n"
);

// Advisory mode: surfaces violations in CI logs but exits 0. Used on landing
// while follow-up tasks catalogued in the historical-scan reports are being
// worked. Flip to enforcing (default) once the backlog is zero.
if (ADVISORY && !STRICT) {
  console.error("[rulepack-scan] advisory mode — exiting 0 despite violations.\n");
  process.exit(0);
}
process.exit(1);
