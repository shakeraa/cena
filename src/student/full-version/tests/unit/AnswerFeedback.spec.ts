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

function makeI18n(locale = 'en') {
  return createI18n({
    legacy: false,
    locale,
    fallbackLocale: 'en',
    messages: {
      en: {
        session: {
          runner: {
            correct: 'Correct!',
            wrong: 'Not quite.',
            xpAwarded: '+{xp} XP',
            continueWhenReady: 'Continue',
            explanationLangNote: 'Explanation (English)',
          },
        },
      },
      he: {
        session: {
          runner: {
            correct: '\u05E0\u05DB\u05D5\u05DF!',
            wrong: '\u05DC\u05D0 \u05D1\u05D3\u05D9\u05D5\u05E7.',
            xpAwarded: '+{xp} \u05E0\u05E7\u05F3',
            continueWhenReady: '\u05D4\u05DE\u05E9\u05DA',
            explanationLangNote: '\u05D4\u05E1\u05D1\u05E8 (\u05D1\u05D0\u05E0\u05D2\u05DC\u05D9\u05EA)',
          },
        },
      },
      ar: {
        session: {
          runner: {
            correct: '\u0635\u062D\u064A\u062D!',
            wrong: '\u0644\u064A\u0633 \u0628\u0627\u0644\u0636\u0628\u0637.',
            xpAwarded: '+{xp} \u0646\u0642\u0637\u0629',
            continueWhenReady: '\u0645\u062A\u0627\u0628\u0639\u0629',
            explanationLangNote: '\u062A\u0641\u0633\u064A\u0631 (\u0628\u0627\u0644\u0625\u0646\u062C\u0644\u064A\u0632\u064A\u0629)',
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
          feedback: '', // FIND-pedagogy-017: deprecated — empty string
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
    // FIND-pedagogy-017: the raw feedback.feedback field is no longer rendered
    expect(wrapper.find('[data-testid="feedback-xp"]').text()).toContain('10')
  })

  it('renders the wrong state without XP awarded', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: '', // FIND-pedagogy-017: deprecated — empty string
          xpAwarded: 0,
          masteryDelta: -0.02,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="answer-feedback"]')

    expect(root.attributes('data-correct')).toBe('false')
    // The i18n heading "Not quite." renders via t('session.runner.wrong')
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
          feedback: '',
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
          feedback: '',
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
          feedback: '',
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
          feedback: '',
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

// ─────────────────────────────────────────────────────────────────────
// FIND-pedagogy-017 — i18n regression: no English leaking into RTL locales
//
// The old template rendered `{{ feedback.feedback }}` (the English server
// pill "Correct" / "Not quite") below the translated heading, producing a
// bilingual mash-up for Hebrew and Arabic users.
//
// Fix: the template no longer renders the `feedback` field at all. The
// heading comes from the i18n key. Explanation blocks are wrapped with
// `lang="en"` and get a translated "(English)" label when locale != 'en'.
//
// Cite: Hattie & Timperley (2007) DOI 10.3102/003465430298487
// ─────────────────────────────────────────────────────────────────────

/** Regex matching ASCII Latin letters (A-Z, a-z). */
const LATIN_RE = /[A-Za-z]/

describe('AnswerFeedback i18n (FIND-pedagogy-017)', () => {
  it('HE locale — correct answer heading contains only Hebrew, no Latin text', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: '',
          xpAwarded: 10,
          masteryDelta: 0.15,
          nextQuestionId: 'q2',
          explanation: null,
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('he'), makeVuetify()] },
    })

    const heading = wrapper.find('.text-h6')
    expect(heading.exists()).toBe(true)
    // The heading must be the Hebrew "נכון!" — no Latin characters
    expect(heading.text()).toContain('\u05E0\u05DB\u05D5\u05DF')
    expect(heading.text()).not.toMatch(LATIN_RE)
  })

  it('HE locale — wrong answer heading contains only Hebrew, no Latin text', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: '',
          xpAwarded: 0,
          masteryDelta: -0.1,
          nextQuestionId: null,
          explanation: null,
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('he'), makeVuetify()] },
    })

    const heading = wrapper.find('.text-h6')
    expect(heading.exists()).toBe(true)
    expect(heading.text()).not.toMatch(LATIN_RE)
  })

  it('AR locale — correct answer heading contains only Arabic, no Latin text', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: '',
          xpAwarded: 10,
          masteryDelta: 0.15,
          nextQuestionId: 'q2',
          explanation: null,
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('ar'), makeVuetify()] },
    })

    const heading = wrapper.find('.text-h6')
    expect(heading.exists()).toBe(true)
    expect(heading.text()).not.toMatch(LATIN_RE)
  })

  it('does NOT render the deprecated feedback.feedback field even if non-empty', () => {
    // Even if the server mistakenly ships a non-empty Feedback string,
    // the component must NOT render it anywhere.
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: 'Correct', // old English pill — should NOT appear
          xpAwarded: 10,
          masteryDelta: 0.15,
          nextQuestionId: 'q2',
          explanation: null,
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('he'), makeVuetify()] },
    })

    const cardText = wrapper.find('[data-testid="answer-feedback"]').text()
    expect(cardText).not.toContain('Correct')
  })

  it('HE locale — shows Hebrew language note when explanation is present', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: '',
          xpAwarded: 10,
          masteryDelta: 0.15,
          nextQuestionId: 'q2',
          explanation: 'Adding fractions with same denominator.',
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('he'), makeVuetify()] },
    })

    const explanationBlock = wrapper.find('[data-testid="feedback-explanation"]')
    expect(explanationBlock.exists()).toBe(true)

    // Must have lang="en" on the explanation text wrapper
    const langEnDiv = explanationBlock.find('[lang="en"]')
    expect(langEnDiv.exists()).toBe(true)

    // Must show the Hebrew label
    expect(explanationBlock.text()).toContain('\u05D4\u05E1\u05D1\u05E8')
  })

  it('EN locale — does NOT show language note on explanation', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: '',
          xpAwarded: 10,
          masteryDelta: 0.15,
          nextQuestionId: 'q2',
          explanation: 'Adding fractions with same denominator.',
          distractorRationale: null,
        },
      },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    const explanationBlock = wrapper.find('[data-testid="feedback-explanation"]')
    expect(explanationBlock.exists()).toBe(true)
    expect(explanationBlock.text()).not.toContain('Explanation (English)')
  })

  it('AR locale — distractor rationale has lang="en" wrapper and Arabic label', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: '',
          xpAwarded: 0,
          masteryDelta: -0.1,
          nextQuestionId: null,
          explanation: 'Some explanation.',
          distractorRationale: 'You added numerators AND denominators.',
        },
      },
      global: { plugins: [makeI18n('ar'), makeVuetify()] },
    })

    const rationaleBlock = wrapper.find('[data-testid="feedback-distractor-rationale"]')
    expect(rationaleBlock.exists()).toBe(true)

    const langEnDiv = rationaleBlock.find('[lang="en"]')
    expect(langEnDiv.exists()).toBe(true)
    expect(langEnDiv.text()).toContain('You added numerators AND denominators.')

    // Arabic language note label
    expect(rationaleBlock.text()).toContain('\u062A\u0641\u0633\u064A\u0631')
  })

  it('component source does not contain {{ feedback.feedback }}', () => {
    // FIND-pedagogy-017 regression guard: the template must never render
    // the raw server feedback string. This catches accidental re-introduction.
    const src = readFileSync(
      resolve(process.cwd(), 'src/components/session/AnswerFeedback.vue'),
      'utf8',
    )

    expect(src).not.toContain('{{ feedback.feedback }}')
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
