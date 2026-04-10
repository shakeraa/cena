<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { DailyChallengeDto } from '@/api/types/common'

interface Props {
  challenge: DailyChallengeDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const difficultyColor = computed(() => {
  switch (props.challenge.difficulty) {
    case 'easy': return 'success'
    case 'medium': return 'warning'
    case 'hard': return 'error'
    default: return 'primary'
  }
})

const timeLeft = computed(() => {
  const expires = new Date(props.challenge.expiresAt)
  const now = new Date()
  const diffMs = expires.getTime() - now.getTime()
  if (diffMs <= 0)
    return t('challenges.daily.expired')

  const hours = Math.floor(diffMs / 3600_000)
  const minutes = Math.floor((diffMs % 3600_000) / 60_000)

  return t('challenges.daily.timeLeft', { hours, minutes })
})
</script>

<template>
  <VCard
    class="daily-challenge-card pa-5"
    variant="flat"
    color="primary"
    data-testid="daily-challenge-card"
  >
    <div class="d-flex align-start justify-space-between mb-3">
      <div>
        <div class="text-caption text-white opacity-80 text-uppercase">
          {{ t('challenges.daily.label') }}
        </div>
        <div class="text-h5 font-weight-bold text-white mt-1">
          {{ challenge.title }}
        </div>
      </div>
      <VIcon
        icon="tabler-sparkles"
        size="40"
        color="yellow-accent-2"
        aria-hidden="true"
      />
    </div>

    <p class="text-body-2 text-white opacity-90 mb-4">
      {{ challenge.description }}
    </p>

    <div class="d-flex align-center flex-wrap ga-2 mb-4">
      <VChip
        size="small"
        variant="flat"
        :color="difficultyColor"
        data-testid="daily-difficulty"
      >
        {{ t(`challenges.difficulty.${challenge.difficulty}`) }}
      </VChip>
      <VChip
        size="small"
        variant="tonal"
        color="white"
      >
        {{ challenge.subject }}
      </VChip>
      <VChip
        v-if="challenge.attempted"
        size="small"
        variant="tonal"
        color="white"
        prepend-icon="tabler-check"
        data-testid="daily-attempted"
      >
        {{ t('challenges.daily.attempted') }}
      </VChip>
    </div>

    <div class="d-flex align-center justify-space-between">
      <div
        class="text-caption text-white opacity-90"
        data-testid="daily-time-left"
      >
        <VIcon
          icon="tabler-clock"
          size="14"
          class="me-1"
          aria-hidden="true"
        />
        {{ timeLeft }}
      </div>
      <VBtn
        color="white"
        variant="flat"
        data-testid="daily-start"
      >
        {{ challenge.attempted ? t('challenges.daily.reattempt') : t('challenges.daily.start') }}
      </VBtn>
    </div>
  </VCard>
</template>

<style scoped>
.daily-challenge-card {
  border-radius: 16px;
}
</style>
