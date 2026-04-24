#!/usr/bin/env node
// =============================================================================
// generate-freeze-windows.mjs
//
// Reads contracts/exam-catalog/*.yml and produces ops/release/freeze-windows.yml
// — the canonical source of truth for the CD change-freeze gate enforced by
// .github/workflows/exam-day-freeze.yml.
//
// Source task: prr-016 (freeze gate) + prr-231 (SAT + PET sittings in scope).
//
// Rules:
//   - Every catalog entry with availability in ("launch", "pilot") contributes
//     its sittings to the freeze.
//   - A freeze window spans T-48h to T+6h around the sitting's canonical_date.
//     The sitting time-of-day is assumed to be UTC noon (we do not have the
//     Ministry schedule at sub-day precision; this is the worst-case envelope).
//   - Overlapping windows are merged (union) into a single compound window.
//   - Exam families (Bagrut / Standardized / Other) are tagged per window so
//     downstream consumers can filter (the runbook describes differential
//     levers per family).
//   - Output is deterministic: windows sorted by start_utc ascending.
//
// Zero-dependency: uses an inline YAML subset reader. Catalog YAMLs are simple
// (2-space indent, scalars, lists, no anchors/flow). Any new catalog shape must
// keep within that subset — the architecture test
// `CatalogYamlShapeTests` asserts this.
//
// Exit codes:
//   0 — generated successfully.
//   1 — catalog malformed (missing fields, unparseable YAML).
//   2 — output file write failed.
// =============================================================================

import { readFileSync, readdirSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..');

const CATALOG_DIR = join(REPO_ROOT, 'contracts', 'exam-catalog');
const OUTPUT_DIR = join(REPO_ROOT, 'ops', 'release');
const OUTPUT_FILE = join(OUTPUT_DIR, 'freeze-windows.yml');

const PRE_HOURS = 48; // T-48h window start relative to sitting
const POST_HOURS = 6; // T+6h window end relative to sitting
const SITTING_ANCHOR_HOUR_UTC = 12; // noon UTC — worst-case envelope

const ELIGIBLE_AVAILABILITY = new Set(['launch', 'pilot']);

// ── Narrow YAML reader ──────────────────────────────────────────────────────
// Handles the shape used by contracts/exam-catalog/*.yml:
//   key: scalar
//   key: "quoted"
//   key:
//     - scalar
//     - scalar
//   key:
//     - map_key: scalar
//       map_key: scalar
// No flow collections, anchors, or multi-line blocks.

function stripComment(line) {
  let inSingle = false;
  let inDouble = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === "'" && !inDouble) inSingle = !inSingle;
    else if (ch === '"' && !inSingle) inDouble = !inDouble;
    else if (ch === '#' && !inSingle && !inDouble) {
      // comment requires whitespace before it to avoid clobbering URLs
      if (i === 0 || /\s/.test(line[i - 1])) return line.slice(0, i);
    }
  }
  return line;
}

function parseScalar(raw) {
  const v = raw.trim();
  if (v === '' || v === '~' || v === 'null') return null;
  if (v === 'true') return true;
  if (v === 'false') return false;
  if (/^-?\d+$/.test(v)) return Number(v);
  if (/^-?\d*\.\d+$/.test(v)) return Number(v);
  if (
    (v.startsWith('"') && v.endsWith('"')) ||
    (v.startsWith("'") && v.endsWith("'"))
  ) {
    return v.slice(1, -1);
  }
  return v;
}

/**
 * Parse a YAML document in the narrow subset above. Returns a plain JS object.
 * Throws on shapes outside the subset (e.g. flow collections).
 */
