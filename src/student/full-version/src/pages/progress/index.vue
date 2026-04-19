<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StreakWidget from '@/components/home/StreakWidget.vue'
import XpProgressCard from '@/components/progress/XpProgressCard.vue'
import BadgeGrid from '@/components/progress/BadgeGrid.vue'
import LeaderboardPreview from '@/components/progress/LeaderboardPreview.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import type {
  BadgeListResponse,
  LeaderboardDto,
  MeBootstrapDto,
  StreakStatusDto,
  XpStatusDto,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.progress',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const meQuery = useApiQuery<MeBootstrapDto>('/api/me')
const xpQuery = useApiQuery<XpStatusDto>('/api/gamification/xp')
const streakQuery = useApiQuery<StreakStatusDto>('/api/gamification/streak')
const badgesQuery = useApiQuery<BadgeListResponse>('/api/gamification/badges')
const leaderboardQuery = useApiQuery<LeaderboardDto>('/api/gamification/leaderboard?scope=global')

const loading = computed(() =>
  meQuery.loading.value
  || xpQuery.loading.value
  || streakQuery.loading.value
  || badgesQuery.loading.value
  || leaderboardQuery.loading.value,
)

const error = computed(() =>
  meQuery.error.value
  || xpQuery.error.value
  || streakQuery.error.value
  || badgesQuery.error.value
  || leaderboardQuery.error.value,
)

const me = computed(() => meQuery.data.value)
const xp = computed(() => xpQuery.data.value)
const streak = computed(() => streakQuery.data.value)
const badges = computed(() => badgesQuery.data.value)
const leaderboard = computed(() => leaderboardQuery.data.value)

function retry() {
  meQuery.refresh()
  xpQuery.refresh()
  streakQuery.refresh()
  badgesQuery.refresh()
  leaderboardQuery.refresh()
}
</script>

<template>
  <div
    class="progress-page pa-4"
    data-testid="progress-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('progress.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('progress.subtitle') }}
    </p>

    <div
      v-if="loading && !xp"
      class="d-flex align-center justify-center py-12"
      data-testid="progress-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="error"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="progress-error"
    >
      <div class="d-flex align-center justify-space-between">
        <div>
          <div class="text-subtitle-1">
            {{ t('error.serverError') }}
          </div>
          <div class="text-body-2 text-medium-emphasis">
            {{ t(error.i18nKey ?? 'common.errorGeneric') }}
          </div>
        </div>
        <VBtn
          color="error"
          variant="outlined"
          size="small"
          @click="retry"
        >
          {{ t('error.tryAgain') }}
        </VBtn>
      </div>
    </VAlert>

    <template v-else-if="xp && streak && badges && leaderboard && me">
      <VRow class="mb-4">
        <VCol
          cols="12"
          md="6"
        >
          <XpProgressCard :xp="xp" />
        </VCol>
        <VCol
          cols="12"
          md="6"
        >
          <StreakWidget
            :days="streak.currentDays"
            :is-new-best="streak.currentDays > 0 && streak.currentDays >= streak.longestDays"
          />
          <!--
            RDY-082 / GD-004: removed the "at risk" banner per the
            ship-gate loss-aversion ban. The {{ streak }} value still
            renders above as an informational counter (pending the
            larger streak-deprecation decision); the banner that
            triggered urgency + loss-aversion is gone.
          -->
        </VCol>
      </VRow>

      <VRow>
        <VCol
          cols="12"
          md="8"
        >
          <BadgeGrid :badges="badges" />
        </VCol>
        <VCol
          cols="12"
          md="4"
        >
          <LeaderboardPreview
            :leaderboard="leaderboard"
            :current-student-id="me.studentId"
            :limit="5"
          />
        </VCol>
      </VRow>
    </template>
  </div>
</template>

<style scoped>
.progress-page {
  max-inline-size: 1200px;
  margin-inline: auto;
}
</style>
