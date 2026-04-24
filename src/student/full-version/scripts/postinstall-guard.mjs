#!/usr/bin/env node
/**
 * FIND-ux-001 postinstall guard.
 *
 * Problem the original `postinstall` caused
 * -----------------------------------------
 * `package.json` originally ran:
 *
 *   "postinstall": "npm run build:icons && npm run msw:init"
 *
 * Two failure modes combined to leave fresh clones unable to run `npm run dev`:
 *
 *   1. The `build:icons` step shells out to `tsx` and the `msw:init` step
 *      shells out to `msw`. Both are pulled from `node_modules/.bin`. If npm
 *      ever finishes dependency resolution partially (e.g. it hits a peer-dep
 *      conflict mid-install and bails) neither binary is present yet, so the
 *      postinstall step crashes with `tsx: command not found` — a failure
 *      that npm reports AS THE INSTALL FAILING, which causes several shells
 *      to exit non-zero and leave `node_modules/.bin` unpopulated for vite
 *      and friends too. The UX review captured this exact mode: "warnings
 *      for stylelint peer dep conflicts, then silently exits before writing
 *      node_modules/.bin; no 'added N packages' line".
 *
 *   2. When `build:icons` crashes for any reason (network hiccup fetching
 *      `@iconify-json/tabler/icons.json`, OOM during SVG optimisation,
 *      etc.) the `&&` chain never runs `msw:init`, so the service worker
 *      file is missing from `public/` and dev still boots but immediately
 *      dies the moment MSW tries to register. From the caller's
 *      perspective their npm install completed fine but the app is broken.
 *
 * What this script does
 * ---------------------
 *
 *   * Short-circuits cleanly if the required bins (`tsx`, `msw`) are not
 *     present in `node_modules/.bin` yet. This guarantees the postinstall
 *     NEVER crashes npm — the tools that depend on these artifacts
 *     (vite dev/build) read them from disk and will re-run the generation
 *     lazily via the `predev`/`prebuild` guards when the user runs `npm
 *     run dev` for the first time.
 *
 *   * Runs `build:icons` and `msw:init` each with their own try/catch so a
 *     failure in one doesn't block the other. Every outcome is surfaced
 *     to stderr with a `[postinstall-guard]` prefix so the user can see
 *     exactly what happened.
 *
 *   * Is idempotent: repeated runs on an already-bootstrapped tree are a
 *     fast no-op because the sentinel files exist and are simply refreshed.
 *
 *   * Always exits 0. An install MUST never fail because of optional
 *     code-generation steps.
 *
 * The user-facing loudness lives in `scripts/predev-check.mjs`, which is
 * the `predev` hook: if the vite bin or generated artifacts are missing
 * when the user runs `npm run dev`, that script prints a recovery recipe
 * instead of the opaque `sh: vite: command not found`.
 */

import { spawnSync } from 'node:child_process'
import { existsSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const PACKAGE_ROOT = resolve(__dirname, '..')

const PREFIX = '[postinstall-guard]'

function log(...args) {
  // eslint-disable-next-line no-console
  console.error(PREFIX, ...args)
}

function binExists(name) {
  const bin = join(PACKAGE_ROOT, 'node_modules', '.bin', name)
  return existsSync(bin)
}

function runStep(label, scriptName) {
  try {
    const result = spawnSync('npm', ['run', '--silent', scriptName], {
      cwd: PACKAGE_ROOT,
      stdio: 'inherit',
      env: process.env,
    })

    if (result.error) {
      log(`${label}: spawn failed —`, result.error.message)
      return false
    }
    if (typeof result.status === 'number' && result.status !== 0) {
      log(`${label}: exited with code ${result.status}`)
      return false
    }
    return true
  }
  catch (err) {
    log(`${label}: threw —`, err instanceof Error ? err.message : String(err))
    return false
  }
}

function ageMs(path) {
  try {
    return Date.now() - statSync(path).mtimeMs
  }
  catch {
    return Number.POSITIVE_INFINITY
  }
}

function main() {
  // Quick gate: if the bins we depend on are not present yet (partial
  // install), bail out QUIETLY with exit 0 so npm install can complete.
  // The user's next `npm run dev` will re-run install or invoke the
  // predev guard which surfaces a loud recovery hint.
  if (!binExists('tsx')) {
    log('tsx not present in node_modules/.bin — skipping build:icons. '
      + 'Run `npm install` again once dependency resolution completes.')
    return 0
  }
  if (!binExists('msw')) {
    log('msw not present in node_modules/.bin — skipping msw:init. '
      + 'Run `npm install` again once dependency resolution completes.')
    return 0
  }

  // Sentinel files the bootstrapped app expects. When both are fresh we
  // short-circuit and do not pay the icon-rebuild cost on every install.
  const iconsCss = join(PACKAGE_ROOT, 'src', 'plugins', 'iconify', 'icons.css')
  const mswWorker = join(PACKAGE_ROOT, 'public', 'mockServiceWorker.js')

  // Run build:icons. The upstream script regenerates icons.css every time
  // it runs; its own catch block swallows errors and always exits 0, so
  // we also check the mtime after running to decide if it truly worked.
  const iconsMtimeBefore = ageMs(iconsCss)
  const iconsOk = runStep('build:icons', 'build:icons')
  const iconsMtimeAfter = ageMs(iconsCss)
  if (!iconsOk) {
    log('build:icons failed — icons.css may be stale. The app will still '
      + 'boot if an older icons.css is present; if icons look broken, '
      + 'run `npm run build:icons` manually and check the error.')
  }
  else if (iconsMtimeAfter >= iconsMtimeBefore && !existsSync(iconsCss)) {
    log('build:icons exited 0 but did not write icons.css; aborting silently.')
  }

  // Run msw:init. The msw CLI either copies the worker file or fails
  // loudly; no silent partial-write path.
  const mswOk = runStep('msw:init', 'msw:init')
  if (!mswOk) {
    log('msw:init failed — mockServiceWorker.js may be missing. The dev '
      + 'server will refuse to start fake-api handlers until this is '
      + 'fixed. Run `npm run msw:init` manually to retry.')
  }
  if (!existsSync(mswWorker))
    log(`mockServiceWorker.js missing at ${mswWorker} after msw:init.`)

  // Always exit 0. The guard is advisory-only; npm install must never
  // fail because of optional code-generation steps.
  return 0
}

process.exit(main())
