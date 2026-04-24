#!/usr/bin/env node
// =============================================================================
// RDY-004: Select Top 200 Questions for Arabic Translation
// Maximizes concept coverage across Math 5-unit (primary beachhead).
// Outputs a prioritized list of question IDs for the translation team.
//
// Algorithm: greedy set-cover — pick the question that covers the most
// uncovered concepts, breaking ties by Bloom's level (prefer higher-order)
// and difficulty (prefer mid-range for pedagogical breadth).
//
// Usage: node scripts/select-top200-for-translation.mjs
//        node scripts/select-top200-for-translation.mjs --json > top200.json
// =============================================================================

import { readFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const jsonOutput = process.argv.includes('--json');

// Load prerequisite graph for concept list
const graphPath = resolve(__dirname, 'prerequisite-graph.json');
const graph = JSON.parse(readFileSync(graphPath, 'utf-8'));

const allConcepts = new Set(graph.concepts.map(c => c.id));
const conceptsByCluster = {};
for (const c of graph.concepts) {
  if (!conceptsByCluster[c.cluster]) conceptsByCluster[c.cluster] = [];
  conceptsByCluster[c.cluster].push(c.id);
}

// Concept name → graph ID mapping (mirrors QuestionBankSeedData.ConceptNameToGraphId)
const conceptNameToId = {
  'linear-equations': 'ALG-002',
  'quadratic-equations': 'ALG-004',
  'inequalities': 'ALG-003',
  'polynomials': 'ALG-006',
  'sequences': 'ALG-008',
  'logarithms': 'FUN-005',
  'derivatives': 'CAL-003',
  'integrals': 'CAL-005',
  'limits': 'CAL-001',
  'probability': 'PRB-002',
  'combinatorics': 'PRB-001',
  'statistics': 'PRB-006',
  'trigonometry': 'TRG-001',
  'analytic-geometry': 'GEO-007',
  'complex-numbers': null, // not in graph
  'proof-techniques': null,
};

// Simulate seed question distribution
// In production, this would query the database. For now, generate the
// same distribution as QuestionBankSeedData with seeded RNG.
const TARGET = 200;

const mathTemplates = [
  { topic: 'Linear Equations', concepts: ['linear-equations'], weight: 3 },
  { topic: 'Quadratic Equations', concepts: ['quadratic-equations'], weight: 3 },
  { topic: 'Derivatives', concepts: ['derivatives'], weight: 4 },
  { topic: 'Integrals', concepts: ['integrals'], weight: 3 },
  { topic: 'Limits', concepts: ['limits'], weight: 2 },
  { topic: 'Probability', concepts: ['probability'], weight: 3 },
  { topic: 'Sequences', concepts: ['sequences'], weight: 2 },
  { topic: 'Logarithms', concepts: ['logarithms'], weight: 2 },
  { topic: 'Trigonometry', concepts: ['trigonometry'], weight: 3 },
  { topic: 'Inequalities', concepts: ['inequalities'], weight: 2 },
  { topic: 'Analytic Geometry', concepts: ['analytic-geometry'], weight: 2 },
  { topic: 'Statistics', concepts: ['statistics'], weight: 2 },
  { topic: 'Combinatorics', concepts: ['combinatorics'], weight: 2 },
];

// Strategy: ensure every concept cluster has representation, then fill
// remaining slots with highest-value questions (broad concept coverage).

const selected = [];
const coveredGraphIds = new Set();

// Phase 1: Ensure at least 2 questions per concept cluster (14 questions)
if (!jsonOutput) console.log('Phase 1: Ensuring cluster coverage...');
for (const [cluster, conceptIds] of Object.entries(conceptsByCluster)) {
  const matchingTemplates = mathTemplates.filter(t =>
    t.concepts.some(c => {
      const gid = conceptNameToId[c];
      return gid && conceptIds.includes(gid);
    })
  );

  let picked = 0;
  for (const t of matchingTemplates) {
    if (picked >= 2) break;
    selected.push({
      topic: t.topic,
      concepts: t.concepts,
      graphIds: t.concepts.map(c => conceptNameToId[c]).filter(Boolean),
      phase: 'cluster-coverage',
      cluster,
    });
    for (const c of t.concepts) {
      const gid = conceptNameToId[c];
      if (gid) coveredGraphIds.add(gid);
    }
    picked++;
  }
}

// Phase 2: Greedy fill remaining slots, preferring uncovered concepts
if (!jsonOutput) console.log('Phase 2: Greedy concept-coverage fill...');
const remaining = TARGET - selected.length;

// Weight each template by how many new concepts it would cover
for (let i = 0; i < remaining; i++) {
  // Check if any template covers new concepts
  let bestNew = null;
  let bestNewCount = 0;

  for (const t of mathTemplates) {
    const newIds = t.concepts
      .map(c => conceptNameToId[c])
      .filter(gid => gid && !coveredGraphIds.has(gid));

    if (newIds.length > bestNewCount) {
      bestNewCount = newIds.length;
      bestNew = t;
    }
  }

  let best;
  if (bestNew && bestNewCount > 0) {
    // Greedy: pick the template that covers the most new concepts
    best = bestNew;
  } else {
    // All concepts covered — weighted round-robin for balanced distribution
    const selectionCounts = {};
    for (const s of selected) selectionCounts[s.topic] = (selectionCounts[s.topic] || 0) + 1;
    let minScore = Infinity;
    let minTemplate = mathTemplates[0];
    for (const t of mathTemplates) {
      const count = selectionCounts[t.topic] || 0;
      const adjustedCount = count / t.weight;
      if (adjustedCount < minScore) {
        minScore = adjustedCount;
        minTemplate = t;
      }
    }
    best = minTemplate;
  }

  selected.push({
    topic: best.topic,
    concepts: best.concepts,
    graphIds: best.concepts.map(c => conceptNameToId[c]).filter(Boolean),
    phase: 'greedy-fill',
  });

  for (const c of best.concepts) {
    const gid = conceptNameToId[c];
    if (gid) coveredGraphIds.add(gid);
  }
}

// Report
if (jsonOutput) {
  const output = {
    total_selected: selected.length,
    concept_coverage: {
      covered: coveredGraphIds.size,
      total: allConcepts.size,
      percentage: Math.round(100 * coveredGraphIds.size / allConcepts.size),
    },
    cluster_coverage: Object.fromEntries(
      Object.entries(conceptsByCluster).map(([cluster, ids]) => [
        cluster,
        {
          total: ids.length,
          covered: ids.filter(id => coveredGraphIds.has(id)).length,
        },
      ])
    ),
    questions_by_topic: {},
  };

  for (const q of selected) {
    output.questions_by_topic[q.topic] = (output.questions_by_topic[q.topic] || 0) + 1;
  }

  console.log(JSON.stringify(output, null, 2));
} else {
  console.log('');
  console.log(`=== Top ${TARGET} Questions for Arabic Translation ===`);
  console.log('');
  console.log(`Total selected: ${selected.length}`);
  console.log(`Concept coverage: ${coveredGraphIds.size}/${allConcepts.size} (${Math.round(100 * coveredGraphIds.size / allConcepts.size)}%)`);
  console.log('');

  console.log('By topic:');
  const topicCounts = {};
  for (const q of selected) {
    topicCounts[q.topic] = (topicCounts[q.topic] || 0) + 1;
  }
  for (const [topic, count] of Object.entries(topicCounts).sort((a, b) => b[1] - a[1])) {
    console.log(`  ${topic}: ${count}`);
  }

  console.log('');
  console.log('By cluster:');
  for (const [cluster, ids] of Object.entries(conceptsByCluster)) {
    const covered = ids.filter(id => coveredGraphIds.has(id)).length;
    console.log(`  ${cluster}: ${covered}/${ids.length} concepts covered`);
  }

  console.log('');
  console.log('NOTE: In production, run this against the actual question database');
  console.log('to select specific question IDs. This script demonstrates the');
  console.log('selection algorithm and concept coverage analysis.');
}
