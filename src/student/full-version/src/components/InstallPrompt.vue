<script setup lang="ts">
/**
 * PWA-002: Custom PWA install prompt (Chrome/Edge/Samsung).
 *
 * Shows a non-intrusive banner after the 2nd visit, respecting
 * the 7-day dismiss cooldown. No dark patterns — "Not now" is
 * equally prominent as "Install".
 *
 * This component is a children's educational app surface (students
 * aged 13-18) — no urgency language, no countdown, no restriction
 * of access behind install.
 */
import { useInstallPrompt } from '@/composables/useInstallPrompt'

const { canShow, isIOS, install, dismiss } = useInstallPrompt()

const { t } = useI18n()
</script>

<template>
  <!-- Chrome/Edge/Samsung install banner -->
  <VSnackbar
    v-if="!isIOS"
    v-model="canShow"
    location="bottom center"
    :timeout="-1"
    color="surface"
    rounded="lg"
    class="install-prompt"
    content-class="install-prompt__content"
  >
    <div class="d-flex align-center gap-3">
      <VAvatar
        size="40"
        rounded="lg"
        color="primary"
        variant="tonal"
      >
        <VIcon
          icon="tabler-download"
          size="24"
        />
      </VAvatar>
      <div class="flex-grow-1">
        <p class="text-body-1 font-weight-medium mb-0">
          {{ t('pwa.install.title') }}
        </p>
        <p class="text-body-2 text-medium-emphasis mb-0">
          {{ t('pwa.install.subtitle') }}
        </p>
      </div>
    </div>

    <template #actions>
      <VBtn
        variant="text"
        size="small"
        :aria-label="t('pwa.install.notNow')"
        @click="dismiss"
      >
        {{ t('pwa.install.notNow') }}
      </VBtn>
      <VBtn
        color="primary"
        variant="elevated"
        size="small"
        :aria-label="t('pwa.install.installButton')"
        @click="install"
      >
        <VIcon
          icon="tabler-download"
          start
          size="16"
        />
        {{ t('pwa.install.installButton') }}
      </VBtn>
    </template>
  </VSnackbar>

  <!-- iOS Safari install guide -->
  <IOSInstallGuide
    v-else-if="isIOS && canShow"
    @dismiss="dismiss"
  />
</template>

<style scoped>
.install-prompt {
  z-index: 1900;
}
</style>
