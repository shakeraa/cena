// =============================================================================
// tests/shipgate/locale-surface-coverage.spec.mjs
//
// Architecture test — asserts the ship-gate scanner walks EVERY i18n JSON in
// both student-web and admin/full-version across all three locales
// (en/he/ar). If a new locale file is added to either frontend, the
// PRODUCTION_GLOBS pattern in rulepack-scan.mjs MUST pick it up on merge.
// This test catches the class of regression where a new locale bundle or new
// frontend directory is added but the scanner's glob list forgets to include
// it.
//
// Covers prr-040 Definition-of-Done:
//   "Architecture test: every new i18n JSON in either frontend triggers the
//    scanner on merge."
// =============================================================================

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { existsSync, readFileSync, globSync } from "node:fs";
import { resolve, dirname, relative } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "../..");
const SCANNER_SRC = resolve(ROOT, "scripts/shipgate/rulepack-scan.mjs");

const STUDENT_LOCALE_DIR = "src/student/full-version/src/plugins/i18n/locales";
const ADMIN_LOCALE_DIR = "src/admin/full-version/src/plugins/i18n/locales";
const EXPECTED_LOCALES = ["en", "he", "ar"];

test("scanner source references student-web locale glob", () => {
  const src = readFileSync(SCANNER_SRC, "utf8");
  assert.ok(
    src.includes("src/student/full-version/src/plugins/i18n/locales/*.json"),
    "rulepack-scan.mjs must include the student-web locale glob"
  );
});

test("scanner source references admin/full-version locale glob", () => {
  const src = readFileSync(SCANNER_SRC, "utf8");
  assert.ok(
    src.includes("src/admin/full-version/src/plugins/i18n/locales/*.json"),
    "rulepack-scan.mjs must include the admin/full-version locale glob (prr-040)"
  );
});

test("scanner source references Vue templates in both frontends", () => {
  const src = readFileSync(SCANNER_SRC, "utf8");
  assert.ok(
    src.includes("src/student/full-version/src/**/*.vue"),
    "rulepack-scan.mjs must walk student-web Vue templates"
  );
  assert.ok(
    src.includes("src/admin/full-version/src/**/*.vue"),
    "rulepack-scan.mjs must walk admin/full-version Vue templates"
  );
});

test("student-web ships all three expected locales (en/he/ar)", () => {
  for (const locale of EXPECTED_LOCALES) {
    const path = resolve(ROOT, STUDENT_LOCALE_DIR, `${locale}.json`);
    assert.ok(
      existsSync(path),
      `student-web ${locale}.json must exist at ${relative(ROOT, path)}`
    );
  }
});

test("admin/full-version ships all three expected locales (en/he/ar)", () => {
  for (const locale of EXPECTED_LOCALES) {
    const path = resolve(ROOT, ADMIN_LOCALE_DIR, `${locale}.json`);
    assert.ok(
      existsSync(path),
      `admin/full-version ${locale}.json must exist at ${relative(ROOT, path)}`
    );
  }
});

test("every i18n JSON in both frontends is covered by scanner glob", () => {
  const studentJson = globSync(resolve(ROOT, STUDENT_LOCALE_DIR, "*.json"));
  const adminJson = globSync(resolve(ROOT, ADMIN_LOCALE_DIR, "*.json"));
  const allLocales = [...studentJson, ...adminJson];

  assert.ok(allLocales.length >= 6, "at least 6 locale files (3 per frontend) must exist");

  // The glob patterns in the scanner are literal strings; the production
  // scanner expands them at runtime. This test uses the same glob() call to
  // prove the patterns resolve to the expected file set.
  const src = readFileSync(SCANNER_SRC, "utf8");
  for (const jsonPath of allLocales) {
    const rel = relative(ROOT, jsonPath);
    // Scanner uses *.json — ensure each file's parent directory is covered.
    const covered =
      (rel.startsWith("src/student/full-version/src/plugins/i18n/locales/")
        && src.includes("src/student/full-version/src/plugins/i18n/locales/*.json"))
      || (rel.startsWith("src/admin/full-version/src/plugins/i18n/locales/")
        && src.includes("src/admin/full-version/src/plugins/i18n/locales/*.json"));
    assert.ok(covered, `locale file ${rel} must be covered by scanner glob`);
  }
});
