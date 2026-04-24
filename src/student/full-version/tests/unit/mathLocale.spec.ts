/**
 * PRR-031 / PRR-032 — unit tests for math-locale helpers.
 *
 * Validates:
 *   - Numeral preference inference: ar → eastern, he/en → western.
 *   - toEasternNumerals converts digit runs but preserves HTML structure
 *     (class names with digits, style units like "1em" kept intact).
 *   - buildAriaLabel echoes Hebrew and Arabic vocabulary for `\frac{x+1}{2}`
 *     (the task-body canonical assertion).
 */
import { describe, it, expect } from 'vitest'
import {
  buildAriaLabel,
  inferNumeralsPreference,
  toEasternNumerals,
  toEasternNumeralsText,
} from '@/utils/mathLocale'

describe('inferNumeralsPreference', () => {
  it('returns eastern for Arabic locale', () => {
    expect(inferNumeralsPreference('ar')).toBe('eastern')
  })
  it('returns western for Hebrew', () => {
    expect(inferNumeralsPreference('he')).toBe('western')
  })
  it('returns western for English', () => {
    expect(inferNumeralsPreference('en')).toBe('western')
  })
})

describe('toEasternNumeralsText', () => {
  it('maps all digit positions', () => {
    expect(toEasternNumeralsText('0123456789')).toBe('٠١٢٣٤٥٦٧٨٩')
  })
  it('is a no-op on non-digits', () => {
    expect(toEasternNumeralsText('abc xyz')).toBe('abc xyz')
  })
  it('converts embedded digits only', () => {
    expect(toEasternNumeralsText('x + 42 = y')).toBe('x + ٤٢ = y')
  })
})

describe('toEasternNumerals (HTML-safe)', () => {
  it('converts digits inside text nodes', () => {
    const html = '<span class="x">2</span>'
    expect(toEasternNumerals(html)).toBe('<span class="x">٢</span>')
  })
  it('preserves digits inside tags (class names and numeric attrs)', () => {
    const html = '<span class="base3" data-n="5">2</span>'
    expect(toEasternNumerals(html)).toBe('<span class="base3" data-n="5">٢</span>')
  })
  it('handles empty input', () => {
    expect(toEasternNumerals('')).toBe('')
  })
  it('converts across sibling text nodes', () => {
    const html = '<b>2</b> plus <b>3</b>'
    expect(toEasternNumerals(html)).toBe('<b>٢</b> plus <b>٣</b>')
  })
})

describe('buildAriaLabel', () => {
  it('Hebrew fraction uses Hebrew vocabulary', () => {
    const label = buildAriaLabel('\\frac{x+1}{2}', 'he')
    expect(label).not.toContain('over')
    expect(label).not.toContain('plus')
    expect(label).not.toContain('fraction')
    expect(label).toContain('השבר')
    expect(label).toContain('ועוד')
    expect(label).toContain('חלקי')
    expect(label).toContain('סוף השבר')
  })

  it('Arabic fraction uses Arabic vocabulary', () => {
    const label = buildAriaLabel('\\frac{x+1}{2}', 'ar')
    expect(label).not.toContain('over')
    expect(label).not.toContain('plus')
    expect(label).toContain('الكسر')
    expect(label).toContain('على')
    expect(label).toContain('زائد')
    expect(label).toContain('نهاية الكسر')
  })

  it('English squared special case', () => {
    expect(buildAriaLabel('x^2', 'en')).toBe('x squared')
  })

  it('English cubed special case', () => {
    expect(buildAriaLabel('x^3', 'en')).toBe('x cubed')
  })

  it('Arabic sin uses جا (Arab-sector convention)', () => {
    expect(buildAriaLabel('\\sin(x)', 'ar')).toContain('جا')
  })

  it('Arabic cos uses جتا', () => {
    expect(buildAriaLabel('\\cos(x)', 'ar')).toContain('جتا')
  })

  it('English infinity', () => {
    expect(buildAriaLabel('\\infty', 'en')).toBe('infinity')
  })

  it('empty input returns empty', () => {
    expect(buildAriaLabel('', 'en')).toBe('')
  })

  it('handles \\Delta vs \\delta separately', () => {
    const upper = buildAriaLabel('\\Delta x', 'ar')
    const lower = buildAriaLabel('\\delta x', 'ar')
    expect(upper).toContain('دلتا كبيرة')
    expect(lower).toContain('دلتا')
    expect(lower).not.toContain('كبيرة')
  })

  it('unbraced caret only eats one atom', () => {
    // x^2+1 should be "x squared plus 1", not "x to the power of 2+1"
    expect(buildAriaLabel('x^2+1', 'en')).toBe('x squared plus 1')
  })
})
