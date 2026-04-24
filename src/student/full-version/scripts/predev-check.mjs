#!/usr/bin/env node
/**
 * FIND-ux-001 predev check.
 *
 * Problem this solves
 * -------------------
 * A fresh clone of the repo that hit a peer-dep conflict mid-install would
 * leave `node_modules/.bin/vite` missing, and the user would then see:
 *
 *   $ npm run dev
 *   > cena-student-web@0.0.1 dev
 *   > vite --port 5175
 *   sh: vite: command not found
 *
 * which is the lowest-signal error message in the ecosystem. This script
 * runs as the `predev` hook, checks every precondition the dev server
 * actually needs, and if ANYTHING is missing it prints a recovery recipe
 * that tells the user exactly which command to run.
 *
 * Checks:
 *   1. `node_modules/.bin/vite` exists (dev server itself)
 *   2. `node_modules/.bin/tsx` exists (build:icons)
 *   3. `node_modules/.bin/msw` exists (msw:init)
 *   4. `src/plugins/iconify/icons.css` exists (icons bundle)
 *   5. `public/mockServiceWorker.js` exists (MSW worker file)
 *
 * Every missing item prints one line with the fix command.
 *
 * Exits 0 if all checks pass (dev server proceeds normally). Exits 1 if
 * any check fails, with a non-zero exit preventing vite from being
 * invoked with a cryptic error. The user sees our message instead.
 */

import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const PACKAGE_ROOT = resolve(__dirname, '..')

function rel(absolute) {
  return absolute.startsWith(PACKAGE_ROOT)
    ? absolute.slice(PACKAGE_ROOT.length + 1)
    : absolute
}

const checks = [
  {
    path: join(PACKAGE_ROOT, 'node_modules', '.bin', 'vite'),
    label: 'vite dev server binary',
    fix: 'rm -rf node_modules package-lock.json && npm install',
  },
  {
    path: join(PACKAGE_ROOT, 'node_modules', '.bin', 'tsx'),
    label: 'tsx (runs build:icons)',
    fix: 'rm -rf node_modules package-lock.json && npm install',
  },
  {
    path: join(PACKAGE_ROOT, 'node_modules', '.bin', 'msw'),
    label: 'msw CLI (runs msw:init)',
    fix: 'rm -rf node_modules package-lock.json && npm install',
  },
  {
    path: join(PACKAGE_ROOT, 'src', 'plugins', 'iconify', 'icons.css'),
    label: 'iconify icons.css bundle',
    fix: 'npm run build:icons',
  },
  {
    path: join(PACKAGE_ROOT, 'public', 'mockServiceWorker.js'),
    label: 'MSW worker file in public/',
    fix: 'npm run msw:init',
  },
]

const missing = checks.filter(c => !existsSync(c.path))

if (missing.length === 0)
  process.exit(0)

// eslint-disable-next-line no-console
console.error(`
[predev-check] Cannot start the student dev server — one or more
required artifacts are missing. This usually means \`npm install\` did
not finish cleanly (peer-dep conflict, network hiccup, or the install
was aborted before bins were written).

Missing items:
${missing.map(c => `  - ${c.label}\n      at ${rel(c.path)}\n      fix: ${c.fix}`).join('\n')}

Recovery recipe (run from src/student/full-version/):

  rm -rf node_modules package-lock.json
  npm install
  npm run dev

If that still fails, run \`npm install --ignore-scripts\` to force a
clean dependency tree, then \`npm run build:icons && npm run msw:init\`
to rebuild the local artifacts, then \`npm run dev\`.

See docs/reviews/agent-5-ux-findings.md FIND-ux-001 for the root-cause
analysis.
`)
process.exit(1)
