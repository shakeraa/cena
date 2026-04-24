<script setup lang="ts">
// =============================================================================
// Cena — Stuck-Type Diagnostics (RDY-063 Phase 2a/2b)
//
// Admin-only read surface over StuckDiagnosisDocument. Shows:
//   1. Distribution across the 7 stuck-type categories (bar chart)
//   2. Top items by stuck-type rate (table, filterable)
//
// No student PII: only anon ids exist server-side, and this view only
// consumes aggregate counts by questionId.
//
// i18n: all user-visible strings flow through $t() against the
// `diagnostics.stuckTypes.*` namespace. Supports en / he / ar / fr.
// RTL rendering for Hebrew/Arabic is handled by Vuetify's theme
// direction (which tracks the i18n locale), so this page contains
// no hard-coded `dir=` attributes — the bidi flips happen for free.
// =============================================================================

import { useI18n } from 'vue-i18n'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

const { t, locale } = useI18n()

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

// Stable enum values sent to the server; labels are translated.
const STUCK_TYPE_VALUES = ['Encoding', 'Recall', 'Procedural', 'Strategic', 'Misconception', 'Motivational', 'MetaStuck'] as const

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

// ── i18n helpers ──────────────────────────────────────────────────────
const typeLabel = (value: string) => {
  // Translated label for a stuck-type enum value. Unknown types fall
  // through to the raw value so future backend additions don't show
  // blank strings in the UI.
  const key = `diagnostics.stuckTypes.types.${value}`
  const translated = t(key)

  return translated === key ? value : translated
}

const dayRangeOptions = computed(() => [
  { value: 1, title: t('diagnostics.stuckTypes.rangeLast1Day') },
  { value: 7, title: t('diagnostics.stuckTypes.rangeLast7Days') },
  { value: 14, title: t('diagnostics.stuckTypes.rangeLast14Days') },
  { value: 30, title: t('diagnostics.stuckTypes.rangeLast30Days') },
])

const filterOptions = computed(() => [
  { value: '', label: t('diagnostics.stuckTypes.filterAll') },
  ...STUCK_TYPE_VALUES.map(v => ({ value: v, label: typeLabel(v) })),
])

// ── Data fetch ────────────────────────────────────────────────────────
const fetchDistribution = async () => {
  distributionLoading.value = true
  distributionError.value = null
  try {
    distribution.value = await $api<DistributionResponse>(
      `/admin/stuck-diagnostics/distribution?days=${days.value}`,
    )
  }
  catch (err: any) {
    distributionError.value = err.message ?? t('diagnostics.stuckTypes.errorDistribution')
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
    itemsError.value = err.message ?? t('diagnostics.stuckTypes.errorTopItems')
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
  if (!distribution.value)
    return []

  return distribution.value.counts.filter(c => c.stuckType !== 'Unknown')
})

