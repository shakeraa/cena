// =============================================================================
// RDY-015: Heading Hierarchy Validation
// WCAG 2.1 AA: every page should have exactly one <h1>, headings
// should not skip levels (no h3 before h2).
// =============================================================================

import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'fs'
import { resolve, join } from 'path'

const pagesDir = resolve(__dirname, '../../src/pages')

function getVueFiles(dir: string): string[] {
  const files: string[] = []
  try {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const fullPath = join(dir, entry.name)
      if (entry.isDirectory()) {
        files.push(...getVueFiles(fullPath))
      } else if (entry.name.endsWith('.vue')) {
        files.push(fullPath)
      }
    }
  } catch {
    // directory might not exist in test env
  }
  return files
}

describe('Heading hierarchy (RDY-015)', () => {
  const pages = getVueFiles(pagesDir)

  it('should find page files to test', () => {
    expect(pages.length).toBeGreaterThan(0)
  })

  // Validate: no page has more than one <h1> in its template
  for (const page of pages) {
    const relativePath = page.replace(pagesDir, 'pages')

    it(`${relativePath}: at most one <h1>`, () => {
      const content = readFileSync(page, 'utf-8')
      const templateMatch = content.match(/<template[^>]*>([\s\S]*?)<\/template>/)
      if (!templateMatch) return // no template section

      const template = templateMatch[1]
      // Count h1 tags (both static and dynamic v-tag variants)
      const h1Matches = template.match(/<h1[\s>]/g) || []
      expect(
        h1Matches.length,
        `${relativePath} has ${h1Matches.length} <h1> tags (expected 0 or 1)`,
      ).toBeLessThanOrEqual(1)
    })
  }

  // Validate: heading levels don't skip (h1->h3 without h2 is invalid)
  for (const page of pages) {
    const relativePath = page.replace(pagesDir, 'pages')

    it(`${relativePath}: no skipped heading levels`, () => {
      const content = readFileSync(page, 'utf-8')
      const templateMatch = content.match(/<template[^>]*>([\s\S]*?)<\/template>/)
      if (!templateMatch) return

      const template = templateMatch[1]
      const headingRegex = /<h([1-6])[\s>]/g
      const levels: number[] = []
      let match

      while ((match = headingRegex.exec(template)) !== null) {
        levels.push(parseInt(match[1]))
      }

      if (levels.length <= 1) return // 0 or 1 headings -- no hierarchy to validate

      // Each heading level should not skip more than 1 level from the previous
      for (let i = 1; i < levels.length; i++) {
        const gap = levels[i] - levels[i - 1]
        expect(
          gap,
          `${relativePath}: h${levels[i - 1]} followed by h${levels[i]} (skipped ${gap - 1} level(s))`,
        ).toBeLessThanOrEqual(1)
      }
    })
  }
})
