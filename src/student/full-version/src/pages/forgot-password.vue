<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

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
const submitted = ref(false)
const loading = ref(false)

async function handleSubmit() {
  emailError.value = ''
  if (!email.value.trim()) {
    emailError.value = t('auth.emailRequired')

    return
  }
  if (!/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/.test(email.value.trim())) {
    emailError.value = t('auth.emailInvalid')

    return
  }

  loading.value = true
  await new Promise(resolve => setTimeout(resolve, 120))
  loading.value = false

  // Phase A: no real Firebase call. STU-W-04C wires `sendPasswordResetEmail`.
  submitted.value = true
}
</script>

<template>
  <StudentAuthCard
    v-if="!submitted"
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
        class="mb-4"
      />
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
    <template #footer>
      <RouterLink
        to="/login"
        data-testid="forgot-back-link"
        class="text-decoration-underline"
      >
        {{ t('auth.backToSignIn') }}
      </RouterLink>
    </template>
  </StudentAuthCard>

  <StudentAuthCard
    v-else
    :title="t('auth.checkEmail')"
    :subtitle="t('auth.checkEmailSubtitle')"
  >
    <div
      class="d-flex align-center justify-center mb-4"
      data-testid="forgot-confirmation"
    >
      <VIcon
        icon="tabler-mail-check"
        size="64"
        color="primary"
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
</template>
