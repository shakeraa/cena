#!/usr/bin/env npx tsx
// =============================================================================
// Cena Platform — Glossary Validator (RDY-027)
// Validates that seed questions use canonical glossary terminology.
// Run: npx tsx scripts/glossary-validator.ts [--verbose]
// =============================================================================

import { readFileSync, existsSync } from 'fs';
import { join, resolve } from 'path';

// ── Types ──

interface GlossaryTerm {
  id: string;
  english: string;
  hebrew: string;
  arabic: string;
  domain: string;
  arabicGender?: string;
  notes?: string;
  source?: string;
}

interface Glossary {
  version: string;
  terms: GlossaryTerm[];
}

interface QuestionDoc {
  questionId?: string;
  id?: string;
  prompt?: string;
  stem?: string;
  choices?: string[];
  correctAnswer?: string;
  explanation?: string;
  subject?: string;
  conceptId?: string;
}

interface ValidationIssue {
  questionId: string;
  field: string;
  term: string;
  suggestion: string;
  severity: 'warning' | 'error';
}

// ── Load glossary ──

const ROOT = resolve(__dirname, '..');
const GLOSSARY_PATH = join(ROOT, 'config', 'glossary.json');

function loadGlossary(): Glossary {
  if (!existsSync(GLOSSARY_PATH)) {
    console.error(`ERROR: Glossary not found at ${GLOSSARY_PATH}`);
    process.exit(1);
  }
  return JSON.parse(readFileSync(GLOSSARY_PATH, 'utf8'));
}

// ── Build lookup indices ──

function buildIndices(glossary: Glossary) {
  const hebrewTerms = new Map<string, GlossaryTerm>();
  const arabicTerms = new Map<string, GlossaryTerm>();
  const englishTerms = new Map<string, GlossaryTerm>();

  for (const term of glossary.terms) {
    hebrewTerms.set(term.hebrew.toLowerCase(), term);
    arabicTerms.set(term.arabic.toLowerCase(), term);
    englishTerms.set(term.english.toLowerCase(), term);
  }

  return { hebrewTerms, arabicTerms, englishTerms };
}

// ── Validate a single question ──

function validateQuestion(
  question: QuestionDoc,
  indices: ReturnType<typeof buildIndices>,
): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const qid = question.questionId || question.id || 'unknown';

  // Collect all text fields to check
  const fieldsToCheck: Array<{ name: string; text: string }> = [];
  if (question.prompt) fieldsToCheck.push({ name: 'prompt', text: question.prompt });
  if (question.stem) fieldsToCheck.push({ name: 'stem', text: question.stem });
  if (question.explanation) fieldsToCheck.push({ name: 'explanation', text: question.explanation });
  if (question.choices) {
    question.choices.forEach((c, i) => fieldsToCheck.push({ name: `choice[${i}]`, text: c }));
  }

  for (const { name, text } of fieldsToCheck) {
    // Check for Hebrew text that might use non-standard terms
    if (containsHebrew(text)) {
      checkAgainstGlossary(text, indices.hebrewTerms, 'hebrew', qid, name, issues);
    }
    // Check for Arabic text
    if (containsArabic(text)) {
      checkAgainstGlossary(text, indices.arabicTerms, 'arabic', qid, name, issues);
    }
  }

  return issues;
}

function containsHebrew(text: string): boolean {
  return /[\u0590-\u05FF]/.test(text);
}

function containsArabic(text: string): boolean {
  return /[\u0600-\u06FF]/.test(text);
}

