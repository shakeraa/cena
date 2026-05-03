/**
 * Tests for the Mastery Student page.
 *
 * Validates:
 *  - Template tags are balanced
 *  - Correct route name is used
 *  - Page reads student ID from route params
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const masteryStudentPage = resolve(__dirname, '../../src/pages/apps/mastery/student/[id].vue')
const content = readFileSync(masteryStudentPage, 'utf-8')

function countTag(html: string, tag: string): { open: number; close: number } {
  const selfClosing = new RegExp(`<${tag}\\b[^>]*/\\s*>`, 'gi')
  const cleaned = html.replace(selfClosing, '')
  const openRe = new RegExp(`<${tag}(?:\\s|>)`, 'gi')
  const closeRe = new RegExp(`</${tag}\\s*>`, 'gi')

  return {
    open: (cleaned.match(openRe) || []).length,
    close: (cleaned.match(closeRe) || []).length,
  }
}

describe('Mastery Student page structure', () => {
  it('uses correct route name apps-mastery-student-id', () => {
    expect(content).toContain("useRoute('apps-mastery-student-id')")
  })

  it('all <VCard> tags are balanced', () => {
    const { open, close } = countTag(content, 'VCard')
    expect(close).toBe(open)
  })

  it('all <VRow> tags are balanced', () => {
    const { open, close } = countTag(content, 'VRow')
    expect(close).toBe(open)
  })

  it('all <VCol> tags are balanced', () => {
    const { open, close } = countTag(content, 'VCol')
    expect(close).toBe(open)
  })

  it('all <div> tags are balanced', () => {
    const templateMatch = content.match(/<template>([\s\S]*)<\/template>/)
    if (templateMatch) {
      const { open, close } = countTag(templateMatch[1], 'div')
      expect(close).toBe(open)
    }
  })
})
