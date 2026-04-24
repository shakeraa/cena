/**
 * Tests for the Question Bank List page.
 *
 * Validates:
 *  - Page uses correct CASL meta for Questions read
 *  - Fetches from /admin/questions endpoint
 *  - Table headers include expected columns
 *  - Filter options for subject, Bloom level, difficulty exist
 *  - sanitizeHtml is imported (XSS prevention)
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const questionListPage = resolve(__dirname, '../../src/pages/apps/questions/list/index.vue')
const content = readFileSync(questionListPage, 'utf-8')

describe('Question Bank List page structure', () => {
  it('declares CASL meta for read:Questions', () => {
    expect(content).toContain("action: 'read'")
    expect(content).toContain("subject: 'Questions'")
  })

  it('fetches questions from /admin/questions', () => {
    expect(content).toContain("'/admin/questions'")
  })

  it('has expected table headers', () => {
    const expectedHeaders = ['Stem', 'Subject', 'Concepts', 'Difficulty', 'Status', 'Quality', 'Usage']
    for (const header of expectedHeaders) {
      expect(content).toContain(`'${header}'`)
    }
  })

  it('has Bloom level filter', () => {
    expect(content).toContain('selectedBloom')
    expect(content).toContain('bloomLevel')
  })

  it('has difficulty filter', () => {
    expect(content).toContain('selectedDifficulty')
  })

  it('has subject filter', () => {
    expect(content).toContain('selectedSubject')
  })

  it('has language filter', () => {
    expect(content).toContain('selectedLanguage')
  })

  it('has status filter', () => {
    expect(content).toContain('selectedStatus')
  })

  it('imports sanitizeHtml for XSS prevention', () => {
    expect(content).toContain('sanitizeHtml')
  })

  it('includes QuestionDetail component', () => {
    expect(content).toContain('QuestionDetail')
  })
})
