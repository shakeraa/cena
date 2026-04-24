// =============================================================================
// RDY-030: Touch target size validation (WCAG 2.5.5 Target Size).
//
// Flags clickables that explicitly shrink below the 44x44 CSS px threshold.
// Vuetify defaults meet the threshold; this rule catches manual overrides.
// =============================================================================

import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'fs'
import { resolve, join } from 'path'

const srcDir = resolve(__dirname, '../../src')

function getVueFiles(dir: string): string[] {
  const files: string[] = []
  try {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name)
      if (entry.isDirectory()) files.push(...getVueFiles(full))
      else if (entry.name.endsWith('.vue')) files.push(full)
    }
  } catch { /* ignore */ }
  return files
}

/** v-btn declared with size="small|x-small" AND density="compact" shrinks below 44x44. */
const SMALL_VUETIFY_BTN =
  /<v-btn\b[^>]*\bsize=["'](x-small|small)["'][^>]*\bdensity=["']compact["']/g

/** Dimensions under 32px on clickable selectors. */
const TINY_DIM = /(?:min-)?(?:width|height)\s*:\s*(\d+)\s*px/g

describe('Touch target size (RDY-030 — WCAG 2.5.5)', () => {
  const files = getVueFiles(srcDir)

  it('finds .vue files to lint', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  for (const file of files) {
    const rel = file.replace(srcDir, 'src')

    it(`${rel}: no small+compact Vuetify buttons`, () => {
      const content = readFileSync(file, 'utf-8')
      const hits = content.match(SMALL_VUETIFY_BTN) ?? []
      expect(hits.length, `${rel}: found small+compact v-btn (below 44x44)`).toBe(0)
    })
  }

  it('no clickable declares < 32px dimensions in component styles', () => {
    const offending: string[] = []
    for (const file of files) {
      const content = readFileSync(file, 'utf-8')
      const styleBlocks = content.match(/<style[^>]*>([\s\S]*?)<\/style>/g)
      if (!styleBlocks) continue

      for (const block of styleBlocks) {
        const btnBlocks = block.match(/(\.v-btn|button|\[role=['"]button['"]\])[^{}]*\{[^}]+\}/g) ?? []
        for (const b of btnBlocks) {
          TINY_DIM.lastIndex = 0
          let m: RegExpExecArray | null
          while ((m = TINY_DIM.exec(b)) !== null) {
            if (parseInt(m[1], 10) < 32) {
              offending.push(file.replace(srcDir, 'src'))
              break
            }
          }
        }
      }
    }
    expect(offending.length, `clickable < 32px:\n  ${offending.join('\n  ')}`).toBe(0)
  })
})
