<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { useMockAuth } from '@/plugins/firebase'
import { useFirebaseAuth } from '@/composables/useFirebaseAuth'

/**
 * FIND-ux-023: Student register page — wired to real Firebase Auth.
 *
 * Default path uses `createUserWithEmailAndPassword` from Firebase Auth SDK.
 * Mock path (dev only, VITE_USE_MOCK_AUTH=true) preserves the old stub.
 */

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
const { registerWithEmail, errorKey } = useFirebaseAuth()

const loading = ref(false)
const errorMessage = ref('')

async function handleMockSubmit(payload: { email: string; password: string; displayName?: string }) {
  await new Promise(resolve => setTimeout(resolve, 120))

  if (payload.email === 'exists@test.com') {
    errorMessage.value = t('auth.emailAlreadyExists')
    loading.value = false
    return
  }

  const uid = `mock-${payload.email.replace(/[^a-z0-9]/gi, '-')}`
  const displayName = payload.displayName || payload.email

  authStore.__mockSignIn({ uid, email: payload.email, displayName })

  meStore.__setProfile({
    uid,
    displayName,
    email: payload.email,
    locale: 'en',
    onboardedAt: null,
  })

  loading.value = false
  await router.replace('/onboarding')
}

async function handleFirebaseSubmit(payload: { email: string; password: string; displayName?: string }) {
  try {
    await registerWithEmail(payload.email, payload.password, payload.displayName)

    // onAuthStateChanged in firebase.ts plugin will update the auth store.
    // Wait a tick for the listener to fire.
    await new Promise(resolve => setTimeout(resolve, 50))

    // Fresh registrations start NOT onboarded.
    meStore.__setProfile({
      uid: authStore.uid!,
      displayName: payload.displayName || payload.email,
      email: payload.email,
      locale: 'en',
      onboardedAt: null,
    })

    loading.value = false
    await router.replace('/onboarding')
  }
  catch (error: unknown) {
    loading.value = false
    errorMessage.value = errorKey.value ? t(errorKey.value) : t('auth.signInFailed')

    const err = error as { code?: string }

    console.error('[register] Registration failed', {
      email: payload.email,
      firebaseCode: err.code,
    })
  }
}

async function handleSubmit(payload: { email: string; password: string; displayName?: string }) {
  errorMessage.value = ''
  loading.value = true

  if (useMockAuth) {
    await handleMockSubmit(payload)
    return
  }

  await handleFirebaseSubmit(payload)
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
    <AuthProviderButtons mode="register" />
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
