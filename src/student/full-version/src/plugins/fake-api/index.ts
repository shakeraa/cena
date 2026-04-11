import { setupWorker } from 'msw/browser'

// Handlers
import { handlerAppBarSearch } from '@db/app-bar-search/index'
import { handlerAppsAcademy } from '@db/apps/academy/index'
import { handlerAppsCalendar } from '@db/apps/calendar/index'
import { handlerAppsChat } from '@db/apps/chat/index'
import { handlerAppsEcommerce } from '@db/apps/ecommerce/index'
import { handlerAppsEmail } from '@db/apps/email/index'
import { handlerAppsInvoice } from '@db/apps/invoice/index'
import { handlerAppsKanban } from '@db/apps/kanban/index'
import { handlerAppLogistics } from '@db/apps/logistics/index'
import { handlerAppsPermission } from '@db/apps/permission/index'
import { handlerAppsUsers } from '@db/apps/users/index'
import { handlerAuth } from '@db/auth/index'
import { handlerDashboard } from '@db/dashboard/index'
import { handlerPagesDatatable } from '@db/pages/datatable/index'
import { handlerPagesFaq } from '@db/pages/faq/index'
import { handlerPagesHelpCenter } from '@db/pages/help-center/index'
import { handlerPagesProfile } from '@db/pages/profile/index'

// Student /api/me handlers — used by the dev loop when the student API host
// is offline. Production bypasses MSW entirely.
import { handlerStudentMe } from '@db/student-me/index'

// Student /api/gamification/* handlers for the progress dashboard.
import { handlerStudentGamification } from '@db/student-gamification/index'

// Student /api/tutor/* handlers for the AI tutor chat.
import { handlerStudentTutor } from '@db/student-tutor/index'

// Student /api/challenges/* handlers for the challenges hub.
import { handlerStudentChallenges } from '@db/student-challenges/index'

// Student /api/sessions/* handlers for the learning session runner.
import { handlerStudentSessions } from '@db/student-sessions/index'

// Student /api/analytics/* handlers for progress subpages.
import { handlerStudentAnalytics } from '@db/student-analytics/index'

// Student /api/content/* + /api/knowledge/* handlers.
import { handlerStudentKnowledge } from '@db/student-knowledge/index'

// Student /api/social/* handlers for class feed + peers + friends.
import { handlerStudentSocial } from '@db/student-social/index'

// Student /api/notifications/* handlers for the notifications center.
import { handlerStudentNotifications } from '@db/student-notifications/index'

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

export default function () {
  // Defense-in-depth: even after admin's migration lands, a user with a
  // stale browser profile could still carry poison cookies on first load.
  // Scrub them before MSW's cookie parser runs, not after.
  scrubInvalidCookieNames()

  const workerUrl = `${import.meta.env.BASE_URL ?? '/'}mockServiceWorker.js`

  worker.start({
    serviceWorker: {
      url: workerUrl,
    },
    onUnhandledRequest: 'bypass',
  })
}
