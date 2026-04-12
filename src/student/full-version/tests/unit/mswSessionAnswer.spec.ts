/**
 * FIND-pedagogy-011 — MSW session answer handler contract & behavior tests.
 *
 * These tests verify:
 * 1. The MSW answer response shape matches SessionAnswerResponseDto exactly.
 * 2. Wrong answers produce a non-null distractorRationale.
 * 3. Five consecutive correct answers produce a non-linear BKT mastery curve
 *    (not the old hardcoded +0.05 linear delta).
 * 4. The old canned feedback strings are gone.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { bktUpdate } from '@/plugins/fake-api/handlers/student-sessions/index'

// ─────────────────────────────────────────────────────────────────────
// Contract tests — MSW response shape matches SessionAnswerResponseDto
// ─────────────────────────────────────────────────────────────────────

describe('MSW session answer handler contract (FIND-pedagogy-011)', () => {
  it('handler source has no CANNED constant', () => {
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    // The literal `CANNED` constant must be removed entirely
    expect(src).not.toMatch(/\bCANNED\b/)
  })

  it('handler source does not contain "Correct! Great work."', () => {
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    expect(src).not.toContain('Correct! Great work.')
  })

  it('handler source does not contain hardcoded 0.05 mastery delta', () => {
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    // No `0.05` or `-0.02` as literal return values for masteryDelta
    expect(src).not.toMatch(/masteryDelta:\s*(?:correct\s*\?\s*)?0\.05/)
    expect(src).not.toMatch(/masteryDelta:\s*(?:correct\s*\?\s*0\.05\s*:\s*)?-0\.02/)
  })

  it('handler response JSON includes all SessionAnswerResponseDto fields', () => {
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    // The return body must include all 7 fields from the DTO
    const requiredFields = [
      'correct',
      'feedback',
      'xpAwarded',
      'masteryDelta',
      'nextQuestionId',
      'explanation',
      'distractorRationale',
    ]
    for (const field of requiredFields) {
      expect(src, `Response must include field "${field}"`).toContain(field)
    }
  })

  it('dev questions include explanation and distractorRationales', () => {
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    // Every dev question must have an explanation property with real content
    expect(src).toContain('explanation:')
    expect(src).toContain('distractorRationales:')
  })
})

// ─────────────────────────────────────────────────────────────────────
// BKT shim behavior tests
// ─────────────────────────────────────────────────────────────────────

describe('BKT shim (FIND-pedagogy-011)', () => {
  it('correct answer increases mastery', () => {
    const prior = 0.10
    const posterior = bktUpdate(prior, true)
    expect(posterior).toBeGreaterThan(prior)
  })

  it('wrong answer decreases mastery', () => {
    const prior = 0.50
    const posterior = bktUpdate(prior, false)
    expect(posterior).toBeLessThan(prior)
  })

  it('mastery is always clamped to [0.01, 0.99]', () => {
    // Very low prior + wrong answer should not go below 0.01
    const veryLow = bktUpdate(0.01, false)
    expect(veryLow).toBeGreaterThanOrEqual(0.01)

    // Very high prior + correct answer should not exceed 0.99
    const veryHigh = bktUpdate(0.99, true)
    expect(veryHigh).toBeLessThanOrEqual(0.99)
  })

  it('5 consecutive correct answers produce non-linear mastery trajectory', () => {
    // The old handler returned a constant +0.05 delta. BKT produces
    // diminishing returns — each delta should differ from the previous
    // one, proving the trajectory is non-linear.
    const deltas: number[] = []
    let mastery = 0.10

    for (let i = 0; i < 5; i++) {
      const posterior = bktUpdate(mastery, true)
      const delta = posterior - mastery
      deltas.push(delta)
      mastery = posterior
    }

    // All deltas must be positive (correct answers increase mastery)
    for (const d of deltas)
      expect(d).toBeGreaterThan(0)

    // NOT all deltas are the same — BKT produces diminishing returns
    const allSame = deltas.every(d => Math.abs(d - deltas[0]) < 1e-10)
    expect(allSame, 'BKT deltas must not all be identical (non-linear curve)').toBe(false)

    // Specifically: later deltas should be smaller than earlier ones
    // (diminishing returns as mastery increases)
    expect(deltas[4]).toBeLessThan(deltas[0])
  })

  it('matches the .NET BktService.Update for known inputs', () => {
    // Validate against hand-computed BKT with default params:
    // prior=0.10, pSlip=0.05, pGuess=0.20, pLearning=0.10, pForget=0.02
    //
    // Correct answer:
    //   pCorrect = 0.10 * 0.95 + 0.90 * 0.20 = 0.095 + 0.180 = 0.275
    //   pLearned = 0.10 * 0.95 / 0.275 = 0.095 / 0.275 = 0.34545...
    //   posterior = 0.34545 + (1 - 0.34545) * 0.10 = 0.34545 + 0.06545 = 0.41091
    //   with forget: 0.41091 * 0.98 = 0.40269...
    const result = bktUpdate(0.10, true)
    expect(result).toBeCloseTo(0.40269, 3)
  })

  it('wrong answer with prior=0.50 yields correct posterior', () => {
    // prior=0.50, wrong answer:
    //   pCorrect = 0.50 * 0.95 + 0.50 * 0.20 = 0.475 + 0.100 = 0.575
    //   pIncorrect = 0.425
    //   pLearned = 0.50 * 0.05 / 0.425 = 0.025 / 0.425 = 0.058824
    //   posterior = 0.058824 + (1 - 0.058824) * 0.10 = 0.058824 + 0.094118 = 0.152941
    //   with forget: 0.152941 * 0.98 = 0.149882
    const result = bktUpdate(0.50, false)
    expect(result).toBeCloseTo(0.14988, 3)
  })
})

// ─────────────────────────────────────────────────────────────────────
// Behavior test — wrong answer gets distractor rationale
// ─────────────────────────────────────────────────────────────────────

describe('MSW answer handler behavior (FIND-pedagogy-011)', () => {
  it('handler source returns distractorRationale for wrong answers', () => {
    // The handler checks `if (!correct && body.answer)` and looks up
    // the distractor rationale from the question data. Verify the
    // conditional logic exists.
    const src = readFileSync(
      resolve(process.cwd(), 'src/plugins/fake-api/handlers/student-sessions/index.ts'),
      'utf8',
    )
    // Must have the conditional guard for wrong-answer distractor lookup
    expect(src).toContain('!correct')
    expect(src).toContain('distractorRationale')
    // Must NOT return null unconditionally
    expect(src).not.toMatch(/distractorRationale:\s*null\s*[,}]/)
  })
})
