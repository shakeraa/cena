/**
 * Tests for the Focus Analytics Student page.
 *
 * Validates:
 *  - Page uses correct CASL meta for Focus read
 *  - Reads student ID from route params
 *  - Has timeline range toggle
 *  - Imports StagnationInsightsPanel
 *  - Uses $api for data fetching
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const focusStudentPage = resolve(__dirname, '../../src/pages/apps/focus/student/[id].vue')
const content = readFileSync(focusStudentPage, 'utf-8')

describe('Focus Student page structure', () => {
  it('declares CASL meta for read:Focus', () => {
    expect(content).toContain("action: 'read'")
    expect(content).toContain("subject: 'Focus'")
  })

  it('reads student ID from route params', () => {
    expect(content).toContain('route.params.id')
  })

  it('has timeline range toggle (7d and 30d)', () => {
    expect(content).toContain("'7d'")
    expect(content).toContain("'30d'")
    expect(content).toContain('timelineRange')
  })

  it('imports StagnationInsightsPanel', () => {
    expect(content).toContain('StagnationInsightsPanel')
  })

  it('uses $api for data fetching', () => {
    expect(content).toContain('$api')
  })

  it('defines StudentFocusProfile interface', () => {
    expect(content).toContain('StudentFocusProfile')
    expect(content).toContain('avgFocusScore')
    expect(content).toContain('classAvg')
    expect(content).toContain('gradeAvg')
  })

  it('has timeline chart data structure', () => {
    expect(content).toContain('timelineData')
    expect(content).toContain('TimelinePoint')
  })
})
