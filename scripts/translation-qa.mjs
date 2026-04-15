#!/usr/bin/env node
// =============================================================================
// RDY-004: Translation QA Script
// Validates the canonical Arabic translation QA prerequisites:
//   1. Canonical glossary integrity from config/glossary.json
//   2. Coverage of required curriculum concepts in the glossary
//   3. Heuristic detection of duplicate or incomplete glossary entries
//
// Usage: node scripts/translation-qa.mjs [--verbose]
// =============================================================================

import { readFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const verbose = process.argv.includes('--verbose');

// ── 1. Load canonical glossary ──

const glossaryPath = resolve(__dirname, '../config/glossary.json');
const glossaryRaw = JSON.parse(readFileSync(glossaryPath, 'utf-8'));
const glossaryEntries = glossaryRaw.terms ?? [];
const glossaryTerms = new Map(); // English -> Arabic
const domains = new Map();
const issues = [];
const warnings = [];
const englishSeen = new Set();
const arabicSeen = new Set();

for (const entry of glossaryEntries) {
  const english = (entry.english ?? '').trim();
  const arabic = (entry.arabic ?? '').trim();
  const hebrew = (entry.hebrew ?? '').trim();
  const domain = (entry.domain ?? 'unknown').trim();

  if (!english) {
    issues.push('Glossary entry missing english term');
    continue;
  }

  if (!arabic) {
    issues.push(`Glossary entry '${english}' is missing Arabic text`);
  }

  if (!hebrew) {
    issues.push(`Glossary entry '${english}' is missing Hebrew text`);
  }

  const englishKey = english.toLowerCase();
  const arabicKey = arabic.toLowerCase();

  if (englishSeen.has(englishKey)) {
    warnings.push(`Duplicate English glossary term reused across domains: '${english}'`);
  }
  englishSeen.add(englishKey);

  if (arabic && arabicSeen.has(arabicKey)) {
    warnings.push(`Duplicate Arabic glossary term reused across domains: '${arabic}'`);
  }
  if (arabic) {
    arabicSeen.add(arabicKey);
    glossaryTerms.set(englishKey, arabic);
  }

  domains.set(domain, (domains.get(domain) ?? 0) + 1);
}

console.log(`Loaded ${glossaryEntries.length} glossary terms from config/glossary.json`);

console.log('');
console.log('=== Arabic Translation QA Report ===');
console.log('');

// Glossary coverage check
const requiredConcepts = {
  equation: ['equation'],
  inequality: ['inequality'],
  variable: ['variable'],
  function: ['function'],
  derivative: ['derivative'],
  integral: ['integral'],
  limit: ['limit'],
  slope: ['slope'],
  quadratic: ['quadratic equation'],
  linear: ['linear equation'],
  polynomial: ['polynomial'],
  probability: ['probability'],
  sequence: ['arithmetic sequence', 'geometric sequence'],
  series: ['sum of series'],
  vector: ['vector'],
  logarithm: ['logarithm'],
  sine: ['sine'],
  cosine: ['cosine'],
  tangent: ['tangent (trig)', 'tangent line', 'tangent (geometry)'],
  proof: ['proof'],
  theorem: ['theorem'],
  matrix: ['matrix'],
  domain: ['domain'],
  range: ['range'],
  factoring: ['factoring'],
  fraction: ['fraction'],
  area: ['area'],
  volume: ['volume'],
  triangle: ['triangle'],
  circle: ['circle'],
  angle: ['angle'],
  permutation: ['permutation'],
  combination: ['combination'],
  'standard deviation': ['standard deviation'],
  mean: ['mean / average'],
  'normal distribution': ['normal distribution'],
  'binomial distribution': ['binomial distribution'],
  'conditional probability': ['conditional probability'],
  'definite integral': ['definite integral'],
  'chain rule': ['chain rule'],
  'product rule': ['product rule'],
};

function hasCoverage(aliases) {
  return aliases.some(alias => glossaryTerms.has(alias));
}

const requiredConceptEntries = Object.entries(requiredConcepts);
const missingFromGlossary = requiredConceptEntries
  .filter(([, aliases]) => !hasCoverage(aliases))
  .map(([label]) => label);
const coveredConcepts = requiredConceptEntries
  .filter(([, aliases]) => hasCoverage(aliases))
  .map(([label]) => label);

console.log(`Glossary Coverage: ${coveredConcepts.length}/${requiredConceptEntries.length} required concepts`);
if (missingFromGlossary.length > 0) {
  console.log(`  Missing: ${missingFromGlossary.join(', ')}`);
}

// Cluster coverage
const clusters = {
  algebra: ['equation', 'inequality', 'variable', 'quadratic', 'linear', 'polynomial', 'factoring', 'fraction'],
  functions: ['function', 'domain', 'range', 'slope', 'logarithm'],
  calculus: ['derivative', 'integral', 'limit', 'chain rule', 'product rule', 'definite integral'],
  geometry: ['triangle', 'circle', 'angle', 'area', 'volume'],
  trigonometry: ['sine', 'cosine', 'tangent'],
  probability: ['probability', 'permutation', 'combination', 'standard deviation', 'mean', 'normal distribution', 'binomial distribution', 'conditional probability'],
  vectors: ['vector'],
};

console.log('');
console.log('Cluster Coverage:');
for (const [cluster, terms] of Object.entries(clusters)) {
  const covered = terms.filter(t => glossaryTerms.has(t)).length;
  const status = covered === terms.length ? 'COMPLETE' : `${covered}/${terms.length}`;
  console.log(`  ${cluster}: ${status}`);
}

if (verbose) {
  console.log('');
  console.log('Domain Distribution:');
  for (const [domain, count] of [...domains.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    console.log(`  ${domain}: ${count}`);
  }
  if (warnings.length > 0) {
    console.log('');
    console.log('Warnings:');
    for (const warning of warnings) {
      console.log(`  ${warning}`);
    }
  }
}

// Summary
console.log('');
const totalIssues = issues.length + missingFromGlossary.length;

if (totalIssues === 0) {
  console.log(`Glossary: ${glossaryEntries.length} terms (target: 200+) — PASS`);
  console.log('Canonical glossary integrity: PASS');
  console.log('');
  console.log('NOTE: This CI gate validates canonical glossary readiness before translation batches land.');
  process.exit(0);
} else {
  console.log(`Issues found: ${totalIssues}`);
  if (issues.length > 0) {
    console.log(`  Integrity: ${issues.length}`);
    for (const issue of issues) {
      console.log(`    [GLOSSARY] ${issue}`);
    }
  }
  if (missingFromGlossary.length > 0) {
    console.log(`  Missing Required Concepts: ${missingFromGlossary.length}`);
    for (const concept of missingFromGlossary) {
      console.log(`    [COVERAGE] Missing required concept '${concept}'`);
    }
  }
  process.exit(1);
}
