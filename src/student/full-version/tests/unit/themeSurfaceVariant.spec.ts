/**
 * Theme regression: `surface-variant` and `on-surface-variant` must be
 * defined in both light and dark blocks so components using
 * `color="surface-variant"` (BossBattleTile, _timeline.scss) resolve to
 * a token that meets WCAG 2.1 AA contrast against the on-color.
 *
 * Bug history: until 2026-04-27 the token was undefined and Vuetify's M3
 * fallback rendered a near-charcoal background under the light theme.
 */
import { describe, expect, it } from 'vitest'
import { themes } from '@/plugins/vuetify/theme'

function luminance(hex: string): number {
  const cleaned = hex.replace('#', '')
  const channels = [cleaned.slice(0, 2), cleaned.slice(2, 4), cleaned.slice(4, 6)]
  const linear = channels.map((c) => {
    const value = Number.parseInt(c, 16) / 255
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
}

function ratio(a: string, b: string): number {
  const la = luminance(a)
  const lb = luminance(b)
  const [hi, lo] = la > lb ? [la, lb] : [lb, la]
  return (hi + 0.05) / (lo + 0.05)
}

describe('Vuetify theme — surface-variant contrast', () => {
  it('defines surface-variant pair in light theme', () => {
    const c = themes.light.colors!
    expect(c['surface-variant']).toBeDefined()
    expect(c['on-surface-variant']).toBeDefined()
  })

  it('defines surface-variant pair in dark theme', () => {
    const c = themes.dark.colors!
    expect(c['surface-variant']).toBeDefined()
    expect(c['on-surface-variant']).toBeDefined()
  })

  it('light pair meets WCAG 2.1 AA (>=4.5:1) for body text', () => {
    const c = themes.light.colors!
    expect(ratio(c['surface-variant'] as string, c['on-surface-variant'] as string)).toBeGreaterThanOrEqual(4.5)
  })

  it('dark pair meets WCAG 2.1 AA (>=4.5:1) for body text', () => {
    const c = themes.dark.colors!
    expect(ratio(c['surface-variant'] as string, c['on-surface-variant'] as string)).toBeGreaterThanOrEqual(4.5)
  })
})
