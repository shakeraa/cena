<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  sessionId: string
  subject: string
  startedAt: string
  durationSeconds: number
  accuracyPercent: number
  xpAwarded: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const relativeDate = computed(() => {
  const started = new Date(props.startedAt)
  const now = new Date()
  const diffMs = now.getTime() - started.getTime()
  const diffHours = Math.floor(diffMs / 3600_000)
  const diffDays = Math.floor(diffHours / 24)

  if (diffHours < 1)
    return t('progress.sessions.justNow')
  if (diffHours < 24)
    return t('progress.sessions.hoursAgo', { count: diffHours })

  return t('progress.sessions.daysAgo', { count: diffDays })
})

const durationLabel = computed(() => {
  const mins = Math.floor(props.durationSeconds / 60)
  const secs = props.durationSeconds % 60
  if (mins === 0)
    return `${secs}s`

  return `${mins}m ${secs}s`
})

const accuracyColor = computed(() => {
  if (props.accuracyPercent >= 85) return 'success'
  if (props.accuracyPercent >= 60) return 'warning'

  return 'error'
})
</script>

<template>
  <VCard
    variant="outlined"
    class="pa-4 mb-2 cursor-pointer"
    :data-testid="`session-history-${sessionId}`"
    :to="`/progress/sessions/${sessionId}`"
  >
    <div class="d-flex align-center">
      <VAvatar
        color="primary"
        size="40"
        class="me-3"
      >
        <VIcon
          icon="tabler-book"
          size="20"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ t(`session.setup.subjects.${subject}`, subject) }}
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ relativeDate }} · {{ durationLabel }}
        </div>
      </div>
      <div class="text-end me-3">
        <VChip
          size="small"
          variant="tonal"
          :color="accuracyColor"
        >
          {{ accuracyPercent }}%
        </VChip>
        <div class="text-caption text-medium-emphasis mt-1">
          +{{ xpAwarded }} XP
        </div>
      </div>
      <VIcon
        icon="tabler-chevron-right"
        size="20"
        class="text-medium-emphasis"
        aria-hidden="true"
      />
    </div>
  </VCard>
</template>
