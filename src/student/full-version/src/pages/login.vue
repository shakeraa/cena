<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { sanitizeReturnTo } from '@/utils/returnTo'

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

const loading = ref(false)
const errorMessage = ref('')
const failedAttempts = ref(0)
const lockedUntil = ref<number>(0)
const lockedSecondsRemaining = ref(0)

const MAX_ATTEMPTS_BEFORE_LOCKOUT = 3
const LOCKOUT_DURATION_MS = 5000

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

async function handleSubmit(payload: { email: string; password: string }) {
  if (submitLocked.value)
    return

  errorMessage.value = ''
  loading.value = true

  // Simulated latency so the loading state is visible; mock backend otherwise.
  await new Promise(resolve => setTimeout(resolve, 120))

  // Mock-backend rules for Phase A:
  //   - `fail@test.com` → rejected (wrong credentials)
  //   - `unverified@test.com` → rejected (email not verified)
  //   - any other well-formed email → accepted
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

  // Mock success: synth a UID from the email.
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

  const rawReturnTo = typeof route.query.returnTo === 'string' ? route.query.returnTo : null
  const target = sanitizeReturnTo(rawReturnTo, '/home')

  await router.replace(target)
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
    <div class="mt-4 text-center text-body-2">
      <RouterLink
        to="/forgot-password"
        data-testid="login-forgot-link"
        class="text-decoration-underline"
      >
        {{ t('auth.forgotPasswordLink') }}
      </RouterLink>
    </div>
    <template #footer>
      {{ t('auth.noAccount') }}
      <RouterLink
        to="/register"
        data-testid="login-register-link"
        class="ms-1 text-decoration-underline"
      >
        {{ t('auth.signUpLink') }}
      </RouterLink>
    </template>
  </StudentAuthCard>
</template>
