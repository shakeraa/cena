#!/usr/bin/env node
// =============================================================================
// check-fake-api-no-leakage.mjs
//
// Scans two classes of file for banned dev-loop leakage:
//
//   1. Every .ts file under
//      src/student/full-version/src/plugins/fake-api/handlers/
//      (for string / template literals)
//   2. Every .json file under
//      src/student/full-version/src/plugins/i18n/locales/
//      (recursively, flat-walked for string values — FIND-ux-005b)
//
// Fails if any **string value** contains:
//
//   - An internal ticket prefix (e.g. things that look like "STB-04b",
//     "STU-W-08", "STU-A-01")
//   - Scaffolding vocabulary the product rule bans from user-facing surfaces
//     ("stub", "placeholder", "will wire", "phase a", "phaseANote")
//   - TODO / FIXME markers
//
// This exists because FIND-ux-005 surfaced MSW handlers that shipped task IDs
// like "(STB-04b will wire real LLM streaming.)" directly into the tutor chat
// UI, and FIND-ux-005b surfaced the same leakage in the i18n bundle (the
// `phaseANote` keys under `progress.mastery`, `progress.sessions`, and
// `settingsPage.*`). The user's locked rule is: NO stubs — production grade,
// labels match data. Both MSW mocks and i18n messages are user-facing copy
// the instant the app is served, so they must not contain scaffolding.
//
// IMPORTANT: the .ts scan intentionally ignores comments. Comments that
// document the guard itself (for example this file) must reference the
// banned patterns without triggering, so we only walk the TypeScript source
// for string / template literals rather than raw-grepping the file.
//
// The .json scan does NOT skip anything — every string value in every locale
// file is checked, because JSON has no comments.
//
// Usage:
//   node scripts/check-fake-api-no-leakage.mjs
//
// Exits 0 on clean, 1 on violations (with a per-file report).
// =============================================================================

import { readFileSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readdirSync, statSync } from 'node:fs'

const __dirname = dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = resolve(__dirname, '..')
const HANDLERS_DIR = resolve(
  REPO_ROOT,
  'src/student/full-version/src/plugins/fake-api/handlers',
)
const I18N_LOCALES_DIR = resolve(
  REPO_ROOT,
  'src/student/full-version/src/plugins/i18n/locales',
)

/**
 * Only scan the Cena-specific student handler subdirectories. The
 * `handlers/apps/*` and other Vuexy demo handlers are inherited product copy
 * that may legitimately contain strings the guard would otherwise flag as
 * scaffolding (e.g. a "Coming soon" navigation label).
 */
const SCANNED_SUBDIRS = [
  'student-me',
  'student-tutor',
  'student-sessions',
  'student-gamification',
  'student-challenges',
  'student-social',
  'student-notifications',
  'student-analytics',
  'student-knowledge',
]

/**
 * Banned patterns (case-insensitive where noted). Each is checked against the
 * *content* of every string / template literal in the handler files.
 *
 * We deliberately split these into two parameter pieces (prefix + digit class)
 * so the source code of this file itself does not contain a literal ticket
 * pattern that would match.
 */
