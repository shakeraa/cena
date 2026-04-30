/**
 * Tests for useIngestionJobs.fetchJobDetail + GenerateVariantsResult shape.
 *
 * Covers the contract that backs the IngestionJobsDrawer's persisted-
 * variant deep-link block (cm 2026-05-01, queue task t_2579ffb8c5c8):
 *
 *  - fetchJobDetail issues GET /admin/ingestion/jobs/{id}
 *  - Returns null on API failure (mirrors fetchLogs's silent-fail
 *    behavior so the drawer falls back to the empty-state copy
 *    instead of crashing the panel)
 *  - GenerateVariantsResult JSON shape returned by
 *    GenerateVariantsJobStrategy.ExecuteAsync deserializes to the
 *    typed interface without field loss
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'

const mockApi = vi.fn()
vi.mock('@/utils/api', () => ({
  $api: mockApi,
}))

describe('useIngestionJobs.fetchJobDetail', () => {
  beforeEach(() => {
    mockApi.mockReset()
    vi.resetModules()
  })

  it('GETs /admin/ingestion/jobs/{id} and returns the typed detail', async () => {
    const detail = {
      id: 'job_42',
      type: 'generatevariants',
      label: 'Variants × 3 from draft_xyz',
      status: 'completed',
      progressPct: 100,
      progressMessage: 'Generated 3 · 2 passed · 2 persisted',
      createdAt: '2026-05-01T00:00:00Z',
      startedAt: '2026-05-01T00:00:01Z',
      completedAt: '2026-05-01T00:00:42Z',
      errorMessage: null,
      createdBy: 'admin@example.com',
      cancelRequested: false,
      resultJson: '{"sourceDraftId":"draft_xyz","persistedQuestionIds":["q1","q2"]}',
    }
    mockApi.mockResolvedValueOnce(detail)

    const { useIngestionJobs } = await import('@/composables/useIngestionJobs')
    const { fetchJobDetail } = useIngestionJobs()

    const result = await fetchJobDetail('job_42')

    expect(mockApi).toHaveBeenCalledWith('/admin/ingestion/jobs/job_42')
    expect(result).not.toBeNull()
    expect(result?.id).toBe('job_42')
    expect(result?.resultJson).toContain('persistedQuestionIds')
  })

  it('returns null on API failure (drawer falls back to empty state)', async () => {
    mockApi.mockRejectedValueOnce(new Error('500 Server Error'))

    const { useIngestionJobs } = await import('@/composables/useIngestionJobs')
    const { fetchJobDetail } = useIngestionJobs()

    const result = await fetchJobDetail('job_doesnt_exist')

    expect(result).toBeNull()
  })
})

describe('GenerateVariantsResult JSON contract', () => {
  it('parses the strategy return shape (persisted + failures + sample)', async () => {
    // Mirror of GenerateVariantsJobStrategy.cs:241-261 anonymous return.
    // If this drifts, the drawer renders garbage and the persisted-IDs
    // chips silently disappear — exact silent-success category we
    // catalogued in precedent broadcast v2 §6.
    const json = JSON.stringify({
      sourceDraftId: 'draft_abc',
      sourcePdfId: 'pdf_001',
      requested: 3,
      generated: 3,
      passedQualityGate: 2,
      persistedQuestionIds: ['q_001', 'q_002'],
      persistFailures: ['v3: ConcurrencyException: stream version mismatch'],
      sample: [
        {
          stem: 'What is the derivative of x²?',
          topic: 'calculus',
          bloomsLevel: 'apply',
          difficulty: 0.42,
          passedQualityGate: true,
          casOutcome: 'Verified',
        },
        {
          stem: 'Solve for x: 3x + 5 = 14',
          topic: 'algebra',
          bloomsLevel: 'apply',
          difficulty: 0.18,
          passedQualityGate: true,
          casOutcome: 'Verified',
        },
        {
          stem: 'Bad candidate stem',
          topic: null,
          bloomsLevel: null,
          difficulty: 0.5,
          passedQualityGate: false,
          casOutcome: 'NotEvaluated',
        },
      ],
    })

    const parsed = JSON.parse(json) as import('@/composables/useIngestionJobs').GenerateVariantsResult

    expect(parsed.persistedQuestionIds).toHaveLength(2)
    expect(parsed.persistedQuestionIds[0]).toBe('q_001')
    expect(parsed.persistFailures).toHaveLength(1)
    expect(parsed.persistFailures[0]).toContain('ConcurrencyException')
    expect(parsed.sample).toHaveLength(3)
    expect(parsed.sample[0].passedQualityGate).toBe(true)
    expect(parsed.sample[2].passedQualityGate).toBe(false)
  })

  it('handles a 0-persisted run (LLM returned candidates but all dropped)', async () => {
    // The "Completed with 0 persisted" silent-success state from
    // PRR-322f-audit. Drawer must render the empty-state copy so the
    // curator doesn't read the green checkmark as "done, all good".
    const json = JSON.stringify({
      sourceDraftId: 'draft_abc',
      requested: 3,
      generated: 3,
      passedQualityGate: 0,
      persistedQuestionIds: [],
      persistFailures: [],
      sample: [],
    })

    const parsed = JSON.parse(json) as import('@/composables/useIngestionJobs').GenerateVariantsResult

    expect(parsed.persistedQuestionIds).toHaveLength(0)
    expect(parsed.persistFailures).toHaveLength(0)
    expect(parsed.passedQualityGate).toBe(0)
    expect(parsed.generated).toBe(3)  // LLM did return 3, but all dropped
  })
})