function parseYamlSubset(text) {
  const lines = text.split(/\r?\n/)
    .map((l, i) => ({ raw: l, idx: i, content: stripComment(l) }))
    .filter(l => l.content.trim() !== '');

  // Result node + stack of { indent, node (array or object), kind }
  const root = {};
  const stack = [{ indent: -1, node: root, kind: 'object' }];

  function currentContainer() { return stack[stack.length - 1]; }

  for (const { raw, content, idx } of lines) {
    const indentMatch = content.match(/^(\s*)/);
    const indent = indentMatch[1].length;
    const body = content.slice(indent);

    // Pop to the container at this indent
    while (stack.length > 1 && stack[stack.length - 1].indent >= indent) {
      stack.pop();
    }
    const parent = currentContainer();

    if (body.startsWith('- ')) {
      // list item
      if (parent.kind !== 'array' && parent.kind !== 'pending-array') {
        throw new Error(`Unexpected list item at line ${idx + 1}: ${raw}`);
      }
      if (parent.kind === 'pending-array') {
        parent.kind = 'array';
      }

      const rest = body.slice(2);
      if (rest.includes(':')) {
        // inline mapping start
        const obj = {};
        parent.node.push(obj);
        // Now recurse by creating a new object scope at indent + 2
        const childIndent = indent + 2;
        stack.push({ indent: childIndent - 1, node: obj, kind: 'object' });
        // consume the inline key
        const [k, v] = splitKv(rest);
        handleKv(obj, k, v, childIndent, stack);
      } else {
        parent.node.push(parseScalar(rest));
      }
    } else if (body.startsWith('-')) {
      throw new Error(`Malformed list item at line ${idx + 1}: ${raw}`);
    } else {
      // key: value
      if (parent.kind !== 'object') {
        throw new Error(`Expected key at line ${idx + 1}, got: ${raw}`);
      }
      const [k, v] = splitKv(body);
      handleKv(parent.node, k, v, indent, stack);
    }
  }
  return root;
}

function splitKv(s) {
  const i = s.indexOf(':');
  if (i < 0) throw new Error(`Missing colon in: ${s}`);
  const k = s.slice(0, i).trim();
  const v = s.slice(i + 1);
  return [k, v];
}

function handleKv(obj, key, rawValue, indent, stack) {
  const trimmed = rawValue.trim();
  if (trimmed === '' || trimmed === null) {
    // open a pending container — next non-empty line decides array vs object
    const pending = { };
    // We place a marker that the next deeper line replaces with the right kind.
    // Simpler: peek is hard without the whole line list here, so we push a
    // pending-array (list items start with `-`) and a placeholder object; the
    // branch in the main loop will coerce based on whether it sees `- `.
    // Implementation: push a pending-container; treat object as default but
    // flip on first `- ` encountered.
    obj[key] = [];
    const container = { indent, node: obj[key], kind: 'pending-array', parentObj: obj, parentKey: key, objPlaceholder: pending };
    stack.push(container);
  } else {
    obj[key] = parseScalar(trimmed);
  }
}

// Override: if a `pending-array` receives a non-list child, convert to object.
// To keep the logic tractable, we adjust the main loop to handle that.
// We do this by post-processing the parseYamlSubset function: re-implement
// with a cleaner dispatch.

function parseYamlNarrow(text) {
  // Normalise: strip comments + empty lines while tracking indent.
  const normalised = text.split(/\r?\n/)
    .map(l => stripComment(l))
    .filter(l => l.trim() !== '');

  const root = {};
  // stack frames: { indent, container, kind: 'object' | 'array', pendingForKey: {parent, key} | null }
  const stack = [{ indent: -1, container: root, kind: 'object', pending: null }];

  for (let li = 0; li < normalised.length; li++) {
    const line = normalised[li];
    const indentMatch = line.match(/^(\s*)/);
    const indent = indentMatch[1].length;
    const body = line.slice(indent);

    // Pop frames with indent >= current, but keep the top array frame when
    // the current line is a sibling list item at the same indent (YAML
    // `-` dash indent-neutral rule: list siblings share the parent indent).
    const looksLikeListItem = line.slice(indent).startsWith('- ') || line.slice(indent) === '-';
    while (stack.length > 1) {
      const t = stack[stack.length - 1];
      if (t.indent > indent) { stack.pop(); continue; }
      if (t.indent === indent && !(t.kind === 'array' && looksLikeListItem)) {
        stack.pop();
        continue;
      }
      break;
    }
    let top = stack[stack.length - 1];

    // If the top frame has a pending key (object/array parent didn't know
    // what kind of value to create), resolve based on this line's shape.
    if (top.pending && indent > top.indent) {
      const isList = body.startsWith('- ') || body === '-';
      if (isList) {
        const arr = [];
        top.pending.parent[top.pending.key] = arr;
        const frame = { indent, container: arr, kind: 'array', pending: null };
        stack.push(frame);
        top.pending = null;
        top = frame;
      } else {
        const obj = {};
        top.pending.parent[top.pending.key] = obj;
        const frame = { indent, container: obj, kind: 'object', pending: null };
        stack.push(frame);
        top.pending = null;
        top = frame;
      }
    }

    if (top.kind === 'array') {
      if (!(body.startsWith('- ') || body === '-')) {
        throw new Error(`Expected list item at line ${li + 1}: ${line}`);
      }
      const rest = body === '-' ? '' : body.slice(2);
      if (rest === '') {
        // next line will populate (unusual for our schema; reject)
        throw new Error(`Empty list item at line ${li + 1}: ${line}`);
      }
      if (rest.includes(':') && !isFullyQuoted(rest)) {
        // inline mapping item; create object and push a frame for its siblings
        const obj = {};
        top.container.push(obj);
        // The inline key starts at indent+2 (below `- `)
        const childIndent = indent + 2;
        const frame = { indent: childIndent - 1, container: obj, kind: 'object', pending: null };
        stack.push(frame);
        applyKv(obj, rest, stack);
      } else {
        top.container.push(parseScalar(rest));
      }
    } else {
      // object frame — key: value
      if (body.startsWith('-')) {
        throw new Error(`Unexpected list item in object at line ${li + 1}: ${line}`);
      }
      applyKv(top.container, body, stack);
    }
  }
  return root;
}

