<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SessionCompletedDto } from '@/api/types/common'

interface Props {
  summary: SessionCompletedDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const durationLabel = computed(() => {
  const mins = Math.floor(props.summary.durationSeconds / 60)
  const secs = props.summary.durationSeconds % 60
  if (mins === 0)
    return t('session.summary.durationSecs', { secs })

  return t('session.summary.durationMinsSecs', { mins, secs }, { plural: mins })
})
</script>

<template>
  <VCard
    class="session-summary-card pa-6"
    variant="flat"
    data-testid="session-summary-card"
  >
    <div class="text-center mb-6">
      <VIcon
        icon="tabler-confetti"
        size="56"
        color="primary"
        class="mb-3"
        aria-hidden="true"
      />
      <h2 class="text-h4 mb-1">
        {{ t('session.summary.title') }}
      </h2>
      <p class="text-body-1 text-medium-emphasis">
        {{ t('session.summary.subtitle') }}
      </p>
    </div>

    <VRow>
      <VCol
        cols="6"
        md="3"
      >
        <VCard
          variant="outlined"
          class="pa-4 text-center"
          data-testid="summary-xp"
        >
          <VIcon
            icon="tabler-bolt"
            size="28"
            color="warning"
            aria-hidden="true"
          />
          <div class="text-h5 font-weight-bold mt-1">
            {{ summary.totalXpAwarded }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t('session.summary.xpEarned') }}
          </div>
        </VCard>
      </VCol>

      <VCol
        cols="6"
        md="3"
      >
        <VCard
          variant="outlined"
          class="pa-4 text-center"
          data-testid="summary-accuracy"
        >
          <VIcon
            icon="tabler-target"
            size="28"
            color="success"
            aria-hidden="true"
          />
          <div class="text-h5 font-weight-bold mt-1">
            {{ summary.accuracyPercent }}%
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t('session.summary.accuracy') }}
          </div>
        </VCard>
      </VCol>

      <VCol
        cols="6"
        md="3"
      >
        <VCard
          variant="outlined"
          class="pa-4 text-center"
          data-testid="summary-correct"
        >
          <VIcon
            icon="tabler-circle-check"
            size="28"
            color="primary"
            aria-hidden="true"
          />
          <div class="text-h5 font-weight-bold mt-1">
            {{ summary.totalCorrect }} / {{ summary.totalCorrect + summary.totalWrong }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t('session.summary.correctCount') }}
          </div>
        </VCard>
      </VCol>

      <VCol
        cols="6"
        md="3"
      >
        <VCard
          variant="outlined"
          class="pa-4 text-center"
          data-testid="summary-duration"
        >
          <VIcon
            icon="tabler-clock"
            size="28"
            color="info"
            aria-hidden="true"
          />
          <div class="text-h5 font-weight-bold mt-1">
            {{ durationLabel }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t('session.summary.duration') }}
          </div>
        </VCard>
      </VCol>
    </VRow>
  </VCard>
</template>
