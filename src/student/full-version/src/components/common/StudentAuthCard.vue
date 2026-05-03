<script setup lang="ts">
/**
 * FIND-privacy-002: Added legal footer links (Privacy Policy, Terms, Children's Notice)
 * to every auth page via this shared card component.
 */
import { useI18n } from 'vue-i18n'

interface Props {
  title: string
  subtitle?: string
}

defineProps<Props>()

const { t } = useI18n()
</script>

<template>
  <VCard
    class="student-auth-card pa-6"
    elevation="8"
    max-width="440"
  >
    <div class="text-h4 mb-1">
      {{ title }}
    </div>
    <div
      v-if="subtitle"
      class="text-body-2 text-medium-emphasis mb-6"
    >
      {{ subtitle }}
    </div>
    <slot />
    <div
      v-if="$slots.footer"
      class="student-auth-card__footer mt-6 text-center text-body-2"
    >
      <slot name="footer" />
    </div>
    <!-- FIND-privacy-002: Legal links on every auth page -->
    <div
      class="student-auth-card__legal mt-4 text-center text-caption text-medium-emphasis"
      data-testid="auth-legal-links"
    >
      <RouterLink
        to="/privacy"
        class="text-medium-emphasis text-decoration-underline"
        data-testid="auth-privacy-link"
      >
        {{ t('legal.footer.privacyLink') }}
      </RouterLink>
      <span class="mx-1">&middot;</span>
      <RouterLink
        to="/terms"
        class="text-medium-emphasis text-decoration-underline"
        data-testid="auth-terms-link"
      >
        {{ t('legal.footer.termsLink') }}
      </RouterLink>
      <span class="mx-1">&middot;</span>
      <RouterLink
        to="/privacy/children"
        class="text-medium-emphasis text-decoration-underline"
        data-testid="auth-children-link"
      >
        {{ t('legal.footer.childrenLink') }}
      </RouterLink>
    </div>
  </VCard>
</template>

<style scoped>
.student-auth-card {
  inline-size: 100%;
  max-inline-size: 440px;
  margin-inline: auto;
}

.student-auth-card__footer {
  border-block-start: 1px solid rgb(var(--v-theme-on-surface) / 0.08);
  padding-block-start: 1rem;
}
</style>
