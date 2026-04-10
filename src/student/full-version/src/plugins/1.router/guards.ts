import type { RouteNamedMap, _RouterTyped } from 'unplugin-vue-router'

/**
 * STU-W-01: Disabled the Vuexy admin CASL guards. The scaffold inherited a
 * guard that redirected every route to `{ name: 'login' }` when the user had
 * no CASL abilities — but the student app has no `login` route yet, which
 * caused the router to fail and render an empty page.
 *
 * STU-W-02 will replace this with Firebase-auth guards that send unauthed
 * users to `/login` (once the login route exists) and first-run users to
 * `/onboarding`. Until then the router is fully permissive so the design
 * system, `_dev/*` pages, and the placeholder index are all reachable.
 */
export const setupGuards = (__: _RouterTyped<RouteNamedMap & { [key: string]: any }>) => {
  // no-op — see comment above
}
