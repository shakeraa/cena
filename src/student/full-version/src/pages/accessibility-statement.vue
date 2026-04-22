<script setup lang="ts">
/**
 * PRR-A11Y-STATEMENT-ROUTE — `/accessibility-statement` page.
 *
 * IL Reg 5773-2013 §35(b) requires every public-facing site to publish
 * an accessibility statement containing:
 *   (1) declared conformance level — we target WCAG 2.1 AA.
 *   (2) known exceptions / remediation plan — bullet list below.
 *   (3) assistive tech tested — NVDA, JAWS, VoiceOver, TalkBack.
 *   (4) contact method — accessibility@cena.app.
 *
 * The page is reachable both from the A11yToolbar footer link (visible on
 * every layout, including auth + blank/onboarding) AND as a pre-auth
 * route because §35 mandates pre-auth reachability.
 *
 * Layout: `auth` — the same layout used by the Privacy Policy (auth.vue
 * mounts the A11yToolbar even when unauthenticated so the link loops back
 * to itself).
 *
 * All copy is under the `accessibilityStatement.*` key in en/ar/he.
 * The last-updated date is pulled from a module-level constant so legal
 * can bump it without touching translations.
 */
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

// Bump this when legal reviews the statement. Displayed as YYYY-MM-DD in
// every locale so dates read uniformly regardless of direction.
const LAST_REVIEWED_ISO = '2026-04-21'

definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'accessibilityStatement.title',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t, locale } = useI18n()

const formattedDate = computed(() => {
  // Force ISO for legal clarity. Wrap in LTR bdi at the template site so
  // Hebrew/Arabic pages don't visually flip digits (user rule: math-always-LTR).
  return LAST_REVIEWED_ISO
})

const contactEmail = 'accessibility@cena.app'
</script>

<template>
  <VCard
    class="a11y-statement-page pa-6 pa-md-10 mx-auto my-6"
    max-width="800"
    elevation="2"
  >
    <h1
      class="text-h4 mb-2"
      data-testid="a11y-statement-title"
    >
      {{ t('accessibilityStatement.title') }}
    </h1>

    <p class="text-body-2 text-medium-emphasis mb-6">
      {{ t('accessibilityStatement.lastReviewedLabel') }}
      &mdash;
      <bdi dir="ltr" data-testid="a11y-statement-last-reviewed">{{ formattedDate }}</bdi>
    </p>

    <!-- 1. Commitment / Intro -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.commitmentTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('accessibilityStatement.commitmentBody') }}
      </p>
    </section>

    <!-- 2. Conformance level (§35(b)(1)) -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.conformanceTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('accessibilityStatement.conformanceBody') }}
      </p>
    </section>

    <!-- 3. Features provided -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.featuresTitle') }}
      </h2>
      <ul class="text-body-2 ms-4">
        <li>{{ t('accessibilityStatement.featureToolbar') }}</li>
        <li>{{ t('accessibilityStatement.featureKeyboard') }}</li>
        <li>{{ t('accessibilityStatement.featureLanguage') }}</li>
        <li>{{ t('accessibilityStatement.featureContrast') }}</li>
        <li>{{ t('accessibilityStatement.featureTextSize') }}</li>
        <li>{{ t('accessibilityStatement.featureMotion') }}</li>
      </ul>
    </section>

    <!-- 4. Assistive tech tested (§35(b)(3)) -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.assistiveTechTitle') }}
      </h2>
      <ul class="text-body-2 ms-4">
        <li>{{ t('accessibilityStatement.atNvda') }}</li>
        <li>{{ t('accessibilityStatement.atJaws') }}</li>
        <li>{{ t('accessibilityStatement.atVoiceover') }}</li>
        <li>{{ t('accessibilityStatement.atTalkback') }}</li>
      </ul>
    </section>

    <!-- 5. Known limitations / exceptions (§35(b)(2)) -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.limitationsTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('accessibilityStatement.limitationsIntro') }}
      </p>
      <ul class="text-body-2 ms-4">
        <li>{{ t('accessibilityStatement.limitationMath') }}</li>
        <li>{{ t('accessibilityStatement.limitationDiagrams') }}</li>
        <li>{{ t('accessibilityStatement.limitationThirdParty') }}</li>
      </ul>
    </section>

    <!-- 6. Remediation plan -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.remediationTitle') }}
      </h2>
      <p class="text-body-2">
        {{ t('accessibilityStatement.remediationBody') }}
      </p>
    </section>

    <!-- 7. Contact (§35(b)(4)) -->
    <section class="mb-6">
      <h2 class="text-h6 mb-2">
        {{ t('accessibilityStatement.contactTitle') }}
      </h2>
      <p class="text-body-2 mb-2">
        {{ t('accessibilityStatement.contactBody') }}
      </p>
      <p class="text-body-2">
        <a
          :href="`mailto:${contactEmail}`"
          data-testid="a11y-statement-contact-link"
        >
          <bdi dir="ltr">{{ contactEmail }}</bdi>
        </a>
      </p>
    </section>

    <!-- Navigation -->
    <VDivider class="my-4" />
    <div class="d-flex flex-wrap gap-3">
      <VBtn
        variant="text"
        size="small"
        to="/privacy"
      >
        {{ t('accessibilityStatement.privacyLink') }}
      </VBtn>
      <VSpacer />
      <VBtn
        variant="text"
        size="small"
        to="/login"
        data-testid="a11y-statement-back-to-signin"
      >
        {{ t('accessibilityStatement.backToSignIn') }}
      </VBtn>
    </div>
  </VCard>
</template>

<style scoped>
.a11y-statement-page {
  inline-size: 100%;
}

.a11y-statement-page ul {
  list-style: disc;
  padding-inline-start: 1.5rem;
}

.a11y-statement-page li {
  margin-block-end: 0.25rem;
}
</style>
