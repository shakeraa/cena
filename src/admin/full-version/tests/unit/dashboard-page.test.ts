/**
 * Tests for the Admin Dashboard page.
 *
 * Validates:
 *  - Page uses correct CASL meta for Analytics read
 *  - Fetches overview data from /admin/dashboard/overview
 *  - Widget data shows correct labels
 *  - SAI features (corpus, experiments) are fetched
 *  - Dashboard child components are imported
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const dashboardPage = resolve(__dirname, '../../src/pages/dashboards/admin.vue')
const content = readFileSync(dashboardPage, 'utf-8')

describe('Admin Dashboard page structure', () => {
  it('declares CASL meta for read:Analytics', () => {
    expect(content).toContain("action: 'read'")
    expect(content).toContain("subject: 'Analytics'")
  })

  it('fetches overview from /admin/dashboard/overview', () => {
    expect(content).toContain("'/admin/dashboard/overview'")
  })

  it('has Active Users widget', () => {
    expect(content).toContain("title: 'Active Users'")
  })

  it('has Total Students widget', () => {
    expect(content).toContain("title: 'Total Students'")
  })

  it('has Content Items widget', () => {
    expect(content).toContain("title: 'Content Items'")
  })

  it('has Avg Focus Score widget', () => {
    expect(content).toContain("title: 'Avg Focus Score'")
  })

  it('fetches corpus stats for SAI features', () => {
    expect(content).toContain("'/admin/embeddings/corpus-stats'")
  })

  it('fetches experiments for SAI features', () => {
    expect(content).toContain("'/admin/experiments'")
  })

  it('imports UserActivityChart component', () => {
    expect(content).toContain('UserActivityChart')
  })

  it('imports SystemAlerts component', () => {
    expect(content).toContain('SystemAlerts')
  })

  it('imports RecentActivityTimeline component', () => {
    expect(content).toContain('RecentActivityTimeline')
  })

  it('imports TutoringBudgetCard component', () => {
    expect(content).toContain('TutoringBudgetCard')
  })

  it('displays Platform Overview heading', () => {
    expect(content).toContain('Platform Overview')
  })
})
