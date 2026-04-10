<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.register',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()
const meStore = useMeStore()

const loading = ref(false)
const errorMessage = ref('')

async function handleSubmit(payload: { email: string; password: string; displayName?: string }) {
  errorMessage.value = ''
  loading.value = true

  await new Promise(resolve => setTimeout(resolve, 120))

  // Mock-backend rule: `exists@test.com` → rejected (email already in use).
  if (payload.email === 'exists@test.com') {
    errorMessage.value = t('auth.emailAlreadyExists')
    loading.value = false

    return
  }

  const uid = `mock-${payload.email.replace(/[^a-z0-9]/gi, '-')}`
  const displayName = payload.displayName || payload.email

  authStore.__mockSignIn({ uid, email: payload.email, displayName })

  // Fresh registrations start NOT onboarded so the guard redirects them.
  meStore.__setProfile({
    uid,
    displayName,
    email: payload.email,
    locale: 'en',
    onboardedAt: null,
  })

  loading.value = false

  // Onboarding wizard is STU-W-04C; for Phase A we land on the placeholder.
  await router.replace('/onboarding')
}
</script>

<template>
  <StudentAuthCard
    :title="t('auth.joinCena')"
    :subtitle="t('auth.joinCenaSubtitle')"
  >
    <EmailPasswordForm
      mode="register"
      :loading="loading"
      :error-message="errorMessage"
      @submit="handleSubmit"
    />
    <template #footer>
      {{ t('auth.haveAccount') }}
      <RouterLink
        to="/login"
        data-testid="register-signin-link"
        class="ms-1 text-decoration-underline"
      >
        {{ t('auth.signInLink') }}
      </RouterLink>
    </template>
  </StudentAuthCard>
</template>
