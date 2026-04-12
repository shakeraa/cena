<script setup lang="ts">
/**
 * FIND-privacy-001: Registration page with mandatory age gate.
 *
 * Flow:
 *   1. Student enters DOB → age gate evaluates consent tier
 *   2a. If age >= 16 → proceed to email/password form
 *   2b. If age 13-15 → collect parent email + consent, then form
 *   2c. If age < 13  → collect parent email + consent, then form
 *   3. On submit, DOB + consent data sent to backend alongside credentials
 *
 * The age gate is NOT trivially bypassable:
 *   - DOB is required (no "just click I'm 16" checkbox)
 *   - Backend independently validates age from DOB on submission
 *   - DOB stored in event stream for compliance audit trail
 *
 * Compliance: COPPA §312.5, GDPR Art 8, ICO Children's Code Std 7+11,
 * Israel PPL §11.
 */

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import AgeGateStep from '@/components/onboarding/AgeGateStep.vue'
import ParentalConsentStep from '@/components/onboarding/ParentalConsentStep.vue'
import type { AgeGateResult, ConsentTier } from '@/composables/useAgeGate'

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

// ── Age Gate State ──
type RegistrationStep = 'age-gate' | 'parental-consent' | 'credentials'
const currentStep = ref<RegistrationStep>('age-gate')
const dateOfBirth = ref<string | null>(null)
const ageResult = ref<AgeGateResult | null>(null)
const parentEmail = ref('')
const consentGiven = ref(false)

const stepTitle = computed(() => {
  switch (currentStep.value) {
    case 'age-gate': return t('auth.joinCena')
    case 'parental-consent': return t('auth.parentConsent.title')
    case 'credentials': return t('auth.joinCena')
  }
})

const stepSubtitle = computed(() => {
  switch (currentStep.value) {
    case 'age-gate': return t('auth.ageGate.subtitle')
    case 'parental-consent': return t('auth.parentConsent.subtitle', { age: ageResult.value?.age ?? 0 })
    case 'credentials': return t('auth.joinCenaSubtitle')
  }
})

function handleAgeEvaluated(result: AgeGateResult | null) {
  ageResult.value = result
}

function handleAgeGateNext() {
  if (!ageResult.value || !dateOfBirth.value)
    return

  if (ageResult.value.requiresParentalConsent) {
    currentStep.value = 'parental-consent'
  }
  else {
    currentStep.value = 'credentials'
  }
}

function handleConsentNext() {
  if (!parentEmail.value || !consentGiven.value)
    return
  currentStep.value = 'credentials'
}

function handleBack() {
  if (currentStep.value === 'parental-consent') {
    currentStep.value = 'age-gate'
  }
  else if (currentStep.value === 'credentials') {
    if (ageResult.value?.requiresParentalConsent) {
      currentStep.value = 'parental-consent'
    }
    else {
      currentStep.value = 'age-gate'
    }
  }
}

const canProceedFromAgeGate = computed(() => {
  return ageResult.value !== null && dateOfBirth.value !== null
})

const canProceedFromConsent = computed(() => {
  return parentEmail.value.length > 0 && consentGiven.value
})

async function handleSubmit(payload: { email: string; password: string; displayName?: string }) {
  errorMessage.value = ''
  loading.value = true

  await new Promise(resolve => setTimeout(resolve, 120))

  // Mock-backend rule: `exists@test.com` -> rejected (email already in use).
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

  // FIND-privacy-001: Store DOB + consent data alongside registration.
  // In the real backend, POST /api/me/age-consent is called after auth.
  // For the mock, persist to localStorage so it survives page refresh.
  if (dateOfBirth.value) {
    const consentData = {
      dateOfBirth: dateOfBirth.value,
      ageAtRegistration: ageResult.value?.age,
      consentTier: ageResult.value?.consentTier,
      parentEmail: parentEmail.value || null,
      consentGiven: consentGiven.value,
      consentStatus: ageResult.value?.requiresParentalConsent
        ? 'pending_parent'
        : 'not_required',
      recordedAt: new Date().toISOString(),
    }
    try {
      localStorage.setItem(`cena-consent-${uid}`, JSON.stringify(consentData))
    }
    catch {
      // Quota exceeded — in-memory still works
    }
  }

  loading.value = false

  // Onboarding wizard is STU-W-04C; for Phase A we land on the placeholder.
  await router.replace('/onboarding')
}
</script>

<template>
  <StudentAuthCard
    :title="stepTitle"
    :subtitle="stepSubtitle"
  >
    <!-- STEP 1: Age Gate (always first) -->
    <template v-if="currentStep === 'age-gate'">
      <AgeGateStep
        v-model="dateOfBirth"
        @age-evaluated="handleAgeEvaluated"
      />
      <VBtn
        color="primary"
        variant="flat"
        block
        class="mt-4"
        :disabled="!canProceedFromAgeGate"
        data-testid="age-gate-next"
        @click="handleAgeGateNext"
      >
        {{ t('onboarding.next') }}
      </VBtn>
    </template>

    <!-- STEP 2: Parental Consent (only for minors) -->
    <template v-else-if="currentStep === 'parental-consent'">
      <ParentalConsentStep
        :parent-email="parentEmail"
        :consent-given="consentGiven"
        :consent-tier="(ageResult?.consentTier as ConsentTier) ?? 'teen'"
        :age="ageResult?.age ?? 0"
        @update:parent-email="parentEmail = $event"
        @update:consent-given="consentGiven = $event"
      />
      <div class="d-flex gap-3 mt-4">
        <VBtn
          variant="text"
          data-testid="consent-back"
          @click="handleBack"
        >
          {{ t('onboarding.back') }}
        </VBtn>
        <VBtn
          color="primary"
          variant="flat"
          class="flex-grow-1"
          :disabled="!canProceedFromConsent"
          data-testid="consent-next"
          @click="handleConsentNext"
        >
          {{ t('onboarding.next') }}
        </VBtn>
      </div>
    </template>

    <!-- STEP 3: Credentials (email/password form) -->
    <template v-else-if="currentStep === 'credentials'">
      <VBtn
        variant="text"
        size="small"
        class="mb-3"
        data-testid="credentials-back"
        @click="handleBack"
      >
        <VIcon
          icon="tabler-arrow-left"
          start
          aria-hidden="true"
        />
        {{ t('onboarding.back') }}
      </VBtn>

      <EmailPasswordForm
        mode="register"
        :loading="loading"
        :error-message="errorMessage"
        @submit="handleSubmit"
      />
      <AuthProviderButtons mode="register" />
    </template>

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
