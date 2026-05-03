/**
 * RDY-026: Unit tests for the client-side Arabic math input normalizer.
 * Mirrors the C# ArabicMathNormalizerTests for consistency.
 */
import { describe, it, expect } from 'vitest'
import { needsNormalization, normalize } from '@/utils/arabicMathNormalizer'

describe('arabicMathNormalizer', () => {
  describe('needsNormalization', () => {
    it('returns false for null/empty/pure-Latin input', () => {
      expect(needsNormalization('')).toBe(false)
      expect(needsNormalization('x + y = z')).toBe(false)
      expect(needsNormalization('sin(x)')).toBe(false)
      expect(needsNormalization('3.14')).toBe(false)
    })

    it('returns true for Arabic letters', () => {
      expect(needsNormalization('س')).toBe(true)
      expect(needsNormalization('x + س')).toBe(true)
    })

    it('returns true for Eastern Arabic digits', () => {
      expect(needsNormalization('٣٫١٤')).toBe(true)
    })

    it('returns true for Arabic operators', () => {
      expect(needsNormalization('2×3')).toBe(true)
      expect(needsNormalization('6÷2')).toBe(true)
    })
  })

  describe('normalize', () => {
    it('passes through pure Latin input unchanged', () => {
      expect(normalize('x + y = z')).toBe('x + y = z')
      expect(normalize('sin(x)')).toBe('sin(x)')
    })

    it('maps Arabic variable letters to Latin', () => {
      expect(normalize('س')).toBe('x')
      expect(normalize('ص')).toBe('y')
      expect(normalize('ع')).toBe('z')
      expect(normalize('ن')).toBe('n')
      expect(normalize('م')).toBe('m')
    })

    it('normalizes a complete Arabic expression', () => {
      expect(normalize('س + ص = ع')).toBe('x + y = z')
    })

    it('maps Eastern Arabic digits to Western', () => {
      expect(normalize('٠١٢٣٤٥٦٧٨٩')).toBe('0123456789')
      expect(normalize('٣س + ٥')).toBe('3x + 5')
    })

    it('maps Arabic operators', () => {
      expect(normalize('٢×٣')).toBe('2*3')
      expect(normalize('٦÷٢')).toBe('6/2')
      expect(normalize('٥−٣')).toBe('5-3')
    })

    it('maps Arabic decimal separator', () => {
      expect(normalize('٣٫١٤')).toBe('3.14')
    })

    it('normalizes multi-character math terms (greedy)', () => {
      expect(normalize('جذر(س)')).toBe('sqrt(x)')
      expect(normalize('جيب(س)')).toBe('sin(x)')
      expect(normalize('جتا(س)')).toBe('cos(x)')
      expect(normalize('ظل(س)')).toBe('tan(x)')
      expect(normalize('لو(س)')).toBe('log(x)')
      expect(normalize('لن(س)')).toBe('ln(x)')
    })

    it('handles mixed Arabic + Latin expressions', () => {
      expect(normalize('sin(س)')).toBe('sin(x)')
      expect(normalize('٢x + ٣')).toBe('2x + 3')
      expect(normalize('f(س) = س² + ٣')).toBe('f(x) = x² + 3')
    })

    it('handles empty and null-ish input', () => {
      expect(normalize('')).toBe('')
      expect(normalize('   ')).toBe('   ')
    })

    it('preserves LaTeX operators and superscripts', () => {
      expect(normalize('س² + ٣س')).toBe('x² + 3x')
    })

    it('handles Arabic parentheses and brackets', () => {
      expect(normalize('(س + ص)²')).toBe('(x + y)²')
    })

    it('normalizes complex expression with multiple terms', () => {
      expect(normalize('جذر(س² + ص²)')).toBe('sqrt(x² + y²)')
    })
  })
})
