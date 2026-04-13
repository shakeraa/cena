<script setup lang="ts">
/**
 * PWA-002: iOS Safari install guide.
 *
 * iOS does not fire `beforeinstallprompt`. Instead, we show a gentle
 * tooltip explaining the "Share → Add to Home Screen" flow with a
 * visual guide. Only shown on 2nd visit and dismissed for 14 days.
 *
 * Detection: iOS Safari + NOT running in standalone mode.
 */

const emit = defineEmits<{
  dismiss: []
}>()

const { t } = useI18n()

const show = ref(true)

function handleDismiss() {
  show.value = false
  emit('dismiss')
}
</script>

<template>
  <VDialog
    v-model="show"
    max-width="360"
    persistent
    class="ios-install-guide"
  >
    <VCard rounded="lg">
      <VCardTitle class="text-center pt-6">
        <VAvatar
          size="56"
          rounded="lg"
          color="primary"
          variant="tonal"
          class="mb-3"
        >
          <VIcon icon="tabler-device-mobile-plus" size="32" />
        </VAvatar>
        <div class="text-h6">
          {{ t('pwa.ios.title') }}
        </div>
      </VCardTitle>

      <VCardText class="text-center">
        <p class="text-body-1 mb-4">
          {{ t('pwa.ios.description') }}
        </p>

        <VList density="compact" class="text-start">
          <VListItem prepend-icon="tabler-share">
            <VListItemTitle>
              {{ t('pwa.ios.step1') }}
            </VListItemTitle>
          </VListItem>
          <VListItem prepend-icon="tabler-square-plus">
            <VListItemTitle>
              {{ t('pwa.ios.step2') }}
            </VListItemTitle>
          </VListItem>
          <VListItem prepend-icon="tabler-check">
            <VListItemTitle>
              {{ t('pwa.ios.step3') }}
            </VListItemTitle>
          </VListItem>
        </VList>
      </VCardText>

      <VCardActions class="justify-center pb-5">
        <VBtn
          color="primary"
          variant="elevated"
          :aria-label="t('pwa.ios.gotIt')"
          @click="handleDismiss"
        >
          {{ t('pwa.ios.gotIt') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.ios-install-guide :deep(.v-list-item) {
  min-height: 40px;
}
</style>
