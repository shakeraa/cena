import type { RouteNamedMap, _RouterTyped } from 'unplugin-vue-router'
import type { RouteLocationNormalized } from 'vue-router'
import { onAuthStateChanged } from 'firebase/auth'
import { firebaseAuth } from '@/plugins/firebase'
import { ability } from '@/plugins/casl/ability'
import type { Actions, CenaRole, Subjects } from '@/plugins/casl/ability'
import { mapRoleToAbilities } from '@/plugins/casl/role-abilities'

/**
 * Wait for Firebase Auth to finish restoring session from IndexedDB.
 * Without this, firebaseAuth.currentUser is null on page refresh
 * until the async initialization completes, causing false redirects to /login.
 */
let _authReady: Promise<void> | null = null
function waitForAuthReady(): Promise<void> {
  if (!_authReady) {
    _authReady = new Promise(resolve => {
      const unsubscribe = onAuthStateChanged(firebaseAuth, user => {
        // Restore abilities from Firebase claims if user is logged in
        if (user) {
          user.getIdTokenResult().then(tokenResult => {
            const claims = tokenResult.claims as Record<string, unknown>
            const role = (claims.role as CenaRole) || 'STUDENT'

            const ADMIN_ROLES: CenaRole[] = ['MODERATOR', 'ADMIN', 'SUPER_ADMIN']
            if (ADMIN_ROLES.includes(role)) {
              const abilities = mapRoleToAbilities(role)

              ability.update(abilities)
              useCookie('userAbilityRules').value = abilities as any
              useCookie('accessToken').value = tokenResult.token
            }
            resolve()
          }).catch(() => resolve())
        }
        else {
          resolve()
        }
        unsubscribe()
      })
    })
  }

  return _authReady
}

/**
 * Check if user can navigate to the route using the shared CASL ability instance.
 * Uses the ability instance directly (not useAbility() which requires component context).
 */
function canNavigateRoute(to: RouteLocationNormalized): boolean {
  const targetRoute = to.matched[to.matched.length - 1]

  // If route has explicit CASL meta, check it
  if (targetRoute?.meta?.action && targetRoute?.meta?.subject)
    return ability.can(targetRoute.meta.action as Actions, targetRoute.meta.subject as Subjects)

  // Routes without CASL meta: allow if user has any abilities (is authenticated with a role)
  if (!targetRoute?.meta?.action && !targetRoute?.meta?.subject)
    return ability.rules.length > 0

  // Fallback: check any matched route
  return to.matched.some(route => {
    if (route.meta.action && route.meta.subject)
      return ability.can(route.meta.action as Actions, route.meta.subject as Subjects)

    return false
  })
}

/**
 * Brand label used as the document.title suffix across every admin tab.
 * FIND-ux-008: a hard-coded 'Vuexy' used to leak through from the
 * vite index.html and the route `meta.title` strings; the afterEach
 * hook below rewrites the tab title on every navigation so new tabs,
 * hard refreshes, and deep-links all stay on-brand.
 */
const ADMIN_APP_NAME = 'Cena Admin'

export const setupGuards = (router: _RouterTyped<RouteNamedMap & { [key: string]: any }>) => {
  router.afterEach(to => {
    if (typeof document === 'undefined')
      return

    // route.meta.title is already used by the Vuexy layout for breadcrumb
    // rendering; we reuse it here so we don't duplicate labels. If a
    // route doesn't set meta.title, fall back to just the brand.
    const pageLabel = typeof to.meta.title === 'string' && to.meta.title.length > 0
      ? to.meta.title
      : null

    document.title = pageLabel ? `${pageLabel} · ${ADMIN_APP_NAME}` : ADMIN_APP_NAME
  })

  router.beforeEach(async to => {
    // Public routes: 404, maintenance, etc.
    if (to.meta.public)
      return

    // Wait for Firebase to restore session from IndexedDB before checking auth
    await waitForAuthReady()

    // Check auth state from Firebase (authoritative after waitForAuthReady)
    const firebaseUser = firebaseAuth.currentUser
    const cookieUser = useCookie('userData').value
    const isLoggedIn = !!(firebaseUser || cookieUser)

    // Unauthenticated-only routes: login, register, forgot-password
    if (to.meta.unauthenticatedOnly) {
      if (isLoggedIn)
        return '/'
      else
        return undefined
    }

    // CASL permission check
    if (!canNavigateRoute(to) && to.matched.length) {
      return isLoggedIn
        ? { name: 'not-authorized' }
        : {
            name: 'login',
            query: {
              ...to.query,
              to: to.fullPath !== '/' ? to.path : undefined,
            },
          }
    }
  })
}
