<script setup lang="ts">
/**
 * FIND-privacy-002: Children's Privacy Notice.
 *
 * Public route — no authentication required.
 * Written at Flesch-Kincaid grade level <= 5 for child readability.
 * All text sourced from i18n keys under `legal.children.*`.
 *
 * Canonical source: docs/legal/privacy-policy-children.md
 * Legal review status: DRAFT — awaiting Cena legal counsel review.
 *
 * Compliance: COPPA 16 CFR 312.4(d), GDPR Art 8, ICO Children's Code Std 4+6,
 * Israel PPL 11.
 */
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'legal.children.title',
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
      data-testid="children-privacy-title"
    >
      {{ t('legal.children.title') }}
    </h1>
    <p class="text-body-2 text-medium-emphasis mb-6">
      {{ t('legal.children.lastUpdated') }}
      &mdash;
      {{ t('legal.children.draftNotice') }}
    </p>

    <!-- 1. What is this page? -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.whatIsThisTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.whatIsThisBody') }}
      </p>
    </section>

    <!-- 2. What information do we collect? -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.whatWeCollectTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.children.whatWeCollectIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.children.collectName') }}</li>
        <li>{{ t('legal.children.collectEmail') }}</li>
        <li>{{ t('legal.children.collectAge') }}</li>
        <li>{{ t('legal.children.collectAnswers') }}</li>
        <li>{{ t('legal.children.collectChat') }}</li>
      </ul>
    </section>

    <!-- 3. Why do we collect it? -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.whyWeCollectTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.whyWeCollectBody') }}
      </p>
    </section>

    <!-- 4. No ads, no tracking -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.noAdsTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.noAdsBody') }}
      </p>
    </section>

    <!-- 5. Parents: your permission matters -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.parentPermissionTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.parentPermissionBody') }}
      </p>
    </section>

    <!-- 6. Parents: what you can do -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.parentActionsTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('legal.children.parentActionsIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('legal.children.parentActionSee') }}</li>
        <li>{{ t('legal.children.parentActionDelete') }}</li>
        <li>{{ t('legal.children.parentActionStop') }}</li>
      </ul>
    </section>

    <!-- 7. How we keep data safe -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.safetyTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.safetyBody') }}
      </p>
    </section>

    <!-- 8. How long we keep data -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.retentionTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.retentionBody') }}
      </p>
    </section>

    <!-- 9. Contact us -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('legal.children.contactTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('legal.children.contactBody') }}
      </p>
    </section>

    <!-- Navigation -->
    <VDivider class="my-4" />
    <div class="d-flex flex-wrap gap-3">
      <VBtn
        variant="text"
        size="small"
        to="/privacy"
        data-testid="children-privacy-adult-link"
      >
        {{ t('legal.privacy.title') }}
      </VBtn>
      <VBtn
        variant="text"
        size="small"
        to="/terms"
        data-testid="children-terms-link"
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
