<script setup lang="ts">
import { $api } from '@/utils/api'

interface TrialCohortMetrics {
  windowStart: string
  windowEnd: string
  activeTrialsCount: number
  trialsStartedInWindow: number
  trialsConvertedInWindow: number
  trialsExpiredInWindow: number
  conversionRatePct: number | null
  avgDaysToConvert: number | null
  medianDaysToConvert: number | null
  avgTutorTurnsAtConvert: number | null
  avgPhotoDiagnosticsAtConvert: number | null
}

const loading = ref(true)
const metrics = ref<TrialCohortMetrics | null>(null)
const error = ref<string | null>(null)

const fetchData = async () => {
  loading.value = true
  error.value = null
  try {
    metrics.value = await $api<TrialCohortMetrics>('/admin/cohorts/trial')
  }
  catch (err: any) {
    // Surface the failure honestly — empty-state with a "—" placeholder
    // hides backend wiring problems. Curators need to see when the
    // funnel data is unreachable so they don't act on stale numbers.
    console.error('TrialCohortCard: fetch failed', err)
    error.value = err?.data?.message ?? err?.message ?? 'Unable to load trial cohort metrics.'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchData)

const fmt = (n: number | null | undefined): string =>
  (n === null || n === undefined) ? '—' : n.toLocaleString()

const fmtPct = (n: number | null | undefined): string =>
  (n === null || n === undefined) ? '—' : `${n.toFixed(1)}%`

const fmtDays = (n: number | null | undefined): string =>
  (n === null || n === undefined) ? '—' : `${n.toFixed(1)} d`

// Funnel narrative line. Built honest-not-complimentary per
// feedback_honest_not_complimentary: state the raw counts, do not
// editorialize ("strong conversion" / "low engagement" etc.).
const funnelLine = computed(() => {
  if (!metrics.value) return ''
  const m = metrics.value
  return `${m.trialsStartedInWindow} started · ${m.trialsConvertedInWindow} converted · ${m.trialsExpiredInWindow} expired · ${m.activeTrialsCount} active`
})

// Conversion-rate badge colour reflects whether the rate is computable
// (we have at least one termination in window). A null rate is neutral —
// we don't render a colour because the cohort has not yet been observed.
const conversionRateColor = computed(() => {
  const rate = metrics.value?.conversionRatePct
  if (rate === null || rate === undefined) return undefined
  if (rate >= 60) return 'success'
  if (rate >= 30) return 'warning'
  return 'error'
})
</script>

<template>
  <VCard
    :loading="loading"
    hover
  >
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-x-2">
        <VIcon
          icon="tabler-flag"
          size="20"
          color="primary"
        />
        Trial Cohort
        <VSpacer />
        <VChip
          v-if="metrics?.conversionRatePct !== null && metrics?.conversionRatePct !== undefined"
          :color="conversionRateColor"
          size="small"
          variant="tonal"
        >
          {{ fmtPct(metrics.conversionRatePct) }}
        </VChip>
      </VCardTitle>
      <VCardSubtitle class="mt-1">
        Trailing 30 days
      </VCardSubtitle>
    </VCardItem>

    <VCardText>
      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        density="compact"
        class="mb-3"
        prepend-icon="tabler-alert-circle"
      >
        {{ error }}
      </VAlert>

      <VRow
        v-if="metrics"
        dense
        class="mb-3"
      >
        <VCol cols="3">
          <div class="text-body-2 text-medium-emphasis">
            Active
          </div>
          <h5 class="text-h5 font-weight-bold text-primary">
            {{ fmt(metrics.activeTrialsCount) }}
          </h5>
        </VCol>
        <VCol cols="3">
          <div class="text-body-2 text-medium-emphasis">
            Started
          </div>
          <h5 class="text-h5 font-weight-bold">
            {{ fmt(metrics.trialsStartedInWindow) }}
          </h5>
        </VCol>
        <VCol cols="3">
          <div class="text-body-2 text-medium-emphasis">
            Converted
          </div>
          <h5 class="text-h5 font-weight-bold text-success">
            {{ fmt(metrics.trialsConvertedInWindow) }}
          </h5>
        </VCol>
        <VCol cols="3">
          <div class="text-body-2 text-medium-emphasis">
            Expired
          </div>
          <h5 class="text-h5 font-weight-bold text-medium-emphasis">
            {{ fmt(metrics.trialsExpiredInWindow) }}
          </h5>
        </VCol>
      </VRow>

      <VDivider class="my-2" />

      <VRow
        v-if="metrics"
        dense
      >
        <VCol cols="6">
          <div class="text-body-2 text-medium-emphasis">
            Avg time to convert
          </div>
          <div class="text-body-1 font-weight-medium">
            {{ fmtDays(metrics.avgDaysToConvert) }}
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-body-2 text-medium-emphasis">
            Median time to convert
          </div>
          <div class="text-body-1 font-weight-medium">
            {{ fmtDays(metrics.medianDaysToConvert) }}
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-body-2 text-medium-emphasis">
            Avg tutor turns at convert
          </div>
          <div class="text-body-1">
            {{ fmt(metrics.avgTutorTurnsAtConvert) }}
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-body-2 text-medium-emphasis">
            Avg photo diagnostics at convert
          </div>
          <div class="text-body-1">
            {{ fmt(metrics.avgPhotoDiagnosticsAtConvert) }}
          </div>
        </VCol>
      </VRow>

      <div
        v-if="metrics && !error"
        class="text-caption text-medium-emphasis mt-3"
      >
        {{ funnelLine }}
      </div>
    </VCardText>
  </VCard>
</template>
