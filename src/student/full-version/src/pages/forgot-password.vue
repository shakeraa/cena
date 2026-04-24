<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/api/$api'

/**
 * Student forgot-password page (FIND-ux-006c).
 *
 * Consumes the real POST /api/auth/password-reset endpoint added in
 * FIND-ux-006b. The backend always returns 204 for both known and
 * unknown emails (OWASP account-enumeration defence), so the UI shows
 * a generic "check your email" message regardless.
 *
 * Error handling:
 *   - 204: generic success (never confirms email existence)
 *   - 400: client-side validation should prevent this
 *   - 429: rate-limited, ask user to wait
 *   - 503: Firebase unavailable, ask user to retry later
 *   - Other: generic error message
 */

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.forgotPassword',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()

const email = ref('')
const emailError = ref('')
const loading = ref(false)
const submitted = ref(false)
const errorMessage = ref('')

function validateEmail(): boolean {
  emailError.value = ''

  const trimmed = email.value.trim()
  if (!trimmed) {
    emailError.value = t('auth.emailRequired')

    return false
  }

  if (!/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/.test(trimmed)) {
    emailError.value = t('auth.emailInvalid')

    return false
  }

  return true
}

async function handleSubmit() {
  if (loading.value)
    return

  errorMessage.value = ''

  if (!validateEmail())
    return

  loading.value = true

  try {
    await $api('/auth/password-reset', {
      method: 'POST',
      body: { email: email.value.trim() },
    })

    // 204 No Content — always show generic success (OWASP defence).
    submitted.value = true
  }
  catch (err: unknown) {
    const status = (err as any)?.statusCode ?? (err as any)?.status ?? (err as any)?.response?.status ?? 0

    if (status === 429)
      errorMessage.value = t('auth.resetRateLimited')

    else if (status === 503)
      errorMessage.value = t('auth.resetServiceUnavailable')

    else
      errorMessage.value = t('auth.resetUnexpectedError')

    console.error('[forgot-password] POST /api/auth/password-reset failed', {
      status,
      email: email.value.trim(),
    })
  }
  finally {
    loading.value = false
  }
}
</script>

<template>
  <!-- Success state: after a successful POST (204) -->
  <StudentAuthCard
    v-if="submitted"
    :title="t('auth.checkEmail')"
    :subtitle="t('auth.checkEmailSubtitle')"
  >
    <div
      class="d-flex align-center justify-center mb-4"
      data-testid="forgot-success-icon"
    >
      <VIcon
        icon="tabler-mail-check"
        size="64"
        color="success"
        aria-hidden="true"
      />
    </div>
    <VBtn
      color="primary"
      variant="tonal"
      block
      to="/login"
      data-testid="forgot-return-to-login"
    >
      {{ t('auth.backToSignIn') }}
    </VBtn>
  </StudentAuthCard>

  <!-- Form state: email input + submit -->
  <StudentAuthCard
    v-else
    :title="t('auth.resetYourPassword')"
    :subtitle="t('auth.resetYourPasswordSubtitle')"
  >
    <VForm
      data-testid="forgot-password-form"
      @submit.prevent="handleSubmit"
    >
      <VTextField
        v-model="email"
        type="email"
        :label="t('auth.email')"
        :placeholder="t('auth.emailPlaceholder')"
        :error-messages="emailError"
        data-testid="forgot-email"
        autocomplete="email"
        class="mb-3"
      />
      <VAlert
        v-if="errorMessage"
        type="error"
        variant="tonal"
        density="compact"
        class="mb-3"
        data-testid="forgot-error"
      >
        {{ errorMessage }}
      </VAlert>
      <VBtn
        type="submit"
        color="primary"
        variant="flat"
        block
        :loading="loading"
        data-testid="forgot-submit"
      >
        {{ t('auth.resetPasswordCta') }}
      </VBtn>
    </VForm>
    <div class="mt-4 text-center">
      <RouterLink
        to="/login"
        data-testid="forgot-back-to-login"
        class="text-body-2 text-decoration-underline"
      >
        {{ t('auth.backToSignIn') }}
      </RouterLink>
    </div>
  </StudentAuthCard>
</template>
