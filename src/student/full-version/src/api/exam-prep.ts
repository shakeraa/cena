/**
 * Cena Platform — Mock-exam runner API client.
 *
 * Delegates to $api so retries / token refresh / correlation IDs are
 * inherited. Errors throw ofetch FetchError; callers discriminate on
 * statusCode for 400 (invalid input) / 403 (foreign run) / 404 (no run).
 */

import { $api } from '@/api/$api'
import type {
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

export function submitMockExamRun(runId: string): Promise<MockExamResultResponse> {
  return $api<MockExamResultResponse>(`${ROOT}/${runId}/submit`, { method: 'POST' })
}

export function getMockExamRunResult(runId: string): Promise<MockExamResultResponse> {
  return $api<MockExamResultResponse>(`${ROOT}/${runId}/result`)
}
