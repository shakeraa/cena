<script setup lang="ts">
// =============================================================================
// Cena — Stuck-Type Diagnostics (RDY-063 Phase 2a)
//
// Admin-only read surface over StuckDiagnosisDocument. Shows:
//   1. Distribution across the 7 stuck-type categories (bar chart)
//   2. Top items by stuck-type rate (table, filterable)
//
// No student PII: only anon ids exist server-side, and this view only
// consumes aggregate counts by questionId.
// =============================================================================

import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

interface DistributionBucket {
  stuckType: string
  count: number
  fraction: number
}

interface DistributionResponse {
  days: number
  total: number
  generatedAt: string
  counts: DistributionBucket[]
}

interface ItemRow {
  questionId: string
  stuckType: string
  count: number
  distinctStudents: number
  avgConfidence: number
  firstSeenAt: string
  lastSeenAt: string
}

interface TopItemsResponse {
  days: number
  limit: number
  filter: string | null
  generatedAt: string
  items: ItemRow[]
}

const STUCK_TYPES = [
  { value: '', label: 'All types' },
  { value: 'Encoding', label: 'Encoding' },
  { value: 'Recall', label: 'Recall' },
  { value: 'Procedural', label: 'Procedural' },
  { value: 'Strategic', label: 'Strategic' },
  { value: 'Misconception', label: 'Misconception' },
  { value: 'Motivational', label: 'Motivational' },
  { value: 'MetaStuck', label: 'Meta-stuck' },
]

const COLOR_BY_TYPE: Record<string, string> = {
  Encoding: '#7367F0',
  Recall: '#28C76F',
  Procedural: '#FF9F43',
  Strategic: '#EA5455',
  Misconception: '#00CFE8',
  Motivational: '#A8AAAE',
  MetaStuck: '#6B5B95',
  Unknown: '#CCCCCC',
}

const days = ref(7)
const filter = ref('')
const limit = ref(20)

const distributionLoading = ref(true)
const distributionError = ref<string | null>(null)
const distribution = ref<DistributionResponse | null>(null)

const itemsLoading = ref(true)
const itemsError = ref<string | null>(null)
const items = ref<ItemRow[]>([])

const fetchDistribution = async () => {
  distributionLoading.value = true
  distributionError.value = null
  try {
    distribution.value = await $api<DistributionResponse>(
      `/admin/stuck-diagnostics/distribution?days=${days.value}`,
    )
  }
  catch (err: any) {
    distributionError.value = err.message ?? 'Failed to load distribution'
  }
  finally {
    distributionLoading.value = false
  }
}

const fetchItems = async () => {
  itemsLoading.value = true
  itemsError.value = null
  try {
    const qs = new URLSearchParams({
      days: String(days.value),
      limit: String(limit.value),
    })
    if (filter.value)
      qs.set('stuckType', filter.value)

    const resp = await $api<TopItemsResponse>(`/admin/stuck-diagnostics/top-items?${qs}`)
    items.value = resp.items
  }
  catch (err: any) {
    itemsError.value = err.message ?? 'Failed to load top items'
  }
  finally {
    itemsLoading.value = false
  }
}

const refresh = () => {
  fetchDistribution()
  fetchItems()
}

watch([days, filter, limit], refresh)
onMounted(refresh)

const displayedDistribution = computed(() => {
  if (!distribution.value) return []
  return distribution.value.counts.filter(c => c.stuckType !== 'Unknown')
})

const formatDate = (iso: string) => {
  try { return new Date(iso).toLocaleString() } catch { return iso }
}
</script>

