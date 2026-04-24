<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { PeerSolution } from '@/api/types/common'

interface Props {
  solution: PeerSolution
}

defineProps<Props>()

const emit = defineEmits<{
  vote: [solutionId: string, direction: 'up' | 'down']
  report: [contentId: string, contentType: 'peer-solution']
  block: [studentId: string, displayName: string]
}>()

const { t } = useI18n()
</script>

<template>
  <VCard
    variant="outlined"
    class="peer-solution-card pa-4 mb-3"
    :data-testid="`peer-solution-${solution.solutionId}`"
  >
    <div class="d-flex align-center mb-3">
      <VAvatar
        color="primary"
        size="36"
        class="me-3"
      >
        <VIcon
          icon="tabler-user"
          size="18"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-2 font-weight-bold">
          {{ solution.authorDisplayName }}
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ t('social.peers.forQuestion', { questionId: solution.questionId }) }}
        </div>
      </div>
    </div>

    <p
      class="text-body-2 mb-3"
      data-testid="peer-solution-content"
    >
      {{ solution.content }}
    </p>

    <VDivider class="my-3" />

    <div class="d-flex align-center ga-3">
      <VBtn
        variant="text"
        size="small"
        prepend-icon="tabler-arrow-big-up"
        :data-testid="`upvote-${solution.solutionId}`"
        @click="emit('vote', solution.solutionId, 'up')"
      >
        {{ solution.upvoteCount }}
      </VBtn>
      <VBtn
        variant="text"
        size="small"
        prepend-icon="tabler-arrow-big-down"
        :data-testid="`downvote-${solution.solutionId}`"
        @click="emit('vote', solution.solutionId, 'down')"
      >
        {{ solution.downvoteCount }}
      </VBtn>

      <VSpacer />

      <!-- FIND-privacy-018: Report & Block buttons for safeguarding -->
      <VBtn
        variant="text"
        size="small"
        icon="tabler-flag"
        color="error"
        :aria-label="t('social.report.ariaLabel')"
        :data-testid="`report-${solution.solutionId}`"
        @click="emit('report', solution.solutionId, 'peer-solution')"
      />
      <VBtn
        variant="text"
        size="small"
        icon="tabler-ban"
        :aria-label="t('social.block.ariaLabel', { name: solution.authorDisplayName })"
        :data-testid="`block-${solution.authorStudentId}`"
        @click="emit('block', solution.authorStudentId, solution.authorDisplayName)"
      />
    </div>
  </VCard>
</template>
