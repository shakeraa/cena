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
