import type { App } from 'vue'
import { createI18n } from 'vue-i18n'
import { cookieRef } from '@layouts/stores/config'
import { themeConfig } from '@themeConfig'
import { sanitizeLocale } from '@/composables/useAvailableLocales'

const messages = Object.fromEntries(
  Object.entries(
    import.meta.glob<{ default: any }>('./locales/*.json', { eager: true }))
    .map(([key, value]) => [key.slice(10, -5), value.default]),
)

let _i18n: any = null

/**
 * FIND-pedagogy-015: Returns the active i18n locale code ('en' | 'ar' | 'he').
 * Designed for non-Vue callers (utility functions, formatters) that need the
 * current locale but don't have access to a Vue component's setup context.
 *
 * Falls back to 'en' if i18n hasn't been initialized yet.
 */
export function getActiveLocale(): string {
  if (_i18n === null) {
    console.warn('[cena-i18n] getActiveLocale() called before i18n init — defaulting to "en"')

    return 'en'
  }

  return _i18n.global.locale.value ?? 'en'
}

export const getI18n = () => {
  if (_i18n === null) {
    const langCookie = cookieRef('language', themeConfig.app.i18n.defaultLocale)

    // FIND-pedagogy-010: validate the cookie value against the Hebrew gate
    // BEFORE initializing vue-i18n. If someone injected 'he' into the
    // cookie but the build has Hebrew disabled, fall back to 'en' and
    // rewrite the cookie so subsequent loads don't re-trigger.
    const rawLocale = langCookie.value as string
    const safeLocale = sanitizeLocale(rawLocale)

    if (safeLocale !== rawLocale) {
      // Rewrite the cookie to the safe fallback so the gate is enforced
      // persistently — the user won't be re-served Hebrew on next load.
      langCookie.value = safeLocale
    }

    _i18n = createI18n({
      legacy: false,
      locale: safeLocale,
      fallbackLocale: 'en',
      messages,
    })
  }

  return _i18n
}

export default function (app: App) {
  app.use(getI18n())
}
