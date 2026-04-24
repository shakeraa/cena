<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useServiceWorker } from '@/composables/useServiceWorker'

const { t } = useI18n()

const {
  needRefresh,
  hasActiveSession,
  updateApp,
  dismissUpdate,
} = useServiceWorker()
</script>

<template>
  <VSnackbar
    v-model="needRefresh"
    location="bottom center"
    :timeout="-1"
    color="surface"
    rounded="lg"
    multi-line
  >
    <span v-if="hasActiveSession">
      {{ t('pwa.updateAfterSession') }}
    </span>
    <span v-else>
      {{ t('pwa.updateAvailable') }}
    </span>

    <template #actions>
      <VBtn
        variant="text"
        size="small"
        @click="dismissUpdate"
      >
        {{ t('pwa.later') }}
      </VBtn>
      <VBtn
        color="primary"
        variant="flat"
        size="small"
        :disabled="hasActiveSession"
        @click="updateApp"
      >
        {{ t('pwa.update') }}
      </VBtn>
    </template>
  </VSnackbar>
</template>