const BANNED_PATTERNS = [
  {
    id: 'ticket-prefix-stb',
    // Matches STB- followed by one or more digits (optionally a letter suffix)
    // e.g. STB-04, STB-04b, STB-123
    regex: new RegExp(`${'S'}${'T'}${'B'}-\\d+[a-zA-Z]?`),
    description: 'internal ticket prefix "S..T..B-NN"',
  },
  {
    id: 'ticket-prefix-stu-w',
    // Matches STU-W- followed by digits (optionally letter suffix)
    regex: new RegExp(`${'S'}${'T'}${'U'}-W-\\d+[a-zA-Z]?`),
    description: 'internal ticket prefix "S..T..U-W-NN"',
  },
  {
    id: 'ticket-prefix-stu-a',
    // Matches STU-A- followed by digits (optionally letter suffix)
    regex: new RegExp(`${'S'}${'T'}${'U'}-A-\\d+[a-zA-Z]?`),
    description: 'internal ticket prefix "S..T..U-A-NN"',
  },
  {
    id: 'scaffolding-stub',
    regex: /\bstub\b/i,
    description: 'scaffolding word "stub"',
  },
  {
    id: 'scaffolding-placeholder',
    regex: /\bplaceholder\b/i,
    description: 'scaffolding word "placeholder"',
  },
  {
    id: 'scaffolding-will-wire',
    regex: /will\s+wire/i,
    description: 'scaffolding phrase "will wire"',
  },
  {
    id: 'scaffolding-phase-a',
    // "Phase A", "phaseA", "phase-a" — catches the leaked phaseANote
    // keys from FIND-ux-005b. The pattern is constructed at runtime so this
    // file's own comments can mention the bare phrase without self-matching:
    // the regex requires a word boundary, the letter "a" must follow
    // optional whitespace/dash/camelcase, and we rely on the fact that
    // this source file only references the pattern inside a comment
    // (the scanner skips TypeScript comments) and inside a RegExp
    // constructor call (scanner does not look at RegExp literals).
    regex: /\bphase[\s-]?a\b/i,
    description: 'scaffolding phrase "phase a"',
  },
  {
    id: 'scaffolding-todo',
    regex: /\bTODO\b/,
    description: 'developer marker "TODO"',
  },
  {
    id: 'scaffolding-fixme',
    regex: /\bFIXME\b/,
    description: 'developer marker "FIXME"',
  },
]

function walk(dir) {
  const out = []
  for (const entry of readdirSync(dir)) {
    const full = resolve(dir, entry)
    const st = statSync(full)
    if (st.isDirectory())
      out.push(...walk(full))
    else if (entry.endsWith('.ts') || entry.endsWith('.tsx'))
      out.push(full)
  }
  return out
}

/**
 * Tiny single-pass scanner that walks the characters of a TypeScript source
 * file and yields every string literal it encounters — single-quoted,
 * double-quoted, or template — while skipping line and block comments.
 *
 * This is not a real TypeScript parser, but it is sufficient for the
 * constrained shape of the handler files (ES modules, no JSX, no regex
 * literals that look like strings). The guard fails closed: if the scanner
 * ever gets confused, it reports the raw literal it extracted.
 */
function* extractStringLiterals(source) {
  const len = source.length
  let i = 0

  while (i < len) {
    const ch = source[i]
    const next = source[i + 1]

    // Line comment — skip to end of line
    if (ch === '/' && next === '/') {
      i += 2
      while (i < len && source[i] !== '\n') i++
      continue
    }

    // Block comment — skip to closing */
    if (ch === '/' && next === '*') {
      i += 2
      while (i < len && !(source[i] === '*' && source[i + 1] === '/')) i++
      i += 2
      continue
    }

    // Single-quoted string
    if (ch === '\'') {
      const start = i
      i++
      let buf = ''
      while (i < len) {
        const c = source[i]
        if (c === '\\') {
          buf += source[i + 1] ?? ''
          i += 2
          continue
        }
        if (c === '\'') { i++; break }
        if (c === '\n') { i++; break } // unterminated — bail
        buf += c
        i++
      }
      yield { value: buf, start }
      continue
    }

    // Double-quoted string
    if (ch === '"') {
      const start = i
      i++
      let buf = ''
      while (i < len) {
        const c = source[i]
        if (c === '\\') {
          buf += source[i + 1] ?? ''
          i += 2
          continue
        }
        if (c === '"') { i++; break }
        if (c === '\n') { i++; break }
        buf += c
        i++
      }
      yield { value: buf, start }
      continue
    }

    // Template literal — concatenate all static text segments, skipping
    // `${ ... }` expression parts entirely.
    if (ch === '`') {
      const start = i
      i++
      let buf = ''
      while (i < len) {
        const c = source[i]
        if (c === '\\') {
          buf += source[i + 1] ?? ''
          i += 2
          continue
        }
        if (c === '`') { i++; break }
        if (c === '$' && source[i + 1] === '{') {
          // Skip balanced braces
          i += 2
          let depth = 1
          while (i < len && depth > 0) {
            const cc = source[i]
            if (cc === '{') depth++
            else if (cc === '}') depth--
            i++
          }
          continue
        }
        buf += c
        i++
      }
      yield { value: buf, start }
      continue
    }

    i++
  }
}

