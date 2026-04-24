<script setup lang="ts">
import { VForm } from 'vuetify/components/VForm'
import { useFirebaseAuth } from '@/composables/useFirebaseAuth'
import { useGenerateImageVariant } from '@core/composable/useGenerateImageVariant'
import authV2LoginIllustrationBorderedDark from '@images/pages/auth-v2-login-illustration-bordered-dark.png'
import authV2LoginIllustrationBorderedLight from '@images/pages/auth-v2-login-illustration-bordered-light.png'
import authV2LoginIllustrationDark from '@images/pages/auth-v2-login-illustration-dark.png'
import authV2LoginIllustrationLight from '@images/pages/auth-v2-login-illustration-light.png'
import authV2MaskDark from '@images/pages/misc-mask-dark.png'
import authV2MaskLight from '@images/pages/misc-mask-light.png'
import { VNodeRenderer } from '@layouts/components/VNodeRenderer'
import { themeConfig } from '@themeConfig'

const authThemeImg = useGenerateImageVariant(authV2LoginIllustrationLight, authV2LoginIllustrationDark, authV2LoginIllustrationBorderedLight, authV2LoginIllustrationBorderedDark, true)

const authThemeMask = useGenerateImageVariant(authV2MaskLight, authV2MaskDark)

definePage({
  meta: {
    layout: 'blank',
    unauthenticatedOnly: true,
  },
})

const isPasswordVisible = ref(false)
const isSubmitting = ref(false)

const route = useRoute()
const router = useRouter()

const { loginWithEmail, loginWithGoogle, loginWithApple, authError } = useFirebaseAuth()

const refVForm = ref<VForm>()

const credentials = ref({
  email: '',
  password: '',
})

const rememberMe = ref(false)

const login = async () => {
  isSubmitting.value = true
  try {
    await loginWithEmail(credentials.value.email, credentials.value.password)

    await nextTick(() => {
      router.replace(route.query.to ? String(route.query.to) : '/')
    })
  }
  catch {
    // Error is set in authError by useFirebaseAuth
  }
  finally {
    isSubmitting.value = false
  }
}

const handleGoogleLogin = async () => {
  isSubmitting.value = true
  try {
    await loginWithGoogle()

    await nextTick(() => {
      router.replace(route.query.to ? String(route.query.to) : '/')
    })
  }
  catch {
    // Error is set in authError by useFirebaseAuth
  }
  finally {
    isSubmitting.value = false
  }
}

const handleAppleLogin = async () => {
  isSubmitting.value = true
  try {
    await loginWithApple()

    await nextTick(() => {
      router.replace(route.query.to ? String(route.query.to) : '/')
    })
  }
  catch {
    // Error is set in authError by useFirebaseAuth
  }
  finally {
    isSubmitting.value = false
  }
}

const onSubmit = () => {
  refVForm.value?.validate()
    .then(({ valid: isValid }) => {
      if (isValid)
        login()
    })
}
</script>

