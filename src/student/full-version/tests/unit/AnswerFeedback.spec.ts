import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import AnswerFeedback from '@/components/session/AnswerFeedback.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          runner: {
            correct: 'Correct!',
            wrong: 'Not quite.',
            xpAwarded: '+{xp} XP',
            continueWhenReady: 'Continue',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('AnswerFeedback', () => {
  it('renders the correct state with XP awarded', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: 'Great job!',
          xpAwarded: 10,
          masteryDelta: 0.05,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="answer-feedback"]')

    expect(root.attributes('data-correct')).toBe('true')
    expect(wrapper.text()).toContain('Correct!')
    expect(wrapper.text()).toContain('Great job!')
    expect(wrapper.find('[data-testid="feedback-xp"]').text()).toContain('10')
  })

  it('renders the wrong state without XP awarded', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: 'Not quite — try again next time.',
          xpAwarded: 0,
          masteryDelta: -0.02,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="answer-feedback"]')

    expect(root.attributes('data-correct')).toBe('false')
    expect(wrapper.text()).toContain('Not quite.')
    expect(wrapper.find('[data-testid="feedback-xp"]').exists()).toBe(false)
  })

  // ─────────────────────────────────────────────────────────────────────
  // FIND-pedagogy-005 — tap-to-continue (no auto-dismiss)
  //
  // Asserts the component renders an explicit Continue button, emits
  // `continue` when tapped, and contains NO setTimeout in its source.
  // The previous implementation in pages/session/[sessionId]/index.vue
  // used `setTimeout(..., 1600)` to hard-dismiss feedback; that magic
  // number is gone and the parent now listens to `@continue`.
  //
  // Cite: Shute (2008) "Focus on Formative Feedback", DOI
  //       10.3102/0034654307313795 — learner-controlled pacing.
  // ─────────────────────────────────────────────────────────────────────

  it('renders an explicit Continue button (no auto-dismiss)', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: 'Not quite.',
          xpAwarded: 0,
          masteryDelta: 0,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const btn = wrapper.find('[data-testid="feedback-continue"]')

    expect(btn.exists()).toBe(true)
    expect(btn.text()).toContain('Continue')
  })

  it('emits "continue" when the Continue button is clicked', async () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: 'Nice.',
          xpAwarded: 10,
          masteryDelta: 0.1,
          nextQuestionId: null,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="feedback-continue"]').trigger('click')

    expect(wrapper.emitted('continue')).toBeTruthy()
    expect(wrapper.emitted('continue')).toHaveLength(1)
  })

  it('renders the authored distractor rationale block when present', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: 'Not quite.',
          xpAwarded: 0,
          masteryDelta: -0.03,
          nextQuestionId: 'q_003',
          distractorRationale: 'That\u2019s the voltage — remember Ohm\u2019s law uses V = I × R.',
          explanation: 'The resistance is V / I = 12 / 2 = 6 ohms.',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="feedback-distractor-rationale"]').text())
      .toContain('Ohm')
    expect(wrapper.find('[data-testid="feedback-explanation"]').text())
      .toContain('V / I')
  })

  it('AnswerFeedback component source contains no setTimeout (no auto-dismiss)', () => {
    // FIND-pedagogy-005 regression guard. We load the component file at
    // test time and assert no `setTimeout(` appears in its source. This
    // is cheap protection against re-introducing a magic-number
    // auto-dismiss. Feedback is tap-to-continue only.
    // Uses process.cwd() (the student-web package root at test time) so
    // the path resolution works under both vitest and node. Using
    // `import.meta.url` fails under vite's in-memory transform pipeline.
    const src = readFileSync(
      resolve(process.cwd(), 'src/components/session/AnswerFeedback.vue'),
      'utf8',
    )

    expect(src.includes('setTimeout(')).toBe(false)
  })

  it('disables the Continue button while parent is advancing', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: 'Nice.',
          xpAwarded: 10,
          masteryDelta: 0.1,
          nextQuestionId: 'q_002',
        },
        loading: true,
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const btn = wrapper.find('[data-testid="feedback-continue"]')

    expect(btn.attributes('disabled')).toBeDefined()
  })
})

describe('session runner page (FIND-pedagogy-005)', () => {
  it('contains no setTimeout-based feedback dismissal', () => {
    // The session runner page used to call
    //   setTimeout(async () => { feedback.value = null; ... }, 1600)
    // We assert the literal setTimeout-with-1600 pattern is gone. The
    // page may still use setTimeout for its optional auto-advance on
    // CORRECT answers (gated by the a11y preference), but that delay
    // comes from CORRECT_AUTO_ADVANCE_MS, NOT the banned 1600 literal.
    const src = readFileSync(
      resolve(process.cwd(), 'src/pages/session/[sessionId]/index.vue'),
      'utf8',
    )

    expect(src.includes('1600')).toBe(false)

    // The setTimeout that remains must clearly be tied to the a11y
    // preference path — `autoAdvanceTimer` is the variable name we use.
    expect(src.includes('autoAdvanceTimer')).toBe(true)
  })
})
