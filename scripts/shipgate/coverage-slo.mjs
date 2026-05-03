#!/usr/bin/env node
// =============================================================================
// Cena Platform — Coverage SLO Ship-gate (prr-210)
//
// Walks the cell matrix declared in contracts/coverage/coverage-targets.yml,
// reads per-cell variant counts from ops/reports/coverage-variants-snapshot.json,
// and fails CI if any ACTIVE cell is below its required-N.
//
// Non-negotiables (task body):
//   * Empty-store path fails LOUDLY (exit 1), not silently passes.
//   * No ambient magic: every cell that matters must be declared in
//     coverage-targets.yml. Missing declarations are reported.
//   * Report is generated to ops/reports/coverage-rung-status.md every run.
//   * Active/draft split: active → gating, draft (active: false) → advisory.
//   * Fixture mode (`--fixture=<path>`) lets the architecture test assert
//     the script fails on a crafted under-target fixture without touching the
//     real repo state.
//
// Exit codes:
//   0 — all active cells meet SLO (draft cells may still be under; reported).
//   1 — ≥1 active cell under SLO, OR missing snapshot (no --allow-empty),
//       OR any other hard failure.
//   2 — coverage-targets.yml missing at startup.
//   3 — snapshot file present but unparseable JSON.
//
// Usage:
//   node scripts/shipgate/coverage-slo.mjs
//   node scripts/shipgate/coverage-slo.mjs --snapshot <path>
//   node scripts/shipgate/coverage-slo.mjs --targets <path>
//   node scripts/shipgate/coverage-slo.mjs --report <path>
//   node scripts/shipgate/coverage-slo.mjs --fixture <path>   # uses fixture as snapshot
//   node scripts/shipgate/coverage-slo.mjs --allow-empty      # treat missing snapshot as "all zero" without failing IO
//   node scripts/shipgate/coverage-slo.mjs --json             # machine-readable output
//   node scripts/shipgate/coverage-slo.mjs --quiet
// =============================================================================

import { readFileSync, writeFileSync, existsSync, mkdirSync } from "node:fs";
import { resolve, relative, dirname } from "node:path";

// ── Args ────────────────────────────────────────────────────────────────────

const ROOT = resolve(import.meta.dirname, "../..");

const argv = process.argv.slice(2);
function argValue(name) {
  const i = argv.indexOf(`--${name}`);
  if (i >= 0 && i + 1 < argv.length && !argv[i + 1].startsWith("--")) {
    return argv[i + 1];
  }
  const prefix = `--${name}=`;
  const eq = argv.find((a) => a.startsWith(prefix));
  return eq ? eq.slice(prefix.length) : null;
}
function argFlag(name) {
  return argv.includes(`--${name}`);
}

const TARGETS_PATH = resolve(ROOT, argValue("targets") ?? "contracts/coverage/coverage-targets.yml");
const SNAPSHOT_PATH = resolve(
  ROOT,
  argValue("snapshot") ?? argValue("fixture") ?? "ops/reports/coverage-variants-snapshot.json",
);
const REPORT_PATH = resolve(ROOT, argValue("report") ?? "ops/reports/coverage-rung-status.md");
const ALLOW_EMPTY = argFlag("allow-empty");
const QUIET = argFlag("quiet");
const JSON_OUT = argFlag("json");

// ── Minimal YAML reader (targeted to coverage-targets.yml shape) ────────────
// We avoid js-yaml to keep the script zero-dependency. The schema is narrow:
//   version: <int>
//   defaults: { global:{min}, methodology:{<name>: <int>}, questionType:{<name>: <int>} }
//   cells:
//     - key: value
//       key: value
//       ...
// Values are either a bare scalar (int, string, bool) or a quoted string. No
// flow collections, no anchors, no multi-line blocks.

