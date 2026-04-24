<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { sanitizeReturnTo } from '@/utils/returnTo'
import { useMockAuth } from '@/plugins/firebase'
import { useFirebaseAuth } from '@/composables/useFirebaseAuth'

/**
 * FIND-ux-023: OAuth provider buttons — wired to real Firebase Auth.
 *
 * Google, Apple, Microsoft call real `signInWithPopup` via the composable.
 * Phone auth remains "coming soon" (requires reCAPTCHA integration).
 *
 * Mock path (dev only, VITE_USE_MOCK_AUTH=true) preserves the old stub.
 */

interface Props {

  /** 'login' lands onboarded users on /home; 'register' lands fresh users on /onboarding. */
  mode: 'login' | 'register'
}

const props = defineProps<Props>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const meStore = useMeStore()
const { loginWithProvider, errorKey } = useFirebaseAuth()

const loadingProvider = ref<string | null>(null)
const phoneAuthMessage = ref<string>('')
const providerError = ref<string>('')

type Provider = 'google' | 'apple' | 'microsoft' | 'phone'

interface ProviderButton {
  id: Provider
  icon: string
  labelKey: string
  testId: string
}

const PROVIDERS: ProviderButton[] = [
  { id: 'google', icon: 'tabler-brand-google', labelKey: 'auth.providers.google', testId: 'auth-provider-google' },
  { id: 'apple', icon: 'tabler-brand-apple', labelKey: 'auth.providers.apple', testId: 'auth-provider-apple' },
  { id: 'microsoft', icon: 'tabler-brand-windows', labelKey: 'auth.providers.microsoft', testId: 'auth-provider-microsoft' },
  { id: 'phone', icon: 'tabler-phone', labelKey: 'auth.providers.phone', testId: 'auth-provider-phone' },
]

function navigateAfterAuth() {
  if (props.mode === 'login') {
    const raw = typeof route.query.returnTo === 'string' ? route.query.returnTo : null
    const target = sanitizeReturnTo(raw, '/home')

    return router.replace(target)
  }

  return router.replace('/onboarding')
}

/**
 * Mock provider sign-in — dev only.
 */
async function handleMockProvider(provider: Provider) {
  await new Promise(resolve => setTimeout(resolve, 150))

  const uid = `mock-${provider}-${Date.now().toString(36)}`
  const fakeEmail = `${provider}-user@example.com`
  const displayName = `${provider.charAt(0).toUpperCase()}${provider.slice(1)} User`

  authStore.__mockSignIn({ uid, email: fakeEmail, displayName })

  if (props.mode === 'login') {
    meStore.__setProfile({
      uid,
      displayName,
      email: fakeEmail,
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    })
  }
  else {
    meStore.__setProfile({
      uid,
      displayName,
      email: fakeEmail,
      locale: 'en',
      onboardedAt: null,
    })
  }

  loadingProvider.value = null
  await navigateAfterAuth()
}

/**
 * Real Firebase provider sign-in.
 */
async function handleFirebaseProvider(provider: 'google' | 'apple' | 'microsoft') {
  try {
    await loginWithProvider(provider)

    // onAuthStateChanged will update the auth store.
    // Wait a tick for the listener to fire.
    await new Promise(resolve => setTimeout(resolve, 50))

    // Set meStore profile from auth store data after onAuthStateChanged
    if (props.mode === 'login') {
      meStore.__setProfile({
        uid: authStore.uid!,
        displayName: authStore.displayName || '',
        email: authStore.email || '',
        locale: 'en',
        onboardedAt: '2026-04-10T00:00:00Z',
      })
    }
    else {
      meStore.__setProfile({
        uid: authStore.uid!,
        displayName: authStore.displayName || '',
        email: authStore.email || '',
        locale: 'en',
        onboardedAt: null,
      })
    }

    loadingProvider.value = null
    await navigateAfterAuth()
  }
  catch (error: unknown) {
    const err = error as { code?: string }

    loadingProvider.value = null

    // Don't show error for user-cancelled popups
    if (err.code === 'auth/popup-closed-by-user' || err.code === 'auth/cancelled-popup-request')
      return

    providerError.value = errorKey.value
      ? t(errorKey.value)
      : t('auth.providerSignInFailed', { provider: provider.charAt(0).toUpperCase() + provider.slice(1) })

    console.error('[AuthProviderButtons] Provider sign-in failed', {
      provider,
      firebaseCode: err.code,
    })
  }
}

async function handleProvider(provider: Provider) {
  loadingProvider.value = provider
  phoneAuthMessage.value = ''
  providerError.value = ''

  // Phone auth: still coming soon (requires reCAPTCHA integration)
  if (provider === 'phone') {
    phoneAuthMessage.value = t('auth.providers.phoneComingSoon')
    loadingProvider.value = null

    return
  }

  if (useMockAuth) {
    await handleMockProvider(provider)

    return
  }

  await handleFirebaseProvider(provider)
}
</script>

<template>
  <div class="auth-provider-buttons">
    <div
      class="auth-provider-buttons__divider my-4"
      role="separator"
      :aria-label="t('auth.continueWith')"
    >
      <span class="auth-provider-buttons__divider-text">
        {{ t('auth.continueWith') }}
      </span>
    </div>

    <div
      class="auth-provider-buttons__grid"
      data-testid="auth-provider-buttons"
    >
      <VBtn
        v-for="provider in PROVIDERS"
        :key="provider.id"
        variant="outlined"
        block
        size="large"
        :prepend-icon="provider.icon"
        :loading="loadingProvider === provider.id"
        :disabled="loadingProvider !== null && loadingProvider !== provider.id"
        :data-testid="provider.testId"
        :aria-label="t(provider.labelKey)"
        @click="handleProvider(provider.id)"
      >
        {{ t(provider.labelKey) }}
      </VBtn>
    </div>

    <VAlert
      v-if="phoneAuthMessage"
      type="info"
      variant="tonal"
      density="compact"
      class="mt-3"
      data-testid="auth-provider-phone-message"
    >
      {{ phoneAuthMessage }}
    </VAlert>

    <VAlert
      v-if="providerError"
      type="error"
      variant="tonal"
      density="compact"
      class="mt-3"
      data-testid="auth-provider-error"
    >
      {{ providerError }}
    </VAlert>
  </div>
</template>

<style scoped>
.auth-provider-buttons__divider {
  position: relative;
  text-align: center;
  color: rgb(var(--v-theme-on-surface) / 0.6);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.auth-provider-buttons__divider::before,
.auth-provider-buttons__divider::after {
  content: '';
  position: absolute;
  inset-block-start: 50%;
  inline-size: calc(50% - 5rem);
  block-size: 1px;
  background-color: rgb(var(--v-theme-on-surface) / 0.12);
}

.auth-provider-buttons__divider::before {
  inset-inline-start: 0;
}

.auth-provider-buttons__divider::after {
  inset-inline-end: 0;
}

.auth-provider-buttons__divider-text {
  background-color: rgb(var(--v-theme-surface));
  padding-inline: 0.75rem;
}

.auth-provider-buttons__grid {
  display: grid;
  gap: 0.5rem;
}
</style>
