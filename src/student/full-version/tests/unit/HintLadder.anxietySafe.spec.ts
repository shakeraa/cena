// =============================================================================
// RDY-065b — HintLadder.vue anxiety-safe regression tests.
//
// Extends the QuestionCard.anxietySafe guard to the standalone HintLadder
// component that ships the 5-rung progressive disclosure UI. The ladder
// must never render:
//   - Numeric ordinals ("Hint 1", "Rung 3 of 5", "1/5")
//   - Counters or remaining-of-N ("(2 left)", "3 remaining")
//   - Shame / penalty framing ("-20% credit", "you lost points")
//   - Comparative percentile ("worse than 60% of classmates")
//
// Covers: all three locales (en / ar / he) × all five rungs × the
// request-more-button visibility guard.
// =============================================================================

import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import HintLadder from '@/components/session/HintLadder.vue'

function makeI18n(locale: 'en' | 'ar' | 'he' = 'en') {
  const messages = {
    en: {
      session: {
        runner: {
          hintStep: {
            1: 'A gentle nudge',
            2: 'Building the idea',
            3: 'Walking through it',
            4: 'The key step',
            5: 'A worked example',
          },
          hintLadder: {
            moreHint: 'More help',
            previousRungs: 'Earlier suggestions',
          },
        },
      },
    },
    ar: {
      session: {
        runner: {
          hintStep: {
            1: 'إرشاد لطيف',
            2: 'نبني الفكرة معاً',
            3: 'نسير خطوة بخطوة',
            4: 'الخطوة الأساسية',
            5: 'مثال محلول',
          },
          hintLadder: {
            moreHint: 'المزيد من المساعدة',
            previousRungs: 'الإرشادات السابقة',
          },
        },
      },
    },
    he: {
      session: {
        runner: {
          hintStep: {
            1: 'הכוונה עדינה',
            2: 'בונים את הרעיון',
            3: 'הולכים צעד-צעד',
            4: 'הצעד העיקרי',
            5: 'דוגמה פתורה',
          },
          hintLadder: {
            moreHint: 'עוד עזרה',
            previousRungs: 'הכוונות קודמות',
          },
        },
      },
    },
  }
  return createI18n({ legacy: false, locale, fallbackLocale: 'en', messages })
}

function mountLadder(opts: {
  locale?: 'en' | 'ar' | 'he'
  hints: { hintLevel: number; hintText: string; hintsRemaining?: number }[]
  hintsRemaining?: number
  maxRungs?: number
  locked?: boolean
  loading?: boolean
}) {
  const vuetify = createVuetify({ components, directives })
  const i18n = makeI18n(opts.locale ?? 'en')
  return mount(HintLadder, {
    global: { plugins: [vuetify, i18n] },
    props: {
      hints: opts.hints,
      hintsRemaining: opts.hintsRemaining,
      maxRungs: opts.maxRungs ?? 5,
      locked: opts.locked ?? false,
      loading: opts.loading ?? false,
    },
  })
}

// Banned patterns per GD-004 + RDY-065 phase-1 shipgate rules.
// These run across EVERY locale so any locale regression is caught.
const BANNED_PATTERNS: Array<{ pattern: RegExp; why: string }> = [
  { pattern: /Hint\s+\d/i, why: 'numeric hint ordinal' },
  { pattern: /Rung\s+\d/i, why: 'numeric rung ordinal' },
  { pattern: /\brung\s+\d\s+of\s+\d/i, why: 'ordinal N of M' },
  { pattern: /\bhint\s+\d\s+of\s+\d/i, why: 'ordinal N of M' },
  { pattern: /\(\s*\d+\s+(left|remaining|to go)\s*\)/i, why: 'penalty counter' },
  { pattern: /\brem(a|ai)ning\s*[:=]\s*\d+/i, why: 'remaining counter' },
  { pattern: /[-–]\s*\d+\s*%\s*credit/i, why: 'BKT credit penalty' },
  { pattern: /lost\s+points/i, why: 'penalty shame' },
  { pattern: /worse than/i, why: 'comparative shame' },
  { pattern: /percentile/i, why: 'comparative percentile' },
  { pattern: /streak/i, why: 'streak (loss-aversion)' },
  // Arabic analogs
  { pattern: /رمز\s+\d/i, why: 'Arabic numeric hint' },
  { pattern: /متبق[يٍ]\s*:?\s*\d/i, why: 'Arabic remaining counter' },
  { pattern: /فقدت\s+نقاط/i, why: 'Arabic penalty shame' },
  // Hebrew analogs
  { pattern: /רמז\s+\d/, why: 'Hebrew numeric hint' },
  { pattern: /נותר[ו]?\s*:?\s*\d/, why: 'Hebrew remaining counter' },
  { pattern: /איבדת\s+נקודות/, why: 'Hebrew penalty shame' },
]

function assertNoBannedPatterns(rendered: string, locale: string) {
  for (const { pattern, why } of BANNED_PATTERNS) {
    expect(
      rendered.match(pattern),
      `HintLadder (${locale}) contains banned pattern (${why}): ${pattern}`
    ).toBeNull()
  }
}