// Locale-aware timestamp formatting. The admin locale drives the
// number/date system; Intl.DateTimeFormat handles RTL digits for
// Arabic-Indic etc. automatically.
const formatDate = (iso: string) => {
  try {
    return new Date(iso).toLocaleString(locale.value, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  }
  catch {
    return iso
  }
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
                  {{ t('diagnostics.stuckTypes.pageTitle') }}
                </h5>
                <p class="text-body-2 text-medium-emphasis mb-0">
                  {{ t('diagnostics.stuckTypes.pageSubtitle', { days }) }}
                </p>
              </div>
              <VSpacer />
              <VSelect
                v-model="days"
                :items="dayRangeOptions"
                item-title="title"
                item-value="value"
                density="compact"
                variant="outlined"
                hide-details
                style="max-width: 200px;"
              />
              <VBtn
                variant="tonal"
                prepend-icon="tabler-refresh"
                @click="refresh"
              >
                {{ t('diagnostics.stuckTypes.refresh') }}
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <VRow>
      <VCol
        cols="12"
        md="5"
      >
        <VCard height="100%">
          <VCardItem>
            <VCardTitle>{{ t('diagnostics.stuckTypes.distribution') }}</VCardTitle>
            <VCardSubtitle>
              <template v-if="distribution">
                {{ t('diagnostics.stuckTypes.diagnosesCount', { count: distribution.total.toLocaleString(locale) }) }}
              </template>
              <template v-else>
                {{ t('diagnostics.stuckTypes.loading') }}
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
            <div
              v-else-if="distributionLoading"
              class="text-center py-10"
            >
              <VProgressCircular indeterminate />
            </div>
            <div v-else-if="!distribution?.total">
              <VAlert
                type="info"
                variant="tonal"
              >
                {{ t('diagnostics.stuckTypes.noData') }}
              </VAlert>
            </div>
            <div v-else>
              <div
                v-for="bucket in displayedDistribution"
                :key="bucket.stuckType"
                class="mb-3"
              >
                <div class="d-flex justify-space-between text-body-2 mb-1">
                  <span>{{ typeLabel(bucket.stuckType) }}</span>
                  <span class="text-medium-emphasis">
                    {{ bucket.count.toLocaleString(locale) }} ({{ (bucket.fraction * 100).toFixed(1) }}%)
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

      <VCol
        cols="12"
        md="7"
      >
        <VCard height="100%">
          <VCardItem>
            <VCardTitle>{{ t('diagnostics.stuckTypes.topItemsTitle') }}</VCardTitle>
            <VCardSubtitle>{{ t('diagnostics.stuckTypes.topItemsSubtitle') }}</VCardSubtitle>
          </VCardItem>
          <VCardText>
            <div class="d-flex align-center flex-wrap ga-3 mb-4">
              <VSelect
                v-model="filter"
                :items="filterOptions"
                item-title="label"
                item-value="value"
                :label="t('diagnostics.stuckTypes.filterLabel')"
                density="compact"
                variant="outlined"
                hide-details
                style="max-width: 240px;"
              />
              <VSelect
                v-model="limit"
                :items="[10, 20, 50, 100]"
                :label="t('diagnostics.stuckTypes.rowsLabel')"
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
            <div
              v-else-if="itemsLoading"
              class="text-center py-10"
            >
              <VProgressCircular indeterminate />
            </div>
            <VAlert
              v-else-if="!items.length"
              type="info"
              variant="tonal"
            >
              {{ t('diagnostics.stuckTypes.noItems') }}
            </VAlert>
            <VTable
              v-else
              density="comfortable"
            >
              <thead>
                <tr>
                  <th>{{ t('diagnostics.stuckTypes.colQuestion') }}</th>
                  <th>{{ t('diagnostics.stuckTypes.colType') }}</th>
                  <th class="text-end">
                    {{ t('diagnostics.stuckTypes.colCount') }}
                  </th>
                  <th class="text-end">
                    {{ t('diagnostics.stuckTypes.colStudents') }}
                  </th>
                  <th class="text-end">
                    {{ t('diagnostics.stuckTypes.colAvgConfidence') }}
                  </th>
                  <th class="text-end">
                    {{ t('diagnostics.stuckTypes.colLastSeen') }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in items"
                  :key="`${row.questionId}-${row.stuckType}`"
                >
                  <td>
                    <!--
                      Question ID is always LTR text; wrap in bdi so
                      it renders left-to-right even inside RTL pages.
                    -->
                    <bdi dir="ltr"><code class="text-caption">{{ row.questionId }}</code></bdi>
                  </td>
                  <td>
                    <VChip
                      size="small"
                      :color="COLOR_BY_TYPE[row.stuckType] ?? 'default'"
                      variant="tonal"
                    >
                      {{ typeLabel(row.stuckType) }}
                    </VChip>
                  </td>
                  <td class="text-end">
                    {{ row.count.toLocaleString(locale) }}
                  </td>
                  <td class="text-end">
                    {{ row.distinctStudents.toLocaleString(locale) }}
                  </td>
                  <td class="text-end">
                    {{ (row.avgConfidence * 100).toFixed(0) }}%
                  </td>
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
        <VCard
          variant="tonal"
          color="info"
          density="compact"
        >
          <VCardText class="d-flex align-center ga-3">
            <VIcon icon="tabler-info-circle" />
            <div class="text-body-2">
              <strong>{{ t('diagnostics.stuckTypes.calloutTitle') }}</strong>
              <I18nT
                keypath="diagnostics.stuckTypes.calloutBody"
                tag="span"
                scope="global"
              >
                <template #enabledFlag>
                  <!--
                    Config key is always LTR (snake-case / colon-separated),
                    isolate with bdi so it renders LTR in Hebrew/Arabic pages.
                  -->
                  <bdi dir="ltr"><code>Cena:StuckClassifier:Enabled</code></bdi>
                </template>
                <template #adjustFlag>
                  <bdi dir="ltr"><code>HintAdjustmentEnabled</code></bdi>
                </template>
              </I18nT>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
