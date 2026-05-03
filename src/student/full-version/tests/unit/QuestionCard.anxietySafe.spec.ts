// =============================================================================
// RDY-065 (F11) — Anxiety-safe hint ladder: regression tests.
//
// Verifies QuestionCard does NOT leak penalty-style counters, numeric hint
// levels, or comparative shame text to the student UI when hints are in play.
// The backend (HintAdjustedBktService) applies assisted-credit discount
// internally; the UI must never expose "-20% credit" or "Hint 2 of 3" or
// "(1 left)" style framing.
//
// Why this spec: personas Yael (dyscalculia + formal accommodations) and
// Noa (5-unit ambitious perfectionist) both abandon sessions when UI
// frames hint use as a penalty or a counted help-seeking event. See
// docs/research/cena-user-personas-and-features-2026-04-17.md (F11)
// and docs/research/cena-panel-review-user-personas-2026-04-17.md.
// =============================================================================

import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import QuestionCard from '@/components/session/QuestionCard.vue'
import type { SessionQuestionDto } from '@/api/types/common'

function makeI18n(locale: 'en' | 'ar' | 'he' = 'en') {
  const messages = {
    en: {
      session: {
        runner: {
          questionProgress: 'Question {current} of {total}',
          progressAria: 'Question {current} of {total}',
          choicesAria: 'Choices',
          submitAnswer: 'Submit',
          workedExampleLabel: 'Worked example',
          hintStep: {
            1: 'A gentle nudge',
            2: 'Building the idea',
            3: 'Walking through it',
          },
          stuckCta: 'I\'m stuck',
        },
      },
    },
    ar: {
      session: {
        runner: {
          questionProgress: 'السؤال {current} من {total}',
          progressAria: 'السؤال {current} من {total}',
          choicesAria: 'الخيارات',
          submitAnswer: 'إرسال',
          workedExampleLabel: 'مثال محلول',
          hintStep: {
            1: 'إرشاد لطيف',
            2: 'نبني الفكرة معاً',
            3: 'نسير خطوة بخطوة',
          },
          stuckCta: 'أحتاج مساعدة',
        },
      },
    },
    he: {
      session: {
        runner: {
          questionProgress: 'שאלה {current} מתוך {total}',
          progressAria: 'שאלה {current} מתוך {total}',
          choicesAria: 'תשובות',
          submitAnswer: 'שלח',
          workedExampleLabel: 'דוגמה פתורה',
          hintStep: {
            1: 'הכוונה עדינה',
            2: 'בונים את הרעיון',
            3: 'הולכים צעד-צעד',
          },
          stuckCta: 'עזרה בבקשה',
        },
      },
    },
  }
  return createI18n({ legacy: false, locale, messages })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const question: SessionQuestionDto = {
  questionId: 'q_anxiety_001',
  questionIndex: 0,
  totalQuestions: 5,
  prompt: 'What is 12 × 8?',
  questionType: 'multiple-choice',
  choices: ['92', '96', '104', '108'],
  subject: 'Mathematics',
  expectedTimeSeconds: 30,
  scaffoldingLevel: 'HintsOnly',
  hintsAvailable: 3,
  hintsRemaining: 3,
}

// Patterns that must NEVER appear in rendered student-facing text.
// These are the GD-004 + F11 banned UX surface expressions.
const BANNED_EN = [
  /\(\s*\d+\s+left\s*\)/i, // "(3 left)"
  /\(\s*\{?\s*\w+\s*\}?\s+(left|remaining|to go)\s*\)/i, // "({n} left)"
  /\bhint\s+\d+(\s+of\s+\d+)?\b/i, // "Hint 1", "Hint 1 of 3"
  /\bhints?\s+remaining\b/i,
  /\bhints?\s+used\b/i,
  /-\s*\d+%\s*(credit|score|mastery|points)/i,
  /\byou['']re\s+behind\b/i,
  /\bslower than\b/i,
]

const BANNED_AR = [
  /\(\s*(تبقى|متبقي)\s+\d+\s*\)/u, // "(3 متبقية)"
  /\(\s*(تبقى|متبقي)\s+\{?[^)]*\}?\s*\)/u,
  /تلميح\s+\d+/u, // "تلميح 1"
  /أنت\s+متأخر/u,
]

