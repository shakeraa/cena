#!/usr/bin/env node
// =============================================================================
// Cena — regenerate the student SPA's baked catalog-fallback.json from the
// curated YAML under contracts/exam-catalog/. Runs in CI after a catalog
// YAML change so the offline fallback stays aligned (prr-220, ADR-0050).
//
// Deliberately minimal: YAML parse → object graph → JSON write. No runtime
// reference to the C# service — this is just a build step. The C# service
// does its own parse at startup (see ExamCatalogYamlLoader.cs); this script
// mirrors the shape carefully but is NOT the source of truth.
//
// Usage:
//   node scripts/catalog/rebuild-fallback.mjs
// =============================================================================

import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import YAML from 'yaml';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..', '..');
const catalogDir = join(repoRoot, 'contracts', 'exam-catalog');
const outPath = join(repoRoot, 'src', 'student', 'full-version', 'public', 'catalog-fallback.json');

function loadYaml(path) {
  return YAML.parse(readFileSync(path, 'utf8'));
}

function pickDisplay(display, locale) {
  if (!display) return { name: '(unnamed)', short_description: null };
  const hit = display[locale] ?? display.en ?? Object.values(display)[0];
  return {
    name: hit?.name ?? '(unnamed)',
    short_description: hit?.short_description ?? null,
  };
}

function projectTarget(t, locale = 'en') {
  return {
    exam_code: t.exam_code,
    family: t.family,
    region: t.region,
    track: t.track ?? null,
    units: t.units ?? null,
    regulator: t.regulator,
    ministry_subject_code: t.ministry_subject_code ?? null,
    ministry_question_paper_codes: t.ministry_question_paper_codes ?? [],
    availability: t.availability,
    available_from: t.available_from ?? null,
    item_bank_status: t.item_bank_status,
    passback_eligible: !!t.passback_eligible,
    default_lead_days: t.default_lead_days ?? 90,
    sittings: (t.sittings ?? []).map(s => ({
      code: s.code,
      academic_year: s.academic_year,
      season: s.season,
      moed: s.moed,
      canonical_date: s.canonical_date,
    })),
    display: pickDisplay(t.display, locale),
    topics: [],
  };
}

function main() {
  const metaPath = join(catalogDir, 'catalog-meta.yml');
  const meta = loadYaml(metaPath);
  const targets = {};

  for (const file of readdirSync(catalogDir)) {
    if (!file.endsWith('.yml') || file === 'catalog-meta.yml') continue;
    const obj = loadYaml(join(catalogDir, file));
    if (!obj?.exam_code) {
      console.error(`[catalog] skipping ${file}: no exam_code`);
      continue;
    }
    targets[obj.exam_code] = obj;
  }

  const groups = [];
  for (const family of meta.family_order ?? []) {
    const codes = (meta.families?.[family] ?? []).filter(c => targets[c]);
    if (codes.length === 0) continue;
    groups.push({
      family,
      targets: codes.map(c => projectTarget(targets[c], 'en')),
    });
  }

  const bundle = {
    catalog_version: meta.catalog_version,
    locale: 'en',
    locale_fallback_used: 'en',
    family_order: meta.family_order ?? [],
    groups,
    overlay: {
      tenant_id: null,
      enabled_exam_codes: null,
      disabled_exam_codes: [],
    },
  };

  writeFileSync(outPath, JSON.stringify(bundle, null, 2) + '\n', 'utf8');
  console.log(
    `[catalog] wrote ${outPath} — version=${bundle.catalog_version} groups=${groups.length}`
  );
}

main();
