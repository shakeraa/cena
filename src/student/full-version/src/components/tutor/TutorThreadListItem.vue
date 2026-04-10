<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TutorThreadDto } from '@/api/types/common'

interface Props {
  thread: TutorThreadDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const relativeUpdated = computed(() => {
  const updated = new Date(props.thread.updatedAt)
  const now = new Date()
  const diffMs = now.getTime() - updated.getTime()
  const diffMin = Math.floor(diffMs / 60_000)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)

  if (diffMin < 1)
    return t('tutor.threadList.justNow')
  if (diffMin < 60)
    return t('tutor.threadList.minutesAgo', { count: diffMin })
  if (diffHour < 24)
    return t('tutor.threadList.hoursAgo', { count: diffHour })

  return t('tutor.threadList.daysAgo', { count: diffDay })
})
</script>

<template>
  <VCard
    :data-testid="`tutor-thread-${thread.threadId}`"
    variant="outlined"
    class="tutor-thread-item pa-4 cursor-pointer"
    :to="`/tutor/${thread.threadId}`"
  >
    <div class="d-flex align-center">
      <VAvatar
        color="primary"
        size="40"
        class="me-3"
      >
        <VIcon
          icon="tabler-message-chatbot"
          size="20"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ thread.title }}
        </div>
        <div class="d-flex align-center text-caption text-medium-emphasis mt-1">
          <VChip
            v-if="thread.subject"
            size="x-small"
            variant="tonal"
            class="me-2"
          >
            {{ thread.subject }}
          </VChip>
          <span>{{ t('tutor.threadList.messageCount', { count: thread.messageCount }) }}</span>
          <span class="mx-1">·</span>
          <span>{{ relativeUpdated }}</span>
        </div>
      </div>
      <VIcon
        icon="tabler-chevron-right"
        size="20"
        class="ms-2 text-medium-emphasis"
        aria-hidden="true"
      />
    </div>
  </VCard>
</template>

<style scoped>
.tutor-thread-item {
  transition: transform 0.15s ease-out, border-color 0.15s ease-out;
}

.tutor-thread-item:hover {
  transform: translateY(-2px);
}
</style>
