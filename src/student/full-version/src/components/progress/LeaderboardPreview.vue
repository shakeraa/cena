<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LeaderboardDto, LeaderboardEntry } from '@/api/types/common'

interface Props {
  leaderboard: LeaderboardDto
  currentStudentId: string
  limit?: number
}

const props = withDefaults(defineProps<Props>(), { limit: 5 })
const { t } = useI18n()

const topEntries = computed<LeaderboardEntry[]>(() => props.leaderboard.entries.slice(0, props.limit))

function isYou(entry: LeaderboardEntry): boolean {
  return entry.studentId === props.currentStudentId
}
</script>

<template>
  <VCard
    class="leaderboard-preview pa-5"
    variant="outlined"
    data-testid="leaderboard-preview"
  >
    <div class="d-flex align-center justify-space-between mb-4">
      <div class="text-h6">
        {{ t('gamification.leaderboard.title') }}
      </div>
      <VChip
        size="small"
        color="primary"
        variant="tonal"
        data-testid="leaderboard-your-rank"
      >
        {{ t('gamification.leaderboard.yourRank', { rank: leaderboard.currentStudentRank }) }}
      </VChip>
    </div>

    <VList
      density="compact"
      class="pa-0"
    >
      <VListItem
        v-for="entry in topEntries"
        :key="entry.studentId"
        :class="{ 'leaderboard-preview__you': isYou(entry) }"
        :data-testid="`leaderboard-entry-${entry.rank}`"
      >
        <template #prepend>
          <div
            class="leaderboard-preview__rank text-subtitle-1 font-weight-bold me-3"
            aria-hidden="true"
          >
            {{ entry.rank }}
          </div>
          <VAvatar
            :color="isYou(entry) ? 'primary' : 'surface-variant'"
            size="32"
            class="me-2"
          >
            <VIcon
              icon="tabler-user"
              size="18"
              aria-hidden="true"
            />
          </VAvatar>
        </template>
        <VListItemTitle class="d-flex align-center">
          <span :class="isYou(entry) ? 'font-weight-bold' : ''">
            {{ entry.displayName }}
          </span>
          <VChip
            v-if="isYou(entry)"
            size="x-small"
            color="primary"
            variant="flat"
            class="ms-2"
          >
            {{ t('gamification.leaderboard.youChip') }}
          </VChip>
        </VListItemTitle>
        <template #append>
          <div
            class="text-body-2 text-medium-emphasis"
            :aria-label="t('gamification.leaderboard.xpLabel', entry.xp, { xp: entry.xp })"
          >
            {{ t('gamification.leaderboard.xpValue', entry.xp, { xp: entry.xp }) }}
          </div>
        </template>
      </VListItem>
    </VList>
  </VCard>
</template>

<style scoped>
.leaderboard-preview__rank {
  inline-size: 24px;
  text-align: center;
}

.leaderboard-preview__you {
  background-color: rgb(var(--v-theme-primary) / 0.08);
  border-radius: 8px;
}
</style>
