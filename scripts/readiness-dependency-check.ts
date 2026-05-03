#!/usr/bin/env npx tsx
/**
 * RDY-031: Readiness Task Dependency Validator
 *
 * Reads config/readiness-dependencies.json and validates:
 *   1. No circular dependencies
 *   2. No in-progress task with incomplete dependencies
 *   3. blocks ↔ depends_on bidirectional consistency
 *   4. No done task with undone hard dependencies
 *   5. All referenced task IDs exist
 *   6. Topological sort (optimal execution order)
 *   7. Critical path to production (pending tasks only)
 *   8. Mermaid diagram output
 *
 * Usage:
 *   npx tsx scripts/readiness-dependency-check.ts
 *   npx tsx scripts/readiness-dependency-check.ts --mermaid   # output Mermaid only
 *   npx tsx scripts/readiness-dependency-check.ts --json      # output JSON summary
 */

import { readFileSync } from "fs";
import { resolve } from "path";

// ── Types ──────────────────────────────────────────────────────────────

interface TaskEntry {
  id: string;
  title: string;
  status: "done" | "in_progress" | "pending";
  tier: number;
  depends_on: string[];
  blocks: string[];
  enhances?: string[];  // soft deps: shipped without, retroactively identified
}

interface DepGraph {
  version: number;
  tasks: TaskEntry[];
}

// ── Load ───────────────────────────────────────────────────────────────

const graphPath = resolve(__dirname, "../config/readiness-dependencies.json");
const raw = readFileSync(graphPath, "utf-8");
const graph: DepGraph = JSON.parse(raw);
const tasks = new Map(graph.tasks.map((t) => [t.id, t]));

const flags = new Set(process.argv.slice(2));
const mermaidOnly = flags.has("--mermaid");
const jsonOnly = flags.has("--json");

// ── 1. Structural validation ──────────────────────────────────────────

function validateStructure(): string[] {
  const errors: string[] = [];
  const allIds = new Set(tasks.keys());

  for (const [id, task] of tasks) {
    // Check all referenced IDs exist
    for (const dep of task.depends_on) {
      if (!allIds.has(dep)) errors.push(`ERROR: ${id}.depends_on references unknown task ${dep}`);
    }
    for (const blocked of task.blocks) {
      if (!allIds.has(blocked)) errors.push(`ERROR: ${id}.blocks references unknown task ${blocked}`);
    }
    for (const enh of task.enhances ?? []) {
      if (!allIds.has(enh)) errors.push(`ERROR: ${id}.enhances references unknown task ${enh}`);
    }

    // Bidirectional consistency: if A.blocks contains B, then B.depends_on must contain A
    for (const blocked of task.blocks) {
      const target = tasks.get(blocked);
      if (target && !target.depends_on.includes(id)) {
        errors.push(`INCONSISTENCY: ${id}.blocks includes ${blocked}, but ${blocked}.depends_on does not include ${id}`);
      }
    }

    // Reverse check: if B.depends_on contains A, then A.blocks must contain B
    for (const dep of task.depends_on) {
      const source = tasks.get(dep);
      if (source && !source.blocks.includes(id)) {
        errors.push(`INCONSISTENCY: ${id}.depends_on includes ${dep}, but ${dep}.blocks does not include ${id}`);
      }
    }

    // Done task must not have undone hard dependencies
    if (task.status === "done") {
      for (const dep of task.depends_on) {
        const depTask = tasks.get(dep);
        if (depTask && depTask.status !== "done") {
          errors.push(`INTEGRITY: ${id} is done but depends_on ${dep} which is ${depTask.status}`);
        }
      }
    }
  }

  return errors;
}

// ── 2. Cycle detection (DFS) ──────────────────────────────────────────

function detectCycles(): string[][] {
  const visited = new Set<string>();
  const stack = new Set<string>();
  const cycles: string[][] = [];

  function dfs(id: string, path: string[]): void {
    if (stack.has(id)) {
      cycles.push([...path.slice(path.indexOf(id)), id]);
      return;
    }
    if (visited.has(id)) return;
    visited.add(id);
    stack.add(id);
    const task = tasks.get(id);
    if (task) {
      for (const dep of task.blocks) {
        dfs(dep, [...path, id]);
      }
    }
    stack.delete(id);
  }

  for (const id of tasks.keys()) dfs(id, []);
  return cycles;
}

