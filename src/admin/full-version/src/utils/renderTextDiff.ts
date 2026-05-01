// =============================================================================
// Cena Admin SPA — text-diff renderer for OCR-vs-recreated review surface
//
// Curators reviewing a Bagrut item need to see what the recreate stage
// changed vs the raw OCR. Plain side-by-side rendering is the floor —
// real diff highlighting (insertion green, deletion red+strikethrough)
// is the curator's actual review aid.
//
// Uses jsdiff (`diff` package) for word-level diffing. Math-heavy
// segments produce noisy word diffs (e.g. `\frac{1}{2}` tokenises into
// many words), so for math content this module falls back to character-
// level diff which surfaces single-character swaps (`+` vs `-`, `=` vs
// `\neq`) that are the actual recreate-stage failure mode worth
// catching.
//
// Returned HTML strings are safe to v-html: prose segments are
// HTML-escaped before wrapping, and the only markup we emit is our own
// <ins>/<del>/<span> wrappers.
// =============================================================================

import { diffWordsWithSpace, diffChars, type Change } from 'diff'

interface RenderedDiffSide {
  /** HTML string safe to v-html. Same per-segment markup on both sides. */
  readonly html: string
  /** Number of segments that show a change (insertion or deletion). */
  readonly changeCount: number
}

interface RenderedDiff {
  /** OCR-side rendering. Deletions visible (= what the recreate stage removed). */
  readonly ocr: RenderedDiffSide
  /** Recreated-side rendering. Insertions visible (= what the recreate stage added). */
  readonly recreated: RenderedDiffSide
  /** Total number of changed segments across both sides. */
  readonly totalChanges: number
}

/**
 * Compute and render a side-by-side diff between OCR raw text and the
 * recreated-cleanup output for the same question. Returned strings are
 * suitable for v-html.
 */
export function renderTextDiff(ocrText: string, recreatedText: string): RenderedDiff {
  const isMathHeavy = looksMathHeavy(ocrText) || looksMathHeavy(recreatedText)
  const changes: Change[] = isMathHeavy
    ? diffChars(ocrText, recreatedText)
    : diffWordsWithSpace(ocrText, recreatedText)

  // OCR side: render unchanged + removed segments. Removed segments are
  // marked <del>; added (recreate-only) segments are skipped here so the
  // OCR side shows the OCR-as-it-was with strikethroughs over deletions.
  // Recreated side: render unchanged + added segments. Added segments
  // are marked <ins>; removed segments are skipped.
  const ocrParts: string[] = []
  const recParts: string[] = []
  let ocrChanges = 0
  let recChanges = 0

  for (const c of changes) {
    if (c.added) {
      recParts.push(`<ins class="cena-diff-ins">${escapeHtml(c.value)}</ins>`)
      recChanges++
    }
    else if (c.removed) {
      ocrParts.push(`<del class="cena-diff-del">${escapeHtml(c.value)}</del>`)
      ocrChanges++
    }
    else {
      const safe = `<span class="cena-diff-eq">${escapeHtml(c.value)}</span>`
      ocrParts.push(safe)
      recParts.push(safe)
    }
  }

  return {
    ocr: { html: ocrParts.join(''), changeCount: ocrChanges },
    recreated: { html: recParts.join(''), changeCount: recChanges },
    totalChanges: ocrChanges + recChanges,
  }
}

/**
 * Heuristic: does the input contain enough LaTeX-like markup that
 * word-level diffing would be noisy? Threshold tuned empirically — a
 * single `$..$` island in prose stays word-level, but `\frac` /
 * `\begin{...}` / multiple `$$..$$` blocks tip into character-level.
 */
function looksMathHeavy(s: string): boolean {
  if (!s)
    return false
  const hasMacros = /\\(?:frac|sum|int|begin|end|sqrt|left|right)\b/.test(s)
  const dollars = (s.match(/\$+/g) ?? []).length
  return hasMacros || dollars >= 4
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
