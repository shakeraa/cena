<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import DailyChallengeCard from '@/components/challenges/DailyChallengeCard.vue'
import BossBattleTile from '@/components/challenges/BossBattleTile.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type {
  BossBattleListDto,
  CardChainListDto,
  ChallengeStartResponse,
  DailyChallengeDto,
  TournamentListDto,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.challenges',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const dailyQuery = useApiQuery<DailyChallengeDto>('/api/challenges/daily')
const bossQuery = useApiQuery<BossBattleListDto>('/api/challenges/boss')
const chainsQuery = useApiQuery<CardChainListDto>('/api/challenges/chains')
const tournamentsQuery = useApiQuery<TournamentListDto>('/api/challenges/tournaments')

const loading = computed(() =>
  dailyQuery.loading.value
  || bossQuery.loading.value
  || chainsQuery.loading.value
  || tournamentsQuery.loading.value,
)

const anyError = computed(() =>
  dailyQuery.error.value
  || bossQuery.error.value
  || chainsQuery.error.value
  || tournamentsQuery.error.value,
)

// STU-W-11: navigate into a real /session on successful /start. Network
// failures bubble through useApiMutation.error — kept simple at the hub
// level; the dedicated /challenges/daily and /challenges/boss pages render
// richer error chrome.
const dailyStart = useApiMutation<ChallengeStartResponse, Record<string, never>>(
  '/api/challenges/daily/start',
  'POST',
)
const startingDaily = ref(false)
const startingBossId = ref<string | null>(null)

async function onStartDaily() {
  if (startingDaily.value)
    return
  startingDaily.value = true
  try {
    const res = await dailyStart.execute({})
    await router.push(`/session/${res.sessionId}`)
  }
  catch {
    // surfaced via dailyStart.error.value
  }
  finally {
    startingDaily.value = false
  }
}

async function onSelectBoss(bossBattleId: string) {
  if (startingBossId.value)
    return
  startingBossId.value = bossBattleId
  try {
    const mutation = useApiMutation<ChallengeStartResponse, Record<string, never>>(
      `/api/challenges/boss/${encodeURIComponent(bossBattleId)}/start`,
      'POST',
    )
    const res = await mutation.execute({})
    await router.push(`/session/${res.sessionId}`)
  }
  catch {
    // The boss page will surface a richer error; on the hub we simply
    // release the loading state.
  }
  finally {
    startingBossId.value = null
  }
}
</script>

<template>
  <div
    class="challenges-page pa-4"
    data-testid="challenges-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('challenges.hub.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('challenges.hub.subtitle') }}
    </p>

    <div
      v-if="loading && !dailyQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="challenges-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="anyError"
      type="error"
      variant="tonal"
      data-testid="challenges-error"
    >
      {{ anyError.message }}
    </VAlert>

    <template v-else>
      <!-- DAILY CHALLENGE (hero) -->
      <section class="mb-8">
        <DailyChallengeCard
          v-if="dailyQuery.data.value"
          :challenge="dailyQuery.data.value"
          :starting="startingDaily"
          @start="onStartDaily"
        />
      </section>

      <!-- BOSS BATTLES -->
      <section
        v-if="bossQuery.data.value"
        class="mb-8"
        data-testid="boss-section"
      >
        <h2 class="text-h5 mb-1">
          {{ t('challenges.boss.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-4">
          {{ t('challenges.boss.subtitle') }}
        </p>
        <div class="challenges-page__grid">
          <BossBattleTile
            v-for="b in bossQuery.data.value.available"
            :key="b.bossBattleId"
            :boss="b"
            :starting="startingBossId === b.bossBattleId"
            @select="onSelectBoss"
          />
          <BossBattleTile
            v-for="b in bossQuery.data.value.locked"
            :key="b.bossBattleId"
            :boss="b"
            locked
          />
        </div>
      </section>

      <!-- CARD CHAINS -->
      <section
        v-if="chainsQuery.data.value && chainsQuery.data.value.chains.length > 0"
        class="mb-8"
        data-testid="chains-section"
      >
        <h2 class="text-h5 mb-1">
          {{ t('challenges.chains.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-4">
          {{ t('challenges.chains.subtitle') }}
        </p>
        <div class="challenges-page__grid">
          <VCard
            v-for="chain in chainsQuery.data.value.chains"
            :key="chain.chainId"
            variant="outlined"
            class="pa-4"
            :data-testid="`chain-${chain.chainId}`"
          >
            <div class="text-subtitle-1 font-weight-medium mb-2">
              {{ chain.name }}
            </div>
            <VProgressLinear
              :model-value="(chain.cardsUnlocked / chain.cardsTotal) * 100"
              color="primary"
              height="8"
              rounded
              :aria-label="t('challenges.chains.progressAria', {
                unlocked: chain.cardsUnlocked,
                total: chain.cardsTotal,
              }, { plural: chain.cardsUnlocked })"
            />
            <div class="text-caption text-medium-emphasis mt-2">
              {{ t('challenges.chains.cardsUnlocked', {
                unlocked: chain.cardsUnlocked,
                total: chain.cardsTotal,
              }, { plural: chain.cardsUnlocked }) }}
            </div>
          </VCard>
        </div>
      </section>

      <!-- TOURNAMENTS -->
      <section
        v-if="tournamentsQuery.data.value"
        data-testid="tournaments-section"
      >
        <h2 class="text-h5 mb-1">
          {{ t('challenges.tournaments.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-4">
          {{ t('challenges.tournaments.subtitle') }}
        </p>
        <div
          v-if="tournamentsQuery.data.value.active.length === 0 && tournamentsQuery.data.value.upcoming.length === 0"
          class="text-body-2 text-medium-emphasis"
          data-testid="tournaments-empty"
        >
          {{ t('challenges.tournaments.empty') }}
        </div>
        <div
          v-else
          class="challenges-page__grid"
        >
          <VCard
            v-for="tourn in tournamentsQuery.data.value.active"
            :key="tourn.tournamentId"
            variant="flat"
            color="success"
            class="pa-4"
            :data-testid="`tournament-active-${tourn.tournamentId}`"
          >
            <VChip
              size="x-small"
              variant="flat"
              color="white"
              class="mb-2"
            >
              {{ t('challenges.tournaments.active') }}
            </VChip>
            <div class="text-subtitle-1 font-weight-medium text-white">
              {{ tourn.name }}
            </div>
            <div class="text-caption text-white opacity-90 mt-1">
              {{ t('challenges.tournaments.participants', { count: tourn.participantCount }, { plural: tourn.participantCount }) }}
            </div>
          </VCard>
          <VCard
            v-for="tourn in tournamentsQuery.data.value.upcoming"
            :key="tourn.tournamentId"
            variant="outlined"
            class="pa-4"
            :data-testid="`tournament-upcoming-${tourn.tournamentId}`"
          >
            <VChip
              size="x-small"
              variant="tonal"
              color="primary"
              class="mb-2"
            >
              {{ t('challenges.tournaments.upcoming') }}
            </VChip>
            <div class="text-subtitle-1 font-weight-medium">
              {{ tourn.name }}
            </div>
            <div class="text-caption text-medium-emphasis mt-1">
              {{ t('challenges.tournaments.participants', { count: tourn.participantCount }, { plural: tourn.participantCount }) }}
            </div>
          </VCard>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.challenges-page {
  max-inline-size: 1100px;
  margin-inline: auto;
}

.challenges-page__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}
</style>
