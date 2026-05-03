import { createFetch } from '@vueuse/core'
import { destr } from 'destr'
import { getFirebaseAuth, useMockAuth } from '@/plugins/firebase'

// TASK-E2E-001-FIX: when a real Firebase user is signed in (production
// path + e2e-flow tests that drive /login), pull the freshly-refreshed
// idToken from the SDK and use it as the Bearer credential. The legacy
// `accessToken` cookie is the mock-auth path and stays as a fallback so
// existing dev flows that haven't migrated keep working.
async function resolveBearer(): Promise<string | null> {
  if (!useMockAuth) {
    try {
      const auth = getFirebaseAuth()
      const user = auth?.currentUser
      if (user) {
        // forceRefresh=false — the SDK auto-refreshes when the token is
        // within ~5 min of expiry, so this is cheap on the hot path.
        const token = await user.getIdToken(false)
        if (token)
          return token
      }
    }
    catch (err) {
      console.warn('[useApi] Firebase getIdToken failed, falling back to cookie', err)
    }
  }
  // Mock-auth path or Firebase-not-yet-ready fallback.
  return useCookie('accessToken').value ?? null
}

export const useApi = createFetch({
  baseUrl: import.meta.env.VITE_API_BASE_URL || '/api',
  fetchOptions: {
    headers: {
      Accept: 'application/json',
    },
  },
  options: {
    refetch: true,
    async beforeFetch({ options }) {
      const accessToken = await resolveBearer()

      if (accessToken) {
        options.headers = {
          ...options.headers,
          Authorization: `Bearer ${accessToken}`,
        }
      }

      return { options }
    },
    afterFetch(ctx) {
      const { data, response } = ctx

      // Parse data if it's JSON

      let parsedData = null
      try {
        parsedData = destr(data)
      }
      catch (error) {
        console.error(error)
      }

      return { data: parsedData, response }
    },
  },
})
