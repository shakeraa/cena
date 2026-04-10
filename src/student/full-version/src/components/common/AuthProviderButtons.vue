<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { sanitizeReturnTo } from '@/utils/returnTo'

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

const loadingProvider = ref<string | null>(null)
const phoneAuthMessage = ref<string>('')

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

async function handleProvider(provider: Provider) {
  loadingProvider.value = provider
  phoneAuthMessage.value = ''

  // STU-W-04B stub: phone auth shows a coming-soon placeholder. Real
  // `signInWithPhoneNumber` + reCAPTCHA lands in STU-W-04C.
  if (provider === 'phone') {
    phoneAuthMessage.value = t('auth.providers.phoneComingSoon')
    loadingProvider.value = null

    return
  }

  // Simulated latency
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

    const raw = typeof route.query.returnTo === 'string' ? route.query.returnTo : null
    const target = sanitizeReturnTo(raw, '/home')

    await router.replace(target)
  }
  else {
    meStore.__setProfile({
      uid,
      displayName,
      email: fakeEmail,
      locale: 'en',
      onboardedAt: null,
    })
    await router.replace('/onboarding')
  }

  loadingProvider.value = null
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