function checkAgainstGlossary(
  text: string,
  glossaryMap: Map<string, GlossaryTerm>,
  language: string,
  qid: string,
  field: string,
  issues: ValidationIssue[],
): void {
  // Build a sorted list of glossary terms (longest first for greedy matching)
  const terms = [...glossaryMap.keys()].sort((a, b) => b.length - a.length);
  const lowerText = text.toLowerCase();
  let matchCount = 0;

  for (const term of terms) {
    let idx = 0;
    while ((idx = lowerText.indexOf(term, idx)) !== -1) {
      // Word-boundary check: char before/after must not be a letter
      const charBefore = idx > 0 ? lowerText[idx - 1] : ' ';
      const charAfter = idx + term.length < lowerText.length
        ? lowerText[idx + term.length] : ' ';

      const isWordBound = (c: string) =>
        /[\s\p{P}\p{S}\d]/u.test(c) || c === undefined;

      if (isWordBound(charBefore) && isWordBound(charAfter)) {
        matchCount++;
        break; // one match per term per field
      }
      idx += term.length;
    }
  }

  // Report: if the field contains text in this language but zero glossary terms,
  // flag it as a warning (the question may use non-standard terminology)
  if (matchCount === 0) {
    const textLen = [...text].filter(c => {
      if (language === 'hebrew') return /[\u0590-\u05FF]/.test(c);
      if (language === 'arabic') return /[\u0600-\u06FF]/.test(c);
      return false;
    }).length;

    // Only flag if there's substantial text in this language (>10 chars)
    if (textLen > 10) {
      issues.push({
        questionId: qid,
        field,
        term: `(${language})`,
        suggestion: `Field '${field}' contains ${textLen} ${language} characters but no canonical glossary terms were found. Review terminology.`,
        severity: 'warning',
      });
    }
  }
}

// ── Validate glossary integrity ──

function validateGlossaryIntegrity(glossary: Glossary): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const seenIds = new Set<string>();

  for (const term of glossary.terms) {
    // Duplicate ID check
    if (seenIds.has(term.id)) {
      issues.push({
        questionId: 'glossary',
        field: 'id',
        term: term.id,
        suggestion: `Duplicate term ID: ${term.id}`,
        severity: 'error',
      });
    }
    seenIds.add(term.id);

    // Required fields
    if (!term.english?.trim()) {
      issues.push({
        questionId: 'glossary',
        field: 'english',
        term: term.id,
        suggestion: `Missing English translation for ${term.id}`,
        severity: 'error',
      });
    }
    if (!term.hebrew?.trim()) {
      issues.push({
        questionId: 'glossary',
        field: 'hebrew',
        term: term.id,
        suggestion: `Missing Hebrew translation for ${term.id}`,
        severity: 'error',
      });
    }
    if (!term.arabic?.trim()) {
      issues.push({
        questionId: 'glossary',
        field: 'arabic',
        term: term.id,
        suggestion: `Missing Arabic translation for ${term.id}`,
        severity: 'error',
      });
    }
    if (!term.domain?.trim()) {
      issues.push({
        questionId: 'glossary',
        field: 'domain',
        term: term.id,
        suggestion: `Missing domain for ${term.id}`,
        severity: 'error',
      });
    }

    // Arabic gender for non-notation/unit terms
    const needsGender = !['notation', 'units'].includes(term.domain);
    if (needsGender && !term.arabicGender) {
      issues.push({
        questionId: 'glossary',
        field: 'arabicGender',
        term: term.id,
        suggestion: `Missing Arabic gender for ${term.id} (${term.arabic})`,
        severity: 'warning',
      });
    }
  }

  return issues;
}

// ── Validate seed questions against glossary ──

function findAndValidateSeedQuestions(
  glossary: Glossary,
  verbose: boolean,
): ValidationIssue[] {
  const seedPaths = [
    join(ROOT, 'src', 'shared', 'Cena.Infrastructure', 'Content', 'content-seed.json'),
  ];

  const indices = buildIndices(glossary);
  const allIssues: ValidationIssue[] = [];

  for (const seedPath of seedPaths) {
    if (!existsSync(seedPath)) {
      if (verbose) console.log(`  Skipping (not found): ${seedPath}`);
      continue;
    }

    try {
      const seedData = JSON.parse(readFileSync(seedPath, 'utf8'));
      const questions: QuestionDoc[] = seedData.questions || seedData.items || [];

      for (const q of questions) {
        const issues = validateQuestion(q, indices);
        allIssues.push(...issues);
      }

      if (verbose) {
        console.log(`  Checked ${questions.length} questions from ${seedPath}`);
      }
    } catch (err) {
      console.error(`  Error parsing ${seedPath}: ${err}`);
    }
  }

  return allIssues;
}

