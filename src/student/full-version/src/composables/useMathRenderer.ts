// =============================================================================
// PRR-031 / PRR-032 — useMathRenderer
//
// Central math-rendering composable. All KaTeX output on the student-web side
// MUST flow through this composable so the following invariants hold on every
// math surface (QuestionCard, WorkedExamplePanel, StepInput, MasteryMap,
// QuestionFigure labels, and any future surface):
//
//   1. Visual rendering stays LTR even on Arabic / Hebrew RTL pages.
//      Math notation is universally LTR — we emit <bdi dir="ltr"> so the
//      Unicode Bidi Algorithm does not reverse `f(x) = x² − 4x + 3` into
//      `3 + 4x − ²x = f(x)` (user-reported bug, 2026-04-13).
//
//   2. The aria-label spoken by assistive tech uses the student's locale,
//      not the raw LaTeX. KaTeX-rendered `\frac{x+1}{2}` under he locale
//      must announce Hebrew math vocabulary, not "backslash frac brace".
//
//   3. Arabic-numeral preference is honored. When numeralsPreference is
//      'eastern', digits 0-9 in the KaTeX output are post-processed to
//      Eastern Arabic digits ٠-٩. This is purely visual — the underlying
//      LaTeX and any CAS round-trip value are unchanged.
//
// Why not extend the existing inline `katex.renderToString` call sites?
//   - Five-plus components currently inline KaTeX. Centralising the
//     bidi wrapper + aria label + numerals post-processing in one place
//     is the only way to guarantee the invariants across the codebase.
//   - The CI scanner tests/a11y/math-ltr-wrapper.spec.ts already accepts
//     delegation to a known helper as a passing signal; this composable
//     is that helper.
//
// Not in scope (delegated elsewhere):
//   - LaTeX authoring / validation: Cena.Infrastructure.Content
//   - CAS verification: Cena.Actors / sympy sidecar
//   - Server-rendered aria labels: MathAriaLabels.cs (this composable
//     mirrors a subset of that lexicon — the server is authoritative).
// =============================================================================

import { computed, unref, type MaybeRefOrGetter } from 'vue'
import katex from 'katex'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { useOnboardingStore, type SupportedLocale } from '@/stores/onboardingStore'
import {
  buildAriaLabel,
  inferNumeralsPreference,
  toEasternNumerals,
  type NumeralsPreference,
} from '@/utils/mathLocale'

export interface RenderMathOptions {
  /** Display mode math uses block-level rendering (e.g. for standalone eqns). */
  readonly displayMode?: boolean
  /** Override locale. Defaults to active i18n locale. */
  readonly locale?: SupportedLocale
  /** Override numerals preference. Defaults to inferred from locale. */
  readonly numerals?: NumeralsPreference
}

export interface RenderedMath {
  /** HTML string safe to v-html into a <bdi dir="ltr"> element. */
  readonly html: string
  /** Aria label for the wrapping element; locale-aware spoken text. */
  readonly ariaLabel: string
  /**
   * Convenience full-HTML wrapper with <bdi dir="ltr" aria-label="…">.
   * Use this when the consuming component can v-html a single block;
   * prefer `html` + manual bdi when the consumer already manages the
   * wrapper (e.g. QuestionCard has its own bdi styling).
   */
  readonly wrappedHtml: string
}

/**
 * Pure functional renderer — no Vue context required.
 * Exposed for unit tests and for server-side preview pipelines.
 */
export function renderMath(
  latex: string,
  opts: RenderMathOptions & { locale: SupportedLocale },
): RenderedMath {
  if (!latex) {
    return { html: '', ariaLabel: '', wrappedHtml: '' }
  }

  const numerals: NumeralsPreference = opts.numerals ?? inferNumeralsPreference(opts.locale)

  let html: string
  try {
    html = katex.renderToString(latex, {
      throwOnError: false,
      displayMode: opts.displayMode ?? false,
      output: 'html',
    })
  }
  catch {
    // KaTeX failed (shouldn't happen with throwOnError:false but defence in
    // depth); fall back to escaped raw text so we never render raw LaTeX
    // source as HTML (XSS path).
    html = escapeHtml(latex)
  }

  if (numerals === 'eastern' && opts.locale === 'ar')
    html = toEasternNumerals(html)

  const ariaLabel = buildAriaLabel(latex, opts.locale)

  const wrappedHtml = `<bdi dir="ltr" aria-label="${escapeAttribute(ariaLabel)}">${html}</bdi>`

  return { html, ariaLabel, wrappedHtml }
}

/**
 * Reactive composable wrapper. Use inside `<script setup>` blocks.
 *
 * @example
 *   const math = useMathRenderer(() => question.latex)
 *   // math.value.html is v-htmled into <bdi dir="ltr">, aria-label=math.value.ariaLabel
 */
export function useMathRenderer(
  source: MaybeRefOrGetter<string>,
  options?: MaybeRefOrGetter<Omit<RenderMathOptions, 'locale'>>,
) {
  const { locale } = useI18n()
  const onboarding = useOnboardingStore()
  const { effectiveNumerals } = storeToRefs(onboarding)

  const result = computed<RenderedMath>(() => {
    const latex = resolve(source) ?? ''
    const opts = resolve(options) ?? {}
    return renderMath(latex, {
      ...opts,
      locale: locale.value as SupportedLocale,
      // The caller may explicitly override with `numerals`; otherwise
      // follow the student's onboarding preference.
      numerals: opts.numerals ?? effectiveNumerals.value,
    })
  })

  return result
}

// ── Helpers ──────────────────────────────────────────────────────────────

function resolve<T>(value: MaybeRefOrGetter<T> | undefined): T | undefined {
  if (value === undefined)
    return undefined
  if (typeof value === 'function')
    return (value as () => T)()
  return unref(value) as T
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function escapeAttribute(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}
