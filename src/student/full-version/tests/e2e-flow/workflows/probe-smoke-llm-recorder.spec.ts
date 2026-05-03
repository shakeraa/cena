// =============================================================================
// probe smoke — LLM recorder
//
// Exercises the probes/llm-recorder.ts helpers against the test-only
// admin-api endpoint registered at /api/test/llm-recorder/recordings
// when `Cena:Testing:LlmRecorderEnabled` is true.
//
//   npm run test:e2e:flow -- --grep "probe-smoke llm-recorder"
//
// Gracefully skips when the recorder endpoint is not mounted (prod-like
// config). Specs in EPIC-E2E-I-05 and EPIC-E2E-D-05 require the recorder;
// if this smoke skips, those specs won't be runnable either — that's a
// clear signal to the operator to flip the flag.
// =============================================================================

import { test, expect } from '@playwright/test'
import {
  createLlmRecorder,
  assertPromptScrubbed,
  assertPromptGrounded,
  type LlmCallRecording,
} from '../probes/llm-recorder'

const ADMIN_API_BASE_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'

async function recorderEnabled(): Promise<boolean> {
  try {
    // A GET with sinceMs=0 always returns ok when the endpoint is mounted,
    // with an empty list when the Marten table is empty. A 404 means the
    // endpoint wasn't registered because the config flag was off.
    const res = await fetch(`${ADMIN_API_BASE_URL}/api/test/llm-recorder/recordings?sinceMs=0`)
    return res.status === 200
  }
  catch {
    return false
  }
}

test.describe('@probe-smoke llm-recorder', () => {
  test.beforeEach(async () => {
    const enabled = await recorderEnabled()
    test.skip(!enabled,
      'LLM recorder endpoint not mounted. ' +
      'Set Cena:Testing:LlmRecorderEnabled=true in the admin-api host config ' +
      '(e.g. docker/overrides/Cena:Testing__LlmRecorderEnabled=true) then rerun.')
  })

  test('reset clears prior recordings', async () => {
    const recorder = createLlmRecorder()
    await recorder.reset()
    const after = await recorder.list(0)
    expect(after).toEqual([])
  })

  test('list honours sinceMs filter', async () => {
    const recorder = createLlmRecorder()
    await recorder.reset()
    // Future timestamp → always empty. Validates the filter applies.
    const future = Date.now() + 60_000
    const calls = await recorder.list(future)
    expect(calls).toEqual([])
  })

  test('waitFor times out when no recordings arrive', async () => {
    const recorder = createLlmRecorder()
    await recorder.reset()
    const since = Date.now()
    await expect(recorder.waitFor({ sinceMs: since, minCount: 1, timeoutMs: 1500 }))
      .rejects.toThrow(/Timed out/)
  })

  // The PII assertion helpers do not require a real LLM call — they are
  // pure functions over the LlmCallRecording shape. These smoke tests
  // assert the helpers behave correctly on a synthesised recording so
  // a spec author can trust them.

  test('assertPromptScrubbed passes when PII is redacted', () => {
    const rec = synthRecording({
      systemPrompt: 'You are a math tutor.',
      userPrompt: 'I live at <redacted:address>. What is 2+2?',
    })
    // Passing case — no throw.
    assertPromptScrubbed(rec, [{ value: '5 King St, Tel Aviv', kind: 'address' }])
  })

  test('assertPromptScrubbed fails when raw PII leaks through', () => {
    const rec = synthRecording({
      systemPrompt: 'You are a math tutor.',
      userPrompt: 'I live at 5 King St, Tel Aviv. What is 2+2?',
    })
    expect(() => assertPromptScrubbed(rec, [{ value: '5 King St, Tel Aviv', kind: 'address' }]))
      .toThrow(/Raw PII leaked/)
  })

  test('assertPromptScrubbed fails when scrubber silently dropped the PII', () => {
    const rec = synthRecording({
      systemPrompt: 'You are a math tutor.',
      // PII stripped but no <redacted:*> marker — silent drop.
      userPrompt: 'I live at . What is 2+2?',
    })
    expect(() => assertPromptScrubbed(rec, [{ value: '5 King St, Tel Aviv', kind: 'address' }]))
      .toThrow(/silent drop/)
  })

  test('assertPromptGrounded catches forbidden context bleed', () => {
    const rec = synthRecording({
      systemPrompt: 'Hint for: Solve x^2 + 3x - 4 = 0',
      userPrompt: 'Student attempted: x = 1. And here\'s another student\'s work on x = -2',
    })
    expect(() => assertPromptGrounded(rec, {
      mustContain: ['Solve x^2 + 3x - 4 = 0'],
      mustNotContain: ['another student'],
    })).toThrow(/Unexpected context bled/)
  })
})

function synthRecording(overrides: Partial<LlmCallRecording>): LlmCallRecording {
  return {
    id: 'synthetic',
    timestamp: new Date().toISOString(),
    modelId: 'test-model',
    systemPrompt: '',
    userPrompt: '',
    responseContent: '',
    inputTokens: 0,
    outputTokens: 0,
    latencyMs: 0,
    fromCache: false,
    errorKind: null,
    errorMessage: null,
    tag: null,
    ...overrides,
  }
}
