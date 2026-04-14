<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { BossBattleSummary } from '@/api/types/common'

interface Props {
  boss: BossBattleSummary
  locked?: boolean
}

withDefaults(defineProps<Props>(), { locked: false })

const { t } = useI18n()
</script>

<template>
  <VCard
    :data-testid="`boss-${boss.bossBattleId}`"
    :variant="locked ? 'outlined' : 'flat'"
    :color="locked ? undefined : 'surface-variant'"
    class="boss-tile pa-4"
    :class="{ 'boss-tile--locked': locked }"
  >
    <div class="d-flex align-center">
      <VAvatar
        :color="locked ? 'grey-lighten-2' : 'error'"
        size="48"
        class="me-3"
      >
        <VIcon
          :icon="locked ? 'tabler-lock' : 'tabler-skull'"
          size="24"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ boss.name }}
        </div>
        <div class="d-flex align-center text-caption text-medium-emphasis mt-1 ga-2">
          <VChip
            size="x-small"
            variant="tonal"
          >
            {{ boss.subject }}
          </VChip>
          <span>·</span>
          <span>{{ t(`challenges.difficulty.${boss.difficulty}`) }}</span>
          <template v-if="locked">
            <span>·</span>
            <span data-testid="boss-lock-reason">
              {{ t('challenges.boss.requiresLevel', { level: boss.requiredMasteryLevel }) }}
            </span>
          </template>
        </div>
      </div>
      <VIcon
        v-if="!locked"
        icon="tabler-chevron-right"
        class="ms-2 text-medium-emphasis"
        aria-hidden="true"
      />
    </div>
  </VCard>
</template>

<style scoped>
.boss-tile--locked {
  opacity: 0.7;
}
</style>
