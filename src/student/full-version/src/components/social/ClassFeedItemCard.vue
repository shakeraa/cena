<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ClassFeedItem } from '@/api/types/common'

interface Props {
  item: ClassFeedItem
}

const props = defineProps<Props>()

const emit = defineEmits<{
  react: [itemId: string]
  report: [contentId: string, contentType: 'feed-item']
  block: [studentId: string, displayName: string]
}>()

const { t } = useI18n()

const kindColor = computed(() => {
  switch (props.item.kind) {
    case 'achievement': return 'success'
    case 'milestone': return 'primary'
    case 'question': return 'info'
    case 'announcement': return 'warning'
    default: return 'grey'
  }
})

const kindIcon = computed(() => {
  switch (props.item.kind) {
    case 'achievement': return 'tabler-trophy'
    case 'milestone': return 'tabler-flag'
    case 'question': return 'tabler-help-circle'
    case 'announcement': return 'tabler-speakerphone'
    default: return 'tabler-news'
  }
})

const relativePostedAt = computed(() => {
  const now = Date.now()
  const posted = new Date(props.item.postedAt).getTime()
  const diffMin = Math.floor((now - posted) / 60_000)
  const diffHour = Math.floor(diffMin / 60)

  if (diffMin < 1)
    return t('social.feed.justNow')
  if (diffMin < 60)
    return t('social.feed.minutesAgo', { count: diffMin }, { plural: diffMin })

  return t('social.feed.hoursAgo', { count: diffHour }, { plural: diffHour })
})

function handleReact() {
  emit('react', props.item.itemId)
}
</script>

<template>
  <VCard
    variant="outlined"
    class="class-feed-item pa-4 mb-3"
    :data-testid="`feed-item-${item.itemId}`"
  >
    <div class="d-flex align-start mb-3">
      <VAvatar
        :color="kindColor"
        size="40"
        class="me-3"
      >
        <VIcon
          :icon="kindIcon"
          size="20"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="d-flex align-center ga-2 mb-1">
          <span class="text-subtitle-2 font-weight-bold">
            {{ item.authorDisplayName }}
          </span>
          <VChip
            size="x-small"
            variant="tonal"
            :color="kindColor"
          >
            {{ t(`social.feed.kind.${item.kind}`) }}
          </VChip>
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ relativePostedAt }}
        </div>
      </div>
    </div>

    <div
      class="text-subtitle-1 font-weight-medium mb-1"
      data-testid="feed-item-title"
    >
      {{ item.title }}
    </div>
    <p
      v-if="item.body"
      class="text-body-2 text-medium-emphasis mb-3"
    >
      {{ item.body }}
    </p>

    <VDivider class="my-3" />

    <div class="d-flex align-center ga-3">
      <VBtn
        variant="text"
        size="small"
        prepend-icon="tabler-heart"
        :aria-label="t('social.feed.reactionAriaLabel', { count: item.reactionCount }, { plural: item.reactionCount })"
        :data-testid="`react-${item.itemId}`"
        @click="handleReact"
      >
        {{ item.reactionCount }}
      </VBtn>
      <VBtn
        variant="text"
        size="small"
        prepend-icon="tabler-message"
        :aria-label="t('social.feed.commentAriaLabel', { count: item.commentCount }, { plural: item.commentCount })"
      >
        {{ item.commentCount }}
      </VBtn>

      <VSpacer />

      <!-- FIND-privacy-018: Report & Block buttons for safeguarding -->
      <VBtn
        variant="text"
        size="small"
        icon="tabler-flag"
        color="error"
        :aria-label="t('social.report.ariaLabel')"
        :data-testid="`report-${item.itemId}`"
        @click="emit('report', item.itemId, 'feed-item')"
      />
      <VBtn
        variant="text"
        size="small"
        icon="tabler-ban"
        :aria-label="t('social.block.ariaLabel', { name: item.authorDisplayName })"
        :data-testid="`block-${item.authorStudentId}`"
        @click="emit('block', item.authorStudentId, item.authorDisplayName)"
      />
    </div>
  </VCard>
</template>
