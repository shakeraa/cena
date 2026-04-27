<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import DailyChallengeCard from '@/components/challenges/DailyChallengeCard.vue'
import DailyChallengeLeaderboard from '@/components/challenges/DailyChallengeLeaderboard.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type {
  ChallengeStartResponse,
  DailyChallengeDto,
  DailyChallengeLeaderboardDto,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.dailyChallenge',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const dailyQuery = useApiQuery<DailyChallengeDto>('/api/challenges/daily')
const leaderboardQuery = useApiQuery<DailyChallengeLeaderboardDto>(
  '/api/challenges/daily/leaderboard',
)

const startMutation = useApiMutation<ChallengeStartResponse, Record<string, never>>(
  '/api/challenges/daily/start',
  'POST',
)
const starting = ref(false)

const loadingPrimary = computed(() =>
  dailyQuery.loading.value && !dailyQuery.data.value,
)

const fatalError = computed(() => dailyQuery.error.value)

async function onStart() {
  if (starting.value)
    return
  starting.value = true
  try {
    const res = await startMutation.execute({})
    // STU-CHL-009 — start CTA hands the student to /session/{id}.
    await router.push(`/session/${res.sessionId}`)
  }
  catch {
    // surfaced via startMutation.error.value
  }
  finally {
    starting.value = false
  }
}
</script>

<template>
  <div
    class="daily-page pa-4"
    data-testid="daily-challenge-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('challenges.daily.pageTitle') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-4">
      {{ t('challenges.daily.pageSubtitle') }}
    </p>

    <div
      v-if="loadingPrimary"
      class="d-flex justify-center py-12"
      data-testid="daily-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="fatalError"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="daily-error"
    >
      {{ t(fatalError.i18nKey) }}
    </VAlert>

    <div
      v-else-if="!dailyQuery.data.value"
      class="text-center py-12"
      data-testid="daily-empty"
    >
      <VIcon
        icon="tabler-mood-empty"
        size="48"
        class="text-medium-emphasis mb-3"
        aria-hidden="true"
      />
      <div class="text-h6 mb-1">
        {{ t('challenges.daily.empty.title') }}
      </div>
      <div class="text-body-2 text-medium-emphasis">
        {{ t('challenges.daily.empty.subtitle') }}
      </div>
    </div>

    <template v-else>
      <DailyChallengeCard
        :challenge="dailyQuery.data.value"
        :starting="starting"
        @start="onStart"
      />

      <VAlert
        v-if="startMutation.error.value"
        type="error"
        variant="tonal"
        class="mt-3"
        data-testid="daily-start-error"
      >
        {{ t(startMutation.error.value.i18nKey) }}
      </VAlert>

      <p
        class="text-body-2 text-medium-emphasis mt-4 mb-6"
        data-testid="daily-how-it-works"
      >
        {{ t('challenges.daily.howItWorks') }}
      </p>

      <div
        v-if="dailyQuery.data.value.attempted && dailyQuery.data.value.bestScore !== null"
        class="text-body-2 mb-6"
        data-testid="daily-best-score"
      >
        {{ t('challenges.daily.bestScore', { score: dailyQuery.data.value.bestScore }) }}
      </div>

      <section data-testid="daily-leaderboard-section">
        <div
          v-if="leaderboardQuery.loading.value && !leaderboardQuery.data.value"
          class="d-flex justify-center py-6"
          data-testid="daily-leaderboard-loading"
        >
          <VProgressCircular indeterminate size="32" />
        </div>
        <DailyChallengeLeaderboard
          v-else-if="leaderboardQuery.data.value"
          :leaderboard="leaderboardQuery.data.value"
        />
        <VAlert
          v-else-if="leaderboardQuery.error.value"
          type="warning"
          variant="tonal"
          density="compact"
          data-testid="daily-leaderboard-error"
        >
          {{ t(leaderboardQuery.error.value.i18nKey) }}
        </VAlert>
      </section>
    </template>
  </div>
</template>

<style scoped>
.daily-page {
  max-inline-size: 880px;
  margin-inline: auto;
}
</style>
