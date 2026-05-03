#!/usr/bin/env node
// =============================================================================
// RDY-003: Prerequisite Graph Validator
// Validates scripts/prerequisite-graph.json is acyclic, fully connected from
// roots, and structurally consistent.
// Usage: node scripts/validate-prerequisite-graph.mjs
// =============================================================================

import { readFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const graphPath = resolve(__dirname, 'prerequisite-graph.json');

const graph = JSON.parse(readFileSync(graphPath, 'utf-8'));
const errors = [];
const warnings = [];

// ── 1. Structural checks ──

const conceptIds = new Set(graph.concepts.map(c => c.id));
console.log(`Concepts: ${conceptIds.size}`);
console.log(`Edges: ${graph.edges.length}`);
console.log(`Clusters: ${[...new Set(graph.concepts.map(c => c.cluster))].join(', ')}`);

// Check all edge endpoints reference valid concepts
for (const edge of graph.edges) {
  if (!conceptIds.has(edge.from))
    errors.push(`Edge from unknown concept: ${edge.from} -> ${edge.to}`);
  if (!conceptIds.has(edge.to))
    errors.push(`Edge to unknown concept: ${edge.from} -> ${edge.to}`);
}

// Check validation metadata matches actual data
if (graph.validation.total_concepts !== conceptIds.size)
  warnings.push(`Metadata says ${graph.validation.total_concepts} concepts but found ${conceptIds.size}`);
if (graph.validation.total_edges !== graph.edges.length)
  warnings.push(`Metadata says ${graph.validation.total_edges} edges but found ${graph.edges.length}`);

// ── 2. Acyclicity (topological sort via Kahn's algorithm) ──

const inDegree = new Map();
const adj = new Map();
for (const id of conceptIds) {
  inDegree.set(id, 0);
  adj.set(id, []);
}
for (const edge of graph.edges) {
  adj.get(edge.from).push(edge.to);
  inDegree.set(edge.to, inDegree.get(edge.to) + 1);
}

const queue = [];
for (const [id, deg] of inDegree) {
  if (deg === 0) queue.push(id);
}

let sorted = 0;
while (queue.length > 0) {
  const node = queue.shift();
  sorted++;
  for (const neighbor of adj.get(node)) {
    const newDeg = inDegree.get(neighbor) - 1;
    inDegree.set(neighbor, newDeg);
    if (newDeg === 0) queue.push(neighbor);
  }
}

if (sorted !== conceptIds.size) {
  const cycleNodes = [...inDegree.entries()]
    .filter(([, deg]) => deg > 0)
    .map(([id]) => id);
  errors.push(`CYCLE DETECTED! ${conceptIds.size - sorted} nodes in cycle: ${cycleNodes.join(', ')}`);
} else {
  console.log('Acyclicity: PASS (topological sort succeeded)');
}

// ── 3. Reachability from roots ──

const roots = graph.foundational_concepts || [];
if (roots.length === 0) {
  // Infer roots: concepts with no incoming edges
  for (const [id, deg] of [...inDegree.entries()]) {
    // Reset inDegree since Kahn's mutated it
  }
  warnings.push('No foundational_concepts declared; inferring roots from zero in-degree nodes');
}

// BFS from roots
const reachable = new Set();
const bfsQueue = [...roots];
// Also add all zero-indegree nodes as implicit roots
const inDegreeOriginal = new Map();
for (const id of conceptIds) inDegreeOriginal.set(id, 0);
for (const edge of graph.edges) {
  inDegreeOriginal.set(edge.to, inDegreeOriginal.get(edge.to) + 1);
}
for (const [id, deg] of inDegreeOriginal) {
  if (deg === 0 && !roots.includes(id)) bfsQueue.push(id);
}

while (bfsQueue.length > 0) {
  const node = bfsQueue.shift();
  if (reachable.has(node)) continue;
  reachable.add(node);
  for (const neighbor of (adj.get(node) || [])) {
    if (!reachable.has(neighbor)) bfsQueue.push(neighbor);
  }
}

const unreachable = [...conceptIds].filter(id => !reachable.has(id));
if (unreachable.length > 0) {
  errors.push(`${unreachable.length} concepts not reachable from roots: ${unreachable.join(', ')}`);
} else {
  console.log(`Reachability: PASS (all ${conceptIds.size} concepts reachable from roots)`);
}

// ── 4. Depth consistency ──

for (const edge of graph.edges) {
  const fromConcept = graph.concepts.find(c => c.id === edge.from);
  const toConcept = graph.concepts.find(c => c.id === edge.to);
  if (fromConcept && toConcept && fromConcept.depth > toConcept.depth) {
    warnings.push(`Edge ${edge.from} (depth ${fromConcept.depth}) -> ${edge.to} (depth ${toConcept.depth}): prerequisite is deeper than dependent`);
  }
}

// ── 5. Orphan check ──

const referenced = new Set();
for (const edge of graph.edges) {
  referenced.add(edge.from);
  referenced.add(edge.to);
}
const orphans = [...conceptIds].filter(id => !referenced.has(id));
if (orphans.length > 0) {
  warnings.push(`${orphans.length} isolated concepts (no edges): ${orphans.join(', ')}`);
}

// ── Report ──

console.log('');
if (warnings.length > 0) {
  console.log(`WARNINGS (${warnings.length}):`);
  for (const w of warnings) console.log(`  [WARN] ${w}`);
}
if (errors.length > 0) {
  console.log(`ERRORS (${errors.length}):`);
  for (const e of errors) console.log(`  [ERROR] ${e}`);
  process.exit(1);
} else {
  console.log('Validation: ALL CHECKS PASSED');
  process.exit(0);
}
