/**
 * Regression test for FIND-ux-028: Notifications bell badge labeled "תג"
 *
 * Vuetify's VBadge ships a default aria-label ("Badge") that gets translated
 * to "תג" in Hebrew via Vuetify's internal locale system. This test verifies
 * at the source level that:
 *
 *   1. The Notifications.vue VBadge has aria-label="" to suppress the default.
 *   2. The UserProfile.vue VBadge (green online dot) also has aria-label="".
 *   3. The nav.notificationsBell i18n key exists in en, ar, and he locales.
 *   4. The Notifications bell button uses the i18n key for its aria-label.
 *
 * This is a static-analysis test — it reads the source files directly rather
 * than mounting components, which avoids the slow Vuetify component tree load.
 *
 * Task: t_e74d3e4c9ee0
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC_ROOT = resolve(__dirname, '../../src')

function readSrc(relPath: string): string {
  return readFileSync(resolve(SRC_ROOT, relPath), 'utf-8')
}

function readLocale(lang: string): Record<string, unknown> {
  const raw = readFileSync(
    resolve(SRC_ROOT, `plugins/i18n/locales/${lang}.json`),
    'utf-8',
  )

  return JSON.parse(raw)
}

describe('FIND-ux-028: VBadge aria-label suppression', () => {
  describe('Notifications.vue bell badge', () => {
    const src = readSrc('@core/components/Notifications.vue')

    it('VBadge has aria-label="" to suppress Vuetify default', () => {
      // Extract the VBadge opening tag (which spans multiple lines and may
      // contain `>` inside attribute values like arrow functions). We match
      // from <VBadge up to the closing `>` that is followed by a newline and
      // either content or a child tag.
      const vbadgeBlock = src.match(/<VBadge[\s\S]*?>\s*\n/m)

      expect(vbadgeBlock).not.toBeNull()
      expect(vbadgeBlock![0]).toContain('aria-label=""')
    })

    it('outer button uses i18n key nav.notificationsBell for its aria-label', () => {
      // The IconBtn must reference the i18n key, not a hardcoded string
      expect(src).toMatch(/aria-label=.*\$?t\(['"]nav\.notificationsBell['"]\)/)
    })

    it('template does not contain the Hebrew word "תג" (badge default leak)', () => {
      expect(src).not.toContain('תג')
    })
  })

  describe('UserProfile.vue online-dot badge', () => {
    const src = readSrc('layouts/components/UserProfile.vue')

    it('VBadge has aria-label="" to suppress Vuetify default', () => {
      const vbadgeMatch = src.match(/<VBadge[\s\S]*?>/m)

      expect(vbadgeMatch).not.toBeNull()

      const vbadgeTag = vbadgeMatch![0]

      expect(vbadgeTag).toContain('aria-label=""')
    })
  })

  describe('i18n keys for nav.notificationsBell', () => {
    it.each([
      ['en', 'Notifications'],
      ['ar', 'إشعارات'],
      ['he', 'התראות'],
    ])('locale %s has nav.notificationsBell = "%s"', (lang, expected) => {
      const locale = readLocale(lang)
      const nav = locale.nav as Record<string, string>

      expect(nav).toBeDefined()
      expect(nav.notificationsBell).toBe(expected)
    })

    it('Hebrew nav.notificationsBell is "התראות" (notifications), not "תג" (badge)', () => {
      const locale = readLocale('he')
      const nav = locale.nav as Record<string, string>

      expect(nav.notificationsBell).toBe('התראות')
      expect(nav.notificationsBell).not.toBe('תג')
    })
  })
})