// ── 3. Dependency violations (runtime) ────────────────────────────────

function findViolations(): string[] {
  const warnings: string[] = [];
  for (const [id, task] of tasks) {
    if (task.status === "done") continue;
    for (const depId of task.depends_on) {
      const dep = tasks.get(depId);
      if (!dep) continue; // caught by structure validation
      if (dep.status !== "done") {
        const severity = task.status === "in_progress" ? "BLOCKED" : "not yet ready";
        warnings.push(`${id} (${task.status}) depends on ${depId} (${dep.status}) — ${severity}`);
      }
    }
  }
  return warnings;
}

// ── 4. Topological sort (Kahn's algorithm) ────────────────────────────

function topoSort(): string[] {
  const inDegree = new Map<string, number>();
  for (const id of tasks.keys()) inDegree.set(id, 0);

  for (const [, task] of tasks) {
    for (const blocked of task.blocks) {
      inDegree.set(blocked, (inDegree.get(blocked) ?? 0) + 1);
    }
  }

  const queue: string[] = [];
  for (const [id, deg] of inDegree) {
    if (deg === 0) queue.push(id);
  }

  const sortQueue = () =>
    queue.sort((a, b) => {
      const ta = tasks.get(a)!;
      const tb = tasks.get(b)!;
      return ta.tier !== tb.tier ? ta.tier - tb.tier : a.localeCompare(b);
    });

  sortQueue();
  const order: string[] = [];

  while (queue.length > 0) {
    const id = queue.shift()!;
    order.push(id);
    const task = tasks.get(id)!;
    for (const blocked of task.blocks) {
      const newDeg = (inDegree.get(blocked) ?? 1) - 1;
      inDegree.set(blocked, newDeg);
      if (newDeg === 0) queue.push(blocked);
    }
    sortQueue();
  }

  if (order.length < tasks.size) {
    const missing = [...tasks.keys()].filter((id) => !order.includes(id));
    console.error(`TOPO SORT INCOMPLETE: ${missing.join(", ")} unreachable (likely cycle)`);
  }

  return order;
}

// ── 5. Critical path (longest chain of PENDING tasks) ─────────────────

function criticalPath(): { path: string[]; length: number } {
  const memo = new Map<string, { path: string[]; length: number }>();

  function longest(id: string): { path: string[]; length: number } {
    if (memo.has(id)) return memo.get(id)!;
    const task = tasks.get(id);
    if (!task || task.blocks.length === 0) {
      const len = task && task.status !== "done" ? 1 : 0;
      const result = { path: len > 0 ? [id] : [], length: len };
      memo.set(id, result);
      return result;
    }

    let best = { path: task.status !== "done" ? [id] : [] as string[], length: task.status !== "done" ? 1 : 0 };
    for (const blocked of task.blocks) {
      const sub = longest(blocked);
      const candidateLen = (task.status !== "done" ? 1 : 0) + sub.length;
      if (candidateLen > best.length) {
        best = {
          path: task.status !== "done" ? [id, ...sub.path] : [...sub.path],
          length: candidateLen,
        };
      }
    }
    memo.set(id, best);
    return best;
  }

  let overall = { path: [] as string[], length: 0 };
  for (const id of tasks.keys()) {
    const result = longest(id);
    if (result.length > overall.length) overall = result;
  }
  return overall;
}

// ── 6. Mermaid diagram ────────────────────────────────────────────────

function generateMermaid(): string {
  const lines: string[] = ["graph TD"];

  lines.push("  classDef done fill:#4caf50,stroke:#333,color:#fff");
  lines.push("  classDef inprog fill:#ff9800,stroke:#333,color:#fff");
  lines.push("  classDef pending fill:#e0e0e0,stroke:#666,color:#333");
  lines.push("  classDef tier0 stroke:#f44336,stroke-width:3px");
  lines.push("");

  for (const [id, task] of tasks) {
    const short = task.title.length > 30 ? task.title.slice(0, 28) + "..." : task.title;
    lines.push(`  ${id}["${id}: ${short}"]`);

    const cls = task.status === "done" ? "done" : task.status === "in_progress" ? "inprog" : "pending";
    lines.push(`  class ${id} ${cls}`);
    if (task.tier === 0) lines.push(`  class ${id} tier0`);
  }

  lines.push("");

  // Hard dependency edges (solid)
  for (const [id, task] of tasks) {
    for (const blocked of task.blocks) {
      lines.push(`  ${id} --> ${blocked}`);
    }
  }

  // Soft enhancement edges (dashed)
  for (const [id, task] of tasks) {
    for (const enh of task.enhances ?? []) {
      lines.push(`  ${id} -.->|enhances| ${enh}`);
    }
  }

  return lines.join("\n");
}

