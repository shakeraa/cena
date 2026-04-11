<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  subject: string
  masteryPercent: number
  questionsAttempted: number
  accuracyPercent: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const masteryTier = computed<'novice' | 'learning' | 'proficient' | 'mastered'>(() => {
  if (props.masteryPercent >= 85) return 'mastered'
  if (props.masteryPercent >= 60) return 'proficient'
  if (props.masteryPercent >= 30) return 'learning'

  return 'novice'
})

const tierColor = computed(() => {
  switch (masteryTier.value) {
    case 'mastered': return 'success'
    case 'proficient': return 'primary'
    case 'learning': return 'warning'
    default: return 'grey'
  }
})
</script>

<template>
  <VCard
    variant="outlined"
    class="pa-4 mb-3"
    :data-testid="`mastery-row-${subject}`"
  >
    <div class="d-flex align-center justify-space-between mb-2">
      <div class="d-flex align-center">
        <VAvatar
          :color="tierColor"
          size="36"
          class="me-3"
        >
          <VIcon
            icon="tabler-book"
            size="18"
            color="white"
            aria-hidden="true"
          />
        </VAvatar>
        <div>
          <div
            class="text-subtitle-1 font-weight-medium"
            :data-testid="`mastery-row-${subject}-name`"
          >
            {{ t(`session.setup.subjects.${subject}`, subject) }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t(`mastery.${masteryTier}`) }}
          </div>
        </div>
      </div>
      <div class="text-end">
        <div
          class="text-h6 font-weight-bold"
          :data-testid="`mastery-row-${subject}-percent`"
        >
          {{ masteryPercent }}%
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ t('progress.mastery.masteryLabel') }}
        </div>
      </div>
    </div>

    <VProgressLinear
      :model-value="masteryPercent"
      :color="tierColor"
      height="8"
      rounded
      :aria-label="t('progress.mastery.rowAria', { subject, percent: masteryPercent })"
    />

    <div class="d-flex justify-space-between mt-2 text-caption text-medium-emphasis">
      <span>
        {{ t('progress.mastery.questionsAttempted', { count: questionsAttempted }) }}
      </span>
      <span>
        {{ t('progress.mastery.accuracy', { percent: accuracyPercent }) }}
      </span>
    </div>
  </VCard>
</template>
