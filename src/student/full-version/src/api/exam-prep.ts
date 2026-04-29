/**
 * Cena Platform — Mock-exam runner API client.
 *
 * Delegates to $api so retries / token refresh / correlation IDs are
 * inherited. Errors throw ofetch FetchError; callers discriminate on
 * statusCode for 400 (invalid input) / 403 (foreign run) / 404 (no run).
 */

import { $api } from '@/api/$api'
import type {
  ExamPrepFeatureFlags,
  ExamPrepQuestionPreview,
  MockExamResultResponse,
  MockExamRunStartedResponse,
  MockExamRunStateResponse,
  SelectPartBRequest,
  StartMockExamRunRequest,
  SubmitAnswerRequest,
} from '@/api/types/exam-prep'

const ROOT = '/api/me/exam-prep/runs'

export function startMockExamRun(body: StartMockExamRunRequest): Promise<MockExamRunStartedResponse> {
  return $api<MockExamRunStartedResponse>(`${ROOT}/`, { method: 'POST', body })
}

export function getMockExamRunState(runId: string): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}`)
}

export function selectMockExamPartB(
  runId: string,
  body: SelectPartBRequest,
): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/select-part-b`, { method: 'POST', body })
}

export function submitMockExamAnswer(
  runId: string,
  body: SubmitAnswerRequest,
): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/answer`, { method: 'POST', body })
}

/** Phase 3 #8 — bulk submit. Used by the runner on submit-flush so a
 * 7-Q exam with multi-part subparts collapses to one round-trip. */
export function submitMockExamAnswersBulk(
  runId: string,
  answers: SubmitAnswerRequest[],
): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/answers`, {
    method: 'POST',
    body: { answers },
  })
}

export function submitMockExamRun(runId: string): Promise<MockExamResultResponse> {
  return $api<MockExamResultResponse>(`${ROOT}/${runId}/submit`, { method: 'POST' })
}

export function getMockExamRunResult(runId: string): Promise<MockExamResultResponse> {
  return $api<MockExamResultResponse>(`${ROOT}/${runId}/result`)
}

// Phase 3 #9 — sessionStorage cache for the feature-flags read. The
// flag is global and rarely flips; refetching on every entry-page
// mount is wasteful. 60s TTL keeps admin kill-switch responsiveness
// reasonable (one-minute window after a flip-off).
const FLAGS_CACHE_KEY = 'cena.examPrep.featureFlags'
const FLAGS_TTL_MS = 60_000

interface CachedFlags { value: ExamPrepFeatureFlags; expires: number }

export async function getMockExamFeatureFlags(): Promise<ExamPrepFeatureFlags> {
  try {
    const raw = sessionStorage.getItem(FLAGS_CACHE_KEY)
    if (raw) {
      const cached = JSON.parse(raw) as CachedFlags
      if (cached.expires > Date.now()) return cached.value
    }
  }
  catch { /* SSR / disabled storage */ }

  const fresh = await $api<ExamPrepFeatureFlags>('/api/me/exam-prep/feature-flags')
  try {
    sessionStorage.setItem(FLAGS_CACHE_KEY, JSON.stringify({
      value: fresh,
      expires: Date.now() + FLAGS_TTL_MS,
    }))
  }
  catch { /* ignore */ }
  return fresh
}

export function getMockExamQuestionPreview(
  runId: string,
  questionId: string,
): Promise<ExamPrepQuestionPreview> {
  return $api<ExamPrepQuestionPreview>(`${ROOT}/${runId}/question/${questionId}`)
}
