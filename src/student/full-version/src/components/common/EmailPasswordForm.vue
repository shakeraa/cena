<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  mode: 'login' | 'register'
  loading?: boolean
  errorMessage?: string
  submitLocked?: boolean
  lockedSecondsRemaining?: number
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  errorMessage: '',
  submitLocked: false,
  lockedSecondsRemaining: 0,
})

const emit = defineEmits<{
  submit: [value: { email: string; password: string; displayName?: string }]
}>()

const { t } = useI18n()

const email = ref('')
const password = ref('')
const displayName = ref('')

const emailError = ref('')
const passwordError = ref('')
const displayNameError = ref('')

function validate(): boolean {
  emailError.value = ''
  passwordError.value = ''
  displayNameError.value = ''

  if (!email.value.trim())
    emailError.value = t('auth.emailRequired')

  else if (!/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/.test(email.value.trim()))
    emailError.value = t('auth.emailInvalid')

  if (!password.value)
    passwordError.value = t('auth.passwordRequired')

  else if (password.value.length < 6)
    passwordError.value = t('auth.passwordMinLength')

  if (props.mode === 'register' && !displayName.value.trim())
    displayNameError.value = t('auth.displayNameRequired')

  return !emailError.value && !passwordError.value && !displayNameError.value
}

function handleSubmit() {
  if (props.submitLocked || props.loading)
    return
  if (!validate())
    return

  const payload: { email: string; password: string; displayName?: string } = {
    email: email.value.trim(),
    password: password.value,
  }

  if (props.mode === 'register')
    payload.displayName = displayName.value.trim()
  emit('submit', payload)
}

const ctaLabel = computed(() => {
  if (props.submitLocked && props.lockedSecondsRemaining > 0)
    return t('auth.tryAgainInSeconds', { seconds: props.lockedSecondsRemaining })

  return props.mode === 'login' ? t('auth.signInCta') : t('auth.signUpCta')
})
</script>

<template>
  <VForm
    class="email-password-form"
    data-testid="email-password-form"
    @submit.prevent="handleSubmit"
  >
    <VTextField
      v-if="mode === 'register'"
      v-model="displayName"
      :label="t('auth.displayName')"
      :placeholder="t('auth.displayNamePlaceholder')"
      :error-messages="displayNameError"
      data-testid="auth-display-name"
      autocomplete="name"
      class="mb-3"
    />
    <VTextField
      v-model="email"
      type="email"
      :label="t('auth.email')"
      :placeholder="t('auth.emailPlaceholder')"
      :error-messages="emailError"
      data-testid="auth-email"
      autocomplete="email"
      class="mb-3"
    />
    <VTextField
      v-model="password"
      type="password"
      :label="t('auth.password')"
      :placeholder="t('auth.passwordPlaceholder')"
      :error-messages="passwordError"
      data-testid="auth-password"
      :autocomplete="mode === 'login' ? 'current-password' : 'new-password'"
      class="mb-3"
    />
    <VAlert
      v-if="errorMessage"
      type="error"
      variant="tonal"
      density="compact"
      class="mb-3"
      data-testid="auth-error"
    >
      {{ errorMessage }}
    </VAlert>
    <VBtn
      type="submit"
      color="primary"
      variant="flat"
      block
      :loading="loading"
      :disabled="submitLocked"
      data-testid="auth-submit"
    >
      {{ ctaLabel }}
    </VBtn>
  </VForm>
</template>