describe('HintLadder — anxiety-safe DOM guards', () => {
  const threeRungs = [
    { hintLevel: 1, hintText: 'identify the equation type', hintsRemaining: 4 },
    { hintLevel: 2, hintText: 'recall p(x) = 0 form', hintsRemaining: 3 },
    { hintLevel: 3, hintText: 'move terms to one side', hintsRemaining: 2 },
  ]

  it('renders each received rung with its qualitative label — en', () => {
    const wrapper = mountLadder({ locale: 'en', hints: threeRungs })
    const html = wrapper.html()
    expect(html).toContain('A gentle nudge')
    expect(html).toContain('Building the idea')
    expect(html).toContain('Walking through it')
    assertNoBannedPatterns(html, 'en')
  })

  it('renders each received rung with its qualitative label — ar', () => {
    const wrapper = mountLadder({ locale: 'ar', hints: threeRungs })
    const html = wrapper.html()
    expect(html).toContain('إرشاد لطيف')
    expect(html).toContain('نبني الفكرة معاً')
    expect(html).toContain('نسير خطوة بخطوة')
    assertNoBannedPatterns(html, 'ar')
  })

  it('renders each received rung with its qualitative label — he', () => {
    const wrapper = mountLadder({ locale: 'he', hints: threeRungs })
    const html = wrapper.html()
    expect(html).toContain('הכוונה עדינה')
    expect(html).toContain('בונים את הרעיון')
    expect(html).toContain('הולכים צעד-צעד')
    assertNoBannedPatterns(html, 'he')
  })

  it('renders rungs 4 and 5 when backend delivers them — en', () => {
    const wrapper = mountLadder({
      locale: 'en',
      hints: [
        { hintLevel: 4, hintText: 'apply the quadratic formula', hintsRemaining: 1 },
        { hintLevel: 5, hintText: 'worked example: x² − 5x + 6 = 0 factors as (x−2)(x−3)', hintsRemaining: 0 },
      ],
    })
    const html = wrapper.html()
    expect(html).toContain('The key step')
    expect(html).toContain('A worked example')
    assertNoBannedPatterns(html, 'en')
  })

  it('does NOT render hintsRemaining as text anywhere in the DOM', () => {
    const wrapper = mountLadder({
      locale: 'en',
      hints: threeRungs,
      hintsRemaining: 7,
    })
    const html = wrapper.html()
    // A literal "7" may appear inside hintText, but the "remaining"/"left"
    // framing must never reach the DOM.
    expect(html).not.toMatch(/\b7\s+(left|remaining|to go)/i)
    expect(html).not.toMatch(/remaining\s*:?\s*7/i)
    assertNoBannedPatterns(html, 'en')
  })

  it('hides the request-more button when hintsRemaining is 0', () => {
    const wrapper = mountLadder({
      locale: 'en',
      hints: threeRungs,
      hintsRemaining: 0,
    })
    expect(wrapper.find('[data-testid="hint-ladder-request-more"]').exists()).toBe(false)
  })

  it('hides the request-more button when maxRungs reached', () => {
    const fiveRungs = [
      { hintLevel: 1, hintText: 'r1' },
      { hintLevel: 2, hintText: 'r2' },
      { hintLevel: 3, hintText: 'r3' },
      { hintLevel: 4, hintText: 'r4' },
      { hintLevel: 5, hintText: 'r5' },
    ]
    const wrapper = mountLadder({
      locale: 'en',
      hints: fiveRungs,
      hintsRemaining: 99,
    })
    expect(wrapper.find('[data-testid="hint-ladder-request-more"]').exists()).toBe(false)
  })

  it('hides the request-more button when locked', () => {
    const wrapper = mountLadder({ locale: 'en', hints: threeRungs, locked: true })
    expect(wrapper.find('[data-testid="hint-ladder-request-more"]').exists()).toBe(false)
  })

  it('shows the request-more button with neutral "More help" copy when eligible', () => {
    const wrapper = mountLadder({
      locale: 'en',
      hints: [threeRungs[0]],
      hintsRemaining: 3,
    })
    const btn = wrapper.find('[data-testid="hint-ladder-request-more"]')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toBe('More help')
    // Never a counter / ordinal on the button label.
    assertNoBannedPatterns(btn.text(), 'en-button')
  })

  it('emits request-next-rung on click', async () => {
    const wrapper = mountLadder({
      locale: 'en',
      hints: [threeRungs[0]],
      hintsRemaining: 3,
    })
    await wrapper.find('[data-testid="hint-ladder-request-more"]').trigger('click')
    expect(wrapper.emitted('request-next-rung')).toHaveLength(1)
  })

  it('renders math inside <bdi dir="ltr"> for RTL safety', () => {
    const wrapper = mountLadder({
      locale: 'ar',
      hints: [{ hintLevel: 1, hintText: 'x² − 5x + 6 = 0' }],
    })
    // Use selector rather than string match — Vue's scoped CSS injects
    // `data-v-*=""` attributes between the tag name and our `dir` attr,
    // so a literal '<bdi dir="ltr"' contains() check is brittle.
    const bdi = wrapper.find('bdi[dir="ltr"]')
    expect(bdi.exists()).toBe(true)
    expect(bdi.text()).toContain('x² − 5x + 6 = 0')
  })

  it('empty hints array renders no rungs but still exposes the ladder region', () => {
    const wrapper = mountLadder({ locale: 'en', hints: [], hintsRemaining: 3 })
    expect(wrapper.findAll('[data-testid^="hint-rung-"]').length).toBe(0)
    expect(wrapper.find('[data-testid="hint-ladder"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="hint-ladder-request-more"]').exists()).toBe(true)
  })
})
