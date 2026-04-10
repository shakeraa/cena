// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'

import * as directives from 'vuetify/directives'
import { beforeEach, vi } from 'vitest'
import { config } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createI18n } from 'vue-i18n'
import { themes } from '@/plugins/vuetify/theme'

const vuetify = createVuetify({
  components,
  directives,
  theme: {
    defaultTheme: 'light',
    themes,
  },
})

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: {
    en: {
      common: { save: 'Save', cancel: 'Cancel', copy: 'Copy' },
      error: {
        serverError: 'Something went wrong',
        tryAgain: 'Try again',
        reportToSupport: 'Report',
        errorCode: 'Error code',
      },
      kpi: { trendUp: 'Trending up', trendDown: 'Trending down', trendFlat: 'No change' },
      language: { switchLanguage: 'Switch language' },
      empty: { noSessions: 'No sessions yet' },
      auth: {
        email: 'Email',
        password: 'Password',
        displayName: 'Display name',
        emailPlaceholder: 'Enter your email',
        passwordPlaceholder: 'Your password',
        displayNamePlaceholder: 'Display name',
        signInCta: 'Sign in',
        signUpCta: 'Create account',
        emailRequired: 'Email is required.',
        emailInvalid: 'Enter a valid email address.',
        passwordRequired: 'Password is required.',
        passwordMinLength: 'Password must be at least 6 characters.',
        displayNameRequired: 'Display name is required.',
        tryAgainInSeconds: 'Try again in {seconds}s',
      },
      home: {
        greeting: {
          morning: 'Good morning, {name}',
          afternoon: 'Good afternoon, {name}',
          evening: 'Good evening, {name}',
          night: 'Hi {name}',
          fallback: 'there',
          subtitle: 'Here is where you left off.',
        },
        streak: {
          singular: 'day streak',
          plural: 'day streak',
          newBest: 'New best!',
        },
        resume: {
          label: 'Resume session',
          cta: 'Continue',
          minutesAgo: 'Started {count} min ago',
          hoursAgo: 'Started {count} h ago',
          progressAria: 'Session progress: {percent} percent',
        },
        quick: {
          startSession: 'Start Session',
          startSessionSubtitle: 'Jump into a new learning block',
          askTutor: 'Ask the Tutor',
          askTutorSubtitle: 'Get hints and explanations',
          dailyChallenge: 'Daily Challenge',
          dailyChallengeSubtitle: '5 minutes, 5 questions',
          progress: 'View Progress',
          progressSubtitle: 'See how far you have come',
        },
      },
    },
    ar: {
      common: { save: 'حفظ', cancel: 'إلغاء', copy: 'نسخ' },
      language: { switchLanguage: 'تغيير اللغة' },
      empty: { noSessions: 'لا توجد جلسات بعد' },
    },
    he: {
      common: { save: 'שמור', cancel: 'בטל', copy: 'העתק' },
      language: { switchLanguage: 'החלף שפה' },
      empty: { noSessions: 'אין שיעורים עדיין' },
    },
  },
})

config.global.plugins = [vuetify, i18n]

// Stub ResizeObserver used by some Vuetify components
globalThis.ResizeObserver = globalThis.ResizeObserver || class {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// Stub IntersectionObserver (used by some Vuetify overlays)
globalThis.IntersectionObserver = globalThis.IntersectionObserver || class {
  observe() {}
  unobserve() {}
  disconnect() {}
  takeRecords() { return [] }
  root = null
  rootMargin = ''
  thresholds = []
} as any

// Stub visualViewport — Vuetify VOverlay location strategies read it.
if (!(window as any).visualViewport) {
  (window as any).visualViewport = {
    width: 1024,
    height: 768,
    offsetLeft: 0,
    offsetTop: 0,
    scale: 1,
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => true,
  }
}

// Stub matchMedia with a controllable implementation for useReducedMotion tests
type MqlListener = (e: MediaQueryListEvent) => void
interface MockMql {
  matches: boolean
  media: string
  addEventListener: (type: 'change', cb: MqlListener) => void
  removeEventListener: (type: 'change', cb: MqlListener) => void
  dispatchEvent: (matches: boolean) => void
  _listeners: Set<MqlListener>
}

const mqlRegistry = new Map<string, MockMql>()

function createMockMql(query: string, initial = false): MockMql {
  const mql: MockMql = {
    matches: initial,
    media: query,
    _listeners: new Set(),
    addEventListener(_type, cb) {
      this._listeners.add(cb)
    },
    removeEventListener(_type, cb) {
      this._listeners.delete(cb)
    },
    dispatchEvent(matches: boolean) {
      this.matches = matches

      const event = { matches, media: this.media } as unknown as MediaQueryListEvent

      this._listeners.forEach(cb => cb(event))
    },
  }

  return mql
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => {
    if (!mqlRegistry.has(query))
      mqlRegistry.set(query, createMockMql(query, false))

    return mqlRegistry.get(query)!
  }),
})

export function getMockMql(query: string): MockMql {
  if (!mqlRegistry.has(query))
    mqlRegistry.set(query, createMockMql(query, false))

  return mqlRegistry.get(query)!
}

beforeEach(() => {
  mqlRegistry.clear()
})
