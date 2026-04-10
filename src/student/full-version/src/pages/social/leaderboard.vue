<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { LeaderboardDto, LeaderboardEntry, MeBootstrapDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.leaderboard',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

type Scope = 'global' | 'class' | 'friends'

const { t } = useI18n()

const scope = ref<Scope>('global')

const initialQuery = useApiQuery<LeaderboardDto>('/api/gamification/leaderboard?scope=global')
const meQuery = useApiQuery<MeBootstrapDto>('/api/me')

const data = ref<LeaderboardDto | null>(null)
const loading = ref(false)
const error = ref<Error | null>(null)

watch(
  () => initialQuery.data.value,
  next => {
    if (next && data.value === null)
      data.value = next
  },
  { immediate: true },
)

watch(() => initialQuery.loading.value, v => {
  if (scope.value === 'global')
    loading.value = v
})

async function setScope(next: Scope) {
  scope.value = next
  loading.value = true
  error.value = null
  try {
    data.value = await $api<LeaderboardDto>(`/api/gamification/leaderboard?scope=${next}`)
  }
  catch (err) {
    error.value = err as Error
  }
  finally {
    loading.value = false
  }
}

const topEntries = computed<LeaderboardEntry[]>(() => data.value?.entries.slice(0, 20) ?? [])
const currentStudentId = computed(() => meQuery.data.value?.studentId ?? '')

function isYou(entry: LeaderboardEntry): boolean {
  return entry.studentId === currentStudentId.value
}

function rankIcon(rank: number): string | null {
  if (rank === 1) return 'tabler-trophy'
  if (rank === 2) return 'tabler-medal'
  if (rank === 3) return 'tabler-medal-2'

  return null
}

function rankColor(rank: number): string | undefined {
  if (rank === 1) return 'yellow-darken-2'
  if (rank === 2) return 'grey'
  if (rank === 3) return 'amber-darken-4'

  return undefined
}
</script>

<template>
  <div
    class="leaderboard-page pa-4"
    data-testid="leaderboard-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('leaderboard.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('leaderboard.subtitle') }}
    </p>

    <VTabs
      v-model="scope"
      color="primary"
      class="mb-4"
      data-testid="leaderboard-scope-tabs"
      @update:model-value="setScope($event as Scope)"
    >
      <VTab
        value="global"
        data-testid="scope-global"
      >
        <VIcon
          icon="tabler-world"
          start
          aria-hidden="true"
        />
        {{ t('leaderboard.scope.global') }}
      </VTab>
      <VTab
        value="class"
        data-testid="scope-class"
      >
        <VIcon
          icon="tabler-school"
          start
          aria-hidden="true"
        />
        {{ t('leaderboard.scope.class') }}
      </VTab>
      <VTab
        value="friends"
        data-testid="scope-friends"
      >
        <VIcon
          icon="tabler-users"
          start
          aria-hidden="true"
        />
        {{ t('leaderboard.scope.friends') }}
      </VTab>
    </VTabs>

    <div
      v-if="loading && !data"
      class="d-flex justify-center py-12"
      data-testid="leaderboard-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="error"
      type="error"
      variant="tonal"
      data-testid="leaderboard-error"
    >
      {{ error.message }}
    </VAlert>

    <VCard
      v-else-if="data"
      variant="outlined"
      data-testid="leaderboard-card"
    >
      <div
        class="leaderboard-page__banner pa-4"
        data-testid="your-rank-banner"
      >
        <div class="text-caption text-medium-emphasis">
          {{ t('leaderboard.yourRankLabel') }}
        </div>
        <div class="text-h4 font-weight-bold">
          {{ t('leaderboard.yourRankValue', { rank: data.currentStudentRank }) }}
        </div>
      </div>

      <VDivider />

      <VList class="pa-0">
        <VListItem
          v-for="entry in topEntries"
          :key="entry.studentId"
          :class="{ 'leaderboard-page__you': isYou(entry) }"
          :data-testid="`leaderboard-entry-${entry.rank}`"
        >
          <template #prepend>
            <div
              class="leaderboard-page__rank text-h6 font-weight-bold me-3"
              aria-hidden="true"
            >
              <VIcon
                v-if="rankIcon(entry.rank)"
                :icon="rankIcon(entry.rank)!"
                :color="rankColor(entry.rank)"
                size="28"
              />
              <template v-else>
                {{ entry.rank }}
              </template>
            </div>
            <VAvatar
              :color="isYou(entry) ? 'primary' : 'surface-variant'"
              size="40"
              class="me-3"
            >
              <VIcon
                icon="tabler-user"
                size="20"
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
              data-testid="entry-you-chip"
            >
              {{ t('leaderboard.youChip') }}
            </VChip>
          </VListItemTitle>
          <template #append>
            <div class="text-body-1 font-weight-medium">
              {{ t('leaderboard.xpValue', { xp: entry.xp }) }}
            </div>
          </template>
        </VListItem>
      </VList>
    </VCard>
  </div>
</template>

<style scoped>
.leaderboard-page {
  max-inline-size: 900px;
  margin-inline: auto;
}

.leaderboard-page__banner {
  background-color: rgb(var(--v-theme-primary) / 0.08);
}

.leaderboard-page__rank {
  inline-size: 32px;
  text-align: center;
}

.leaderboard-page__you {
  background-color: rgb(var(--v-theme-primary) / 0.1);
}
</style>
