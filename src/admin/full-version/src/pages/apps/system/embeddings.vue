<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'manage', subject: 'System' } })

interface CorpusStats {
  totalBlocks: number
  subjectsCovered: number
  conceptsCovered: number
  indexSizeMb: number
}

interface SearchResult {
  text: string
  similarity: number
  subject: string
  contentType: string
}

interface DuplicatePair {
  block1Preview: string
  block2Preview: string
  similarity: number
  subject: string
}

interface DuplicateResponse {
  items: DuplicatePair[]
  totalCount: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const corpusStats = ref<CorpusStats>({
  totalBlocks: 0,
  subjectsCovered: 0,
  conceptsCovered: 0,
  indexSizeMb: 0,
})

// Semantic search
const searchQuery = ref('')
const searchSubject = ref<string | null>(null)
const searchLoading = ref(false)
const searchResults = ref<SearchResult[]>([])
const searchError = ref<string | null>(null)

// Duplicates
const duplicates = ref<DuplicatePair[]>([])
const duplicateTotalCount = ref(0)
const duplicatePage = ref(1)
const duplicatePageSize = ref(10)
const duplicateThreshold = ref(0.95)
const duplicateLoading = ref(false)

const duplicateHeaders = [
  { title: 'Block 1 Preview', key: 'block1Preview', sortable: false },
  { title: 'Block 2 Preview', key: 'block2Preview', sortable: false },
  { title: 'Similarity', key: 'similarity', sortable: true, align: 'center' as const },
  { title: 'Subject', key: 'subject', sortable: true },
]

const truncate = (text: string, len = 80): string => {
  if (!text) return '-'

  return text.length > len ? `${text.slice(0, len)}...` : text
}

const similarityColor = (score: number): string => {
  if (score >= 0.95) return 'error'
  if (score >= 0.85) return 'warning'

  return 'success'
}

const fetchCorpusStats = async () => {
  try {
    const data = await $api<CorpusStats>('/admin/embeddings/corpus-stats')

    corpusStats.value = {
      totalBlocks: data.totalBlocks ?? 0,
      subjectsCovered: data.subjectsCovered ?? 0,
      conceptsCovered: data.conceptsCovered ?? 0,
      indexSizeMb: data.indexSizeMb ?? 0,
    }
  }
  catch (err: any) {
    console.error('Failed to fetch corpus stats:', err)
    error.value = err.message ?? 'Failed to load corpus stats'
  }
}

const fetchDuplicates = async () => {
  duplicateLoading.value = true
  try {
    const params = new URLSearchParams({
      threshold: duplicateThreshold.value.toString(),
      page: duplicatePage.value.toString(),
      pageSize: duplicatePageSize.value.toString(),
    })

    const data = await $api<DuplicateResponse>(`/admin/embeddings/duplicates?${params}`)

    duplicates.value = data.items ?? []
    duplicateTotalCount.value = data.totalCount ?? 0
  }
  catch (err: any) {
    console.error('Failed to fetch duplicates:', err)
    error.value = err.message ?? 'Failed to load duplicates'
  }
  finally {
    duplicateLoading.value = false
  }
}

const onDuplicatePageUpdate = async (options: { page: number; itemsPerPage: number }) => {
  duplicatePage.value = options.page
  duplicatePageSize.value = options.itemsPerPage
  await fetchDuplicates()
}

const runSearch = async () => {
  if (!searchQuery.value.trim()) return

  searchLoading.value = true
  searchError.value = null
  searchResults.value = []

  try {
    const body: Record<string, unknown> = { query: searchQuery.value }
    if (searchSubject.value)
      body.subject = searchSubject.value

    const data = await $api<SearchResult[]>('/admin/embeddings/search', {
      method: 'POST',
      body,
    })

    searchResults.value = data ?? []
  }
  catch (err: any) {
    console.error('Semantic search failed:', err)
    searchError.value = err.message ?? 'Search failed'
  }
  finally {
    searchLoading.value = false
  }
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchCorpusStats(), fetchDuplicates()])
  loading.value = false
}

onMounted(fetchAll)

