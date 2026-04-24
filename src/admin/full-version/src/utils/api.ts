import { ofetch } from 'ofetch'
import { signOut } from 'firebase/auth'
import { firebaseAuth } from '@/plugins/firebase'
import { ability } from '@/plugins/casl/ability'

/**
 * Clear all auth state and redirect to login.
 * Works outside component context by importing the router instance directly.
 */
async function handleAuthFailure(redirectPath?: string) {
  // Clear cookies
  useCookie('userData').value = null
  useCookie('accessToken').value = null
  useCookie('userAbilityRules').value = null

  // Clear CASL abilities
  ability.update([])

  // Sign out of Firebase (token may be revoked server-side)
  try {
    await signOut(firebaseAuth)
  }
  catch {
    // Already signed out or Firebase unavailable
  }

  // Redirect to login using the router instance (not useRouter which needs component context)
  const { router } = await import('@/plugins/1.router')

  const query: Record<string, string> = {}
  if (redirectPath && redirectPath !== '/')
    query.to = redirectPath

  await router.push({ name: 'login', query })
}

export const $api = ofetch.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  async onRequest({ options }) {
    // Get fresh Firebase ID token (auto-refreshes if expired)
    const user = firebaseAuth.currentUser
    if (user) {
      try {
        const token = await user.getIdToken()

        options.headers = new Headers(options.headers)
        options.headers.set('Authorization', `Bearer ${token}`)
      }
      catch {
        // Token refresh failed — force re-login
        await handleAuthFailure()
      }
    }
  },
  async onResponseError({ response, request }) {
    const status = response.status

    // 401 = token invalid/expired/revoked — redirect to login
    if (status === 401) {
      const currentPath = typeof window !== 'undefined' ? window.location.pathname : '/'
      await handleAuthFailure(currentPath)
    }

    // 403 = authenticated but insufficient permissions — show not-authorized page
    if (status === 403) {
      const { router } = await import('@/plugins/1.router')
      await router.push({ name: 'not-authorized' })
    }
  },
})
