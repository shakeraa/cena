/**
 * ADR-0058 §2 — PII scrubbing is source-layer and MUST be identical in
 * both SPAs. This test hashes the `scrubEvent` function body in each
 * copy of `sentry.config.ts` and asserts the hashes match. If a dev
 * updates the student copy without updating the admin copy (or vice
 * versa), the privacy contract drifts — this test fails.
 */
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { describe, expect, it } from 'vitest'

const studentPath = path.resolve(__dirname, '../../src/plugins/sentry.config.ts')
const adminPath = path.resolve(__dirname, '../../../../admin/full-version/src/plugins/sentry.config.ts')

function hashScrubEvent(file: string): string {
  const content = fs.readFileSync(file, 'utf8')

  // Match `export function scrubEvent` up to its closing `}` at column 0.
  // Multiline mode with `m` flag so `^\}` matches a bare `}` at line start.
  const match = content.match(/export function scrubEvent[\s\S]*?^\}/m)

  if (!match)
    throw new Error(`scrubEvent not found in ${file}`)

  return crypto.createHash('sha256').update(match[0]).digest('hex')
}

describe('ADR-0058: scrubEvent drift detection', () => {
  it('scrubEvent stays identical across admin + student SPAs', () => {
    const studentHash = hashScrubEvent(studentPath)
    const adminHash = hashScrubEvent(adminPath)

    expect(
      studentHash,
      `scrubEvent drift detected between student (${studentPath}) and admin (${adminPath}). `
      + 'Both SPAs MUST carry identical privacy contracts (ADR-0058 §2). '
      + 'Update both files together.',
    ).toBe(adminHash)
  })

  it('both SPA copies contain the same locked-down privacy markers', () => {
    // Belt-and-suspenders: not just the function hash but also the
    // specific substrings that encode the privacy contract. If a dev
    // renames the function or restructures the logic, the hash still
    // works — these assertions make sure neither copy silently weakens.
    const studentSrc = fs.readFileSync(studentPath, 'utf8')
    const adminSrc = fs.readFileSync(adminPath, 'utf8')

    for (const marker of [
      'defaultPii: false',
      'replaysSessionSampleRate: 0',
      'replaysOnErrorSampleRate: 0',
      'sentry_pii_scrub',
      'redacted:localStorage',
    ]) {
      expect(studentSrc, `student sentry.config.ts missing marker: ${marker}`).toContain(marker)
      expect(adminSrc, `admin sentry.config.ts missing marker: ${marker}`).toContain(marker)
    }
  })
})
