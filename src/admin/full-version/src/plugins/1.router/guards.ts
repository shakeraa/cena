import type { RouteNamedMap, _RouterTyped } from 'unplugin-vue-router'
import type { RouteLocationNormalized } from 'vue-router'
import { firebaseAuth } from '@/plugins/firebase'
import { ability } from '@/plugins/casl/ability'

/**
 * Check if user can navigate to the route using the shared CASL ability instance.
 * Uses the ability instance directly (not useAbility() which requires component context).
 */
function canNavigateRoute(to: RouteLocationNormalized): boolean {
  const targetRoute = to.matched[to.matched.length - 1]

  // If route has explicit CASL meta, check it
  if (targetRoute?.meta?.action && targetRoute?.meta?.subject)
    return ability.can(targetRoute.meta.action as string, targetRoute.meta.subject as string)

  // Routes without CASL meta: allow if user has any abilities (is authenticated with a role)
  if (!targetRoute?.meta?.action && !targetRoute?.meta?.subject)
    return ability.rules.length > 0

  // Fallback: check any matched route
  return to.matched.some(route => {
    if (route.meta.action && route.meta.subject)
      return ability.can(route.meta.action as string, route.meta.subject as string)
    return false
  })
}

export const setupGuards = (router: _RouterTyped<RouteNamedMap & { [key: string]: any }>) => {
  router.beforeEach(to => {
    // Public routes: 404, maintenance, etc.
    if (to.meta.public)
      return

    // Check auth state from Firebase (cookie is kept in sync by useFirebaseAuth)
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
