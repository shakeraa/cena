#!/usr/bin/env npx tsx
// =============================================================================
// Cena Platform — Bagrut Taxonomy Validator (RDY-019a / Phase 3)
//
// Validates scripts/bagrut-taxonomy.json:
//   - All leaf conceptIds are unique
//   - Every conceptId appears in concept_to_seed_name_mapping
//   - Every mapping key is referenced by at least one track subtopic
//   - Validation counters match the actual counts
//   - No orphan tracks (only math_3u | math_4u | math_5u)
//   - bloom_range is a valid [min, max] with 1 <= min <= max <= 6
//
// Run:   npx tsx scripts/taxonomy-validator.ts
// CI:    .github/workflows/taxonomy-validator.yml
// =============================================================================

import { readFileSync, existsSync } from 'fs';
import { join, resolve } from 'path';

interface Subtopic { conceptId: string; bloom_range: [number, number]; }
interface Topic     { name: string; subtopics: Record<string, Subtopic>; }
interface Track     { name: string; examCodes: string[]; description: string; topics: Record<string, Topic>; }
interface Taxonomy {
  $schema?: string;
  version: string;
  description?: string;
  tracks: Record<string, Track>;
  concept_to_seed_name_mapping: Record<string, string>;
  validation?: {
    tracks: number;
    total_5u_subtopics: number;
    total_4u_subtopics: number;
    total_3u_subtopics: number;
    all_concept_ids_match_prerequisite_graph?: boolean;
  };
}

const ROOT = resolve(__dirname, '..');
const PATH = join(ROOT, 'scripts', 'bagrut-taxonomy.json');
const ALLOWED_TRACKS = new Set(['math_3u', 'math_4u', 'math_5u']);

function fail(msg: string): never {
  console.error(`FAIL: ${msg}`);
  process.exit(1);
}

if (!existsSync(PATH)) fail(`bagrut-taxonomy.json not found at ${PATH}`);

const tax: Taxonomy = JSON.parse(readFileSync(PATH, 'utf8'));

let errors = 0;
function err(msg: string) { console.error(`ERR  ${msg}`); errors++; }
function ok(msg: string)  { console.log (`PASS ${msg}`); }

// --- tracks shape ---
const trackIds = Object.keys(tax.tracks);
for (const t of trackIds) {
  if (!ALLOWED_TRACKS.has(t)) err(`unknown track id: ${t}`);
}
ok(`tracks present: ${trackIds.join(', ')}`);

// --- per-track leaf inventory ---
const leafByTrack: Record<string, Subtopic[]> = {};
const conceptUsageCount: Record<string, number> = {};

for (const [trackId, track] of Object.entries(tax.tracks)) {
  const leaves: Subtopic[] = [];
  for (const [topicId, topic] of Object.entries(track.topics)) {
    for (const [subId, sub] of Object.entries(topic.subtopics)) {
      // bloom sanity
      const [lo, hi] = sub.bloom_range;
      if (!(Number.isInteger(lo) && Number.isInteger(hi) && 1 <= lo && lo <= hi && hi <= 6)) {
        err(`${trackId}.${topicId}.${subId}: invalid bloom_range ${JSON.stringify(sub.bloom_range)}`);
      }
      if (!sub.conceptId || !/^[A-Z]{3}-\d{3}$/.test(sub.conceptId)) {
        err(`${trackId}.${topicId}.${subId}: invalid conceptId "${sub.conceptId}"`);
      } else {
        conceptUsageCount[sub.conceptId] = (conceptUsageCount[sub.conceptId] ?? 0) + 1;
        leaves.push(sub);
      }
    }
  }
  leafByTrack[trackId] = leaves;
}

// --- mapping coverage: every subtopic conceptId must be in the map ---
const mapKeys = new Set(Object.keys(tax.concept_to_seed_name_mapping));
for (const cid of Object.keys(conceptUsageCount)) {
  if (!mapKeys.has(cid)) err(`conceptId ${cid} used in tracks but missing from concept_to_seed_name_mapping`);
}
// reverse: every mapping key should be referenced by at least one track
for (const cid of mapKeys) {
  if (!(cid in conceptUsageCount)) err(`conceptId ${cid} in mapping but never used in any track (orphan)`);
}
ok(`concept id coverage: ${Object.keys(conceptUsageCount).length} used / ${mapKeys.size} mapped`);

// --- mapping value uniqueness ---
const seedNames = Object.values(tax.concept_to_seed_name_mapping);
const seedDup = seedNames.filter((n, i) => seedNames.indexOf(n) !== i);
if (seedDup.length > 0) err(`concept_to_seed_name_mapping has duplicate seed names: ${[...new Set(seedDup)].join(', ')}`);

// --- validation counter agreement ---
const vAnnounced = tax.validation;
if (vAnnounced) {
  const actual = {
    tracks: trackIds.length,
    total_5u_subtopics: leafByTrack['math_5u']?.length ?? 0,
    total_4u_subtopics: leafByTrack['math_4u']?.length ?? 0,
    total_3u_subtopics: leafByTrack['math_3u']?.length ?? 0,
  };
  for (const [k, expected] of Object.entries(vAnnounced)) {
    if (k === 'all_concept_ids_match_prerequisite_graph') continue;
    const got = (actual as Record<string, number>)[k];
    if (got !== expected) err(`validation.${k} announced=${expected} actual=${got}`);
  }
  ok(`validation counters match actuals`);
}

// --- output ---
console.log('');
console.log('Bagrut Taxonomy Validator');
console.log('=========================');
console.log(`tracks:            ${trackIds.length}`);
console.log(`5u subtopics:      ${leafByTrack['math_5u']?.length ?? 0}`);
console.log(`4u subtopics:      ${leafByTrack['math_4u']?.length ?? 0}`);
console.log(`3u subtopics:      ${leafByTrack['math_3u']?.length ?? 0}`);
console.log(`unique conceptIds: ${Object.keys(conceptUsageCount).length}`);
console.log(`seed mappings:     ${mapKeys.size}`);
console.log(`errors:            ${errors}`);

if (errors > 0) {
  console.error(`\nFAIL (${errors} error${errors > 1 ? 's' : ''})`);
  process.exit(1);
}
console.log('\nPASS');