function isFullyQuoted(s) {
  const t = s.trim();
  return (t.startsWith('"') && t.endsWith('"')) || (t.startsWith("'") && t.endsWith("'"));
}

function applyKv(obj, body, stack) {
  const colon = body.indexOf(':');
  if (colon < 0) throw new Error(`Missing colon in key line: ${body}`);
  const key = body.slice(0, colon).trim();
  const after = body.slice(colon + 1);
  const trimmed = after.trim();
  if (trimmed === '') {
    // Pending container — resolved by next line.
    const top = stack[stack.length - 1];
    top.pending = { parent: obj, key };
  } else {
    obj[key] = parseScalar(trimmed);
  }
}

// ── Core logic ──────────────────────────────────────────────────────────────

function familyOf(examCode, family) {
  if (family === 'BAGRUT') return 'Bagrut';
  if (family === 'STANDARDIZED') return 'Standardized';
  return 'Other';
}

function computeWindow(canonicalDate) {
  const anchor = new Date(
    `${canonicalDate}T${String(SITTING_ANCHOR_HOUR_UTC).padStart(2, '0')}:00:00Z`,
  );
  if (Number.isNaN(anchor.getTime())) {
    throw new Error(`Unparseable canonical_date: ${canonicalDate}`);
  }
  const startUtc = new Date(anchor.getTime() - PRE_HOURS * 3600 * 1000);
  const endUtc = new Date(anchor.getTime() + POST_HOURS * 3600 * 1000);
  return { startUtc, endUtc, anchorUtc: anchor };
}

function mergeWindows(windows) {
  if (windows.length === 0) return [];
  const sorted = [...windows].sort((a, b) => a.startUtc - b.startUtc);
  const merged = [sorted[0]];
  for (let i = 1; i < sorted.length; i++) {
    const prev = merged[merged.length - 1];
    const cur = sorted[i];
    if (cur.startUtc <= prev.endUtc) {
      prev.endUtc = new Date(Math.max(prev.endUtc.getTime(), cur.endUtc.getTime()));
      prev.families = [...new Set([...prev.families, ...cur.families])].sort();
      prev.sittings = [...prev.sittings, ...cur.sittings];
    } else {
      merged.push(cur);
    }
  }
  return merged;
}

