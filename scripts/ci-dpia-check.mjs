#!/usr/bin/env node
// =============================================================================
// Cena Platform -- CI DPIA Existence Gate (FIND-privacy-009)
//
// Asserts that a Data Protection Impact Assessment (DPIA) document exists for
// the current calendar quarter. This prevents the DPIA from lapsing without
// anyone noticing.
//
// Naming convention: docs/compliance/dpia-YYYY-QN.md
//   YYYY = calendar year
//   QN   = Q1 (Jan-Mar), Q2 (Apr-Jun), Q3 (Jul-Sep), Q4 (Oct-Dec)
//
// Exit codes:
//   0 = DPIA found for current quarter
//   1 = No DPIA found (CI should fail)
// =============================================================================

import { readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';

const COMPLIANCE_DIR = resolve(
  import.meta.dirname ?? process.cwd(),
  '..',
  'docs',
  'compliance'
);

/**
 * Returns the quarter string for a given date, e.g. "2026-Q2".
 * @param {Date} date
 * @returns {string}
 */
function getQuarterString(date) {
  const year = date.getFullYear();
  const month = date.getMonth(); // 0-indexed
  const quarter = Math.floor(month / 3) + 1;
  return `${year}-Q${quarter}`;
}

/**
 * Returns a regex-safe quarter pattern for matching filenames.
 * Matches both dpia-YYYY-QN.md and dpia-YYYY-0N.md (month-based legacy).
 * @param {string} quarterStr e.g. "2026-Q2"
 * @returns {RegExp}
 */
function buildPattern(quarterStr) {
  // Match dpia-2026-Q2.md or dpia-2026-04.md (first month of the quarter)
  const [year, q] = quarterStr.split('-');
  const quarterNum = parseInt(q.replace('Q', ''), 10);
  const firstMonth = String((quarterNum - 1) * 3 + 1).padStart(2, '0');

  // Accept either naming convention
  return new RegExp(
    `^dpia-${year}-(Q${quarterNum}|${firstMonth})\\.md$`,
    'i'
  );
}

function main() {
  const now = new Date();
  const currentQuarter = getQuarterString(now);
  const pattern = buildPattern(currentQuarter);

  console.log(`[DPIA check] Current quarter: ${currentQuarter}`);
  console.log(`[DPIA check] Looking in: ${COMPLIANCE_DIR}`);
  console.log(`[DPIA check] Pattern: ${pattern}`);

  let files;
  try {
    files = readdirSync(COMPLIANCE_DIR);
  } catch (err) {
    if (err.code === 'ENOENT') {
      console.error(
        `[DPIA check] FAIL: Compliance directory does not exist: ${COMPLIANCE_DIR}`
      );
      console.error(
        '[DPIA check] A DPIA is required under GDPR Art 35 for high-risk processing of children\'s data.'
      );
      process.exit(1);
    }
    throw err;
  }

  const matches = files.filter((f) => {
    if (!pattern.test(f)) return false;
    // Ensure it's actually a file, not a directory
    try {
      return statSync(join(COMPLIANCE_DIR, f)).isFile();
    } catch {
      return false;
    }
  });

  if (matches.length === 0) {
    console.error(`[DPIA check] FAIL: No DPIA found for ${currentQuarter}`);
    console.error(
      `[DPIA check] Expected a file matching ${pattern} in ${COMPLIANCE_DIR}`
    );
    console.error(
      '[DPIA check] GDPR Art 35 requires a DPIA for high-risk processing. ' +
      'Create docs/compliance/dpia-YYYY-QN.md using docs/compliance/dpia-template.md.'
    );
    process.exit(1);
  }

  console.log(
    `[DPIA check] PASS: Found ${matches.length} DPIA(s) for ${currentQuarter}:`
  );
  for (const m of matches) {
    console.log(`  - ${m}`);
  }

  process.exit(0);
}

main();