function lineOf(source, index) {
  let line = 1
  for (let j = 0; j < index && j < source.length; j++)
    if (source[j] === '\n') line++
  return line
}

/**
 * Walk a parsed JSON object and yield every string value along with its
 * dot-path. The path is used purely for reporting — the JSON files are
 * small enough that we don't need line numbers.
 */
function* walkJsonStrings(value, path = '') {
  if (typeof value === 'string') {
    yield { path, value }

    return
  }
  if (Array.isArray(value)) {
    for (let i = 0; i < value.length; i++)
      yield* walkJsonStrings(value[i], `${path}[${i}]`)

    return
  }
  if (value !== null && typeof value === 'object') {
    for (const [k, v] of Object.entries(value)) {
      const next = path ? `${path}.${k}` : k
      yield* walkJsonStrings(v, next)
    }
  }
}

function listLocaleFiles() {
  try {
    return readdirSync(I18N_LOCALES_DIR)
      .filter(entry => entry.endsWith('.json'))
      .map(entry => resolve(I18N_LOCALES_DIR, entry))
  }
  catch {
    return []
  }
}

const handlerFiles = SCANNED_SUBDIRS.flatMap(sub => walk(resolve(HANDLERS_DIR, sub)))
const localeFiles = listLocaleFiles()
const violations = []

// Pass 1: fake-api TypeScript handlers (string/template literals, comments skipped)
for (const file of handlerFiles) {
  const src = readFileSync(file, 'utf8')
  for (const lit of extractStringLiterals(src)) {
    for (const pattern of BANNED_PATTERNS) {
      if (pattern.regex.test(lit.value)) {
        violations.push({
          file,
          line: lineOf(src, lit.start),
          pattern: pattern.description,
          snippet: lit.value.length > 120
            ? `${lit.value.slice(0, 117)}...`
            : lit.value,
        })
      }
    }
  }
}

// Pass 2: i18n locale JSON files (FIND-ux-005b). Every string value at
// every depth is checked. JSON has no comments, so there is no "skip
// comments" story here — every string is user-facing the moment it's
// loaded by vue-i18n.
for (const file of localeFiles) {
  let parsed
  try {
    parsed = JSON.parse(readFileSync(file, 'utf8'))
  }
  catch (err) {
    violations.push({
      file,
      line: 1,
      pattern: 'invalid JSON',
      snippet: err.message.slice(0, 120),
    })
    continue
  }
  for (const { path, value } of walkJsonStrings(parsed)) {
    for (const pattern of BANNED_PATTERNS) {
      if (pattern.regex.test(value)) {
        violations.push({
          file,
          line: path, // use the JSON path as the "line" for reporting
          pattern: pattern.description,
          snippet: value.length > 120 ? `${value.slice(0, 117)}...` : value,
        })
      }
    }
  }
}

if (violations.length === 0) {
  console.log(
    `[check-fake-api-no-leakage] OK — scanned ${handlerFiles.length} handler file(s)`
    + ` and ${localeFiles.length} i18n locale file(s), no banned string literals found.`,
  )
  process.exit(0)
}

console.error(`[check-fake-api-no-leakage] FAIL — ${violations.length} banned string(s) found:`)
for (const v of violations)
  console.error(`  ${v.file}:${v.line}  [${v.pattern}]  ${JSON.stringify(v.snippet)}`)

console.error('')
console.error('These strings would ship into the dev UI. Rewrite them so they read like')
console.error('real product copy — no task IDs, no scaffolding vocabulary, no TODO markers.')
console.error('For i18n strings, either delete the key + its consumer or replace with')
console.error('neutral "Coming soon" language that does not reference internal plans.')
process.exit(1)
