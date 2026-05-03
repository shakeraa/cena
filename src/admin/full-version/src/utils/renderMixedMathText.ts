// =============================================================================
// Cena Admin SPA — mixed-text + math renderer
//
// OCR'd Bagrut content arrives as plain text with LaTeX islands embedded
// inline:
//   "Solve $x^2 - 5x + 6 = 0$ over the reals."
//   "Evaluate \\(\\int_0^1 x^2 dx\\) using the Newton-Leibniz formula."
//   "Display equation:\n$$\\sum_{k=1}^n k = \\frac{n(n+1)}{2}$$"
//
// Curators on the InReview screen need to see the math as math, not as
// raw LaTeX. This module:
//
//   1. Splits a string into alternating prose / math segments by
//      recognising the canonical delimiters: $...$, $$...$$, \(...\),
//      \[...\]. Single-dollar inline + double-dollar block + escape-paren
//      inline + escape-bracket block — the four forms Anthropic / Mathpix
//      OCR consistently emit.
//
//   2. Renders math segments via katex.renderToString. Falls back to the
//      escaped LaTeX source if KaTeX rejects a fragment so we never inject
//      raw user-controlled content as HTML (XSS path).
//
//   3. Wraps each math output in <bdi dir="ltr"> per the math-always-LTR
//      memory rule — the admin panel can be rendered RTL on Hebrew/Arabic
//      locales, and mixed-direction blocks reverse equations visually
//      without an explicit bdi wrapper.
//
//   4. Escapes prose segments to prevent OCR text from injecting HTML.
//
// Deliberately scoped narrower than the student-side useMathRenderer.ts:
// no Eastern numerals (admin curators read both numeral systems and the
// LaTeX source is Latin), no aria-label vocabulary lookup (curator UX,
// not student a11y), no Pinia/i18n coupling (would import student-only
// stores into admin).
// =============================================================================

import katex from 'katex'

interface Segment {
  kind: 'text' | 'math'
  content: string
  /** Display mode (block) vs inline. Block = $$...$$ or \[...\]. */
  display: boolean
}

interface Delimiter {
  open: string
  close: string
  display: boolean
}

// Order matters: longer delimiters must be tried before shorter ones,
// otherwise `$$x$$` would be parsed as two empty-`$$` followed by `x$$`.
const DELIMITERS: readonly Delimiter[] = [
  { open: '$$', close: '$$', display: true },
  { open: '\\[', close: '\\]', display: true },
  { open: '\\(', close: '\\)', display: false },
  { open: '$', close: '$', display: false },
] as const

/**
 * Split a string into prose vs math segments. Pure function, no DOM.
 * Exposed for unit tests.
 */
export function tokenizeMixedMath(input: string): Segment[] {
  if (!input)
    return []

  const segments: Segment[] = []
  let cursor = 0

  while (cursor < input.length) {
    // Find the earliest opening delimiter beyond cursor across all
    // delimiter pairs. indexOf-based scan — no regex needed since
    // delimiters are fixed strings.
    let bestStart = -1
    let bestDelim: Delimiter | null = null

    for (const d of DELIMITERS) {
      const idx = input.indexOf(d.open, cursor)
      if (idx === -1)
        continue
      if (bestStart === -1 || idx < bestStart) {
        bestStart = idx
        bestDelim = d
      }
    }

    if (bestStart === -1 || !bestDelim) {
      segments.push({ kind: 'text', content: input.slice(cursor), display: false })
      break
    }

    if (bestStart > cursor) {
      segments.push({
        kind: 'text',
        content: input.slice(cursor, bestStart),
        display: false,
      })
    }

    const contentStart = bestStart + bestDelim.open.length
    const closeAt = input.indexOf(bestDelim.close, contentStart)

    if (closeAt === -1) {
      // Unclosed math — bail out, treat the rest as prose so we never
      // dump half-LaTeX as if it were valid math.
      segments.push({ kind: 'text', content: input.slice(bestStart), display: false })
      break
    }

    segments.push({
      kind: 'math',
      content: input.slice(contentStart, closeAt),
      display: bestDelim.display,
    })
    cursor = closeAt + bestDelim.close.length
  }

  return segments
}

/**
 * Render a string containing prose + LaTeX math into a single HTML string
 * safe to v-html. Math segments are KaTeX-rendered and wrapped in
 * <bdi dir="ltr">; prose segments are HTML-escaped and wrapped in plain
 * spans.
 *
 * KaTeX failures fall back to the escaped LaTeX source — never raw HTML.
 */
