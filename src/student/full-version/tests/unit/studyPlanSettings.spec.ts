/**
 * PRR-227: /settings/study-plan edit UI MUST wire the three actions
 * (add, edit, archive) to the existing wave1a PRR-218 endpoints.
 *
 * Source-level spec — asserts the Vue file wires the correct endpoints +
 * mutations without mounting the full Vuetify/router stack (same pattern
 * as tests/unit/notificationSettings.spec.ts).
 */
import * as fs from 'node:fs'
import * as path from 'node:path'
import { beforeEach, describe, expect, it } from 'vitest'

const STUDY_PLAN_VUE_PATH = path.resolve(
  __dirname, '../../src/pages/settings/study-plan.vue')
const SETTINGS_INDEX_PATH = path.resolve(
  __dirname, '../../src/pages/settings/index.vue')

describe('PRR-227: /settings/study-plan edit UI', () => {
  let vueSrc: string

  beforeEach(() => {
    vueSrc = fs.readFileSync(STUDY_PLAN_VUE_PATH, 'utf-8')
  })

  it('study-plan.vue loads targets via GET /api/me/exam-targets?includeArchived=true', () => {
    expect(vueSrc).toContain('useApiQuery')
    expect(vueSrc).toContain('/api/me/exam-targets?includeArchived=true')
  })

  it('addTarget wires POST /api/me/exam-targets', () => {
    expect(vueSrc).toContain('useApiMutation')
    // The POST path must appear as a string literal.
    expect(vueSrc).toMatch(/['"]\/api\/me\/exam-targets['"],\s*['"]POST['"]/)
    expect(vueSrc).toContain('function addTarget')
  })

  it('editTarget wires PUT /api/me/exam-targets/{id}', () => {
    expect(vueSrc).toContain('function editTarget')
    // PUT method on the per-id path.
    expect(vueSrc).toContain('\'PUT\'')
    expect(vueSrc).toContain('/api/me/exam-targets/${encodeURIComponent')
  })

  it('archiveTarget wires POST /api/me/exam-targets/{id}/archive', () => {
    expect(vueSrc).toContain('function archiveTarget')
    expect(vueSrc).toMatch(/\/api\/me\/exam-targets\/\$\{encodeURIComponent\([^)]+\)\}\/archive/)
  })

  it('study-plan.vue surfaces an archive confirm dialog (no one-click destructive)', () => {
    expect(vueSrc).toContain('archiveConfirmFor')
    expect(vueSrc).toContain('data-testid="archive-confirm-dialog"')
    expect(vueSrc).toContain('promptArchive')
    expect(vueSrc).toContain('confirmArchive')
  })

  it('study-plan.vue renders per-target ParentVisibility label (PRR-230)', () => {
    // The UI shows whether the target is visible to parent or hidden.
    expect(vueSrc).toContain('parentVisibility')
    expect(vueSrc).toContain('settingsPage.studyPlan.parentVisibility.visible')
    expect(vueSrc).toContain('settingsPage.studyPlan.parentVisibility.hidden')
  })

  it('study-plan.vue shows neutral archive copy — no streak / celebration (shipgate)', () => {
    // The shipgate scanner bans these terms; source-level sanity check.
    const banned = ['streak', 'celebrate', 'congratulations', 'rewarded']
    for (const term of banned)
      expect(vueSrc.toLowerCase()).not.toContain(term)
  })

  it('study-plan.vue wraps exam codes + tracks in <bdi dir="ltr"> for RTL pages', () => {
    // Memory feedback_math_always_ltr applies to identifiers too.
    expect(vueSrc).toContain('<bdi dir="ltr">{{ target.examCode }}</bdi>')
  })

  it('study-plan.vue warns when total weekly hours exceed 40', () => {
    expect(vueSrc).toContain('overLimit')
    expect(vueSrc).toContain('settingsPage.studyPlan.overLimitWarning')
    expect(vueSrc).toContain('data-testid="over-limit-warning"')
  })

  it('settings index exposes a study-plan link', () => {
    const indexSrc = fs.readFileSync(SETTINGS_INDEX_PATH, 'utf-8')

    expect(indexSrc).toContain('/settings/study-plan')
    expect(indexSrc).toContain('settingsPage.studyPlan.title')
  })

  // ---- i18n completeness (all three locales have the new keys) ----

  it('i18n studyPlan block exists in en.json', () => {
    const enPath = path.resolve(__dirname, '../../src/plugins/i18n/locales/en.json')
    const en = JSON.parse(fs.readFileSync(enPath, 'utf-8'))

    expect(en.settingsPage.studyPlan).toBeTruthy()
    expect(en.settingsPage.studyPlan.title).toBeTruthy()
    expect(en.settingsPage.studyPlan.addTarget).toBeTruthy()
    expect(en.settingsPage.studyPlan.archiveTarget).toBeTruthy()
    expect(en.settingsPage.studyPlan.editTarget).toBeTruthy()
    expect(en.settingsPage.studyPlan.parentVisibility.visible).toBeTruthy()
  })

  it('i18n studyPlan block exists in ar.json', () => {
    const arPath = path.resolve(__dirname, '../../src/plugins/i18n/locales/ar.json')
    const ar = JSON.parse(fs.readFileSync(arPath, 'utf-8'))

    expect(ar.settingsPage.studyPlan).toBeTruthy()
    expect(ar.settingsPage.studyPlan.title).toBeTruthy()
    expect(ar.settingsPage.studyPlan.addTarget).toBeTruthy()
  })

  it('i18n studyPlan block exists in he.json', () => {
    const hePath = path.resolve(__dirname, '../../src/plugins/i18n/locales/he.json')
    const he = JSON.parse(fs.readFileSync(hePath, 'utf-8'))

    expect(he.settingsPage.studyPlan).toBeTruthy()
    expect(he.settingsPage.studyPlan.title).toBeTruthy()
    expect(he.settingsPage.studyPlan.addTarget).toBeTruthy()
  })
})
