import type { RouteNamedMap, _RouterTyped } from 'unplugin-vue-router'
import { canNavigate } from '@layouts/plugins/casl'
import { firebaseAuth } from '@/plugins/firebase'

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
    if (!canNavigate(to) && to.matched.length) {
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
