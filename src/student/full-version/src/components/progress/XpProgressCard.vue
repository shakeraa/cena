<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { XpStatusDto } from '@/api/types/common'

interface Props {
  xp: XpStatusDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const percent = computed(() => {
  if (props.xp.xpToNextLevel <= 0)
    return 0

  return Math.min(100, Math.round((props.xp.currentXp / props.xp.xpToNextLevel) * 100))
})

const xpRemaining = computed(() => Math.max(0, props.xp.xpToNextLevel - props.xp.currentXp))
</script>

<template>
  <VCard
    class="xp-progress-card pa-5"
    variant="flat"
    color="primary"
    data-testid="xp-progress-card"
  >
    <div class="d-flex align-center justify-space-between mb-3">
      <div>
        <div class="text-caption text-white opacity-80">
          {{ t('gamification.xp.levelLabel') }}
        </div>
        <div
          class="text-h3 font-weight-bold text-white"
          data-testid="xp-current-level"
        >
          {{ xp.currentLevel }}
        </div>
      </div>
      <VIcon
        icon="tabler-bolt"
        size="48"
        color="yellow-accent-2"
        aria-hidden="true"
      />
    </div>

    <VProgressLinear
      :model-value="percent"
      color="white"
      bg-color="white"
      bg-opacity="0.3"
      height="10"
      rounded
      :aria-label="t('gamification.xp.progressAria', { percent })"
      data-testid="xp-progress-bar"
    />

    <div class="d-flex align-center justify-space-between mt-2">
      <div
        class="text-caption text-white opacity-90"
        data-testid="xp-current-xp"
      >
        {{ t('gamification.xp.currentXp', { current: xp.currentXp, target: xp.xpToNextLevel }) }}
      </div>
      <div class="text-caption text-white opacity-90">
        {{ t('gamification.xp.xpToGo', xpRemaining, { count: xpRemaining }) }}
      </div>
    </div>

    <div
      class="text-caption text-white opacity-80 mt-3"
      data-testid="xp-total-earned"
    >
      {{ t('gamification.xp.totalEarned', { total: xp.totalXpEarned }) }}
    </div>
  </VCard>
</template>

<style scoped>
.xp-progress-card {
  border-radius: 16px;
}
</style>
