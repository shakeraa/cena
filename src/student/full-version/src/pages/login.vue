<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { sanitizeReturnTo } from '@/utils/returnTo'
import { useMockAuth } from '@/plugins/firebase'
import { useFirebaseAuth } from '@/composables/useFirebaseAuth'

/**
 * FIND-ux-023: Student login page — wired to real Firebase Auth.
 *
 * Default path uses `signInWithEmailAndPassword` from Firebase Auth SDK.
 * Mock path (dev only, VITE_USE_MOCK_AUTH=true) preserves the old stub
 * behavior for offline development.
 */

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.login',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const meStore = useMeStore()
const { loginWithEmail, errorKey } = useFirebaseAuth()

const loading = ref(false)
const errorMessage = ref('')
const failedAttempts = ref(0)
const lockedUntil = ref<number>(0)
const lockedSecondsRemaining = ref(0)

const MAX_ATTEMPTS_BEFORE_LOCKOUT = 5
const LOCKOUT_DURATION_MS = 30000

let tickHandle: ReturnType<typeof setInterval> | null = null

const submitLocked = computed(() => lockedUntil.value > Date.now())

function startLockoutTick() {
  if (tickHandle)
    return
  tickHandle = setInterval(() => {
    const remaining = Math.max(0, Math.ceil((lockedUntil.value - Date.now()) / 1000))

    lockedSecondsRemaining.value = remaining
    if (remaining === 0 && tickHandle) {
      clearInterval(tickHandle)
      tickHandle = null
    }
  }, 250)
}

onBeforeUnmount(() => {
  if (tickHandle) {
    clearInterval(tickHandle)
    tickHandle = null
  }
})

function navigateAfterLogin() {
  const rawReturnTo = typeof route.query.returnTo === 'string' ? route.query.returnTo : null
  const target = sanitizeReturnTo(rawReturnTo, '/home')

  return router.replace(target)
}

/**
 * Mock sign-in path — only reachable when VITE_USE_MOCK_AUTH=true in dev mode.
 */
async function handleMockSubmit(payload: { email: string; password: string }) {
  await new Promise(resolve => setTimeout(resolve, 120))

  if (payload.email === 'fail@test.com') {
    failedAttempts.value += 1
    loading.value = false
    if (failedAttempts.value >= MAX_ATTEMPTS_BEFORE_LOCKOUT) {
      lockedUntil.value = Date.now() + LOCKOUT_DURATION_MS
      lockedSecondsRemaining.value = Math.ceil(LOCKOUT_DURATION_MS / 1000)
      errorMessage.value = t('auth.tooManyAttempts')
      startLockoutTick()
    }
    else {
      errorMessage.value = t('auth.invalidCredentials')
    }

    return
  }

  const uid = `mock-${payload.email.replace(/[^a-z0-9]/gi, '-')}`

  authStore.__mockSignIn({ uid, email: payload.email, displayName: payload.email })
  meStore.__setProfile({
    uid,
    displayName: payload.email,
    email: payload.email,
    locale: 'en',
    onboardedAt: '2026-04-10T00:00:00Z',
  })

  loading.value = false
  await navigateAfterLogin()
}

/**
 * Real Firebase Auth sign-in path — the default.
 */
async function handleFirebaseSubmit(payload: { email: string; password: string }) {
  try {
    await loginWithEmail(payload.email, payload.password)

    // Wait for firebase.ts's onAuthStateChanged handler to (a) update
    // authStore via __firebaseSignIn and (b) hydrate meStore from
    // /api/me. Without (b) the route guard sees `profile == null` for
    // every fresh sign-in and bounces signed-in students to /onboarding
    // even when the projection has `onboardedAt`. The previous 50ms
    // timeout was too short to race the /api/me round-trip.
    await new Promise<void>(resolve => {
      if (meStore.profile) {
        resolve()
        return
      }
      const stop = watch(
        () => meStore.profile,
        profile => {
          if (profile) {
            stop()
            resolve()
          }
        },
      )
      // Hard cap so a backend hiccup doesn't strand the user on /login.
      setTimeout(() => { stop(); resolve() }, 5_000)
    })

    loading.value = false
    await navigateAfterLogin()
  }
  catch (error: unknown) {
    const err = error as { code?: string }

    failedAttempts.value += 1
    loading.value = false

    // Firebase's own rate limiting returns auth/too-many-requests
    if (err.code === 'auth/too-many-requests') {
      lockedUntil.value = Date.now() + LOCKOUT_DURATION_MS
      lockedSecondsRemaining.value = Math.ceil(LOCKOUT_DURATION_MS / 1000)
      errorMessage.value = t('auth.tooManyAttempts')
      startLockoutTick()

      // Structured log for production monitoring
      console.error('[login] Firebase rate limit hit', {
        email: payload.email,
        failedAttempts: failedAttempts.value,
        firebaseCode: err.code,
      })

      return
    }

    // Client-side lockout as additional UX safeguard
    if (failedAttempts.value >= MAX_ATTEMPTS_BEFORE_LOCKOUT) {
      lockedUntil.value = Date.now() + LOCKOUT_DURATION_MS
      lockedSecondsRemaining.value = Math.ceil(LOCKOUT_DURATION_MS / 1000)
      errorMessage.value = t('auth.tooManyAttempts')
      startLockoutTick()
    }
    else {
      // Translate Firebase error code to user-friendly i18n message
      errorMessage.value = errorKey.value ? t(errorKey.value) : t('auth.signInFailed')
    }

    // Structured log for production monitoring
    console.error('[login] Sign-in failed', {
      email: payload.email,
      failedAttempts: failedAttempts.value,
      firebaseCode: err.code,
    })
  }
}

async function handleSubmit(payload: { email: string; password: string }) {
  if (submitLocked.value)
    return

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
    :title="t('auth.welcome')"
    :subtitle="t('auth.welcomeSubtitle')"
  >
    <EmailPasswordForm
      mode="login"
      :loading="loading"
      :error-message="errorMessage"
      :submit-locked="submitLocked"
      :locked-seconds-remaining="lockedSecondsRemaining"
      @submit="handleSubmit"
    />
    <AuthProviderButtons mode="login" />
    <div class="mt-4 text-center text-body-2">
      <!-- text-high-emphasis pins the link to the theme's high-contrast
           text color (~near-black on light, near-white on dark). The
           default RouterLink color was ~3.4:1 against the auth-card
           background, failing WCAG 2.1 AA color-contrast (rule
           color-contrast). Same rationale on the footer link below. -->
      <RouterLink
        to="/forgot-password"
        data-testid="login-forgot-link"
        class="text-high-emphasis text-decoration-underline"
      >
        {{ t('auth.forgotPasswordLink') }}
      </RouterLink>
    </div>
    <template #footer>
      {{ t('auth.noAccount') }}
      <RouterLink
        to="/register"
        data-testid="login-register-link"
        class="ms-1 text-high-emphasis text-decoration-underline"
      >
        {{ t('auth.signUpLink') }}
      </RouterLink>
    </template>
  </StudentAuthCard>
</template>