// ── Print stats ──

function printGlossaryStats(glossary: Glossary): void {
  const byDomain: Record<string, number> = {};
  let withGender = 0;
  let withNotes = 0;

  for (const term of glossary.terms) {
    byDomain[term.domain] = (byDomain[term.domain] || 0) + 1;
    if (term.arabicGender) withGender++;
    if (term.notes) withNotes++;
  }

  const mathDomains = ['algebra', 'functions', 'calculus', 'geometry', 'trigonometry',
    'probability', 'statistics', 'sequences', 'vectors', 'general'];
  const physDomains = Object.keys(byDomain).filter(
    d => d.startsWith('phys') || d === 'units' || d === 'notation',
  );

  const mathCount = mathDomains.reduce((s, d) => s + (byDomain[d] || 0), 0);
  const physCount = physDomains.reduce((s, d) => s + (byDomain[d] || 0), 0);

  console.log('\n=== Glossary Statistics ===');
  console.log(`Version: ${glossary.version}`);
  console.log(`Total terms: ${glossary.terms.length}`);
  console.log(`Math terms: ${mathCount} (target: 150+)`);
  console.log(`Physics terms: ${physCount} (target: 80+)`);
  console.log(`With Arabic gender: ${withGender}/${glossary.terms.length}`);
  console.log(`With notes: ${withNotes}/${glossary.terms.length}`);
  console.log('\nBy domain:');
  for (const [domain, count] of Object.entries(byDomain).sort()) {
    console.log(`  ${domain}: ${count}`);
  }

  // Threshold checks
  const failures: string[] = [];
  if (mathCount < 150) failures.push(`Math terms ${mathCount} < 150`);
  if (physCount < 80) failures.push(`Physics terms ${physCount} < 80`);
  if (failures.length > 0) {
    console.log('\n❌ THRESHOLD FAILURES:');
    failures.forEach(f => console.log(`  - ${f}`));
  } else {
    console.log('\n✅ All thresholds met');
  }
}

// ── Main ──

function main(): void {
  const args = process.argv.slice(2);
  const verbose = args.includes('--verbose') || args.includes('-v');

  console.log('Cena Glossary Validator (RDY-027)');
  console.log('=================================\n');

  // 1. Load and validate glossary integrity
  console.log('1. Loading glossary...');
  const glossary = loadGlossary();
  console.log(`   Loaded ${glossary.terms.length} terms from ${GLOSSARY_PATH}`);

  console.log('\n2. Validating glossary integrity...');
  const integrityIssues = validateGlossaryIntegrity(glossary);
  const errors = integrityIssues.filter(i => i.severity === 'error');
  const warnings = integrityIssues.filter(i => i.severity === 'warning');

  if (errors.length > 0) {
    console.log(`\n   ❌ ${errors.length} errors:`);
    errors.forEach(e => console.log(`      [ERROR] ${e.suggestion}`));
  }
  if (warnings.length > 0) {
    console.log(`\n   ⚠️  ${warnings.length} warnings:`);
    if (verbose) {
      warnings.forEach(w => console.log(`      [WARN] ${w.suggestion}`));
    } else {
      console.log('      (use --verbose to see details)');
    }
  }
  if (errors.length === 0 && warnings.length === 0) {
    console.log('   ✅ No integrity issues');
  }

  // 2. Validate seed questions
  console.log('\n3. Checking seed questions against glossary...');
  const seedIssues = findAndValidateSeedQuestions(glossary, verbose);
  if (seedIssues.length === 0) {
    console.log('   ✅ No terminology issues in seed questions');
  } else {
    console.log(`   ⚠️  ${seedIssues.length} terminology issues found`);
    seedIssues.forEach(i => console.log(`      [${i.severity.toUpperCase()}] ${i.questionId}.${i.field}: ${i.suggestion}`));
  }

  // 3. Stats
  printGlossaryStats(glossary);

  // Exit code
  if (errors.length > 0) {
    process.exit(1);
  }
}

main();