function parseYaml(text) {
  const root = {};
  const stack = [{ indent: -1, node: root, isList: false }];
  const lines = text.split(/\r?\n/);

  for (let rawIdx = 0; rawIdx < lines.length; rawIdx++) {
    const rawLine = lines[rawIdx];
    // Strip comments (outside quoted strings — we don't emit quoted #)
    let line = rawLine;
    const commentMatch = line.match(/(^|\s)#/);
    if (commentMatch) {
      const pos = commentMatch.index + (commentMatch[1] ? 1 : 0);
      line = line.slice(0, pos);
    }
    if (!line.trim()) continue;

    const indent = line.match(/^(\s*)/)[1].length;
    const content = line.trim();

    // Pop stack until we're at the right indent level.
    while (stack.length > 1 && stack[stack.length - 1].indent >= indent) {
      stack.pop();
    }

    const parent = stack[stack.length - 1].node;

    if (content.startsWith("- ")) {
      // List item. Parent must be the key this list belongs to (an array).
      const itemBody = content.slice(2).trim();
      if (!Array.isArray(parent)) {
        throw new Error(`YAML parse: list item at line ${rawIdx + 1} but parent is not an array`);
      }

      // A list item can be either a scalar or a map. For coverage-targets
      // we only care about maps ("- topic: ...").
      const m = itemBody.match(/^([A-Za-z_][\w-]*):\s*(.*)$/);
      if (!m) {
        throw new Error(`YAML parse: expected map item at line ${rawIdx + 1}, got "${content}"`);
      }
      const obj = {};
      const key = m[1];
      const val = m[2];
      if (val === "" || val === undefined) {
        obj[key] = {};
        parent.push(obj);
        stack.push({ indent, node: obj, isList: false });
        stack.push({ indent: indent + 2, node: obj[key], isList: false });
      } else {
        obj[key] = parseScalar(val);
        parent.push(obj);
        stack.push({ indent, node: obj, isList: false });
      }
      continue;
    }

    // "key:" or "key: value"
    const m = content.match(/^([A-Za-z_][\w\-.]*):\s*(.*)$/);
    if (!m) {
      throw new Error(`YAML parse: unexpected line ${rawIdx + 1}: "${content}"`);
    }
    const key = m[1];
    const val = m[2];

    if (val === "" || val === undefined) {
      // Nested map or list depending on next non-empty line.
      const next = findNextNonEmpty(lines, rawIdx + 1);
      const container = next && next.trim().startsWith("- ") ? [] : {};
      parent[key] = container;
      stack.push({ indent, node: container, isList: Array.isArray(container) });
    } else {
      parent[key] = parseScalar(val);
    }
  }

  return root;
}

function findNextNonEmpty(lines, from) {
  for (let i = from; i < lines.length; i++) {
    let l = lines[i];
    const cm = l.match(/(^|\s)#/);
    if (cm) l = l.slice(0, cm.index + (cm[1] ? 1 : 0));
    if (l.trim()) return l;
  }
  return null;
}

function parseScalar(raw) {
  const s = raw.trim();
  if (s === "null" || s === "~") return null;
  if (s === "true") return true;
  if (s === "false") return false;
  if (/^-?\d+$/.test(s)) return parseInt(s, 10);
  if (/^-?\d+\.\d+$/.test(s)) return parseFloat(s);
  if (
    (s.startsWith('"') && s.endsWith('"')) ||
    (s.startsWith("'") && s.endsWith("'"))
  ) {
    return s.slice(1, -1);
  }
  return s;
}

// ── Load & validate targets ────────────────────────────────────────────────

function loadTargets(path) {
  if (!existsSync(path)) {
    console.error(`[coverage-slo] Missing targets file at ${path}`);
    process.exit(2);
  }
  const yamlText = readFileSync(path, "utf8");
  const doc = parseYaml(yamlText);

  if (typeof doc.version !== "number") {
    console.error("[coverage-slo] targets: missing 'version: <int>' at top level");
    process.exit(2);
  }
  if (!doc.defaults || typeof doc.defaults !== "object") {
    console.error("[coverage-slo] targets: missing 'defaults' block");
    process.exit(2);
  }
  if (typeof doc.defaults?.global?.min !== "number") {
    console.error("[coverage-slo] targets: missing defaults.global.min");
    process.exit(2);
  }
  if (!Array.isArray(doc.cells)) {
    console.error("[coverage-slo] targets: 'cells' must be a list");
    process.exit(2);
  }

  for (const [i, c] of doc.cells.entries()) {
    for (const k of ["topic", "difficulty", "methodology", "track", "questionType"]) {
      if (typeof c[k] !== "string" || !c[k]) {
        console.error(`[coverage-slo] targets: cell #${i} missing required string '${k}'`);
        process.exit(2);
      }
    }
    if (typeof c.active !== "boolean") {
      console.error(`[coverage-slo] targets: cell #${i} missing 'active: true|false'`);
      process.exit(2);
    }
    if (c.min !== undefined && (typeof c.min !== "number" || c.min < 0)) {
      console.error(`[coverage-slo] targets: cell #${i} has invalid 'min'`);
      process.exit(2);
    }
  }

  return doc;
}

function resolveRequiredN(cell, defaults) {
  if (typeof cell.min === "number") return cell.min;
  const qt = defaults.questionType?.[cell.questionType];
  if (typeof qt === "number") return qt;
  const meth = defaults.methodology?.[cell.methodology];
  if (typeof meth === "number") return meth;
  return defaults.global.min;
}

function cellAddress(c) {
  return [
    c.track,
    c.topic,
    c.difficulty,
    c.methodology,
    c.questionType,
    c.language ?? "en",
  ].join("/");
}

// ── Load snapshot ──────────────────────────────────────────────────────────

function loadSnapshot(path, allowEmpty) {
  if (!existsSync(path)) {
    if (allowEmpty) {
      if (!QUIET && !JSON_OUT) {
        console.warn(`[coverage-slo] snapshot not found at ${relative(ROOT, path)}; --allow-empty → treating as zeroes`);
      }
      return { schemaVersion: "1.0", runAt: null, source: "allow-empty", cells: [] };
    }
    console.error(
      `[coverage-slo] snapshot missing at ${relative(ROOT, path)}.\n` +
      "  The coverage SLO script treats a missing snapshot as a FAILURE (loud).\n" +
      "  If this is a first-run bootstrap, pass --allow-empty to acknowledge\n" +
      "  that zero-variants baseline is the intended starting state.",
    );
    process.exit(1);
  }
  let raw;
  try {
    raw = readFileSync(path, "utf8");
  } catch (err) {
    console.error(`[coverage-slo] unable to read snapshot ${path}: ${err.message}`);
    process.exit(1);
  }
  let doc;
  try {
    doc = JSON.parse(raw);
  } catch (err) {
    console.error(`[coverage-slo] snapshot parse error (${path}): ${err.message}`);
    process.exit(3);
  }
  if (!doc || !Array.isArray(doc.cells)) {
    console.error(`[coverage-slo] snapshot malformed — expected { cells: [...] }`);
    process.exit(3);
  }
  return doc;
}

// ── Match snapshot row to declared cell ────────────────────────────────────

function matchSnapshotCell(declared, snapshot) {
  return snapshot.cells.find((s) =>
    s.topic === declared.topic &&
    s.difficulty === declared.difficulty &&
    s.methodology === declared.methodology &&
    s.track === declared.track &&
    s.questionType === declared.questionType &&
    (s.language ?? "en") === (declared.language ?? "en"),
  );
}

// ── Build the grade table ──────────────────────────────────────────────────

function grade(targetsDoc, snapshot) {
  const rows = [];
  for (const declared of targetsDoc.cells) {
    const snap = matchSnapshotCell(declared, snapshot);
    const variantCount = snap?.variantCount ?? 0;
    const required = resolveRequiredN(declared, targetsDoc.defaults);
    const gap = Math.max(0, required - variantCount);
    const meets = variantCount >= required;
    const failingActive = declared.active && !meets;
    rows.push({
      address: cellAddress(declared),
      topic: declared.topic,
      difficulty: declared.difficulty,
      methodology: declared.methodology,
      track: declared.track,
      questionType: declared.questionType,
      language: declared.language ?? "en",
      active: declared.active,
      required,
      variantCount,
      gap,
      meets,
      failingActive,
      curatorTaskId: snap?.curatorTaskId ?? null,
      belowSlo: snap?.belowSlo ?? !meets,
      notes: declared.notes ?? null,
    });
  }

  // Undeclared cells (present in snapshot but not in targets) — surfaced as
  // informational drift. Not gating.
  const declaredSet = new Set(targetsDoc.cells.map((c) => cellAddress(c)));
  const undeclared = (snapshot.cells ?? [])
    .filter((s) => !declaredSet.has(cellAddress(s)))
    .map((s) => ({
      address: cellAddress(s),
      variantCount: s.variantCount ?? 0,
    }));

  return { rows, undeclared };
}

// ── Render report ──────────────────────────────────────────────────────────

function renderReport({ rows, undeclared, snapshotMeta, targetsMeta, failingActive }) {
  const now = new Date().toISOString();
  const lines = [];
  lines.push("# Coverage Rung Status (prr-210)");
  lines.push("");
  lines.push(`**Generated**: ${now}`);
  lines.push(`**Targets**: \`${relative(ROOT, TARGETS_PATH)}\` (v${targetsMeta.version})`);
  lines.push(`**Snapshot**: \`${relative(ROOT, SNAPSHOT_PATH)}\` (${snapshotMeta.source ?? "unknown"}, runAt=${snapshotMeta.runAt ?? "unknown"})`);
  lines.push(`**Declared cells**: ${rows.length} (${rows.filter((r) => r.active).length} active, ${rows.filter((r) => !r.active).length} draft)`);
  const failingRows = rows.filter((r) => r.failingActive);
  const advisoryFailing = rows.filter((r) => !r.active && !r.meets);
  lines.push(`**Active failing**: ${failingRows.length}`);
  lines.push(`**Advisory failing (draft)**: ${advisoryFailing.length}`);
  lines.push(`**Undeclared cells in snapshot**: ${undeclared.length}`);
  lines.push("");
  lines.push("Legend: ✅ meets SLO · ❌ below SLO (active, gating) · ⚠️  below SLO (draft, advisory)");
  lines.push("");
  lines.push("## Cells");
  lines.push("");
  lines.push("| Status | Active | Topic | Difficulty | Methodology | Track | Q-type | Variants | Required | Gap | Curator task | Notes |");
  lines.push("|--------|--------|-------|------------|-------------|-------|--------|----------|----------|-----|--------------|-------|");
  for (const r of rows) {
    const status = r.meets ? "✅" : r.active ? "❌" : "⚠️";
    const active = r.active ? "active" : "draft";
    const curator = r.curatorTaskId ? `\`${r.curatorTaskId}\`` : "—";
    const notes = r.notes ? r.notes.replace(/\|/g, "/").slice(0, 60) : "";
    lines.push(
      `| ${status} | ${active} | ${r.topic} | ${r.difficulty} | ${r.methodology} | ${r.track} | ${r.questionType} | ${r.variantCount} | ${r.required} | ${r.gap} | ${curator} | ${notes} |`,
    );
  }

  if (undeclared.length > 0) {
    lines.push("");
    lines.push("## Undeclared snapshot cells (drift)");
    lines.push("");
    lines.push("Present in the variant snapshot but not listed in `contracts/coverage/coverage-targets.yml`. Add an entry to either enforce the cell (active: true) or explicitly mark it advisory (active: false).");
    lines.push("");
    lines.push("| Cell address | Variants |");
    lines.push("|--------------|----------|");
    for (const u of undeclared) {
      lines.push(`| \`${u.address}\` | ${u.variantCount} |`);
    }
  }

  if (failingRows.length > 0) {
    lines.push("");
    lines.push("## Failing (gating)");
    lines.push("");
    for (const r of failingRows) {
      const curator = r.curatorTaskId ? ` · curator=${r.curatorTaskId}` : "";
      lines.push(`- \`${r.address}\` — have ${r.variantCount}, need ${r.required} (gap ${r.gap})${curator}`);
    }
  }

  lines.push("");
  lines.push("---");
  lines.push("");
  lines.push("Regenerate: `node scripts/shipgate/coverage-slo.mjs`. See `ops/slo/coverage-rung-slo.md` for policy.");
  lines.push("");
  return lines.join("\n");
}

// ── Main ───────────────────────────────────────────────────────────────────

function main() {
  const targets = loadTargets(TARGETS_PATH);
  const snapshot = loadSnapshot(SNAPSHOT_PATH, ALLOW_EMPTY);
  const { rows, undeclared } = grade(targets, snapshot);
  const failingActive = rows.filter((r) => r.failingActive);

  const report = renderReport({
    rows,
    undeclared,
    snapshotMeta: { runAt: snapshot.runAt, source: snapshot.source },
    targetsMeta: { version: targets.version },
    failingActive,
  });

  mkdirSync(dirname(REPORT_PATH), { recursive: true });
  writeFileSync(REPORT_PATH, report, "utf8");

  if (JSON_OUT) {
    console.log(JSON.stringify({
      exitCode: failingActive.length > 0 ? 1 : 0,
      targetsPath: relative(ROOT, TARGETS_PATH),
      snapshotPath: relative(ROOT, SNAPSHOT_PATH),
      reportPath: relative(ROOT, REPORT_PATH),
      totalCells: rows.length,
      activeCells: rows.filter((r) => r.active).length,
      draftCells: rows.filter((r) => !r.active).length,
      failingActive: failingActive.map((r) => ({
        address: r.address,
        variantCount: r.variantCount,
        required: r.required,
        gap: r.gap,
        curatorTaskId: r.curatorTaskId,
      })),
      advisoryFailing: rows.filter((r) => !r.active && !r.meets).map((r) => r.address),
      undeclared,
    }, null, 2));
  } else if (!QUIET) {
    if (failingActive.length === 0) {
      console.log(
        `[coverage-slo] ✅ all ${rows.filter((r) => r.active).length} active cells meet SLO ` +
        `(${rows.filter((r) => !r.active && !r.meets).length} advisory draft cell(s) under target). ` +
        `Report: ${relative(ROOT, REPORT_PATH)}`,
      );
    } else {
      console.error(
        `[coverage-slo] ❌ ${failingActive.length} active cell(s) below SLO:`,
      );
      for (const r of failingActive) {
        const curator = r.curatorTaskId ? `  curator=${r.curatorTaskId}` : "";
        console.error(`  ${r.address}  have=${r.variantCount}  need=${r.required}  gap=${r.gap}${curator}`);
      }
      console.error(
        `\nSee ${relative(ROOT, REPORT_PATH)} for the full status table.\n` +
        "Hot-fix runbook: ops/slo/coverage-rung-slo.md#hot-fix-runbook.",
      );
    }
  }

  process.exit(failingActive.length > 0 ? 1 : 0);
}

main();
