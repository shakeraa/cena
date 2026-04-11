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

// STU-W-05B: student /api/me handlers for dev-loop when Cena.Api.Host is offline.
import { handlerStudentMe } from '@db/student-me/index'

// STU-W-07: student /api/gamification/* handlers for the progress dashboard.
import { handlerStudentGamification } from '@db/student-gamification/index'

// STU-W-08: student /api/tutor/* handlers for the AI tutor chat.
import { handlerStudentTutor } from '@db/student-tutor/index'

// STU-W-11: student /api/challenges/* handlers for the challenges hub.
import { handlerStudentChallenges } from '@db/student-challenges/index'

// STU-W-06: student /api/sessions/* handlers for the learning session runner.
import { handlerStudentSessions } from '@db/student-sessions/index'

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