<template>
  <div>
    <VRow class="mb-4">
      <VCol cols="12">
        <VCard>
          <VCardText>
            <div class="d-flex align-center flex-wrap ga-4">
              <div>
                <h5 class="text-h5 mb-1">
                  Stuck-Type Diagnostics
                </h5>
                <p class="text-body-2 text-medium-emphasis mb-0">
                  Aggregate classifier output over the last
                  {{ days }} day(s). Aggregate-only; never student-level.
                </p>
              </div>
              <VSpacer />
              <VSelect
                v-model="days"
                :items="[
                  { value: 1, title: 'Last 1 day' },
                  { value: 7, title: 'Last 7 days' },
                  { value: 14, title: 'Last 14 days' },
                  { value: 30, title: 'Last 30 days' },
                ]"
                item-title="title"
                item-value="value"
                density="compact"
                variant="outlined"
                hide-details
                style="max-width: 180px;"
              />
              <VBtn
                variant="tonal"
                prepend-icon="tabler-refresh"
                @click="refresh"
              >
                Refresh
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <VRow>
      <VCol cols="12" md="5">
        <VCard height="100%">
          <VCardItem>
            <VCardTitle>Distribution</VCardTitle>
            <VCardSubtitle>
              <template v-if="distribution">
                {{ distribution.total.toLocaleString() }} diagnoses
              </template>
              <template v-else>
                Loading…
              </template>
            </VCardSubtitle>
          </VCardItem>
          <VCardText>
            <VAlert
              v-if="distributionError"
              type="error"
              variant="tonal"
              class="mb-4"
            >
              {{ distributionError }}
            </VAlert>
            <div v-else-if="distributionLoading" class="text-center py-10">
              <VProgressCircular indeterminate />
            </div>
            <div v-else-if="!distribution?.total">
              <VAlert type="info" variant="tonal">
                No diagnoses recorded in this window. Classifier may be
                disabled, or no students have requested hints yet.
              </VAlert>
            </div>
            <div v-else>
              <div
                v-for="bucket in displayedDistribution"
                :key="bucket.stuckType"
                class="mb-3"
              >
                <div class="d-flex justify-space-between text-body-2 mb-1">
                  <span>{{ bucket.stuckType }}</span>
                  <span class="text-medium-emphasis">
                    {{ bucket.count }} ({{ (bucket.fraction * 100).toFixed(1) }}%)
                  </span>
                </div>
                <VProgressLinear
                  :model-value="bucket.fraction * 100"
                  :color="COLOR_BY_TYPE[bucket.stuckType] ?? '#7367F0'"
                  height="8"
                  rounded
                />
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="12" md="7">
        <VCard height="100%">
          <VCardItem>
            <VCardTitle>Top items by stuck-type rate</VCardTitle>
            <VCardSubtitle>Curriculum-review candidates</VCardSubtitle>
          </VCardItem>
          <VCardText>
            <div class="d-flex align-center flex-wrap ga-3 mb-4">
              <VSelect
                v-model="filter"
                :items="STUCK_TYPES"
                item-title="label"
                item-value="value"
                label="Filter by type"
                density="compact"
                variant="outlined"
                hide-details
                style="max-width: 220px;"
              />
              <VSelect
                v-model="limit"
                :items="[10, 20, 50, 100]"
                label="Rows"
                density="compact"
                variant="outlined"
                hide-details
                style="max-width: 140px;"
              />
            </div>

            <VAlert
              v-if="itemsError"
              type="error"
              variant="tonal"
            >
              {{ itemsError }}
            </VAlert>
            <div v-else-if="itemsLoading" class="text-center py-10">
              <VProgressCircular indeterminate />
            </div>
            <VAlert
              v-else-if="!items.length"
              type="info"
              variant="tonal"
            >
              No items match this filter in the selected window.
            </VAlert>
            <VTable v-else density="comfortable">
              <thead>
                <tr>
                  <th>Question</th>
                  <th>Type</th>
                  <th class="text-end">Count</th>
                  <th class="text-end">Students</th>
                  <th class="text-end">Avg conf.</th>
                  <th class="text-end">Last seen</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in items" :key="`${row.questionId}-${row.stuckType}`">
                  <td>
                    <code class="text-caption">{{ row.questionId }}</code>
                  </td>
                  <td>
                    <VChip
                      size="small"
                      :color="COLOR_BY_TYPE[row.stuckType] ?? 'default'"
                      variant="tonal"
                    >
                      {{ row.stuckType }}
                    </VChip>
                  </td>
                  <td class="text-end">{{ row.count }}</td>
                  <td class="text-end">{{ row.distinctStudents }}</td>
                  <td class="text-end">{{ (row.avgConfidence * 100).toFixed(0) }}%</td>
                  <td class="text-end text-caption text-medium-emphasis">
                    {{ formatDate(row.lastSeenAt) }}
                  </td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <VRow class="mt-4">
      <VCol cols="12">
        <VCard variant="tonal" color="info" density="compact">
          <VCardText class="d-flex align-center ga-3">
            <VIcon icon="tabler-info-circle" />
            <div class="text-body-2">
              <strong>Two flags, two phases.</strong>
              <code>Cena:StuckClassifier:Enabled</code> turns the
              classifier on (reads + logs + persists).
              <code>HintAdjustmentEnabled</code> turns hint-level
              adjustment on (clamps/bumps the level the student sees,
              per ADR-0036 rules). Both default OFF in production;
              adjustment respects a 500&nbsp;ms internal timeout and a
              0.65 min-confidence threshold. Data retention: 30 days;
              anon ids only (salt rotation severs cross-session
              linkability).
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