export function renderMixedMathText(input: string | null | undefined): string {
  if (!input)
    return ''

  const segments = tokenizeMixedMath(input)
  return segments.map((seg) => {
    if (seg.kind === 'text') {
      return `<span class="cena-mmt-text">${escapeHtml(seg.content)}</span>`
    }

    let mathHtml: string
    try {
      mathHtml = katex.renderToString(seg.content, {
        throwOnError: false,
        displayMode: seg.display,
        output: 'html',
      })
    }
    catch {
      // Defence in depth — KaTeX with throwOnError:false should never
      // throw here, but if it does we surface the escaped source so the
      // curator sees the LaTeX they need to fix rather than nothing.
      mathHtml = `<code class="cena-mmt-math-error">${escapeHtml(seg.content)}</code>`
    }

    // bdi dir="ltr" per memory rule feedback_math_always_ltr.md — keeps
    // equations visually LTR even when the parent is RTL Hebrew/Arabic.
    return `<bdi dir="ltr" class="cena-mmt-math${seg.display ? ' cena-mmt-math--block' : ''}">${mathHtml}</bdi>`
  }).join('')
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

// =============================================================================
// Figure-anchor splitter (ADR-0062 Phase 1.5, gap B 2026-05-03)
//
// The OCR-cleanup LLM emits markers shaped like [[FIGURE:p<page>]] (or bare
// [[FIGURE]] when no page is known) at positions where the cleaned text
// references a diagram. The SPA needs to interleave inline thumbnails of the
// source PDF anchored to that page rather than dump the literal marker as
// text.
//
// Design choice: split BEFORE the math renderer instead of extending
// renderMixedMathText to emit a placeholder span and post-processing the
// HTML. Two reasons:
//
//   1. Vue's v-html is one-shot. Once the string lands on the DOM there is
//      no clean per-fragment binding for an embed element that needs a
//      reactive pdfBlobUrl ref. v-bind only works on template elements.
//   2. The marker grammar is fixed and orthogonal to the math grammar.
//      Mixing the two state machines invites edge cases (e.g. a marker
//      embedded inside a math segment) that are simpler to handle by
//      tokenising figure markers first.
//
// Returned shape is a flat array of fragments. Each fragment is either:
//   - { kind: 'html', html: string }    — already KaTeX-rendered, v-html'd
//                                          on the consumer side.
//   - { kind: 'figure', page: number | null } — placeholder; consumer renders
//                                                an embed bound to its own
//                                                pdfBlobUrl ref.
//
// Marker grammar:
//   [[FIGURE:p<digits>]]    — page-anchored
//   [[FIGURE]]              — page-unknown
// =============================================================================

export interface RenderedFragment {
  kind: 'html' | 'figure'
  /** Set when kind === 'html'. Already KaTeX-rendered HTML, safe to v-html. */
  html?: string
  /** Set when kind === 'figure'. Page number from the marker; null for bare [[FIGURE]]. */
  page?: number | null
}

const FIGURE_MARKER_RE = /\[\[FIGURE(?::p(\d+))?\]\]/g

/**
 * Split a string into html (KaTeX-rendered) and figure (placeholder)
 * fragments. The consumer (Vue template) iterates the list and renders
 * each kind appropriately.
 *
 * Pure function — no DOM, no Vue reactivity. Exposed for unit tests.
 */
export function renderTextWithFigures(input: string | null | undefined): RenderedFragment[] {
  if (!input)
    return []

  const fragments: RenderedFragment[] = []
  let cursor = 0

  // Reset lastIndex defensively — module-scoped /g regexes carry state
  // across calls, which silently breaks on the second invocation.
  FIGURE_MARKER_RE.lastIndex = 0

  let match: RegExpExecArray | null = null

  // eslint-disable-next-line no-cond-assign
  while ((match = FIGURE_MARKER_RE.exec(input)) !== null) {
    if (match.index > cursor) {
      const chunk = input.slice(cursor, match.index)
      const html = renderMixedMathText(chunk)
      if (html.length > 0)
        fragments.push({ kind: 'html', html })
    }
    const pageStr = match[1]
    const page = pageStr != null ? Number.parseInt(pageStr, 10) : null
    fragments.push({
      kind: 'figure',
      page: Number.isFinite(page) ? page : null,
    })
    cursor = match.index + match[0].length
  }

  if (cursor < input.length) {
    const tail = input.slice(cursor)
    const html = renderMixedMathText(tail)
    if (html.length > 0)
      fragments.push({ kind: 'html', html })
  }

  return fragments
}