const BANNED_HE = [
  /\(\s*נותרו?\s+\d+\s*\)/u, // "(3 נותרו)"
  /\(\s*נותרו?\s+\{?[^)]*\}?\s*\)/u,
  /רמז\s+\d+/u, // "רמז 1"
  /אתה\s+מפגר/u,
]

function expectNoBannedText(text: string, patterns: RegExp[]) {
  for (const pat of patterns)
    expect(text, `must not match ${pat} — banned UX surface (RDY-065)`).not.toMatch(pat)
}

describe('QuestionCard — RDY-065 anxiety-safe hint ladder', () => {
  it('CTA button renders as "I\'m stuck" — no counter in any form (EN)', () => {
    const wrapper = mount(QuestionCard, {
      props: { question: { ...question, hintsRemaining: 3 } },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    const btn = wrapper.find('[data-testid="question-hint-request"]')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toContain('I\'m stuck')
    expectNoBannedText(btn.text(), BANNED_EN)
  })

  it('CTA button renders localized stuck copy with no counter (AR)', () => {
    const wrapper = mount(QuestionCard, {
      props: { question: { ...question, hintsRemaining: 3 } },
      global: { plugins: [makeI18n('ar'), makeVuetify()] },
    })

    const btn = wrapper.find('[data-testid="question-hint-request"]')
    expect(btn.text()).toContain('أحتاج مساعدة')
    expectNoBannedText(btn.text(), BANNED_AR)
  })

  it('CTA button renders localized stuck copy with no counter (HE)', () => {
    const wrapper = mount(QuestionCard, {
      props: { question: { ...question, hintsRemaining: 3 } },
      global: { plugins: [makeI18n('he'), makeVuetify()] },
    })

    const btn = wrapper.find('[data-testid="question-hint-request"]')
    expect(btn.text()).toContain('עזרה בבקשה')
    expectNoBannedText(btn.text(), BANNED_HE)
  })

  it('hint display shows qualitative rung label, never "Hint N"', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: { ...question, hintsRemaining: 2 },
        lastHint: {
          hintLevel: 2,
          hintText: 'Try thinking about what 10 × 8 would be first.',
          hasMoreHints: true,
          hintsRemaining: 1,
        },
      },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    const hintDisplay = wrapper.find('[data-testid="question-hint-display"]')
    expect(hintDisplay.exists()).toBe(true)
    expect(hintDisplay.text()).toContain('Building the idea')
    expectNoBannedText(hintDisplay.text(), BANNED_EN)
  })

  it('hint display uses info tone, not warning — hints are not penalties', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: { ...question, hintsRemaining: 2 },
        lastHint: {
          hintLevel: 1,
          hintText: 'Re-read the problem carefully.',
          hasMoreHints: true,
          hintsRemaining: 2,
        },
      },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    const hintDisplay = wrapper.find('[data-testid="question-hint-display"]')
    // Vuetify VAlert applies the type via a class. Info (blue) is neutral;
    // warning (orange) frames hint use as a cautionary event, which is
    // the F11 violation we are fixing.
    expect(hintDisplay.classes().some(c => c.includes('warning'))).toBe(false)
    expect(
      hintDisplay.classes().some(c => c.includes('info')),
      'hint alert should use info tone, not warning',
    ).toBe(true)
  })

  it('whole-card text contains no banned penalty/shame patterns at any hint state', () => {
    // Walk through all three backend hint levels and verify the rendered
    // surface is clean in each state. Covers 1st, 2nd, 3rd hint deliveries.
    for (const level of [1, 2, 3] as const) {
      const wrapper = mount(QuestionCard, {
        props: {
          question: {
            ...question,
            hintsRemaining: 3 - level,
          },
          lastHint: {
            hintLevel: level,
            hintText: `Hint payload ${level}.`,
            hasMoreHints: level < 3,
            hintsRemaining: 3 - level,
          },
        },
        global: { plugins: [makeI18n('en'), makeVuetify()] },
      })

      expectNoBannedText(wrapper.text(), BANNED_EN)
    }
  })

  it('hint button hides at hintsRemaining=0 (no "0 left" ever rendered)', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: { ...question, hintsRemaining: 0, scaffoldingLevel: 'HintsOnly' },
      },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="question-hint-request"]').exists()).toBe(false)
    // And critically: no "0 left" / "(0 remaining)" fallback sneaked in.
    expectNoBannedText(wrapper.text(), BANNED_EN)
  })
})
