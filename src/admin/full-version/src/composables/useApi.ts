import { createFetch } from '@vueuse/core'
import { destr } from 'destr'
import { firebaseAuth } from '@/plugins/firebase'

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
      // Get fresh Firebase ID token (auto-refreshes if near expiry)
      const user = firebaseAuth.currentUser
      if (user) {
        const token = await user.getIdToken()

        options.headers = {
          ...options.headers,
          Authorization: `Bearer ${token}`,
        }
      }

      return { options }
    },
    afterFetch(ctx) {
      const { data, response } = ctx

      let parsedData = null
      try {
        parsedData = destr(data)
      }
      catch (error) {
        console.error(error)
      }

      return { data: parsedData, response }
    },
    onFetchError(ctx) {
      // 401 = redirect to login
      if (ctx.response?.status === 401) {
        useCookie('userData').value = null
        useCookie('accessToken').value = null
        useCookie('userAbilityRules').value = null
      }

      return ctx
    },
  },
})
