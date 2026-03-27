import { ofetch } from 'ofetch'
import { firebaseAuth } from '@/plugins/firebase'

export const $api = ofetch.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  async onRequest({ options }) {
    // Get fresh Firebase ID token (auto-refreshes if expired)
    const user = firebaseAuth.currentUser
    if (user) {
      const token = await user.getIdToken()

      options.headers = new Headers(options.headers)
      options.headers.set('Authorization', `Bearer ${token}`)
    }
  },
  async onResponseError({ response }) {
    // 401 = token invalid/expired, redirect to login
    if (response.status === 401) {
      useCookie('userData').value = null
      useCookie('accessToken').value = null
      useCookie('userAbilityRules').value = null

      const router = useRouter()

      await router.push({ name: 'login' })
    }
  },
})
