/**
 * FIND-ux-032 regression test: Settings/Notifications MUST call
 * PATCH /api/me/settings on toggle change (not just localStorage).
 *
 * This spec validates the wiring at the source level — it reads the
 * notifications.vue source and asserts the API composables are used.
 * It also exercises an MSW-style handler shape to ensure the mock
 * endpoint contract works.
 *
 * Does NOT mount the Vue component (avoids the full Vuetify + router
 * plugin chain). Uses only node:fs for source-level assertions.
 */
import * as fs from 'node:fs'
import * as path from 'node:path'
import { beforeEach, describe, expect, it } from 'vitest'

const NOTIF_VUE_PATH = path.resolve(__dirname, '../../src/pages/settings/notifications.vue')
const MSW_HANDLER_PATH = path.resolve(__dirname, '../../src/plugins/fake-api/handlers/student-me/index.ts')

describe('FIND-ux-032: notification settings persistence', () => {
  let notifSrc: string
  let mswSrc: string

  beforeEach(() => {
    notifSrc = fs.readFileSync(NOTIF_VUE_PATH, 'utf-8')
    mswSrc = fs.readFileSync(MSW_HANDLER_PATH, 'utf-8')
  })

  it('notifications.vue imports useApiQuery for GET /api/me/settings', () => {
    expect(notifSrc).toContain('useApiQuery')
    expect(notifSrc).toContain('/api/me/settings')
  })

  it('notifications.vue imports useApiMutation for PATCH /api/me/settings', () => {
    expect(notifSrc).toContain('useApiMutation')
    expect(notifSrc).toContain('\'/api/me/settings\', \'PATCH\'')
  })

  it('notifications.vue calls persistToggle on toggle change, not bare persist()', () => {
    expect(notifSrc).toContain('persistToggle')

    // The old localStorage-only pattern was a standalone `function persist()` that
    // only wrote to localStorage. The new pattern has `cacheToStorage` (cache) and
    // `persistToggle` (API call + cache). Ensure the old-style is gone.
    expect(notifSrc).not.toMatch(/function persist\(\)/)
  })

  it('notifications.vue shows error snackbar on save failure', () => {
    expect(notifSrc).toContain('saveError')
    expect(notifSrc).toContain('notif-save-error-snackbar')
    expect(notifSrc).toContain('t(\'settingsPage.notifications.saveError\')')
  })

  it('notifications.vue emits structured log on error path for re-regression detection', () => {
    expect(notifSrc).toContain('[FIND-ux-032]')
    expect(notifSrc).toContain('notification_pref_save_failed')
  })

  it('notifications.vue reverts toggle on API failure (optimistic UI rollback)', () => {
    // The source must contain the revert logic: flipping the value back
    expect(notifSrc).toContain('!prefs.value[key]')
  })

  it('MSW student-me handler includes GET /api/me/settings', () => {
    expect(mswSrc).toContain('http.get(\'/api/me/settings\'')
  })

  it('MSW student-me handler includes PATCH /api/me/settings', () => {
    expect(mswSrc).toContain('http.patch(\'/api/me/settings\'')
  })

  it('MSW student-me handler returns 204 for PATCH (not 200 with body)', () => {
    // Backend returns 204 No Content. MSW handler should match.
    expect(mswSrc).toContain('status: 204')
  })

  it('i18n saveError key exists in en.json', () => {
    const enPath = path.resolve(__dirname, '../../src/plugins/i18n/locales/en.json')
    const en = JSON.parse(fs.readFileSync(enPath, 'utf-8'))

    expect(en.settingsPage.notifications.saveError).toBeTruthy()
    expect(typeof en.settingsPage.notifications.saveError).toBe('string')
  })

  it('i18n saveError key exists in ar.json', () => {
    const arPath = path.resolve(__dirname, '../../src/plugins/i18n/locales/ar.json')
    const ar = JSON.parse(fs.readFileSync(arPath, 'utf-8'))

    expect(ar.settingsPage.notifications.saveError).toBeTruthy()
  })

  it('i18n saveError key exists in he.json', () => {
    const hePath = path.resolve(__dirname, '../../src/plugins/i18n/locales/he.json')
    const he = JSON.parse(fs.readFileSync(hePath, 'utf-8'))

    expect(he.settingsPage.notifications.saveError).toBeTruthy()
  })
})
