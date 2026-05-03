// =============================================================================
// Cena E2E flow — LLM recorder probe (PII-scrub + stem-grounded assertions)
//
// Playwright-side client for the test-only endpoint registered at
// /api/test/llm-recorder/recordings when `Cena:Testing:LlmRecorderEnabled`
// is true in the admin-api host. Corresponds to
// src/shared/Cena.Infrastructure/Llm/RecordingLlmClient.cs + the admin-api
// LlmRecorderEndpoints.
//
// EPIC-E2E-I-05 / EPIC-E2E-D-05 (PII scrubber) usage pattern:
//
//   const recorder = createLlmRecorder()
//   await recorder.reset()
//   const since = Date.now()
//   // ... drive the UI to a path that calls an LLM ...
//   const calls = await recorder.waitFor({ sinceMs: since, minCount: 1 })
//   assertNoPii(calls[0].userPrompt, { address: '5 King St, Tel Aviv' })
//
// EPIC-E2E-D-07 (stem-grounded hints) usage pattern:
//
//   const calls = await recorder.waitFor({ sinceMs: since, minCount: 1 })
//   expect(calls[0].systemPrompt).toContain(expectedStem)
//   assertNoForbiddenContext(calls[0], {
//     otherStudentIds: ['student-other-1'],
//     otherSessionStems: ['unrelated stem about chemistry'],
//   })
// =============================================================================

import { expect } from '@playwright/test'

const ADMIN_API_BASE_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'

export interface LlmCallRecording {
  id: string
  timestamp: string
  modelId: string
  systemPrompt: string
  userPrompt: string
  responseContent: string
  inputTokens: number
  outputTokens: number
  latencyMs: number
  fromCache: boolean
  errorKind: string | null
  errorMessage: string | null
  tag: string | null
}

export interface WaitForRecordingsOptions {
  /** Unix ms lower bound. Use `Date.now()` captured before the trigger. */
  sinceMs: number
  /** Minimum number of recordings to wait for. Default 1. */
  minCount?: number
  /** Overall wait ceiling. Default 10_000ms. Raise for LLM-slow paths. */
  timeoutMs?: number
  /** Poll cadence. Default 250ms. */
  pollMs?: number
}

export interface LlmRecorder {
  reset(): Promise<void>
  list(sinceMs: number, limit?: number): Promise<LlmCallRecording[]>
  waitFor(opts: WaitForRecordingsOptions): Promise<LlmCallRecording[]>
}

export function createLlmRecorder(baseUrl: string = ADMIN_API_BASE_URL): LlmRecorder {
  const endpoint = `${baseUrl.replace(/\/$/, '')}/api/test/llm-recorder/recordings`
  return {
    async reset() {
      const res = await fetch(endpoint, { method: 'DELETE' })
      if (!res.ok && res.status !== 404)
        throw new Error(`[llm-recorder] DELETE returned ${res.status}`)
    },
    async list(sinceMs, limit = 50) {
      const url = `${endpoint}?sinceMs=${sinceMs}&limit=${limit}`
      const res = await fetch(url)
      if (res.status === 404) {
        throw new Error(
          `[llm-recorder] 404 at ${url} — the recorder endpoint is not mounted. ` +
          'Set Cena:Testing:LlmRecorderEnabled=true in dev overrides before running this spec.',
        )
      }
      if (!res.ok)
        throw new Error(`[llm-recorder] GET returned ${res.status}`)
      const body = (await res.json()) as { count: number; recordings: LlmCallRecording[] }
      return body.recordings
    },
    async waitFor(opts) {
      const timeout = opts.timeoutMs ?? 10_000
      const poll = opts.pollMs ?? 250
      const minCount = opts.minCount ?? 1
      const deadline = Date.now() + timeout
      for (;;) {
        const calls = await this.list(opts.sinceMs)
        if (calls.length >= minCount)
          return calls
        if (Date.now() >= deadline) {
          throw new Error(
            `[llm-recorder] Timed out after ${timeout}ms waiting for ${minCount} ` +
            `recording(s) since ${opts.sinceMs}. Got ${calls.length}.`,
          )
        }
        await sleep(poll)
      }
    },
  }
}

// ── PII assertion helpers ──────────────────────────────────────────────────
//
// The backend scrubber (ADR-0047) replaces detected PII with `<redacted:*>`
// tokens before the prompt hits the LLM. These helpers let a spec assert:
//
//   (a) the actual PII string never appears in the captured prompts
//   (b) a `<redacted:*>` token does appear (so the scrubber ran, not that
//       the PII simply got dropped — a silent drop is a bug too)
//
// Call with the PII strings the spec entered into the SPA so the assertion
// is specific. "Does prompt contain any email shape" is too broad; a valid
// example URL in the system prompt shouldn't trigger.

export interface PiiSample {
  /** e.g. 'user@example.com' as typed into the UI */
  readonly value: string
  /** e.g. 'email' — used to assert the redacted token type. */
  readonly kind: 'email' | 'phone' | 'address' | 'israeli-id' | 'uk-postcode' | 'other'
}

/**
 * Assert that none of the PII sample strings appear anywhere in the
 * captured prompt text, AND that each sample produced at least one
 * `<redacted:{kind}>` marker. Fails loudly with both the sample and the
 * actual prompt excerpt on regression.
 */
export function assertPromptScrubbed(
  recording: LlmCallRecording,
  samples: readonly PiiSample[],
): void {
  const combined = `${recording.systemPrompt}\n${recording.userPrompt}`
  for (const sample of samples) {
    expect(combined, `Raw PII leaked into prompt: ${sample.kind}=${sample.value}`)
      .not.toContain(sample.value)
    const marker = `<redacted:${sample.kind}>`
    expect(combined, `Scrubber didn't emit ${marker} for ${sample.value} — silent drop?`)
      .toContain(marker)
  }
}

/**
 * Assert that the recording contains only expected context substrings
 * and none of the forbidden ones. Used by EPIC-E2E-D-07 stem-grounded
 * hint assertion: the hint prompt must contain the question stem + the
 * student's current attempt, and MUST NOT contain any other session's
 * content.
 */
export function assertPromptGrounded(
  recording: LlmCallRecording,
  allowed: { mustContain: readonly string[]; mustNotContain: readonly string[] },
): void {
  const combined = `${recording.systemPrompt}\n${recording.userPrompt}`
  for (const needle of allowed.mustContain) {
    expect(combined, `Expected grounding anchor missing from prompt: ${needle}`)
      .toContain(needle)
  }
  for (const forbidden of allowed.mustNotContain) {
    expect(combined, `Unexpected context bled into prompt: ${forbidden}`)
      .not.toContain(forbidden)
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}
