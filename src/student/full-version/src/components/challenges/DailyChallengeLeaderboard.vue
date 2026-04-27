<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { DailyChallengeLeaderboardDto } from '@/api/types/common'

interface Props {
  leaderboard: DailyChallengeLeaderboardDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const ownRank = computed(() => props.leaderboard.currentStudentRank ?? null)
const isEmpty = computed(() => (props.leaderboard.entries?.length ?? 0) === 0)

function fmtTime(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  // Times are pure digits → wrap in <bdi dir="ltr"> at the template level
  // so RTL pages don't render "1:42" as "42:1".
  return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`
}
</script>

<template>
  <VCard
    variant="outlined"
    data-testid="daily-leaderboard"
    class="pa-0"
  >
    <div class="pa-4 pb-2">
      <div class="text-subtitle-1 font-weight-medium">
        {{ t('challenges.daily.leaderboard.title') }}
      </div>
      <div class="text-caption text-medium-emphasis">
        {{ t('challenges.daily.leaderboard.subtitle') }}
      </div>
      <div
        v-if="ownRank !== null"
        class="text-caption text-primary mt-1"
        data-testid="daily-leaderboard-own-rank"
      >
        {{ t('challenges.daily.leaderboard.yourRank', { rank: ownRank }) }}
      </div>
      <div
        v-else
        class="text-caption text-medium-emphasis mt-1"
        data-testid="daily-leaderboard-no-rank"
      >
        {{ t('challenges.daily.leaderboard.noRankYet') }}
      </div>
    </div>

    <div
      v-if="isEmpty"
      class="text-body-2 text-medium-emphasis pa-4"
      data-testid="daily-leaderboard-empty"
    >
      {{ t('challenges.daily.leaderboard.empty') }}
    </div>

    <VTable
      v-else
      density="comfortable"
    >
      <thead>
        <tr>
          <th class="text-start" scope="col">
            {{ t('challenges.daily.leaderboard.rankHeader') }}
          </th>
          <th class="text-start" scope="col">
            {{ t('challenges.daily.leaderboard.studentHeader') }}
          </th>
          <th class="text-end" scope="col">
            {{ t('challenges.daily.leaderboard.scoreHeader') }}
          </th>
          <th class="text-end" scope="col">
            {{ t('challenges.daily.leaderboard.timeHeader') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="entry in leaderboard.entries"
          :key="`${entry.rank}-${entry.studentId}`"
          :data-testid="`daily-leaderboard-row-${entry.rank}`"
          :class="{ 'leaderboard-row--self': ownRank !== null && entry.rank === ownRank }"
        >
          <td>
            <bdi dir="ltr">{{ entry.rank }}</bdi>
          </td>
          <td>
            {{ entry.displayName }}
            <VChip
              v-if="ownRank !== null && entry.rank === ownRank"
              size="x-small"
              variant="tonal"
              color="primary"
              class="ms-2"
              data-testid="daily-leaderboard-you-chip"
            >
              {{ t('challenges.daily.leaderboard.you') }}
            </VChip>
          </td>
          <td class="text-end">
            <bdi dir="ltr">{{ entry.score }}</bdi>
          </td>
          <td class="text-end">
            <bdi dir="ltr">{{ fmtTime(entry.timeSeconds) }}</bdi>
          </td>
        </tr>
      </tbody>
    </VTable>
  </VCard>
</template>

<style scoped>
.leaderboard-row--self {
  background-color: rgba(var(--v-theme-primary), 0.06);
}
</style>