// ── 7. Summary ────────────────────────────────────────────────────────

function summary() {
  const all = [...tasks.values()];
  const done = all.filter((t) => t.status === "done").length;
  const inProgress = all.filter((t) => t.status === "in_progress").length;
  const pending = all.filter((t) => t.status === "pending").length;

  const parallelizable = all.filter(
    (t) => t.status === "pending" && t.depends_on.every((d) => tasks.get(d)?.status === "done")
  );

  return { total: tasks.size, done, inProgress, pending, parallelizable: parallelizable.map((t) => t.id) };
}

// ── Main ──────────────────────────────────────────────────────────────

const structureErrors = validateStructure();
const cycles = detectCycles();
const violations = findViolations();
const order = topoSort();
const cp = criticalPath();
const stats = summary();

const hasErrors = structureErrors.length > 0 || cycles.length > 0;

if (mermaidOnly) {
  console.log(generateMermaid());
  process.exit(0);
}

if (jsonOnly) {
  console.log(JSON.stringify({ structureErrors, cycles, violations, order, criticalPath: cp, stats }, null, 2));
  process.exit(hasErrors ? 1 : 0);
}

// Human-readable report
console.log("=== Readiness Task Dependency Report ===\n");

console.log(`Tasks: ${stats.total} total, ${stats.done} done, ${stats.inProgress} in-progress, ${stats.pending} pending`);
console.log(`Progress: ${((stats.done / stats.total) * 100).toFixed(0)}%\n`);

if (structureErrors.length > 0) {
  console.log("STRUCTURAL ERRORS:");
  for (const e of structureErrors) console.log(`  ${e}`);
  console.log("");
}

if (cycles.length > 0) {
  console.log("CIRCULAR DEPENDENCIES:");
  for (const c of cycles) console.log(`  ${c.join(" -> ")}`);
  console.log("");
}

if (violations.length > 0) {
  console.log("DEPENDENCY WARNINGS:");
  for (const v of violations) console.log(`  ${v}`);
  console.log("");
}

if (structureErrors.length === 0 && cycles.length === 0) {
  console.log("GRAPH INTEGRITY: PASS (no cycles, no inconsistencies)\n");
}

console.log("CRITICAL PATH (longest pending chain):");
if (cp.path.length > 0) {
  console.log(`  ${cp.path.join(" -> ")} (${cp.length} remaining tasks)`);
} else {
  console.log("  (none — all dependency chains resolved)");
}
console.log("");

if (stats.parallelizable.length > 0) {
  console.log("READY TO START (all dependencies met):");
  for (const id of stats.parallelizable) {
    const t = tasks.get(id)!;
    console.log(`  ${id} — ${t.title} (Tier ${t.tier})`);
  }
  console.log("");
}

// Enhancement edges (soft deps)
const enhancements = [...tasks.values()].filter((t) => (t.enhances ?? []).length > 0);
if (enhancements.length > 0) {
  console.log("SOFT DEPENDENCIES (enhances, shipped without):");
  for (const t of enhancements) {
    for (const enh of t.enhances!) {
      console.log(`  ${t.id} enhances ${enh}`);
    }
  }
  console.log("");
}

console.log("OPTIMAL EXECUTION ORDER (topological sort):");
for (let i = 0; i < order.length; i++) {
  const t = tasks.get(order[i])!;
  const icon = t.status === "done" ? "+" : t.status === "in_progress" ? "~" : " ";
  console.log(`  ${icon} ${i + 1}. ${order[i]} — ${t.title}`);
}

process.exit(hasErrors ? 1 : 0);
