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
  MockExamRunSummary,
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

/** PRR-287 — pause + resume the run. */
export function pauseMockExamRun(runId: string): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/pause`, { method: 'POST' })
}
export function resumeMockExamRun(runId: string): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/resume`, { method: 'POST' })
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

export async function getMockExamRunResult(runId: string): Promise<MockExamResultResponse> {
  // PRR-278 — retry on transient 5xx. The result endpoint runs the
  // grader, which calls SymPy via the CAS router; a sidecar blip
  // returns 502/503/504 and the runner page would show "could not
  // load result" with no recovery. 3 attempts at 0/600/1800 ms
  // covers the common transient window (sidecar restart ~2s).
  // ofetch's built-in retry covers $api, but that fires on 5xx in
  // general — we want to be explicit here so the runner specifically
  // surfaces "result temporarily unavailable" rather than a stale
  // error.
  const backoffs = [0, 600, 1800]
  let lastErr: unknown
  for (const wait of backoffs) {
    if (wait > 0) await new Promise(r => setTimeout(r, wait))
    try {
      return await $api<MockExamResultResponse>(`${ROOT}/${runId}/result`)
    }
    catch (err: unknown) {
      const status = (err as { statusCode?: number; status?: number }).statusCode
        ?? (err as { status?: number }).status
      lastErr = err
      // Only retry on transient 5xx; everything else surfaces immediately.
      if (!status || status < 500 || status >= 600) throw err
    }
  }
  throw lastErr
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

/** PRR-294 — recent submitted runs for the trend card. */
export function getMockExamHistory(
  examCode?: string,
  paperCode?: string,
  limit: number = 5,
): Promise<{ runs: MockExamRunSummary[] }> {
  const params = new URLSearchParams()
  if (examCode) params.set('examCode', examCode)
  if (paperCode) params.set('paperCode', paperCode)
  params.set('limit', String(limit))
  return $api<{ runs: MockExamRunSummary[] }>(`${ROOT}/history?${params.toString()}`)
}

export function getMockExamQuestionPreview(
  runId: string,
  questionId: string,
): Promise<ExamPrepQuestionPreview> {
  return $api<ExamPrepQuestionPreview>(`${ROOT}/${runId}/question/${questionId}`)
}

/** Phase-4 #1 — report a tab-switch / visibility-change to the server.
 * Real Ministry exam day proctors care about these. */
export function reportMockExamVisibility(
  runId: string,
  state: 'hidden' | 'visible' | 'blur',
  durationAwayMs: number,
): Promise<MockExamRunStateResponse> {
  return $api<MockExamRunStateResponse>(`${ROOT}/${runId}/visibility`, {
    method: 'POST',
    body: { state, durationAwayMs },
  })
}