const statCards = computed(() => [
  { title: 'Total Blocks', value: corpusStats.value.totalBlocks.toLocaleString(), icon: 'tabler-cube', color: 'primary' },
  { title: 'Subjects Covered', value: corpusStats.value.subjectsCovered.toLocaleString(), icon: 'tabler-book', color: 'info' },
  { title: 'Concepts Covered', value: corpusStats.value.conceptsCovered.toLocaleString(), icon: 'tabler-bulb', color: 'warning' },
  { title: 'Index Size', value: `${corpusStats.value.indexSizeMb.toFixed(1)} MB`, icon: 'tabler-database', color: 'success' },
])
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Embedding Corpus
        </h4>
        <div class="text-body-1">
          Vector index stats, semantic search, and duplicate detection
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

    <!-- Semantic Search Tester -->
    <VCard class="mb-6">
      <VCardItem title="Semantic Search Tester" />
      <VCardText>
        <VRow class="mb-4">
          <VCol
            cols="12"
            md="7"
          >
            <VTextField
              v-model="searchQuery"
              label="Search query"
              prepend-inner-icon="tabler-search"
              density="compact"
              @keyup.enter="runSearch"
            />
          </VCol>
          <VCol
            cols="12"
            md="3"
          >
            <VTextField
              v-model="searchSubject"
              label="Subject filter (optional)"
              density="compact"
              clearable
            />
          </VCol>
          <VCol
            cols="12"
            md="2"
          >
            <VBtn
              color="primary"
              block
              :loading="searchLoading"
              :disabled="!searchQuery.trim()"
              @click="runSearch"
            >
              Search
            </VBtn>
          </VCol>
        </VRow>

        <VAlert
          v-if="searchError"
          type="error"
          variant="tonal"
          class="mb-4"
          closable
          @click:close="searchError = null"
        >
          {{ searchError }}
        </VAlert>

        <VList
          v-if="searchResults.length"
          lines="three"
        >
          <VListItem
            v-for="(result, idx) in searchResults"
            :key="idx"
          >
            <VListItemTitle class="text-body-2 mb-1">
              {{ truncate(result.text, 200) }}
            </VListItemTitle>
            <VListItemSubtitle>
              <div class="d-flex gap-2 mt-1">
                <VChip
                  size="x-small"
                  :color="similarityColor(result.similarity)"
                  label
                >
                  {{ (result.similarity * 100).toFixed(1) }}%
                </VChip>
                <VChip
                  size="x-small"
                  variant="tonal"
                  color="primary"
                  label
                >
                  {{ result.subject }}
                </VChip>
                <VChip
                  size="x-small"
                  variant="tonal"
                  color="secondary"
                  label
                >
                  {{ result.contentType }}
                </VChip>
              </div>
            </VListItemSubtitle>
          </VListItem>
        </VList>

        <div
          v-else-if="!searchLoading && searchQuery && searchResults.length === 0"
          class="text-center py-4 text-disabled"
        >
          No results found
        </div>
      </VCardText>
    </VCard>

    <!-- Duplicate Review -->
    <VCard>
      <VCardItem title="Duplicate Review" />
      <VCardText>
        <VDataTableServer
          :headers="duplicateHeaders"
          :items="duplicates"
          :items-length="duplicateTotalCount"
          :items-per-page="duplicatePageSize"
          :page="duplicatePage"
          :loading="duplicateLoading"
          @update:options="onDuplicatePageUpdate"
        >
          <template #item.block1Preview="{ item }">
            <span :title="item.block1Preview">{{ truncate(item.block1Preview) }}</span>
          </template>

          <template #item.block2Preview="{ item }">
            <span :title="item.block2Preview">{{ truncate(item.block2Preview) }}</span>
          </template>

          <template #item.similarity="{ item }">
            <VChip
              :color="similarityColor(item.similarity)"
              label
              size="small"
            >
              {{ (item.similarity * 100).toFixed(1) }}%
            </VChip>
          </template>

          <template #no-data>
            <div class="text-center py-4 text-disabled">
              No duplicates found above threshold
            </div>
          </template>
        </VDataTableServer>
      </VCardText>
    </VCard>
  </div>
</template>
