#!/usr/bin/env node
// =============================================================================
// Ship-gate LLM-routing scanner (ADR-0026, prr-004)
//
// Walks src/**/*.cs and reports every class that looks like an LLM call site
// but does NOT carry a [TaskRouting("tierN", "task-name")] attribute.
//
// Heuristics for "LLM call site":
//   (a) the file references `Anthropic`, `AnthropicClient`, `OpenAi`, or the
//       ILlmClient interface;
//   (b) the class name ends in LlmService, LlmClient, or LlmGenerator;
//   (c) the file instantiates AnthropicClient directly (new AnthropicClient).
//
// A class is considered tagged if [TaskRouting(...)] appears anywhere in the
// source file within 6 lines above a `public ... class <Name>` declaration.
// (Attribute-to-class proximity check — not a full C# parse, deliberately
// simple per the rulepack-scan.mjs precedent.)
//
// Allowlist: scripts/shipgate/llm-routing-allowlist.yml contains paths that
// are LLM-adjacent but must not be tagged (abstract interfaces, DI factories,
// test doubles, the router dispatcher, provider adapters, and Epic-B pending
// consumers with TODO(prr-012) justifications).
//
// Exit codes:
//   0 — clean, OR advisory mode (violations surfaced but not enforced)
//   1 — one or more violations (strict mode)
//   2 — scanner configuration error (allowlist missing/malformed, etc.)
//
// Flags:
//   --advisory            Surface violations but exit 0 (CI-friendly soft launch)
//   --strict              Fail on any violation (default for Epic B completion)
//   --json                Machine-readable report
//   --quiet               Suppress non-violation output
//
// Usage:
//   node scripts/shipgate/llm-routing-scanner.mjs --advisory
//   node scripts/shipgate/llm-routing-scanner.mjs --strict --json
// =============================================================================

import { readFileSync, readdirSync, statSync, existsSync } from "fs";
import { resolve, relative, sep } from "path";

const ROOT = resolve(import.meta.dirname, "../..");

const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const ADVISORY = flag("--advisory");
const STRICT = flag("--strict") || !ADVISORY;
const JSON_OUT = flag("--json");
const QUIET = flag("--quiet");
const FIXTURE_MODE = flag("--fixture-mode");

// ---------------------------------------------------------------------------
// Minimal YAML allowlist loader
// ---------------------------------------------------------------------------

function loadAllowlist() {
  const path = resolve(ROOT, "scripts/shipgate/llm-routing-allowlist.yml");
  if (!existsSync(path)) {
    return { entries: [], error: `allowlist missing at ${path}` };
  }
  const text = readFileSync(path, "utf8");
  const entries = [];
  let cur = null;
  for (const raw of text.split("\n")) {
    const line = raw.replace(/\s+$/, "");
    if (!line.trim() || line.trim().startsWith("#")) continue;
    const itemMatch = line.match(/^\s*-\s+path:\s+"(.+)"\s*$/);
    if (itemMatch) {
      if (cur) entries.push(cur);
      cur = { path: itemMatch[1], reason: "" };
      continue;
    }
    if (!cur) continue;
    const reasonMatch = line.match(/^\s+reason:\s+"(.+)"\s*$/);
    if (reasonMatch) { cur.reason = reasonMatch[1]; continue; }
  }
  if (cur) entries.push(cur);
  return { entries, error: null };
}

function isAllowlisted(relPath, allowlist) {
  const normalized = relPath.replace(/\\/g, "/");
  for (const entry of allowlist) {
    const p = entry.path.replace(/\\/g, "/");
    if (p.endsWith("/")) {
      if (normalized.startsWith(p)) return entry;
    } else if (normalized === p) {
      return entry;
    }
  }
  return null;
}

// ---------------------------------------------------------------------------
// File walker (src/**/*.cs, excluding bin/obj and test projects unless fixture)
// ---------------------------------------------------------------------------

function walkCs(dir, acc = []) {
  let items;
  try {
    items = readdirSync(dir);
  } catch {
    return acc;
  }
  for (const item of items) {
    const full = resolve(dir, item);
    let st;
    try { st = statSync(full); } catch { continue; }
    if (st.isDirectory()) {
      if (item === "bin" || item === "obj" || item === "node_modules" || item === ".git") continue;
      walkCs(full, acc);
    } else if (item.endsWith(".cs")) {
      acc.push(full);
    }
  }
  return acc;
}

