<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ConceptTile from '@/components/knowledge/ConceptTile.vue'
import { $api } from '@/api/$api'
import { ApiError } from '@/composables/useApiQuery'
import type { ConceptListDto, ConceptSummary } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.knowledgeGraph',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const SUBJECTS = ['math', 'physics', 'chemistry', 'biology'] as const
type Subject = typeof SUBJECTS[number]

const subject = ref<Subject>('math')
const concepts = ref<ConceptSummary[]>([])
const loading = ref(false)
const error = ref<ApiError | null>(null)

async function loadConcepts(next: Subject) {
  loading.value = true
  error.value = null
  try {
    const res = await $api<ConceptListDto>(`/api/content/concepts?subject=${next}`)

    concepts.value = res.items
  }
  catch (err) {
    // $api throws raw; wrap into ApiError so the template's
    // `error.i18nKey` resolves consistently with useApiQuery callers.
    const status = (err as { statusCode?: number; status?: number })?.statusCode
      ?? (err as { status?: number })?.status
      ?? 0
    const code = status === 429
      ? 'RATE_LIMITED'
      : status >= 500
        ? 'SERVER_ERROR'
        : status > 0
          ? `HTTP_${status}`
          : 'FETCH_FAILED'
    error.value = new ApiError(
      err instanceof Error ? err.message : String(err),
      'knowledgeGraph.unavailable',
      code,
    )
  }
  finally {
    loading.value = false
  }
}

watch(subject, next => loadConcepts(next), { immediate: true })

const masteredCount = computed(() =>
  concepts.value.filter(c => c.status === 'mastered').length,
)

const totalCount = computed(() => concepts.value.length)
</script>

<template>
  <div
    class="knowledge-graph-page pa-4"
    data-testid="knowledge-graph-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('knowledgeGraph.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('knowledgeGraph.subtitle') }}
    </p>

    <VTabs
      v-model="subject"
      color="primary"
      class="mb-4"
      data-testid="subject-tabs"
    >
      <VTab
        v-for="s in SUBJECTS"
        :key="s"
        :value="s"
        :data-testid="`subject-tab-${s}`"
      >
        {{ t(`session.setup.subjects.${s}`, s) }}
      </VTab>
    </VTabs>

    <div
      v-if="loading && concepts.length === 0"
      class="d-flex justify-center py-12"
      data-testid="concepts-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="error"
      type="error"
      variant="tonal"
      data-testid="concepts-error"
    >
      {{ t(error.i18nKey ?? 'knowledgeGraph.unavailable') }}
    </VAlert>

    <template v-else>
      <VCard
        variant="flat"
        color="primary"
        class="pa-4 mb-6"
        data-testid="subject-summary"
      >
        <div class="d-flex align-center justify-space-between">
          <div>
            <div class="text-caption text-white opacity-80">
              {{ t('knowledgeGraph.progressLabel') }}
            </div>
            <div class="text-h4 font-weight-bold text-white">
              {{ masteredCount }} / {{ totalCount }}
            </div>
          </div>
          <VIcon
            icon="tabler-affiliate"
            size="56"
            color="yellow-accent-2"
            aria-hidden="true"
          />
        </div>
      </VCard>

      <div
        class="knowledge-graph-page__grid"
        data-testid="concepts-grid"
      >
        <ConceptTile
          v-for="c in concepts"
          :key="c.conceptId"
          :concept="c"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.knowledge-graph-page {
  max-inline-size: 1100px;
  margin-inline: auto;
}

.knowledge-graph-page__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 1rem;
}
</style>
