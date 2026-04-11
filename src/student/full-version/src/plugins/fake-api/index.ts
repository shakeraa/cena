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

export default function () {
  const workerUrl = `${import.meta.env.BASE_URL ?? '/'}mockServiceWorker.js`

  worker.start({
    serviceWorker: {
      url: workerUrl,
    },
    onUnhandledRequest: 'bypass',
  })
}
