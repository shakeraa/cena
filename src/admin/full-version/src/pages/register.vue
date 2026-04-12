<script setup lang="ts">
import { VForm } from 'vuetify/components/VForm'

import { VNodeRenderer } from '@layouts/components/VNodeRenderer'
import { themeConfig } from '@themeConfig'

import authV2RegisterIllustrationBorderedDark from '@images/pages/auth-v2-register-illustration-bordered-dark.png'
import authV2RegisterIllustrationBorderedLight from '@images/pages/auth-v2-register-illustration-bordered-light.png'
import authV2RegisterIllustrationDark from '@images/pages/auth-v2-register-illustration-dark.png'
import authV2RegisterIllustrationLight from '@images/pages/auth-v2-register-illustration-light.png'
import authV2MaskDark from '@images/pages/misc-mask-dark.png'
import authV2MaskLight from '@images/pages/misc-mask-light.png'

const imageVariant = useGenerateImageVariant(authV2RegisterIllustrationLight,
  authV2RegisterIllustrationDark,
  authV2RegisterIllustrationBorderedLight,
  authV2RegisterIllustrationBorderedDark, true)

const authThemeMask = useGenerateImageVariant(authV2MaskLight, authV2MaskDark)

definePage({
  meta: {
    layout: 'blank',
    unauthenticatedOnly: true,
  },
})

const form = ref({
  username: '',
  email: '',
  password: '',
  privacyPolicies: false,
})

const isPasswordVisible = ref(false)
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
          style="padding-inline: 100px;"
        >
          <VImg
            max-width="500"
            :src="imageVariant"
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
      style="background-color: rgb(var(--v-theme-surface));"
    >
      <VCard
        flat
        :max-width="500"
        class="mt-12 mt-sm-0 pa-4"
      >
        <VCardText>
          <h1 class="text-h4 mb-1">
            {{ $t('auth.registerTitle') }}
          </h1>
          <p class="mb-0">
            {{ $t('auth.registerSubtitle') }}
          </p>
        </VCardText>

        <VCardText>
          <VForm @submit.prevent="() => {}">
            <VRow>
              <!-- Username -->
              <VCol cols="12">
                <AppTextField
                  v-model="form.username"
                  :rules="[requiredValidator]"
                  autofocus
                  :label="$t('auth.username')"
                  placeholder="Johndoe"
                />
              </VCol>

              <!-- email -->
              <VCol cols="12">
                <AppTextField
                  v-model="form.email"
                  :rules="[requiredValidator, emailValidator]"
                  :label="$t('auth.email')"
                  type="email"
                  placeholder="johndoe@email.com"
                />
              </VCol>

              <!-- password -->
              <VCol cols="12">
                <AppTextField
                  v-model="form.password"
                  :rules="[requiredValidator]"
                  :label="$t('auth.password')"
                  placeholder="············"
                  :type="isPasswordVisible ? 'text' : 'password'"
                  autocomplete="password"
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

                <!-- FIND-privacy-002: Link to real privacy policy and terms -->
                <div class="d-flex align-center my-6">
                  <VCheckbox
                    id="privacy-policy"
                    v-model="form.privacyPolicies"
                    inline
                  />
                  <VLabel
                    for="privacy-policy"
                    style="opacity: 1;"
                  >
                    <span class="me-1 text-high-emphasis">{{ $t('auth.iAgreeTo') }}</span>
                    <RouterLink
                      to="/privacy"
                      class="text-high-emphasis text-decoration-underline"
                      data-testid="admin-register-privacy-link"
                    >
                      {{ $t('auth.privacyPolicy') }}
                    </RouterLink>
                    <span class="mx-1 text-high-emphasis">{{ $t('auth.and') }}</span>
                    <RouterLink
                      to="/terms"
                      class="text-high-emphasis text-decoration-underline"
                      data-testid="admin-register-terms-link"
                    >
                      {{ $t('auth.termsOfService') }}
                    </RouterLink>
                  </VLabel>
                </div>

                <VBtn
                  block
                  type="submit"
                >
                  {{ $t('auth.signUp') }}
                </VBtn>
              </VCol>

              <!-- create account -->
              <VCol
                cols="12"
                class="text-center text-base"
              >
                <span class="d-inline-block">{{ $t('auth.alreadyHaveAccount') }}</span>
                <RouterLink
                  class="text-high-emphasis ms-1 d-inline-block text-decoration-underline"
                  :to="{ name: 'login' }"
                >
                  {{ $t('auth.signInInstead') }}
                </RouterLink>
              </VCol>

              <VCol
                cols="12"
                class="d-flex align-center"
              >
                <VDivider />
                <span class="mx-4">{{ $t('auth.or') }}</span>
                <VDivider />
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
