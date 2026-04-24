#!/usr/bin/env node
// =============================================================================
// RDY-004a: Translation Gap Report
// Builds a source-based concept report for the committed question-bank seeds.
// =============================================================================

import { mkdirSync, readFileSync, writeFileSync } from 'fs';
import { dirname, resolve } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const seedPath = resolve(__dirname, '../src/api/Cena.Admin.Api/QuestionBankSeedData.cs');
const reportDir = resolve(__dirname, '../docs/content');
const today = new Date().toISOString().slice(0, 10);
const reportPath = resolve(reportDir, `translation-gap-${today}.md`);

const seedSource = readFileSync(seedPath, 'utf-8');
const handCraftedQuestionPattern =
  /yield return Q\((?<stem>"(?:\\.|[^"])*")\s*,\s*"(?<subject>[^"]+)"\s*,\s*"(?<topic>[^"]+)"\s*,\s*"(?<grade>[^"]+)"\s*,\s*(?<bloom>\d+)\s*,\s*(?<difficulty>[\d.]+)f\s*,\s*new\[\]\s*\{(?<concepts>[^}]*)\}\s*,\s*"(?<language>he|ar|en)"\s*,\s*"(?<source>[^"]+)"/gs;
const conceptPattern = /"([^"]+)"/g;

const conceptStats = new Map();
let totalQuestions = 0;

for (const match of seedSource.matchAll(handCraftedQuestionPattern)) {
  totalQuestions += 1;
  const concepts = [...match.groups.concepts.matchAll(conceptPattern)].map(([_, concept]) => concept);
  const hasArabic = match.groups.language === 'ar';

  for (const concept of concepts) {
    const current = conceptStats.get(concept) ?? {
      concept,
      total: 0,
      withArabic: 0,
      missingArabic: 0,
    };

    current.total += 1;
    if (hasArabic) {
      current.withArabic += 1;
    } else {
      current.missingArabic += 1;
    }

    conceptStats.set(concept, current);
  }
}

const sortedConcepts = [...conceptStats.values()].sort((a, b) =>
  b.missingArabic - a.missingArabic
  || b.total - a.total
  || a.concept.localeCompare(b.concept));

const totals = sortedConcepts.reduce((acc, item) => {
  acc.totalAssignments += item.total;
  acc.withArabic += item.withArabic;
  acc.missingArabic += item.missingArabic;
  return acc;
}, { totalAssignments: 0, withArabic: 0, missingArabic: 0 });

const reportLines = [
  '# Translation Gap Report',
  '',
  `Generated: ${today}`,
  '',
  '## Scope',
  '- Source: committed hand-crafted seed questions in `src/api/Cena.Admin.Api/QuestionBankSeedData.cs`.',
  '- This report is source-based and does not inspect a live Marten/PostgreSQL database.',
  '- Programmatically generated seed questions are excluded because their final distribution materializes at runtime.',
  '',
  '## Summary',
  `- Hand-crafted questions scanned: ${totalQuestions}`,
  `- Concept assignments scanned: ${totals.totalAssignments}`,
  `- Assignments already in Arabic: ${totals.withArabic}`,
  `- Assignments missing Arabic: ${totals.missingArabic}`,
  '',
  '## Priority Queue',
  '| Concept | Total | With Arabic | Missing Arabic | Priority |',
  '|---|---:|---:|---:|---|',
];

for (const item of sortedConcepts) {
  const priority = item.missingArabic >= 3 ? 'High'
    : item.missingArabic >= 2 ? 'Medium'
    : 'Low';
  reportLines.push(`| ${item.concept} | ${item.total} | ${item.withArabic} | ${item.missingArabic} | ${priority} |`);
}

mkdirSync(reportDir, { recursive: true });
writeFileSync(reportPath, `${reportLines.join('\n')}\n`, 'utf8');

console.log(`Wrote ${reportPath}`);