// ---------------------------------------------------------------------------
// Detection heuristics
// ---------------------------------------------------------------------------

const LLM_SIGNALS = [
  /\busing\s+Anthropic\b/,
  /\bAnthropicClient\b/,
  /\busing\s+OpenAi\b/i,
  /\bILlmClient\b/,
  /\bnew\s+AnthropicClient\b/,
];

const CLASS_NAME_SIGNALS = [
  /\bclass\s+\w*LlmService\b/,
  /\bclass\s+\w*LlmClient\b/,
  /\bclass\s+\w*LlmGenerator\b/,
];

function isLlmCallSite(source) {
  return LLM_SIGNALS.some((re) => re.test(source))
    || CLASS_NAME_SIGNALS.some((re) => re.test(source));
}

// Attribute-to-class proximity check — [TaskRouting(...)] within 6 lines above
// the first matching `class` declaration.
function hasTaskRoutingAttribute(source) {
  const lines = source.split("\n");
  const classLines = [];
  for (let i = 0; i < lines.length; i++) {
    if (/^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+\w+/.test(lines[i])) {
      classLines.push(i);
    }
  }
  if (classLines.length === 0) return false;

  // Any attribute within the preceding 6 lines (allowing for docstrings / blank lines).
  for (const ln of classLines) {
    const windowStart = Math.max(0, ln - 6);
    for (let j = windowStart; j < ln; j++) {
      if (/\[TaskRouting\s*\(/.test(lines[j])) return true;
    }
  }
  // Also look for method-level attribute — any line with [TaskRouting(...)].
  return /\[TaskRouting\s*\(/.test(source);
}

// ---------------------------------------------------------------------------
// Main scan
// ---------------------------------------------------------------------------

function scan() {
  const { entries: allowlist, error } = loadAllowlist();
  if (error) {
    console.error(`[llm-routing-scanner] ${error}`);
    process.exit(2);
  }

  const srcRoot = FIXTURE_MODE
    ? resolve(ROOT, "shipgate/fixtures/llm-routing")
    : resolve(ROOT, "src");

  if (!existsSync(srcRoot)) {
    if (FIXTURE_MODE) {
      console.error(`[llm-routing-scanner] fixture dir missing: ${srcRoot}`);
      process.exit(2);
    }
    console.error(`[llm-routing-scanner] src dir missing: ${srcRoot}`);
    process.exit(2);
  }

  const files = walkCs(srcRoot);
  const violations = [];
  let scanned = 0;
  let tagged = 0;
  let allowlisted = 0;

  for (const full of files) {
    const rel = relative(ROOT, full).split(sep).join("/");

    // Skip allowlisted paths (but not in fixture mode — we want the fixture to fail).
    if (!FIXTURE_MODE) {
      const hit = isAllowlisted(rel, allowlist);
      if (hit) { allowlisted++; continue; }
    }

    let source;
    try { source = readFileSync(full, "utf8"); } catch { continue; }

    if (!isLlmCallSite(source)) continue;
    scanned++;

    if (hasTaskRoutingAttribute(source)) {
      tagged++;
      continue;
    }

    // Find a representative line number (first class declaration).
    const lines = source.split("\n");
    const lineIdx = lines.findIndex((l) =>
      /^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+\w+/.test(l)
    );

    violations.push({
      file: rel,
      line: lineIdx >= 0 ? lineIdx + 1 : 1,
      message: "Missing [TaskRouting(\"tierN\", \"task-name\")] attribute. "
        + "Either tag the class with a tier matching a row in contracts/llm/routing-config.yaml "
        + "OR add this file to scripts/shipgate/llm-routing-allowlist.yml with a justification.",
    });
  }

  const report = {
    mode: ADVISORY && !STRICT ? "advisory" : "strict",
    scanned,
    tagged,
    allowlisted,
    violations,
  };

  if (JSON_OUT) {
    process.stdout.write(JSON.stringify(report, null, 2) + "\n");
  } else if (!QUIET) {
    console.log(`[llm-routing-scanner] mode=${report.mode} scanned=${scanned} tagged=${tagged} allowlisted=${allowlisted} violations=${violations.length}`);
    for (const v of violations) {
      console.log(`  ${v.file}:${v.line}  ${v.message}`);
    }
  }

  if (violations.length > 0 && !ADVISORY) {
    process.exit(1);
  }
  process.exit(0);
}

scan();
