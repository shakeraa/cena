// =============================================================================
// Tests for renderMixedMathText — the LaTeX-aware mixed-text renderer used
// by ItemDetailPanel to show OCR'd Bagrut content with math rendered as
// math (not raw LaTeX source).
//
// Coverage targets:
//   1. Tokenization — splits prose vs math correctly across all four
//      delimiter forms ($..$, $$..$$, \(..\), \[..\]).
//   2. Empty / null / undefined inputs do not throw and return ''.
//   3. KaTeX failures fall back to escaped LaTeX (XSS path closed).
//   4. Prose containing HTML metacharacters is escaped (XSS path closed).
//   5. Math output is wrapped in <bdi dir="ltr"> per the math-always-LTR
//      memory rule.
//   6. Unclosed math doesn't crash — bails out gracefully.
//   7. Adjacent / nested-looking delimiters round-trip correctly.
// =============================================================================

import { describe, it, expect } from 'vitest'
import { renderMixedMathText, tokenizeMixedMath } from '@/utils/renderMixedMathText'

describe('tokenizeMixedMath', () => {
  it('returns empty list for empty / null / undefined', () => {
    expect(tokenizeMixedMath('')).toEqual([])
    // @ts-expect-error testing runtime safety
    expect(tokenizeMixedMath(null)).toEqual([])
    // @ts-expect-error testing runtime safety
    expect(tokenizeMixedMath(undefined)).toEqual([])
  })

  it('returns single text segment for prose-only input', () => {
    const out = tokenizeMixedMath('Solve the equation over the reals.')
    expect(out).toEqual([
      { kind: 'text', content: 'Solve the equation over the reals.', display: false },
    ])
  })

  it('splits inline $..$ math from surrounding prose', () => {
    const out = tokenizeMixedMath('Solve $x^2 - 5x + 6 = 0$ for x.')
    expect(out).toEqual([
      { kind: 'text', content: 'Solve ', display: false },
      { kind: 'math', content: 'x^2 - 5x + 6 = 0', display: false },
      { kind: 'text', content: ' for x.', display: false },
    ])
  })

  it('handles block $$..$$ as display math', () => {
    const out = tokenizeMixedMath('Equation:\n$$x = \\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}$$\nDone.')
    expect(out).toHaveLength(3)
    expect(out[1].kind).toBe('math')
    expect(out[1].display).toBe(true)
    expect(out[1].content).toBe('x = \\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}')
  })

  it('handles \\(..\\) as inline math', () => {
    const out = tokenizeMixedMath('Compute \\(\\int_0^1 x^2 dx\\) please.')
    expect(out).toHaveLength(3)
    expect(out[1].kind).toBe('math')
    expect(out[1].display).toBe(false)
    expect(out[1].content).toBe('\\int_0^1 x^2 dx')
  })

  it('handles \\[..\\] as block math', () => {
    const out = tokenizeMixedMath('Theorem:\n\\[\\sum_{k=1}^n k = \\frac{n(n+1)}{2}\\]\nQED.')
    expect(out).toHaveLength(3)
    expect(out[1].kind).toBe('math')
    expect(out[1].display).toBe(true)
  })

  it('prefers $$ over $ when both could match (longest delimiter wins)', () => {
    const out = tokenizeMixedMath('a $$x$$ b')
    expect(out[1].kind).toBe('math')
    expect(out[1].display).toBe(true)
    expect(out[1].content).toBe('x')
  })

  it('treats unclosed math as trailing prose (no crash)', () => {
    const out = tokenizeMixedMath('Start of $unclosed math')
    expect(out).toEqual([
      { kind: 'text', content: 'Start of ', display: false },
      { kind: 'text', content: '$unclosed math', display: false },
    ])
  })

  it('round-trips two adjacent inline math islands', () => {
    const out = tokenizeMixedMath('$a$$b$')
    // First $..$ matches `a`, then `$b$` is the next island.
    expect(out.filter(s => s.kind === 'math').map(s => s.content)).toEqual(['a', 'b'])
  })
})

describe('renderMixedMathText', () => {
  it('returns empty string for empty / null / undefined inputs', () => {
    expect(renderMixedMathText('')).toBe('')
    expect(renderMixedMathText(null)).toBe('')
    expect(renderMixedMathText(undefined)).toBe('')
  })

  it('wraps math output in <bdi dir="ltr"> per math-always-LTR rule', () => {
    const html = renderMixedMathText('Result: $x = 3$.')
    expect(html).toContain('<bdi dir="ltr"')
    expect(html).toContain('cena-mmt-math')
  })

  it('uses display block class for $$..$$ math', () => {
    const html = renderMixedMathText('$$x^2$$')
    expect(html).toContain('cena-mmt-math--block')
  })

  it('does NOT use display block class for $..$ inline math', () => {
    const html = renderMixedMathText('$x^2$')
    expect(html).toContain('cena-mmt-math')
    expect(html).not.toContain('cena-mmt-math--block')
  })

  it('escapes HTML metacharacters in prose (XSS path closed)', () => {
    const html = renderMixedMathText('User said <script>alert(1)</script> here.')
    expect(html).not.toContain('<script>')
    expect(html).toContain('&lt;script&gt;')
  })

  it('escapes HTML metacharacters in unclosed-math fallback (XSS path closed)', () => {
    const html = renderMixedMathText('Bad $</script><img src=x onerror=alert(1)>')
    // The unclosed-$ case bails out and treats remainder as prose, so the
    // dangerous tags must be escaped.
    expect(html).not.toContain('<script>')
    expect(html).not.toContain('<img src=x')
    expect(html).toContain('&lt;')
  })

  it('preserves Hebrew RTL prose alongside LTR math (no reversal)', () => {
    const html = renderMixedMathText('פתור $x^2 - 4 = 0$ עבור x ממשי.')
    // Prose retains Hebrew chars unmangled.
    expect(html).toContain('פתור')
    expect(html).toContain('עבור')
    // Math is bdi-wrapped LTR.
    expect(html).toMatch(/<bdi dir="ltr"[^>]*>/)
  })

  it('falls back to escaped LaTeX source on KaTeX rejection (no raw HTML emission)', () => {
    // KaTeX has throwOnError: false so legit invalid LaTeX renders as red
    // markup inside the bdi wrapper rather than throwing — the explicit
    // try/catch fallback is defence-in-depth. Use a clearly invalid input
    // and verify we still don't leak raw user content.
    const html = renderMixedMathText('$<script>alert(1)</script>$')
    expect(html).not.toContain('<script>alert')
  })
})
