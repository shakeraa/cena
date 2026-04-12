/**
 * FIND-arch-017: MSW fake-api plugin — dev-only.
 *
 * This entire module is gated behind `import.meta.env.DEV`. In production
 * builds Vite statically evaluates the condition to `false` and tree-shakes
 * the whole MSW dependency graph (setupWorker, all handlers, cookie-es,
 * etc.) out of the bundle. The exported default function becomes a no-op
 * that Vite's dead-code elimination removes entirely.
 *
 * The companion change in `@core/utils/plugins.ts` also excludes this
 * directory from the eager glob in production so the module is never even
 * imported. Belt-and-suspenders: if some future refactor re-adds it to
 * the import graph, this guard still prevents MSW from starting.
 */

// eslint-disable-next-line import/no-mutable-exports
let startFakeApi: () => Promise<void> = async () => {
  // Production no-op. Structured log so monitoring can detect if this path
  // is ever reached in a production build (it should not be).
  if (typeof console !== 'undefined') {
    console.warn(
      '[fake-api] FIND-arch-017: fake-api plugin called in production '
      + '— this is a no-op. If you see this in production logs, the '
      + 'build-time tree-shaking did not exclude the fake-api module.',
    )
  }
}

if (import.meta.env.DEV) {
  // Dynamic imports ensure Vite never includes MSW or handler code in
  // production chunks. In dev mode these resolve synchronously from the
  // dev server's module graph.
  const { setupWorker } = await import('msw/browser')

  // Handlers
  const { handlerAppBarSearch } = await import('@db/app-bar-search/index')
  const { handlerAppsAcademy } = await import('@db/apps/academy/index')
  const { handlerAppsCalendar } = await import('@db/apps/calendar/index')
  const { handlerAppsChat } = await import('@db/apps/chat/index')
  const { handlerAppsEcommerce } = await import('@db/apps/ecommerce/index')
  const { handlerAppsEmail } = await import('@db/apps/email/index')
  const { handlerAppsInvoice } = await import('@db/apps/invoice/index')
  const { handlerAppsKanban } = await import('@db/apps/kanban/index')
  const { handlerAppLogistics } = await import('@db/apps/logistics/index')
  const { handlerAppsPermission } = await import('@db/apps/permission/index')
  const { handlerAppsUsers } = await import('@db/apps/users/index')
  const { handlerAuth } = await import('@db/auth/index')
  const { handlerDashboard } = await import('@db/dashboard/index')
  const { handlerPagesDatatable } = await import('@db/pages/datatable/index')
  const { handlerPagesFaq } = await import('@db/pages/faq/index')
  const { handlerPagesHelpCenter } = await import('@db/pages/help-center/index')
  const { handlerPagesProfile } = await import('@db/pages/profile/index')

  // Student /api/me handlers — used by the dev loop when the student API
  // host is offline.
  const { handlerStudentMe } = await import('@db/student-me/index')

  // Student /api/gamification/* handlers for the progress dashboard.
  const { handlerStudentGamification } = await import('@db/student-gamification/index')

  // Student /api/tutor/* handlers for the AI tutor chat.
  const { handlerStudentTutor } = await import('@db/student-tutor/index')

  // Student /api/challenges/* handlers for the challenges hub.
  const { handlerStudentChallenges } = await import('@db/student-challenges/index')

  // Student /api/sessions/* handlers for the learning session runner.
  const { handlerStudentSessions } = await import('@db/student-sessions/index')

  // Student /api/analytics/* handlers for progress subpages.
  const { handlerStudentAnalytics } = await import('@db/student-analytics/index')

  // Student /api/content/* + /api/knowledge/* handlers.
  const { handlerStudentKnowledge } = await import('@db/student-knowledge/index')

  // Student /api/social/* handlers for class feed + peers + friends.
  const { handlerStudentSocial } = await import('@db/student-social/index')

  // Student /api/notifications/* handlers for the notifications center.
  const { handlerStudentNotifications } = await import('@db/student-notifications/index')

  const worker = setupWorker(
    ...handlerAppsEcommerce,
    ...handlerAppsAcademy,
    ...handlerAppsInvoice,
    ...handlerAppsUsers,
    ...handlerAppsEmail,
    ...handlerAppsCalendar,
    ...handlerAppsChat,
    ...handlerAppsPermission,
    ...handlerPagesHelpCenter,
    ...handlerPagesProfile,
    ...handlerPagesFaq,
    ...handlerPagesDatatable,
    ...handlerAppBarSearch,
    ...handlerAppLogistics,
    ...handlerAuth,
    ...handlerAppsKanban,
    ...handlerDashboard,
    ...handlerStudentMe,
    ...handlerStudentGamification,
    ...handlerStudentTutor,
    ...handlerStudentChallenges,
    ...handlerStudentSessions,
    ...handlerStudentAnalytics,
    ...handlerStudentKnowledge,
    ...handlerStudentSocial,
    ...handlerStudentNotifications,
  )

  /**
   * FIND-ux-003: scrub any cookie whose name violates RFC 6265 token grammar
   * before MSW starts reading the Cookie header.
   *
   * Root cause: the admin app historically wrote cookies whose names
   * embedded a SP character (brand string was two words with a space),
   * which makes MSW's internal `cookie-es` parser throw
   * `TypeError: argument name is invalid` on every request, including
   * Vite's HMR dynamic-import fetches. That throw happens inside MSW's
   * request-start pipeline before any of our handlers run, so there is no
   * way to catch it with `onUnhandledRequest` or a handler-level try/catch.
   * The only defense that reaches the buggy code path is preventing such
   * cookies from reaching MSW at all.
   *
   * This function runs once on module load, enumerates `document.cookie`,
   * and expires any cookie whose name contains a space or any other
   * character disallowed from the RFC 7230 token grammar used by
   * `cookie-name`. Valid token characters (RFC 7230 §3.2.6):
   *   token = 1*tchar
   *   tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "."
   *         / "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
   *
   * We check via a whitelist to reject SP, HT, ";", "=", "(", ")", "<", ">",
   * "@", ",", "\"", "/", "[", "]", "?", "{", "}", ":", "\\", and any CTL.
   * `\w` is (ALPHA|DIGIT|_) plus the `i` flag can collapse A-Z+a-z — we
   * encode both so the regex is both correct and eslint-happy under the
   * project's `regexp/*` ruleset.
   */
  const RFC_7230_TOKEN = /^[!#$%&'*+\-.\w^`|~]+$/

  function scrubInvalidCookieNames(): void {
    if (typeof document === 'undefined')
      return

    const raw = document.cookie
    if (!raw)
      return

    const pairs = raw.split(/;\s*/).filter(Boolean)
    for (const pair of pairs) {
      const eq = pair.indexOf('=')
      const name = eq === -1 ? pair : pair.slice(0, eq)
      if (name.length === 0)
        continue

      if (!RFC_7230_TOKEN.test(name)) {
        // Expire the offending cookie so MSW never sees it. We try both
        // the bare form and the `path=/` form to match whichever scope
        // the original write used.
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT`
        document.cookie = `${name}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`

        if (typeof console !== 'undefined') {
          console.warn(
            '[fake-api] scrubbed cookie with invalid name',
            JSON.stringify(name),
            '- see FIND-ux-003',
          )
        }
      }
    }
  }

  startFakeApi = async () => {
    // Defense-in-depth: even after admin's migration lands, a user with a
    // stale browser profile could still carry poison cookies on first load.
    // Scrub them before MSW's cookie parser runs, not after.
    scrubInvalidCookieNames()

    const workerUrl = `${import.meta.env.BASE_URL ?? '/'}mockServiceWorker.js`

    // FIND-ux-021: await worker.start() so the MSW service worker is fully
    // registered and intercepting requests BEFORE the Vue app mounts and
    // fires its first API calls. Without this, cold-load races produce
    // 404s from the dev server for /api/* paths that MSW should intercept.
    await worker.start({
      serviceWorker: {
        url: workerUrl,
      },
      onUnhandledRequest: 'bypass',
    })
  }
}

export default startFakeApi
