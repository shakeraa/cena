// MSW fake-api disabled — Cena uses real Firebase Auth + .NET backend API endpoints.
// The Vuexy demo mock handlers intercepted Vite module requests causing crashes.
// To re-enable for local dev without a backend, uncomment the code below.

/*
import { setupWorker } from 'msw/browser'
import { handlerAppsUsers } from '@db/apps/users/index'
import { handlerAppsPermission } from '@db/apps/permission/index'
import { handlerAuth } from '@db/auth/index'

const worker = setupWorker(
  ...handlerAppsUsers,
  ...handlerAppsPermission,
  ...handlerAuth,
)
*/

export default function () {
  // No-op: real API calls go to VITE_API_BASE_URL
}
