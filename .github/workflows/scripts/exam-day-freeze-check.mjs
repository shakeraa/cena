#!/usr/bin/env node
// =============================================================================
// exam-day-freeze-check.mjs
//
// Invoked by .github/workflows/exam-day-freeze.yml. Reads
// ops/release/freeze-windows.yml (generated), checks whether NOW is inside
// any active window, and fails the step with a clear message when a freeze
// is active AND the caller is not exempt via the `exam-day/break-glass`
// label.
//
// Inputs (env vars, never argv — hardened against workflow injection):
//   - PR_LABELS     : JSON array of label names on the PR.
//   - EVENT_NAME    : GitHub Actions event name (pull_request | workflow_dispatch).
//   - TARGET_ENV    : optional — dispatch target env (production by default).
//   - PR_NUMBER     : optional — PR number for log context.
//
// Exit codes:
//   0 — no active freeze window, OR break-glass label present, OR non-prod
//       dispatch (staging passes through).
//   1 — active freeze AND no break-glass label AND targeting prod.
//   2 — freeze-windows.yml missing or malformed (fail-closed).
//
// The break-glass label is advisory — every use is recorded in the Action's
// step summary and should be followed by a 48h post-mortem per
// docs/ops/runbooks/exam-day-slo.md §3c.
// =============================================================================

import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseYamlNarrow } from '../../../scripts/ops/generate-freeze-windows.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..', '..');
const WINDOWS_FILE = join(REPO_ROOT, 'ops', 'release', 'freeze-windows.yml');

const BREAK_GLASS_LABEL = 'exam-day/break-glass';

function readLabels() {
  const raw = process.env.PR_LABELS ?? '[]';
  try {
    const arr = JSON.parse(raw);
    if (!Array.isArray(arr)) return [];
    return arr.filter(l => typeof l === 'string');
  } catch {
    return [];
  }
}

function loadWindows() {
  let doc;
  try {
    doc = parseYamlNarrow(readFileSync(WINDOWS_FILE, 'utf8'));
  } catch (e) {
    process.stderr.write(`freeze-check: cannot parse ${WINDOWS_FILE}: ${e.message}\n`);
    process.exit(2);
  }
  if (!Array.isArray(doc.windows)) {
    process.stderr.write(`freeze-check: malformed freeze-windows.yml (missing windows array)\n`);
    process.exit(2);
  }
  return doc.windows;
}

function activeWindow(windows, nowMs) {
  for (const w of windows) {
    if (!w.start_utc || !w.end_utc) continue;
    const start = Date.parse(w.start_utc);
    const end = Date.parse(w.end_utc);
    if (Number.isNaN(start) || Number.isNaN(end)) continue;
    if (start <= nowMs && nowMs < end) return w;
  }
  return null;
}

function describeWindow(w) {
  const sittings = Array.isArray(w.sittings) ? w.sittings : [];
  const codes = sittings.map(s => `${s.exam_code}/${s.sitting_code ?? 'unnamed'}`);
  return `families=[${(w.families ?? []).join(', ')}] start=${w.start_utc} end=${w.end_utc} sittings=[${codes.join(', ')}]`;
}

function main() {
  const eventName = process.env.EVENT_NAME ?? '';
  const targetEnv = (process.env.TARGET_ENV ?? 'production').toLowerCase();
  const prNumber = process.env.PR_NUMBER ?? '(none)';
  const labels = readLabels();

  // Staging dispatches pass through regardless.
  if (eventName === 'workflow_dispatch' && targetEnv !== 'production') {
    process.stdout.write(`freeze-check: dispatch to ${targetEnv} is not gated by the freeze.\n`);
    return;
  }

  const windows = loadWindows();
  const now = Date.now();
  const w = activeWindow(windows, now);

  if (!w) {
    process.stdout.write('freeze-check: no active freeze window — proceeding.\n');
    return;
  }

  if (labels.includes(BREAK_GLASS_LABEL)) {
    process.stdout.write(
      `freeze-check: ACTIVE freeze (${describeWindow(w)}) bypassed via ${BREAK_GLASS_LABEL} label on PR #${prNumber}.\n`,
    );
    process.stdout.write('freeze-check: REMINDER — file a 48h post-mortem per docs/ops/runbooks/exam-day-slo.md §3c.\n');
    return;
  }

  process.stderr.write('\nfreeze-check: ACTIVE EXAM-DAY FREEZE — production-affecting change blocked.\n');
  process.stderr.write(`  ${describeWindow(w)}\n`);
  process.stderr.write(`\n  Break-glass: apply label "${BREAK_GLASS_LABEL}" to this PR (SRE approval required).\n`);
  process.stderr.write('  Runbook: docs/ops/runbooks/exam-day-slo.md §3.\n\n');
  process.exit(1);
}

main();
