<script setup lang="ts">
/**
 * FIND-privacy-001: ParentalConsentStep.vue
 *
 * Collects the parent/guardian's email address and displays a consent
 * notice explaining what data is collected and why. This step is shown
 * only when the age gate determines the student is under 16.
 *
 * The component does NOT implement the email verification flow — that
 * is handled by the backend's /api/auth/parent-consent-challenge and
 * /api/auth/parent-consent-verify/{token} endpoints. This component
 * collects and stores the parent email + consent acknowledgement flag.
 *
 * Compliance: COPPA §312.5(b)(2), GDPR Art 8, ICO Children's Code Std 11.
 */

import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ConsentTier } from '@/composables/useAgeGate'

interface Props {
  parentEmail: string
  consentGiven: boolean
  consentTier: ConsentTier
  age: number
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:parentEmail': [value: string]
  'update:consentGiven': [value: boolean]
}>()

const { t } = useI18n()

const emailInput = ref(props.parentEmail)
const consentChecked = ref(props.consentGiven)
const emailError = ref('')

watch(() => props.parentEmail, (val) => {
  if (val !== emailInput.value)
    emailInput.value = val
})

watch(() => props.consentGiven, (val) => {
  if (val !== consentChecked.value)
    consentChecked.value = val
})

function handleEmailChange(val: string) {
  emailInput.value = val
  emailError.value = ''

  if (!val.trim()) {
    emailError.value = t('auth.parentConsent.emailRequired')
    emit('update:parentEmail', '')
    return
  }

  if (!/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/.test(val.trim())) {
    emailError.value = t('auth.parentConsent.emailInvalid')
    emit('update:parentEmail', '')
    return
  }

  emit('update:parentEmail', val.trim())
}

function handleConsentToggle(val: boolean) {
  consentChecked.value = val
  emit('update:consentGiven', val)
}
</script>

<template>
  <div
    class="parental-consent-step"
    data-testid="parental-consent-step"
  >
    <h2 class="text-h5 mb-1">
      {{ t('auth.parentConsent.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('auth.parentConsent.subtitle', { age: props.age }) }}
    </p>

    <!-- Data collection notice (COPPA §312.5(b)(1)) -->
    <VCard
      variant="tonal"
      class="mb-4 pa-4"
      data-testid="consent-notice"
    >
      <h3 class="text-subtitle-1 font-weight-bold mb-2">
        {{ t('auth.parentConsent.whatWeCollect') }}
      </h3>
      <ul class="text-body-2 ps-4">
        <li>{{ t('auth.parentConsent.collectName') }}</li>
        <li>{{ t('auth.parentConsent.collectEmail') }}</li>
        <li>{{ t('auth.parentConsent.collectDob') }}</li>
        <li>{{ t('auth.parentConsent.collectProgress') }}</li>
      </ul>
      <h3 class="text-subtitle-1 font-weight-bold mt-3 mb-2">
        {{ t('auth.parentConsent.whyWeCollect') }}
      </h3>
      <p class="text-body-2">
        {{ t('auth.parentConsent.whyExplanation') }}
      </p>
    </VCard>

    <!-- Parent email field -->
    <VTextField
      :model-value="emailInput"
      type="email"
      :label="t('auth.parentConsent.emailLabel')"
      :placeholder="t('auth.parentConsent.emailPlaceholder')"
      :error-messages="emailError"
      data-testid="parent-email"
      autocomplete="off"
      class="mb-3"
      @update:model-value="handleEmailChange"
    />

    <!-- Consent acknowledgement -->
    <VCheckbox
      :model-value="consentChecked"
      :label="t('auth.parentConsent.consentLabel')"
      data-testid="consent-checkbox"
      class="mb-2"
      @update:model-value="handleConsentToggle"
    />

    <p class="text-caption text-medium-emphasis">
      {{ t('auth.parentConsent.verificationNotice') }}
    </p>
  </div>
</template>