<template>
  <RouterLink to="/">
    <div class="auth-logo d-flex align-center gap-x-3">
      <VNodeRenderer :nodes="themeConfig.app.logo" />
      <!-- FIND-ux-026: Brand mark demoted from h1 to span; not a content heading -->
      <span class="auth-title">
        {{ themeConfig.app.brandTitle ?? themeConfig.app.title }}
      </span>
    </div>
  </RouterLink>

  <VRow
    no-gutters
    class="auth-wrapper bg-surface"
  >
    <VCol
      md="8"
      class="d-none d-md-flex"
    >
      <div class="position-relative bg-background w-100 me-0">
        <div
          class="d-flex align-center justify-center w-100 h-100"
          style="padding-inline: 6.25rem;"
        >
          <VImg
            max-width="613"
            :src="authThemeImg"
            class="auth-illustration mt-16 mb-2"
          />
        </div>

        <img
          class="auth-footer-mask"
          :src="authThemeMask"
          alt="auth-footer-mask"
          height="280"
          width="100"
        >
      </div>
    </VCol>

    <VCol
      cols="12"
      md="4"
      class="auth-card-v2 d-flex align-center justify-center"
    >
      <VCard
        flat
        :max-width="500"
        class="mt-12 mt-sm-0 pa-4"
      >
        <VCardText>
          <h1 class="text-h4 mb-1">
            {{ $t('auth.cenaAdmin') }}
          </h1>
          <p class="mb-0">
            {{ $t('auth.signInSubtitle') }}
          </p>
        </VCardText>

        <VCardText v-if="authError">
          <VAlert
            color="error"
            variant="tonal"
            closable
            @click:close="authError = null"
          >
            {{ authError }}
          </VAlert>
        </VCardText>

        <VCardText>
          <VForm
            ref="refVForm"
            @submit.prevent="onSubmit"
          >
            <VRow>
              <!-- email -->
              <VCol cols="12">
                <AppTextField
                  v-model="credentials.email"
                  :label="$t('auth.email')"
                  placeholder="admin@cena.edu"
                  type="email"
                  autofocus
                  :rules="[requiredValidator, emailValidator]"
                  :disabled="isSubmitting"
                />
              </VCol>

              <!-- password -->
              <VCol cols="12">
                <AppTextField
                  v-model="credentials.password"
                  :label="$t('auth.password')"
                  placeholder="············"
                  :rules="[requiredValidator]"
                  :type="isPasswordVisible ? 'text' : 'password'"
                  autocomplete="current-password"
                  :disabled="isSubmitting"
                >
                  <template #append-inner>
                    <IconBtn
                      data-testid="password-toggle-btn"
                      size="small"
                      :aria-label="isPasswordVisible ? $t('auth.hidePassword') : $t('auth.showPassword')"
                      @click="isPasswordVisible = !isPasswordVisible"
                    >
                      <VIcon
                        :icon="isPasswordVisible ? 'tabler-eye-off' : 'tabler-eye'"
                        size="20"
                      />
                    </IconBtn>
                  </template>
                </AppTextField>

                <div class="d-flex align-center flex-wrap justify-space-between my-6">
                  <VCheckbox
                    v-model="rememberMe"
                    :label="$t('auth.rememberMe')"
                  />
                  <RouterLink
                    class="text-high-emphasis ms-2 mb-1 text-decoration-underline"
                    :to="{ name: 'forgot-password' }"
                  >
                    {{ $t('auth.forgotPassword') }}
                  </RouterLink>
                </div>

                <VBtn
                  block
                  type="submit"
                  :loading="isSubmitting"
                  :disabled="isSubmitting"
                >
                  {{ $t('auth.signIn') }}
                </VBtn>
              </VCol>

              <!-- divider -->
              <VCol
                cols="12"
                class="d-flex align-center"
              >
                <VDivider />
                <span class="mx-4">{{ $t('auth.or') }}</span>
                <VDivider />
              </VCol>

              <!-- Google sign-in -->
              <VCol
                cols="12"
                class="text-center"
              >
                <VBtn
                  variant="outlined"
                  block
                  :disabled="isSubmitting"
                  @click="handleGoogleLogin"
                >
                  <VIcon
                    icon="tabler-brand-google"
                    size="20"
                    class="me-2"
                  />
                  {{ $t('auth.signInWithGoogle') }}
                </VBtn>

                <VBtn
                  variant="outlined"
                  block
                  :disabled="isSubmitting"
                  class="mt-3"
                  @click="handleAppleLogin"
                >
                  <VIcon
                    icon="tabler-brand-apple"
                    size="20"
                    class="me-2"
                  />
                  {{ $t('auth.signInWithApple') }}
                </VBtn>
              </VCol>

              <!-- FIND-privacy-002: Legal links on admin login page -->
              <VCol
                cols="12"
                class="text-center text-caption text-medium-emphasis"
                data-testid="admin-login-legal-links"
              >
                <RouterLink
                  to="/privacy"
                  class="text-medium-emphasis text-decoration-underline"
                  data-testid="admin-login-privacy-link"
                >
                  {{ $t('auth.privacyPolicy') }}
                </RouterLink>
                <span class="mx-1">&middot;</span>
                <RouterLink
                  to="/terms"
                  class="text-medium-emphasis text-decoration-underline"
                  data-testid="admin-login-terms-link"
                >
                  {{ $t('auth.termsOfService') }}
                </RouterLink>
              </VCol>
            </VRow>
          </VForm>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>

<style lang="scss">
@use "@core/scss/template/pages/page-auth";
</style>
