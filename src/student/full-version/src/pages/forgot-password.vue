<script setup lang="ts">
import { useI18n } from 'vue-i18n'

/**
 * Student forgot-password page.
 *
 * Earlier iterations of this page shipped a form that called no network
 * endpoint, waited 120ms, and flipped a `submitted` boolean — showing a
 * fake "check your email" success state to a user who had typed a real
 * email and expected a real recovery email. See docs/reviews/agent-5-ux-
 * findings.md#FIND-ux-006 for the failure trace.
 *
 * Until the student web app has a real end-to-end password-reset path
 * (student-facing backend endpoint that wraps the Firebase Admin SDK, or
 * the Firebase Auth web SDK wired directly into the app), this route
 * renders an honest unavailable state. It:
 *
 *   - Tells the user the self-service flow isn't wired in this build.
 *   - Directs them to contact their school admin, who has a real
 *     force-password-reset path via the admin console.
 *   - Offers a clear way back to the sign-in page.
 *
 * No form field, no network call, no success toast. The rule this
 * enforces is the user's locked product rule: labels must match data,
 * and we do not ship success UI that lies.
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
</script>

<template>
  <StudentAuthCard
    :title="t('auth.resetUnavailableTitle')"
    :subtitle="t('auth.resetYourPasswordSubtitle')"
  >
    <div
      class="d-flex align-center justify-center mb-4"
      data-testid="forgot-unavailable-icon"
    >
      <VIcon
        icon="tabler-lock-off"
        size="64"
        color="primary"
        aria-hidden="true"
      />
    </div>
    <p
      class="text-body-2 text-medium-emphasis mb-6"
      data-testid="forgot-unavailable-body"
    >
      {{ t('auth.resetUnavailableBody') }}
    </p>
    <VBtn
      color="primary"
      variant="tonal"
      block
      to="/login"
      data-testid="forgot-return-to-login"
    >
      {{ t('auth.backToSignIn') }}
    </VBtn>
    <template #footer>
      <span
        class="text-body-2 text-medium-emphasis"
        data-testid="forgot-contact-admin"
      >
        {{ t('auth.resetContactAdmin') }}
      </span>
    </template>
  </StudentAuthCard>
</template>
