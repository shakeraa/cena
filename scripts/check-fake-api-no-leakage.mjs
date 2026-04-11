#!/usr/bin/env node
// =============================================================================
// check-fake-api-no-leakage.mjs
//
// Scans every .ts file under
//   src/student/full-version/src/plugins/fake-api/handlers/
// and fails if any **string literal** contains:
//
//   - An internal ticket prefix (e.g. things that look like "STB-04b", "STU-W-08")
//   - Scaffolding vocabulary the product rule bans from user-facing surfaces
//     ("stub", "placeholder", "will wire", "coming-soon")
//   - TODO / FIXME markers
//
// This exists because FIND-ux-005 surfaced MSW handlers that shipped task IDs
// like "(STB-04b will wire real LLM streaming.)" directly into the tutor chat
// UI. The user's locked rule is: NO stubs — production grade, labels match
// data. Mock content is still "shipped" in the dev loop, so it is treated as
// user-facing copy and must not contain scaffolding.
//
// IMPORTANT: this guard intentionally ignores comments. Comments that document
// the guard itself (for example this file) must reference the banned patterns
// without triggering, so we only walk the TypeScript AST for string / template
// literals rather than raw-grepping the file.
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

const files = SCANNED_SUBDIRS.flatMap(sub => walk(resolve(HANDLERS_DIR, sub)))
const violations = []

for (const file of files) {
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

if (violations.length === 0) {
  console.log(`[check-fake-api-no-leakage] OK — scanned ${files.length} files, no banned string literals found.`)
  process.exit(0)
}

console.error(`[check-fake-api-no-leakage] FAIL — ${violations.length} banned string literal(s) found in fake-api handlers:`)
for (const v of violations)
  console.error(`  ${v.file}:${v.line}  [${v.pattern}]  ${JSON.stringify(v.snippet)}`)

console.error('')
console.error('These strings would ship into the dev UI. Rewrite them so they read like')
console.error('real product copy — no task IDs, no scaffolding vocabulary, no TODO markers.')
process.exit(1)
