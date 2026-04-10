import type { RouteLocationNormalized, RouteLocationRaw } from 'vue-router'
import type { RouteNamedMap, _RouterTyped } from 'unplugin-vue-router'
import { watch } from 'vue'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { sanitizeReturnTo } from '@/utils/returnTo'
import { getI18n } from '@/plugins/i18n'
import { getVuetify } from '@/plugins/vuetify'

/**
 * STU-W-02 router guards — implements the four cross-cutting concerns the
 * nav shell promises (auth, onboarded, returnTo, title updates) plus the
 * `?embed=1`, `?lang=`, and `?theme=` query-param overrides.
 *
 * The Firebase auth-state resolution is async; the guard awaits
 * `authStore.ready` before making its first routing decision so we never
 * flash the login page for signed-in users on page reload.
 */
export const setupGuards = (router: _RouterTyped<RouteNamedMap & { [key: string]: any }>) => {
  const isPublicRoute = (to: RouteLocationNormalized) => {
    // Public routes don't require auth: the auth pages themselves, the
    // guest trial, the error boundary, the scaffold landing, and every
    // `_dev/*` development tool.
    const publicNames = new Set([
      'login',
      'register',
      'forgot-password',
      '$error',
      'root', // unplugin-vue-router names `pages/index.vue` as `root` — STU-W-01 scaffold landing, STU-W-04 replaces with auth-aware home
    ])

    return (
      (to.name != null && publicNames.has(String(to.name)))
      || to.meta.public === true
      || to.path.startsWith('/_dev/')
    )
  }

  router.beforeEach(async to => {
    const authStore = useAuthStore()
    const meStore = useMeStore()

    // Wait for the initial Firebase auth-state resolution. This only
    // blocks on the very first navigation; subsequent ones pass straight
    // through because `ready` stays true.
    if (!authStore.ready) {
      await new Promise<void>(resolve => {
        const stop = watch(
          () => authStore.ready,
          ready => {
            if (ready) {
              stop()
              resolve()
            }
          },
          { immediate: true },
        )
      })
    }

    // Apply one-shot `?lang=` override before any auth decision so the
    // login page (if redirected) renders in the correct locale.
    if (typeof to.query.lang === 'string') {
      const lang = to.query.lang as 'en' | 'ar' | 'he'
      if (['en', 'ar', 'he'].includes(lang)) {
        const i18n = getI18n()

        i18n.global.locale.value = lang
        if (typeof document !== 'undefined') {
          document.documentElement.lang = lang
          document.documentElement.dir = (lang === 'ar' || lang === 'he') ? 'rtl' : 'ltr'
        }
      }
    }

    if (typeof to.query.theme === 'string') {
      const themeParam = to.query.theme as 'light' | 'dark'
      if (['light', 'dark'].includes(themeParam)) {
        const vuetify = getVuetify()
        if (vuetify)
          vuetify.theme.global.name.value = themeParam
      }
    }

    // Public routes bypass all auth checks.
    if (isPublicRoute(to))
      return true

    // Auth guard: redirect unauthed users to /login with a sanitized returnTo.
    if (to.meta.requiresAuth !== false && !authStore.isSignedIn) {
      const returnTo = sanitizeReturnTo(to.fullPath)

      const redirect: RouteLocationRaw = {
        name: 'login',
        query: returnTo && returnTo !== '/home'
          ? { returnTo }
          : undefined,
      }

      return redirect
    }

    // Onboarded guard: signed-in but not yet onboarded users go to /onboarding.
    // The onboarding route itself is always reachable for signed-in users.
    const isOnboardingRoute = to.name === 'onboarding'
    if (
      authStore.isSignedIn
      && to.meta.requiresOnboarded !== false
      && !meStore.isOnboarded
      && !isOnboardingRoute
    )
      return { name: 'onboarding' }

    // Onboarded users who try to re-visit /onboarding get bounced home.
    if (authStore.isSignedIn && isOnboardingRoute && meStore.isOnboarded)
      return { name: 'home' }

    return true
  })

  // Document title updater. Uses the global i18n instance directly because
  // `useI18n()` only works inside a component setup.
  router.afterEach(to => {
    if (typeof document === 'undefined')
      return

    const titleKey = to.meta.title as string | undefined
    const appName = 'Cena'

    let title = appName
    if (titleKey) {
      const i18n = getI18n()
      const globalI18n = i18n.global as any
      // vue-i18n global.t() returns the key as-is if it can't be resolved,
      // which is fine for our fallback. Wrap in try/catch in case the
      // messages aren't loaded yet (first navigation race).
      let localized = titleKey
      try {
        const tResult = typeof globalI18n.t === 'function' ? globalI18n.t(titleKey) : titleKey
        if (typeof tResult === 'string' && tResult.length > 0)
          localized = tResult
      }
      catch {
        // keep fallback
      }

      title = `${localized} · ${appName}`
    }

    document.title = title
  })
}
