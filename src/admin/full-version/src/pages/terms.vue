<script setup lang="ts">
/**
 * FIND-privacy-002: Admin Terms of Service page.
 *
 * Public route — no authentication required (layout: blank).
 * Mirrors the student terms of service but uses the admin layout.
 * All text sourced from i18n keys under `legal.terms.*`.
 *
 * Canonical source: docs/legal/terms-of-service.md
 * Legal review status: DRAFT — awaiting Cena legal counsel review.
 */
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'blank',
    unauthenticatedOnly: false,
  },
})

const { t } = useI18n()
</script>

<template>
  <VContainer
    class="d-flex justify-center"
    fluid
  >
    <VCard
      class="legal-page pa-6 pa-md-10 my-6"
      max-width="800"
      elevation="2"
    >
      <h1
        class="text-h4 mb-2"
        data-testid="admin-terms-title"
      >
        {{ t('legal.terms.title') }}
      </h1>
      <p class="text-body-2 text-medium-emphasis mb-6">
        {{ t('legal.terms.lastUpdated') }}
        &mdash;
        {{ t('legal.terms.draftNotice') }}
      </p>

      <!-- 1. Acceptance -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.acceptanceTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.acceptanceBody') }}
        </p>
      </section>

      <!-- 2. The Service -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.serviceTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.serviceBody') }}
        </p>
      </section>

      <!-- 3. Eligibility -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.eligibilityTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.eligibilityBody') }}
        </p>
      </section>

      <!-- 4. Accounts -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.accountsTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.accountsBody') }}
        </p>
      </section>

      <!-- 5. Acceptable Use -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.acceptableUseTitle') }}
        </h2>
        <p class="text-body-2 mb-2">
          {{ t('legal.terms.acceptableUseIntro') }}
        </p>
        <ul class="text-body-2 ms-4">
          <li>{{ t('legal.terms.useNoHarm') }}</li>
          <li>{{ t('legal.terms.useNoReverse') }}</li>
          <li>{{ t('legal.terms.useNoScrape') }}</li>
          <li>{{ t('legal.terms.useNoImpersonate') }}</li>
          <li>{{ t('legal.terms.useNoAbuse') }}</li>
        </ul>
      </section>

      <!-- 6. Intellectual Property -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.ipTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.ipBody') }}
        </p>
      </section>

      <!-- 7. AI Tutor -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.aiTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.aiBody') }}
        </p>
      </section>

      <!-- 8. Disclaimers -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.disclaimerTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.disclaimerBody') }}
        </p>
      </section>

      <!-- 9. Limitation of Liability -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.liabilityTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.liabilityBody') }}
        </p>
      </section>

      <!-- 10. Termination -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.terminationTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.terminationBody') }}
        </p>
      </section>

      <!-- 11. Governing Law -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.governingLawTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.governingLawBody') }}
        </p>
      </section>

      <!-- 12. Changes -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.changesTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.changesBody') }}
        </p>
      </section>

      <!-- 13. Contact -->
      <section class="mb-6">
        <h2 class="text-h6 mb-2">
          {{ t('legal.terms.contactTitle') }}
        </h2>
        <p class="text-body-2">
          {{ t('legal.terms.contactBody') }}
        </p>
      </section>

      <!-- Navigation -->
      <VDivider class="my-4" />
      <div class="d-flex flex-wrap gap-3">
        <VBtn
          variant="text"
          size="small"
          to="/privacy"
          data-testid="admin-terms-privacy-link"
        >
          {{ t('legal.privacy.title') }}
        </VBtn>
        <VSpacer />
        <VBtn
          variant="text"
          size="small"
          to="/login"
        >
          Back to sign in
        </VBtn>
      </div>
    </VCard>
  </VContainer>
</template>

<style scoped>
.legal-page {
  inline-size: 100%;
}

.legal-page ul {
  list-style: disc;
  padding-inline-start: 1.5rem;
}

.legal-page li {
  margin-block-end: 0.25rem;
}
</style>
