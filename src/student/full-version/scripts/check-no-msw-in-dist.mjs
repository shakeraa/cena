#!/usr/bin/env node
/**
 * FIND-arch-017: CI guard — verify the production build contains no MSW artifacts.
 *
 * Run after `npm run build` to assert:
 *   1. dist/mockServiceWorker.js does not exist
 *   2. No JS chunk in dist/assets/ references `setupWorker` (the MSW entry point)
 *   3. No JS chunk imports from `msw/browser`
 *
 * Exit 0 on pass, exit 1 on fail with a human-readable error message.
 */

import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)
const DIST = resolve(__dirname, '..', 'dist')

const PREFIX = '[check-no-msw-in-dist]'
const errors = []

// 1. mockServiceWorker.js must not exist in dist/
const mswWorker = join(DIST, 'mockServiceWorker.js')
if (existsSync(mswWorker)) {
  errors.push(
    `${PREFIX} FAIL: dist/mockServiceWorker.js exists (${readFileSync(mswWorker).length} bytes). `
    + 'The stripMswInProduction Vite plugin should have removed it.',
  )
}

// 2+3. Scan all JS assets for MSW runtime references
const assetsDir = join(DIST, 'assets')
if (existsSync(assetsDir)) {
  const jsFiles = readdirSync(assetsDir).filter(f => f.endsWith('.js'))

  for (const file of jsFiles) {
    const content = readFileSync(join(assetsDir, file), 'utf-8')

    if (content.includes('setupWorker')) {
      errors.push(
        `${PREFIX} FAIL: dist/assets/${file} contains 'setupWorker'. `
        + 'MSW runtime code leaked into the production bundle.',
      )
    }

    // Check for msw/browser import (could appear as a dynamic import chunk)
    if (content.includes('msw/browser')) {
      errors.push(
        `${PREFIX} FAIL: dist/assets/${file} contains 'msw/browser'. `
        + 'MSW module reference leaked into the production bundle.',
      )
    }
  }
}
else {
  errors.push(`${PREFIX} FAIL: dist/assets/ directory not found. Did the build run?`)
}

if (errors.length > 0) {
  // eslint-disable-next-line no-console
  console.error(errors.join('\n'))
  process.exit(1)
}

// eslint-disable-next-line no-console
console.log(`${PREFIX} PASS: No MSW artifacts found in dist/`)
process.exit(0)
