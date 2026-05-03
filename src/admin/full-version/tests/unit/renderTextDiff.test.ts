// =============================================================================
// Tests for renderTextDiff — the word-level diff renderer used by
// ItemDetailPanel to compare OCR raw text against the recreated form.
// =============================================================================

import { describe, it, expect } from 'vitest'
import { renderTextDiff } from '@/utils/renderTextDiff'

describe('renderTextDiff', () => {
  it('returns zero changes for identical inputs', () => {
    const out = renderTextDiff('hello world', 'hello world')
    expect(out.totalChanges).toBe(0)
    expect(out.ocr.changeCount).toBe(0)
    expect(out.recreated.changeCount).toBe(0)
    expect(out.ocr.html).not.toContain('cena-diff-ins')
    expect(out.ocr.html).not.toContain('cena-diff-del')
  })

  it('marks insertions on the recreated side, deletions on the OCR side', () => {
    const out = renderTextDiff('solve x', 'Solve the x')
    // OCR loses `solve` (lowercase), gains `Solve` + `the ` on recreated.
    expect(out.recreated.html).toContain('<ins class="cena-diff-ins">')
    expect(out.ocr.html).toContain('<del class="cena-diff-del">')
    expect(out.totalChanges).toBeGreaterThan(0)
  })

  it('escapes HTML in inputs (XSS path closed in both sides)', () => {
    const out = renderTextDiff(
      'malicious <script>alert(1)</script>',
      'malicious <img src=x onerror=alert(1)>',
    )
    // The diff splits content across <span class=eq> / <del> / <ins>
    // wrappers, so the escaped form may not be contiguous. What MUST be
    // true is that the dangerous raw tokens never appear unescaped, and
    // the escaped `&lt;` / `&gt;` markers are present somewhere in the
    // output. Direct containment assertions on the contiguous escaped
    // form fail because of the split — assert the actual security
    // invariant instead.
    // The structural invariant: no unescaped HTML element open-tag
    // appears in the output. The diff engine may split escaped chars
    // into different segments (e.g. `&lt;` in one span, `script>` plain
    // text in the next <del>), and that is safe — the browser parses
    // the surrounding entity-escaped `<` as text, so anything after it
    // is also text content, not a tag. As long as `<TAG ` patterns
    // never appear unescaped, the output is XSS-safe.
    expect(out.ocr.html).not.toMatch(/<script[\s>]/)
    expect(out.ocr.html).not.toMatch(/<img\s/)
    expect(out.recreated.html).not.toMatch(/<script[\s>]/)
    expect(out.recreated.html).not.toMatch(/<img\s/)
    // The escape did happen — `&lt;` markers are present.
    expect(out.ocr.html).toContain('&lt;')
    expect(out.recreated.html).toContain('&lt;')
  })

  it('uses character-level diff when content is math-heavy (LaTeX macros)', () => {
    // Single-char swap inside \frac shouldn't tokenise into "many words".
    // The number of changed segments should be small (single-char edit).
    const out = renderTextDiff('\\frac{x+1}{2}', '\\frac{x-1}{2}')
    expect(out.totalChanges).toBeGreaterThan(0)
    // Char-level diff catches the + → - swap. There should be exactly one
    // ins and one del (the single char swap).
    expect(out.recreated.html.match(/<ins/g)?.length ?? 0).toBe(1)
    expect(out.ocr.html.match(/<del/g)?.length ?? 0).toBe(1)
  })

  it('returns deterministic output for the same inputs', () => {
    const a = renderTextDiff('foo bar baz', 'foo BAR baz')
    const b = renderTextDiff('foo bar baz', 'foo BAR baz')
    expect(a.ocr.html).toBe(b.ocr.html)
    expect(a.recreated.html).toBe(b.recreated.html)
  })

  it('handles empty inputs without crashing', () => {
    const out = renderTextDiff('', '')
    expect(out.totalChanges).toBe(0)
    expect(out.ocr.html).toBe('')
    expect(out.recreated.html).toBe('')
  })

  it('handles empty OCR vs populated recreated as full insertion', () => {
    const out = renderTextDiff('', 'new content here')
    expect(out.recreated.html).toContain('cena-diff-ins')
    expect(out.recreated.changeCount).toBe(1)
    expect(out.ocr.html).toBe('')
  })

  it('preserves Hebrew RTL prose text correctly through the diff', () => {
    const out = renderTextDiff('פתור x = 0', 'פתור x = 1')
    // Hebrew chars must round-trip unmangled.
    expect(out.ocr.html).toContain('פתור')
    expect(out.recreated.html).toContain('פתור')
    // Single character swap visible.
    expect(out.totalChanges).toBeGreaterThan(0)
  })
})
