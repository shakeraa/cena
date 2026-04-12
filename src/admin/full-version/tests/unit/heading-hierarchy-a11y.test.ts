/**
 * FIND-ux-026: Regression test for heading hierarchy on auth pages.
 *
 * WCAG 2.2 AA requires sequential heading levels — no skipping from H1 to H4.
 * This test reads each auth page's .vue template and ensures:
 *   1. Exactly one <h1> exists per page (the content heading).
 *   2. No heading level is skipped (e.g. h1 followed by h4).
 *   3. The brand logo text is NOT wrapped in a heading element.
 *
 * Also validates the admin dashboard page heading hierarchy.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const pagesDir = resolve(__dirname, '../../src/pages')

/** Extract all heading tags from a Vue template string (only the <template> block). */
function extractHeadings(content: string): Array<{ level: number; tag: string }> {
  // Isolate the <template> block to avoid matching strings in <script>
  const templateMatch = content.match(/<template[\s>][\s\S]*<\/template>/)
  const template = templateMatch ? templateMatch[0] : content

  const headingRegex = /<h([1-6])[\s>]/g
  const headings: Array<{ level: number; tag: string }> = []
  let match: RegExpExecArray | null

  while ((match = headingRegex.exec(template)) !== null) {
    headings.push({ level: Number(match[1]), tag: `h${match[1]}` })
  }

  return headings
}

/** Check that heading levels never skip (e.g. h1 -> h3 without h2). */
function assertSequentialHeadings(headings: Array<{ level: number }>, fileName: string) {
  for (let i = 1; i < headings.length; i++) {
    const prev = headings[i - 1].level
    const curr = headings[i].level

    // Going deeper: next heading must be at most 1 level deeper
    if (curr > prev) {
      expect(
        curr - prev,
        `${fileName}: heading level skips from h${prev} to h${curr} (position ${i})`,
      ).toBeLessThanOrEqual(1)
    }
    // Going shallower or same level is always fine
  }
}

describe('FIND-ux-026: Admin auth pages heading hierarchy', () => {
  const authPages = ['login.vue', 'register.vue', 'forgot-password.vue']

  for (const page of authPages) {
    describe(page, () => {
      const content = readFileSync(resolve(pagesDir, page), 'utf-8')
      const templateMatch = content.match(/<template[\s>][\s\S]*<\/template>/)
      const template = templateMatch ? templateMatch[0] : ''
      const headings = extractHeadings(content)

      it('has exactly one h1 element', () => {
        const h1Count = headings.filter(h => h.level === 1).length

        expect(h1Count, `${page} should have exactly 1 h1, found ${h1Count}`).toBe(1)
      })

      it('does not skip heading levels', () => {
        assertSequentialHeadings(headings, page)
      })

      it('brand logo text is NOT in a heading element', () => {
        // The brand logo wrapper (auth-logo class) should not contain any <h*> tag
        const logoMatch = template.match(/class="auth-logo[^"]*"[\s\S]*?<\/div>/)

        if (logoMatch) {
          const logoSection = logoMatch[0]

          expect(
            logoSection,
            `${page}: brand logo should not contain an <h*> heading tag`,
          ).not.toMatch(/<h[1-6][\s>]/)
        }
      })
    })
  }
})

describe('FIND-ux-026: Admin dashboard heading hierarchy', () => {
  const content = readFileSync(resolve(pagesDir, 'dashboards/admin.vue'), 'utf-8')
  const headings = extractHeadings(content)

  it('has exactly one h1 element for the page title', () => {
    const h1Count = headings.filter(h => h.level === 1).length

    expect(h1Count, 'Dashboard should have exactly 1 h1').toBe(1)
  })

  it('does not use heading elements for data display values', () => {
    const templateMatch = content.match(/<template[\s>][\s\S]*<\/template>/)
    const template = templateMatch ? templateMatch[0] : ''

    // Widget data values ({{ data.value }}) should not be in heading tags
    const dataValueInHeading = template.match(/<h[1-6][^>]*>\s*\{\{\s*data\.value\s*\}\}\s*<\/h[1-6]>/)

    expect(
      dataValueInHeading,
      'Data display values should not be in heading elements',
    ).toBeNull()
  })

  it('does not skip heading levels', () => {
    assertSequentialHeadings(headings, 'dashboards/admin.vue')
  })
})
