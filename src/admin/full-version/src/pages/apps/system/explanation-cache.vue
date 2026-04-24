<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'System' } })

interface CacheStats {
  l1Count: number
  l2KeyCount: number
  l3GenerationCount: number
  overallHitRate: number
}

interface QualityScore {
  questionId: string
  questionStem: string
  factual: number
  linguistic: number
  pedagogical: number
  composite: number
}

interface QualityScoreResponse {
  items: QualityScore[]
  totalCount: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const cacheStats = ref<CacheStats>({
  l1Count: 0,
  l2KeyCount: 0,
  l3GenerationCount: 0,
  overallHitRate: 0,
})

const qualityScores = ref<QualityScore[]>([])
const qualityTotalCount = ref(0)
const qualityPage = ref(1)
const qualityPageSize = ref(20)
const qualityLoading = ref(false)

const scoreColor = (score: number): string => {
  if (score > 80) return 'success'
  if (score >= 60) return 'warning'

  return 'error'
}

const truncate = (text: string, len = 60): string => {
  if (!text) return '-'

  return text.length > len ? `${text.slice(0, len)}...` : text
}

const qualityHeaders = [
  { title: 'Question Stem', key: 'questionStem', sortable: false },
  { title: 'Factual', key: 'factual', sortable: true, align: 'center' as const },
  { title: 'Linguistic', key: 'linguistic', sortable: true, align: 'center' as const },
  { title: 'Pedagogical', key: 'pedagogical', sortable: true, align: 'center' as const },
  { title: 'Composite', key: 'composite', sortable: true, align: 'center' as const },
]

const fetchCacheStats = async () => {
  try {
    const data = await $api<CacheStats>('/admin/explanations/cache-stats')

    cacheStats.value = {
      l1Count: data.l1Count ?? 0,
      l2KeyCount: data.l2KeyCount ?? 0,
      l3GenerationCount: data.l3GenerationCount ?? 0,
      overallHitRate: data.overallHitRate ?? 0,
    }
  }
  catch (err: any) {
    console.error('Failed to fetch cache stats:', err)
    error.value = err.message ?? 'Failed to load cache stats'
  }
}

const fetchQualityScores = async () => {
  qualityLoading.value = true
  try {
    const params = new URLSearchParams({
      page: qualityPage.value.toString(),
      pageSize: qualityPageSize.value.toString(),
    })

    const data = await $api<QualityScoreResponse>(`/admin/explanations/quality-scores?${params}`)

    qualityScores.value = data.items ?? []
    qualityTotalCount.value = data.totalCount ?? 0
  }
  catch (err: any) {
    console.error('Failed to fetch quality scores:', err)
    error.value = err.message ?? 'Failed to load quality scores'
  }
  finally {
    qualityLoading.value = false
  }
}

const onQualityPageUpdate = async (options: { page: number; itemsPerPage: number }) => {
  qualityPage.value = options.page
  qualityPageSize.value = options.itemsPerPage
  await fetchQualityScores()
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchCacheStats(), fetchQualityScores()])
  loading.value = false
}

onMounted(fetchAll)

const statCards = computed(() => [
  { title: 'L1 Count', value: cacheStats.value.l1Count.toLocaleString(), icon: 'tabler-database', color: 'primary' },
  { title: 'L2 Key Count', value: cacheStats.value.l2KeyCount.toLocaleString(), icon: 'tabler-key', color: 'info' },
  { title: 'L3 Generation Count', value: cacheStats.value.l3GenerationCount.toLocaleString(), icon: 'tabler-sparkles', color: 'warning' },
  { title: 'Overall Hit Rate', value: `${(cacheStats.value.overallHitRate * 100).toFixed(1)}%`, icon: 'tabler-target', color: 'success' },
])
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Explanation Cache
        </h4>
        <div class="text-body-1">
          Cache performance and explanation quality scores
        </div>
      </div>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <!-- Stat Cards -->
    <VRow class="mb-6">
      <VCol
        v-for="stat in statCards"
        :key="stat.title"
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard :loading="loading">
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              :color="stat.color"
              rounded
              size="48"
            >
              <VIcon
                :icon="stat.icon"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ stat.value }}
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                {{ stat.title }}
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Quality Scores Table -->
    <VCard>
      <VCardItem title="Explanation Quality Scores" />
      <VCardText>
        <VProgressLinear
          v-if="loading && !qualityScores.length"
          indeterminate
          class="mb-4"
        />

        <VDataTableServer
          :headers="qualityHeaders"
          :items="qualityScores"
          :items-length="qualityTotalCount"
          :items-per-page="qualityPageSize"
          :page="qualityPage"
          :loading="qualityLoading"
          @update:options="onQualityPageUpdate"
        >
          <template #item.questionStem="{ item }">
            <span :title="item.questionStem">{{ truncate(item.questionStem) }}</span>
          </template>

          <template #item.factual="{ item }">
            <VChip
              :color="scoreColor(item.factual)"
              label
              size="small"
            >
              {{ item.factual }}
            </VChip>
          </template>

          <template #item.linguistic="{ item }">
            <VChip
              :color="scoreColor(item.linguistic)"
              label
              size="small"
            >
              {{ item.linguistic }}
            </VChip>
          </template>

          <template #item.pedagogical="{ item }">
            <VChip
              :color="scoreColor(item.pedagogical)"
              label
              size="small"
            >
              {{ item.pedagogical }}
            </VChip>
          </template>

          <template #item.composite="{ item }">
            <VChip
              :color="scoreColor(item.composite)"
              label
              size="small"
            >
              {{ item.composite }}
            </VChip>
          </template>

          <template #no-data>
            <div class="text-center py-4 text-disabled">
              No quality scores available
            </div>
          </template>
        </VDataTableServer>
      </VCardText>
    </VCard>
  </div>
</template>
