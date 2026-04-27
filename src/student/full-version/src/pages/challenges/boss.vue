<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import BossBattleTile from '@/components/challenges/BossBattleTile.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import { ApiError } from '@/composables/useApiQuery'
import type {
  BossBattleListDto,
  ChallengeStartResponse,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.bossBattles',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const bossQuery = useApiQuery<BossBattleListDto>('/api/challenges/boss')

const startingId = ref<string | null>(null)
const startError = ref<ApiError | null>(null)

const subjectFilter = ref<string>('all')

const subjectOptions = computed(() => {
  const data = bossQuery.data.value
  if (!data)
    return [{ value: 'all', label: t('challenges.boss.filter.all') }]
  const subjects = new Set<string>()
  for (const b of [...data.available, ...data.locked])
    subjects.add(b.subject)
  return [
    { value: 'all', label: t('challenges.boss.filter.all') },
    ...[...subjects].sort().map(s => ({ value: s, label: s })),
  ]
})

const filteredAvailable = computed(() => {
  const list = bossQuery.data.value?.available ?? []
  if (subjectFilter.value === 'all')
    return list
  return list.filter(b => b.subject === subjectFilter.value)
})

const filteredLocked = computed(() => {
  const list = bossQuery.data.value?.locked ?? []
  if (subjectFilter.value === 'all')
    return list
  return list.filter(b => b.subject === subjectFilter.value)
})

const isEmpty = computed(() =>
  bossQuery.data.value !== null
  && filteredAvailable.value.length === 0
  && filteredLocked.value.length === 0,
)

const loadingPrimary = computed(() =>
  bossQuery.loading.value && !bossQuery.data.value,
)

async function onSelect(bossBattleId: string) {
  if (startingId.value)
    return
  startingId.value = bossBattleId
  startError.value = null
  try {
    // useApiMutation needs the path at composable-construction time, so
    // we instantiate per-click. The composable does not register watchers,
    // so this is allocation-cheap.
    const start = useApiMutation<ChallengeStartResponse, Record<string, never>>(
      `/api/challenges/boss/${encodeURIComponent(bossBattleId)}/start`,
      'POST',
    )
    const res = await start.execute({})
    await router.push(`/session/${res.sessionId}`)
  }
  catch (err) {
    startError.value = err instanceof ApiError ? err : null
  }
  finally {
    startingId.value = null
  }
}
</script>

<template>
  <div
    class="boss-page pa-4"
    data-testid="boss-battles-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('challenges.boss.pageTitle') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('challenges.boss.pageSubtitle') }}
    </p>

    <div
      v-if="loadingPrimary"
      class="d-flex justify-center py-12"
      data-testid="boss-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="bossQuery.error.value"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="boss-error"
    >
      {{ t(bossQuery.error.value.i18nKey) }}
    </VAlert>

    <template v-else-if="bossQuery.data.value">
      <!-- Subject filter -->
      <div
        class="mb-6"
        data-testid="boss-filter"
      >
        <div class="text-caption text-medium-emphasis mb-2">
          {{ t('challenges.boss.filter.label') }}
        </div>
        <VBtnToggle
          v-model="subjectFilter"
          density="comfortable"
          mandatory
          color="primary"
          variant="outlined"
          divided
          class="boss-page__filter-toggle"
        >
          <VBtn
            v-for="opt in subjectOptions"
            :key="opt.value"
            :value="opt.value"
            :data-testid="`boss-filter-${opt.value}`"
          >
            {{ opt.label }}
          </VBtn>
        </VBtnToggle>
      </div>

      <VAlert
        v-if="startError"
        type="error"
        variant="tonal"
        class="mb-4"
        data-testid="boss-start-error"
      >
        {{ t('challenges.boss.startError') }}
      </VAlert>

      <div
        v-if="isEmpty"
        class="text-center py-12"
        data-testid="boss-empty"
      >
        <VIcon
          icon="tabler-shield-off"
          size="48"
          class="text-medium-emphasis mb-3"
          aria-hidden="true"
        />
        <div class="text-h6 mb-1">
          {{ t('challenges.boss.empty.title') }}
        </div>
        <div class="text-body-2 text-medium-emphasis">
          {{ t('challenges.boss.empty.subtitle') }}
        </div>
      </div>

      <template v-else>
        <!-- Available bosses -->
        <section
          v-if="filteredAvailable.length > 0"
          class="mb-8"
          data-testid="boss-available-section"
        >
          <h2 class="text-h6 mb-3">
            {{ t('challenges.boss.available') }}
          </h2>
          <div class="boss-page__grid">
            <BossBattleTile
              v-for="b in filteredAvailable"
              :key="b.bossBattleId"
              :boss="b"
              :starting="startingId === b.bossBattleId"
              @select="onSelect"
            />
          </div>
        </section>

        <!-- Locked bosses -->
        <section
          v-if="filteredLocked.length > 0"
          data-testid="boss-locked-section"
        >
          <h2 class="text-h6 mb-1">
            {{ t('challenges.boss.lockedHeading') }}
          </h2>
          <p class="text-caption text-medium-emphasis mb-3">
            {{ t('challenges.boss.lockedExplain') }}
          </p>
          <div class="boss-page__grid">
            <BossBattleTile
              v-for="b in filteredLocked"
              :key="b.bossBattleId"
              :boss="b"
              locked
            />
          </div>
        </section>
      </template>
    </template>
  </div>
</template>

<style scoped>
.boss-page {
  max-inline-size: 1100px;
  margin-inline: auto;
}

.boss-page__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.boss-page__filter-toggle {
  flex-wrap: wrap;
}
</style>
