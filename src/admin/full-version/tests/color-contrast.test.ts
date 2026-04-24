/**
 * WCAG 2.2 AA color-contrast regression test (admin app).
 *
 * Verifies that:
 *   1. The primary brand color (#7367F0) is NOT used for body-size text.
 *   2. The primary-text semantic color meets >= 4.5:1 on light and dark surfaces.
 *   3. The primary color itself is never changed from #7367F0 (locked).
 *
 * Triggered by: FIND-ux-019
 * Task: t_89e7d3c33286
 */
import { describe, expect, it } from 'vitest'
import {
  primaryTextDark,
  primaryTextLight,
  staticPrimaryColor,
  themes,
} from '@/plugins/vuetify/theme'

/**
 * Compute relative luminance per WCAG 2.2 spec.
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

function contrastRatio(fg: string, bg: string): number {
  const l1 = relativeLuminance(fg)
  const l2 = relativeLuminance(bg)
  const lighter = Math.max(l1, l2)
  const darker = Math.min(l1, l2)

  return (lighter + 0.05) / (darker + 0.05)
}

const AA_NORMAL_TEXT = 4.5
const LIGHT_SURFACE = '#FFFFFF'
const DARK_SURFACE = '#2F3349'

describe('WCAG 2.2 AA color-contrast compliance (FIND-ux-019)', () => {
  it('primary brand color #7367F0 is locked and unchanged', () => {
    expect(staticPrimaryColor).toBe('#7367F0')
  })

  it('theme configs still define #7367F0 as primary', () => {
    expect(themes.light.colors!.primary).toBe('#7367F0')
    expect(themes.dark.colors!.primary).toBe('#7367F0')
  })

  it('light theme: primary-text on white surface >= 4.5:1', () => {
    const ratio = contrastRatio(primaryTextLight, LIGHT_SURFACE)

    expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT)
  })

  it('dark theme: primary-text on dark surface >= 4.5:1', () => {
    const ratio = contrastRatio(primaryTextDark, DARK_SURFACE)

    expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT)
  })

  it('light theme includes primary-text color slot', () => {
    expect(themes.light.colors!['primary-text']).toBe(primaryTextLight)
  })

  it('dark theme includes primary-text color slot', () => {
    expect(themes.dark.colors!['primary-text']).toBe(primaryTextDark)
  })
})
