/**
 * Cena Platform — Session API client (prr-205/206/207/208 bundle).
 *
 * Thin ofetch-backed helpers over the endpoints that back the session
 * runner's new UX surfaces:
 *
 *   - postHintNext        → POST /api/sessions/{sid}/question/{qid}/hint/next
 *                           (prr-203 server-authoritative ladder)
 *
 *   - postStep            → POST /api/sessions/{sid}/question/{qid}/step
 *                           (CAS step-verify for StepSolverCard)
 *
 *   - getTutorContext     → GET  /api/v1/sessions/{sid}/tutor-context
 *                           (prr-204 Sidekick seed data)
 *
 *   - postTutorTurn       → POST /api/v1/sessions/{sid}/tutor-turn
 *                           (Sidekick streaming — the streaming path is
 *                           consumed directly via fetch() in useSidekick
 *                           for ReadableStream access; this helper is a
 *                           non-streaming fallback for test + 4xx paths)
 *
 *   - postFbdSubmission   → POST /api/sessions/{sid}/question/{qid}/fbd
 *                           (FreeBodyDiagramConstruct CAS verify)
 *
 * All helpers delegate to `$api` so they inherit:
 *   - Firebase ID token attachment
 *   - 401 → refresh-and-retry
 *   - 429/5xx backoff retry
 *   - Correlation-id propagation
 *
 * Errors throw ofetch's FetchError so callers can discriminate on
 * `err.statusCode`.
 */

import { $api } from '@/api/$api'
import type {
  FbdSubmissionRequest,
  FbdSubmissionResponseDto,
  HintLadderResponseDto,
  StepSolverSubmitRequest,
  StepSolverSubmitResponseDto,
  TutorContextResponseDto,
  TutorTurnRequest,
  TutorTurnResponseDto,
} from '@/api/types/common'

// =============================================================================
// prr-203 — Hint ladder advance
// =============================================================================

/**
 * Advance the server-authoritative hint ladder by one rung for the
 * (session, question) pair. The body is empty — the server never takes
 * a rung hint from the client (ADR-0045).
 *
 * 404 = session or question not found. UI should treat as a soft error
 * and stop attempting further rungs.
 */
export async function postHintNext(
  sessionId: string,
  questionId: string,
): Promise<HintLadderResponseDto> {
  return $api<HintLadderResponseDto>(
    `/api/sessions/${encodeURIComponent(sessionId)}/question/${encodeURIComponent(questionId)}/hint/next`,
    { method: 'POST' as any },
  )
}

// =============================================================================
// prr-206 — Step-solver step submit
// =============================================================================

/**
 * Submit a single step of a multi-step problem for CAS verification.
 * The server walks the step graph and returns equivalence +
 * optional AST-diff feedback if the expression is wrong.
 */
export async function postStep(
  sessionId: string,
  questionId: string,
  body: StepSolverSubmitRequest,
): Promise<StepSolverSubmitResponseDto> {
  return $api<StepSolverSubmitResponseDto>(
    `/api/sessions/${encodeURIComponent(sessionId)}/question/${encodeURIComponent(questionId)}/step`,
    {
      method: 'POST' as any,
      body: body as any,
    },
  )
}

// =============================================================================
// prr-204 — Session-scoped tutor context (Sidekick seed)
// =============================================================================

/**
 * Fetch the session-scoped tutor context used to seed the Sidekick
 * drawer. Session-scoped per ADR-0003 — the response is NEVER cached
 * across sessions on the client.
 */
export async function getTutorContext(
  sessionId: string,
): Promise<TutorContextResponseDto> {
  return $api<TutorContextResponseDto>(
    `/api/v1/sessions/${encodeURIComponent(sessionId)}/tutor-context`,
  )
}

/**
 * Non-streaming tutor turn helper (used in tests + fallback path when
 * the browser can't open a streaming connection). The primary path in
 * the UI uses `fetch()` directly from `useSidekick` so it can consume
 * the `ReadableStream` SSE body.
 */
export async function postTutorTurn(
  sessionId: string,
  body: TutorTurnRequest,
): Promise<TutorTurnResponseDto> {
  return $api<TutorTurnResponseDto>(
    `/api/v1/sessions/${encodeURIComponent(sessionId)}/tutor-turn`,
    {
      method: 'POST' as any,
      body: body as any,
    },
  )
}

// =============================================================================
// prr-208 — FBD submission
// =============================================================================

/**
 * Submit a student-constructed free-body diagram for CAS verification.
 * Server returns per-force verdicts + overall equilibrium check.
 */
export async function postFbdSubmission(
  sessionId: string,
  questionId: string,
  body: FbdSubmissionRequest,
): Promise<FbdSubmissionResponseDto> {
  return $api<FbdSubmissionResponseDto>(
    `/api/sessions/${encodeURIComponent(sessionId)}/question/${encodeURIComponent(questionId)}/fbd`,
    {
      method: 'POST' as any,
      body: body as any,
    },
  )
}