function loadWindows() {
  const files = readdirSync(CATALOG_DIR)
    .filter(f => f.endsWith('.yml') && f !== 'catalog-meta.yml')
    .sort();
  const windows = [];

  for (const file of files) {
    const path = join(CATALOG_DIR, file);
    let doc;
    try {
      doc = parseYamlNarrow(readFileSync(path, 'utf8'));
    } catch (e) {
      throw new Error(`Failed to parse ${file}: ${e.message}`);
    }
    if (!doc.exam_code || !doc.family) {
      throw new Error(`${file}: missing required fields exam_code and/or family`);
    }
    if (!ELIGIBLE_AVAILABILITY.has(doc.availability)) continue;

    const family = familyOf(doc.exam_code, doc.family);
    const sittings = Array.isArray(doc.sittings) ? doc.sittings : [];
    for (const sitting of sittings) {
      if (!sitting.canonical_date) {
        throw new Error(`${file}: sitting ${sitting.code ?? '(unnamed)'} missing canonical_date`);
      }
      const { startUtc, endUtc, anchorUtc } = computeWindow(sitting.canonical_date);
      windows.push({
        startUtc,
        endUtc,
        families: [family],
        sittings: [{
          exam_code: doc.exam_code,
          sitting_code: sitting.code,
          canonical_date: sitting.canonical_date,
          anchor_utc: anchorUtc.toISOString(),
          season: sitting.season ?? null,
          moed: sitting.moed ?? null,
          academic_year: sitting.academic_year ?? null,
        }],
      });
    }
  }
  return windows;
}

function toYaml(windows, meta) {
  const L = [];
  L.push('# =============================================================================');
  L.push('# Exam-day freeze windows — AUTO-GENERATED, DO NOT EDIT BY HAND');
  L.push('# Source: contracts/exam-catalog/*.yml');
  L.push('# Generator: scripts/ops/generate-freeze-windows.mjs');
  L.push(`# Regenerated: ${meta.generatedAt}`);
  L.push(`# Catalog version: ${meta.catalogVersion}`);
  L.push('# =============================================================================');
  L.push('');
  L.push(`generated_at: "${meta.generatedAt}"`);
  L.push(`catalog_version: "${meta.catalogVersion}"`);
  L.push(`generator: "scripts/ops/generate-freeze-windows.mjs"`);
  L.push(`pre_hours: ${PRE_HOURS}`);
  L.push(`post_hours: ${POST_HOURS}`);
  L.push('windows:');
  for (const w of windows) {
    L.push(`  - start_utc: "${w.startUtc.toISOString()}"`);
    L.push(`    end_utc: "${w.endUtc.toISOString()}"`);
    L.push(`    families:`);
    for (const family of w.families) L.push(`      - ${family}`);
    L.push(`    sittings:`);
    for (const s of w.sittings) {
      L.push(`      - exam_code: ${s.exam_code}`);
      L.push(`        sitting_code: ${s.sitting_code ?? '~'}`);
      L.push(`        canonical_date: "${s.canonical_date}"`);
      L.push(`        anchor_utc: "${s.anchor_utc}"`);
      L.push(`        season: ${s.season ?? '~'}`);
      L.push(`        moed: ${s.moed ?? '~'}`);
      L.push(`        academic_year: "${s.academic_year ?? ''}"`);
    }
  }
  L.push('');
  return L.join('\n');
}

function loadCatalogVersion() {
  const metaPath = join(CATALOG_DIR, 'catalog-meta.yml');
  const doc = parseYamlNarrow(readFileSync(metaPath, 'utf8'));
  if (!doc.catalog_version) {
    throw new Error('catalog-meta.yml is missing catalog_version');
  }
  return doc.catalog_version;
}

function main() {
  const rawWindows = loadWindows();
  const merged = mergeWindows(rawWindows);
  const catalogVersion = loadCatalogVersion();
  const generatedAt = new Date().toISOString();
  const yaml = toYaml(merged, { generatedAt, catalogVersion });

  if (!existsSync(OUTPUT_DIR)) mkdirSync(OUTPUT_DIR, { recursive: true });
  writeFileSync(OUTPUT_FILE, yaml, 'utf8');

  const totalSittings = merged.reduce((acc, w) => acc + w.sittings.length, 0);
  process.stdout.write(
    `generate-freeze-windows: wrote ${merged.length} window(s), ${totalSittings} sitting(s), catalog ${catalogVersion}\n`,
  );
}

// Only run if invoked as main (so unit tests can import parseYamlNarrow).
const invokedDirectly = import.meta.url === `file://${process.argv[1]}`;
if (invokedDirectly) {
  try {
    main();
  } catch (e) {
    process.stderr.write(`generate-freeze-windows: FAILED: ${e.message}\n`);
    process.exit(e.code === 'ENOENT' || e.code === 'EACCES' ? 2 : 1);
  }
}

export { parseYamlNarrow, mergeWindows, computeWindow };
