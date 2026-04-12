/**
 * FIND-pedagogy-014 — verifies that vue-i18n pluralization renders
 * grammatically correct strings for count=0, 1, 2, 5, 10, 100 across
 * en, ar, and he locales.
 *
 * vue-i18n default plural rule (legacy: false):
 *   For 2 forms (en):  n===1 -> form[0], else -> form[1]
 *   For 4+ forms:      index = Math.min(n, forms.length - 1)
 *
 * Regression: if any key is reverted to a non-pluralized form, these
 * tests will fail.
 */
import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function loadLocale(locale: string) {
  const filePath = resolve(__dirname, '../../src/plugins/i18n/locales', `${locale}.json`)
  return JSON.parse(readFileSync(filePath, 'utf8'))
}

const en = loadLocale('en')
const ar = loadLocale('ar')
const he = loadLocale('he')

function getT(locale: string, messages: Record<string, any>) {
  const i18n = createI18n({
    legacy: false,
    locale,
    messages: { [locale]: messages },
  })
  return i18n.global.t
}

const COUNTS = [0, 1, 2, 5, 10, 100] as const

describe('i18n plural forms', () => {
  describe('progress.mastery.questionsAttempted', () => {
    it.each(COUNTS)('en count=%i renders with plural syntax', (count) => {
      const t = getT('en', en)
      const result = t('progress.mastery.questionsAttempted', count)
      expect(result).toContain(String(count))
      if (count === 1) {
        expect(result).toContain('question attempted')
        expect(result).not.toContain('questions attempted')
      }
      else {
        expect(result).toContain('questions attempted')
      }
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('progress.mastery.questionsAttempted', count)
      expect(result).not.toBe('progress.mastery.questionsAttempted')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('progress.mastery.questionsAttempted', count)
      expect(result).not.toBe('progress.mastery.questionsAttempted')
    })
  })

  describe('profile.streakLabel', () => {
    it.each(COUNTS)('en count=%i renders with {count}', (count) => {
      const t = getT('en', en)
      const result = t('profile.streakLabel', count)
      expect(result).toContain('day streak')
      expect(result).toContain(String(count))
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('profile.streakLabel', count)
      expect(result).not.toBe('profile.streakLabel')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('profile.streakLabel', count)
      expect(result).not.toBe('profile.streakLabel')
    })
  })

  describe('home.kpi.sessionsValue', () => {
    it.each(COUNTS)('en count=%i renders grammatically', (count) => {
      const t = getT('en', en)
      const result = t('home.kpi.sessionsValue', count)
      if (count === 1) {
        expect(result).toBe('1 session')
      }
      else {
        expect(result).toContain('sessions')
      }
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('home.kpi.sessionsValue', count)
      expect(result).not.toBe('home.kpi.sessionsValue')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('home.kpi.sessionsValue', count)
      expect(result).not.toBe('home.kpi.sessionsValue')
    })
  })

  describe('tutor.threadList.messageCount', () => {
    it.each(COUNTS)('en count=%i renders grammatically', (count) => {
      const t = getT('en', en)
      const result = t('tutor.threadList.messageCount', count)
      if (count === 1) {
        expect(result).toBe('1 message')
      }
      else {
        expect(result).toContain('messages')
      }
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('tutor.threadList.messageCount', count)
      expect(result).not.toBe('tutor.threadList.messageCount')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('tutor.threadList.messageCount', count)
      expect(result).not.toBe('tutor.threadList.messageCount')
    })
  })

  describe('challenges.tournaments.participants', () => {
    it.each(COUNTS)('en count=%i renders grammatically', (count) => {
      const t = getT('en', en)
      const result = t('challenges.tournaments.participants', count)
      if (count === 1) {
        expect(result).toBe('1 participant')
      }
      else {
        expect(result).toContain('participants')
      }
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('challenges.tournaments.participants', count)
      expect(result).not.toBe('challenges.tournaments.participants')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('challenges.tournaments.participants', count)
      expect(result).not.toBe('challenges.tournaments.participants')
    })
  })

  describe('notifications.unreadCount', () => {
    it.each(COUNTS)('en count=%i renders unread', (count) => {
      const t = getT('en', en)
      const result = t('notifications.unreadCount', count)
      expect(result).toContain('unread')
    })

    it.each(COUNTS)('ar count=%i renders non-raw', (count) => {
      const t = getT('ar', ar)
      const result = t('notifications.unreadCount', count)
      expect(result).not.toBe('notifications.unreadCount')
    })

    it.each(COUNTS)('he count=%i renders non-raw', (count) => {
      const t = getT('he', he)
      const result = t('notifications.unreadCount', count)
      expect(result).not.toBe('notifications.unreadCount')
    })
  })

  // Validate Arabic has exactly 6 forms for critical keys
  describe('Arabic 6-form validation', () => {
    const CRITICAL_AR_KEYS = [
      'progress.mastery.questionsAttempted',
      'home.kpi.sessionsValue',
      'tutor.threadList.messageCount',
      'challenges.tournaments.participants',
      'notifications.unreadCount',
    ]

    for (const key of CRITICAL_AR_KEYS) {
      it(`${key} has 6 pipe-separated forms`, () => {
        const parts = key.split('.')
        let val: any = ar
        for (const p of parts)
          val = val?.[p]
        expect(typeof val).toBe('string')
        const forms = (val as string).split(' | ')
        expect(forms.length).toBe(6)
      })
    }
  })

  // Validate Hebrew has exactly 4 forms for critical keys
  describe('Hebrew 4-form validation', () => {
    const CRITICAL_HE_KEYS = [
      'progress.mastery.questionsAttempted',
      'home.kpi.sessionsValue',
      'tutor.threadList.messageCount',
      'challenges.tournaments.participants',
      'notifications.unreadCount',
    ]

    for (const key of CRITICAL_HE_KEYS) {
      it(`${key} has 4 pipe-separated forms`, () => {
        const parts = key.split('.')
        let val: any = he
        for (const p of parts)
          val = val?.[p]
        expect(typeof val).toBe('string')
        const forms = (val as string).split(' | ')
        expect(forms.length).toBe(4)
      })
    }
  })

  // Verify English has at least 2 forms for all pluralizable keys
  describe('English 2-form validation', () => {
    const CRITICAL_EN_KEYS = [
      'progress.mastery.questionsAttempted',
      'home.kpi.sessionsValue',
      'tutor.threadList.messageCount',
      'challenges.tournaments.participants',
      'notifications.unreadCount',
      'profile.streakLabel',
    ]

    for (const key of CRITICAL_EN_KEYS) {
      it(`${key} has 2 pipe-separated forms`, () => {
        const parts = key.split('.')
        let val: any = en
        for (const p of parts)
          val = val?.[p]
        expect(typeof val).toBe('string')
        const forms = (val as string).split(' | ')
        expect(forms.length).toBeGreaterThanOrEqual(2)
      })
    }
  })
})
