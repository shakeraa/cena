<script setup lang="ts">
/**
 * FIND-privacy-002: Privacy Policy page.
 *
 * Public route — no authentication required.
 * Renders the full adult privacy policy in the current locale (en/ar/he).
 * All text sourced from i18n keys under `legal.privacy.*`.
 *
 * Canonical source: docs/legal/privacy-policy.md
 * Legal review status: DRAFT — awaiting Cena legal counsel review.
 *
 * Compliance: GDPR Art 13/14, COPPA 16 CFR 312.4(d), ICO Children's Code Std 4+6,
 * Israel PPL 11.
 */
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'legal.privacy.title',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
</script>

<template>
  <VCard
    class="legal-page pa-6 pa-md-10 mx-auto my-6"
    max-width="800"
    elevation="2"
  >
    <h1
      class="text-h4 mb-2"
      data-testid="privacy-title"
    >
      {{ t('legal.privacy.title') }}
    </h1>
    <p class="text-body-2 text-medium-emphasis mb-6">
      {{ t('legal.privacy.lastUpdated') }}
      &mdash;
      {{ t('legal.privacy.draftNotice') }}
    </p>

    <!-- 1. Introduction -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.introTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.privacy.introBody') }}
      </p>
    </section>

    <!-- 2. Data We Collect -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.dataCollectedTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.privacy.dataCollectedIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.dataEmail') }}</li>
        <li>{{ t('legal.privacy.dataName') }}</li>
        <li>{{ t('legal.privacy.dataDob') }}</li>
        <li>{{ t('legal.privacy.dataLearning') }}</li>
        <li>{{ t('legal.privacy.dataTutor') }}</li>
        <li>{{ t('legal.privacy.dataDevice') }}</li>
      </ul>
    </section>

    <!-- 3. Purpose of Collection -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.purposeTitle') }}
      </h2>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.purposeAdaptive') }}</li>
        <li>{{ t('legal.privacy.purposeAuth') }}</li>
        <li>{{ t('legal.privacy.purposeProgress') }}</li>
        <li>{{ t('legal.privacy.purposeSafety') }}</li>
        <li>{{ t('legal.privacy.purposeImprove') }}</li>
      </ul>
    </section>

    <!-- 4. Legal Basis -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.legalBasisTitle') }}
      </h2>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.basisConsent') }}</li>
        <li>{{ t('legal.privacy.basisParental') }}</li>
        <li>{{ t('legal.privacy.basisContract') }}</li>
        <li>{{ t('legal.privacy.basisLegitimate') }}</li>
      </ul>
    </section>

    <!-- 5. Data Retention -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.retentionTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.privacy.retentionBody') }}
      </p>
    </section>

    <!-- 6. Third-Party Processors -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.processorsTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.privacy.processorsIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.processorAnthropic') }}</li>
        <li>{{ t('legal.privacy.processorFirebase') }}</li>
      </ul>
    </section>

    <!-- 7. Cross-Border Transfers -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.transfersTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.privacy.transfersBody') }}
      </p>
    </section>

    <!-- 8. Your Rights -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.rightsTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.privacy.rightsIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.rightAccess') }}</li>
        <li>{{ t('legal.privacy.rightRectify') }}</li>
        <li>{{ t('legal.privacy.rightErase') }}</li>
        <li>{{ t('legal.privacy.rightPortability') }}</li>
        <li>{{ t('legal.privacy.rightWithdraw') }}</li>
        <li>{{ t('legal.privacy.rightObject') }}</li>
        <li>{{ t('legal.privacy.rightComplain') }}</li>
      </ul>
    </section>

    <!-- 9. DSAR -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.dsarTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.privacy.dsarBody') }}
      </p>
    </section>

    <!-- 10. Parent Rights (COPPA + GDPR Art 8) -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.parentRightsTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.privacy.parentRightsIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.privacy.parentReview') }}</li>
        <li>{{ t('legal.privacy.parentDelete') }}</li>
        <li>{{ t('legal.privacy.parentRevoke') }}</li>
      </ul>
    </section>

    <!-- 11. Contact -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.privacy.contactTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.privacy.contactBody') }}
      </p>
      <p class="text-body-2 mt-2">
        {{ t('legal.privacy.dpoContact') }}
      </p>
    </section>

    <!-- Navigation -->
    <VDivider class="my-4" />
    <div class="d-flex flex-wrap gap-3">
      <VBtn
        variant="text"
        size="small"
        to="/privacy/children"
        data-testid="privacy-children-link"
      >
        {{ t('legal.privacy.childrenNoticeLink') }}
      </VBtn>
      <VBtn
        variant="text"
        size="small"
        to="/terms"
        data-testid="privacy-terms-link"
      >
        {{ t('legal.terms.title') }}
      </VBtn>
      <VSpacer />
      <VBtn
        variant="text"
        size="small"
        to="/login"
      >
        {{ t('auth.backToSignIn') }}
      </VBtn>
    </div>
  </VCard>
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
