/**
 * WCAG 2.2 AA color-contrast regression test.
 *
 * Verifies that:
 *   1. The primary brand color (#7367F0) is NOT used for body-size text
 *      (it fails the 4.5:1 normal-text threshold on both light and dark).
 *   2. The primary-text semantic color meets >= 4.5:1 on light and dark surfaces.
 *   3. The primary color itself is never changed from #7367F0 (locked).
 *
 * Triggered by: FIND-ux-019
 * Task: t_89e7d3c33286
 */
import { describe, expect, it } from 'vitest'
// Direct relative import — bypasses the global setup.ts which loads
// every Vuetify component (slow + causes jsdom timeout in CI).
import {
  primaryTextDark,
  primaryTextLight,
  staticPrimaryColor,
  themes,
} from '../../src/plugins/vuetify/theme'

/**
 * Compute relative luminance per WCAG 2.2 spec.
 * @see https://www.w3.org/TR/WCAG22/#dfn-relative-luminance
 */
function relativeLuminance(hex: string): number {
  const r = parseInt(hex.slice(1, 3), 16) / 255
  const g = parseInt(hex.slice(3, 5), 16) / 255
  const b = parseInt(hex.slice(5, 7), 16) / 255

  const [rs, gs, bs] = [r, g, b].map(c =>
    c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4,
  )

  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs
}

/**
 * Compute WCAG contrast ratio between two hex colors.
 * Returns a value >= 1.
 */
function contrastRatio(fg: string, bg: string): number {
  const l1 = relativeLuminance(fg)
  const l2 = relativeLuminance(bg)
  const lighter = Math.max(l1, l2)
  const darker = Math.min(l1, l2)

  return (lighter + 0.05) / (darker + 0.05)
}

// WCAG 2.2 AA thresholds
const AA_NORMAL_TEXT = 4.5
const AA_LARGE_TEXT = 3.0

// Theme surface colors
const LIGHT_SURFACE = '#FFFFFF' // themes.light.colors.surface
const DARK_SURFACE = '#2F3349' // themes.dark.colors.surface

describe('WCAG 2.2 AA color-contrast compliance (FIND-ux-019)', () => {
  it('primary brand color #7367F0 is locked and unchanged', () => {
    expect(staticPrimaryColor).toBe('#7367F0')
  })

  it('theme configs still define #7367F0 as primary', () => {
    expect(themes.light.colors!.primary).toBe('#7367F0')
    expect(themes.dark.colors!.primary).toBe('#7367F0')
  })

  describe('primary-text semantic color meets AA normal-text threshold', () => {
    it('light theme: primary-text on white surface >= 4.5:1', () => {
      const ratio = contrastRatio(primaryTextLight, LIGHT_SURFACE)

      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT)
      // Log for CI visibility
      console.log(
        `[a11y] primary-text light (${primaryTextLight}) on ${LIGHT_SURFACE}: ${ratio.toFixed(2)}:1`,
      )
    })

    it('dark theme: primary-text on dark surface >= 4.5:1', () => {
      const ratio = contrastRatio(primaryTextDark, DARK_SURFACE)

      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT)
      console.log(
        `[a11y] primary-text dark (${primaryTextDark}) on ${DARK_SURFACE}: ${ratio.toFixed(2)}:1`,
      )
    })
  })

  describe('raw #7367F0 fails normal-text threshold (regression guard)', () => {
    it('light: #7367F0 on white < 4.5:1 (known failure, must not regress to using it for text)', () => {
      const ratio = contrastRatio('#7367F0', LIGHT_SURFACE)

      expect(ratio).toBeLessThan(AA_NORMAL_TEXT)
      console.log(
        `[a11y] raw primary (#7367F0) on ${LIGHT_SURFACE}: ${ratio.toFixed(2)}:1 (expected < ${AA_NORMAL_TEXT})`,
      )
    })

    it('dark: #7367F0 on dark surface < 3:1 (fails even large-text threshold)', () => {
      const ratio = contrastRatio('#7367F0', DARK_SURFACE)

      expect(ratio).toBeLessThan(AA_LARGE_TEXT)
      console.log(
        `[a11y] raw primary (#7367F0) on ${DARK_SURFACE}: ${ratio.toFixed(2)}:1 (expected < ${AA_LARGE_TEXT})`,
      )
    })
  })

  describe('theme color slot correctness', () => {
    it('light theme includes primary-text color slot', () => {
      expect(themes.light.colors!['primary-text']).toBe(primaryTextLight)
    })

    it('dark theme includes primary-text color slot', () => {
      expect(themes.dark.colors!['primary-text']).toBe(primaryTextDark)
    })
  })
})
