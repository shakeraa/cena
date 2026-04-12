<script setup lang="ts">
import StagnationInsightsPanel from '@/views/apps/pedagogy/StagnationInsightsPanel.vue'
import { $api } from '@/utils/api'

interface Props {
  userId: string
  userRole: string
}

const props = defineProps<Props>()
const isStudent = computed(() => props.userRole === 'STUDENT')

// ── Focus Data ──
interface FocusData {
  avgFocusScore7d: number
  avgFocusScore30d: number
  chronotype: { detectedChronotype: string; optimalStudyTime: string; recommendationText: string } | null
  mindWanderingEvents: { timestamp: string; context: string }[]
  microbreakHistory: { suggestedAt: string; wasTaken: boolean }[]
  sessions: { sessionId: string; startedAt: string; avgFocusScore: number; durationMinutes: number }[]
}

const focusLoading = ref(true)
const focusError = ref<string | null>(null)
const focusData = ref<FocusData | null>(null)

// ── Mastery Data ──
interface ConceptNode {
  conceptId: string
  conceptName: string
  subject: string
  masteryLevel: number
  status: string
}

interface ReviewItem {
  conceptId: string
  conceptName: string
  decayRisk: number
  lastMasteryLevel: number
  lastAttempted: string
  priority: number
}

interface MasteryHistoryPoint {
  date: string
  avgMastery: number
  conceptsAttempted: number
  conceptsMastered: number
}

interface MasteryData {
  knowledgeMap: ConceptNode[]
  reviewQueue: ReviewItem[]
  masteryHistory: MasteryHistoryPoint[]
  learningFrontier: { conceptId: string; conceptName: string; readinessScore: number; reason: string }[]
  scaffolding: { conceptId: string; conceptName: string; recommendedLevel: string; rationale: string }[]
}

const masteryLoading = ref(true)
const masteryError = ref<string | null>(null)
const masteryData = ref<MasteryData | null>(null)

// ── Methodology Profile ──
interface MethodologyEntry {
  id: string
  level: string
  methodology: string
  attemptCount: number
  successRate: number
  confidence: number
  hasSufficientData: boolean
}

const methodologyLoading = ref(true)
const methodologyData = ref<{ subjects: MethodologyEntry[]; topics: MethodologyEntry[]; concepts: MethodologyEntry[] } | null>(null)

// ── Focus Timeline ──
interface TimelinePoint {
  date?: string
  timestamp?: string
  focusScore: number
  mindWanderingCount?: number
  microbreakCount?: number
}

const timelineData = ref<TimelinePoint[]>([])

// ── Computed Insights ──

const focusScore7d = computed(() => focusData.value?.avgFocusScore7d ?? 0)
const focusScore30d = computed(() => focusData.value?.avgFocusScore30d ?? 0)
const focusTrend = computed(() => {
  if (!focusData.value) return 0
  return Math.round(focusData.value.avgFocusScore7d - focusData.value.avgFocusScore30d)
})

const microbreakCompliance = computed(() => {
  const breaks = focusData.value?.microbreakHistory ?? []
  if (breaks.length === 0) return 0
  const taken = breaks.filter(b => b.wasTaken).length
  return Math.round((taken / breaks.length) * 100)
})

const mindWanderingCount = computed(() => focusData.value?.mindWanderingEvents?.length ?? 0)

// Group mastery by subject
const masteryBySubject = computed(() => {
  const map = focusData.value ? new Map<string, ConceptNode[]>() : new Map<string, ConceptNode[]>()
  for (const node of masteryData.value?.knowledgeMap ?? []) {
    const list = map.get(node.subject) ?? []
    list.push(node)
    map.set(node.subject, list)
  }
  return Array.from(map.entries()).map(([subject, concepts]) => ({
    subject,
    avgMastery: Math.round(concepts.reduce((sum, c) => sum + c.masteryLevel, 0) / concepts.length),
    conceptCount: concepts.length,
    masteredCount: concepts.filter(c => c.status === 'mastered').length,
    inProgressCount: concepts.filter(c => c.status === 'in_progress').length,
    lockedCount: concepts.filter(c => c.status === 'locked').length,
  }))
})

// Concepts at risk of decay (top 5)
const decayRisks = computed(() => {
  return (masteryData.value?.reviewQueue ?? [])
    .slice(0, 5)
    .map(r => ({
      ...r,
      daysSinceAttempt: Math.round((Date.now() - new Date(r.lastAttempted).getTime()) / 86400000),
    }))
})

// Struggling concepts: in_progress with low mastery
const strugglingConcepts = computed(() => {
  return (masteryData.value?.knowledgeMap ?? [])
    .filter(c => c.status === 'in_progress' && c.masteryLevel < 40)
    .sort((a, b) => a.masteryLevel - b.masteryLevel)
    .slice(0, 5)
})

// Best methodology per subject
const methodologyBySubject = computed(() => {
  return (methodologyData.value?.subjects ?? [])
    .filter(m => m.hasSufficientData)
    .sort((a, b) => b.successRate - a.successRate)
})

// ── Chart: Mastery by Subject ──
const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const subjectChartOptions = computed(() => ({
  chart: { type: 'bar' as const, parentHeightOffset: 0, toolbar: { show: false } },
  plotOptions: { bar: { horizontal: true, barHeight: '60%', borderRadius: 4 } },
  colors: ['#9055FD'],
  dataLabels: { enabled: true, formatter: (v: number) => `${Math.round(v)}%` },
  xaxis: { min: 0, max: 100, labels: { style: { colors: labelColor, fontSize: '12px' } }, categories: subjectChartCategories.value },
  yaxis: { labels: { style: { colors: labelColor, fontSize: '13px' } } },
  grid: { strokeDashArray: 8, borderColor },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)}%` } },
}))

const subjectChartSeries = computed(() => [{
  name: 'Avg Mastery',
  data: masteryBySubject.value.map(s => s.avgMastery),
}])

const subjectChartCategories = computed(() => masteryBySubject.value.map(s => s.subject))

// ── Chart: Mastery History ──
const historyChartOptions = computed(() => ({
  chart: { type: 'area' as const, parentHeightOffset: 0, toolbar: { show: false }, zoom: { enabled: false } },
  stroke: { curve: 'smooth' as const, width: 2 },
  colors: ['#9055FD', '#56CA00'],
  grid: { strokeDashArray: 8, borderColor },
  xaxis: {
    categories: (masteryData.value?.masteryHistory ?? []).map(p => p.date),
    labels: { style: { colors: labelColor, fontSize: '11px' }, rotateAlways: true },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: [
    { title: { text: 'Avg Mastery %', style: { color: labelColor } }, labels: { style: { colors: labelColor } }, min: 0, max: 100 },
    { opposite: true, title: { text: 'Concepts Mastered', style: { color: labelColor } }, labels: { style: { colors: labelColor } }, min: 0 },
  ],
  tooltip: { shared: true },
  legend: { position: 'top' as const },
}))

const historyChartSeries = computed(() => {
  const history = masteryData.value?.masteryHistory ?? []
  return [
    { name: 'Avg Mastery', data: history.map(p => Math.round(p.avgMastery)) },
    { name: 'Concepts Mastered', data: history.map(p => p.conceptsMastered) },
  ]
})

// ═══════════════════════════════════════════════════════════════
// ROW 6-9 DATA
// ═══════════════════════════════════════════════════════════════

// ── Session Patterns ──
interface SessionPatterns {
  totalSessions: number
  avgDurationMinutes: number
  avgQuestionsPerSession: number
  abandonmentRate: number
  byHour: { timeSlot: string; sessionCount: number }[]
  byDay: { day: string; sessionCount: number }[]
  endReasons: { reason: string; count: number; percentage: number }[]
}

const sessionPatternsLoading = ref(true)
const sessionPatterns = ref<SessionPatterns | null>(null)

// ── Focus Heatmap (per-student) ──
interface HeatmapCellData { day: string; hour: string; avgFocusScore: number; studentCount: number }
const heatmapLoading = ref(true)
const heatmapCells = ref<HeatmapCellData[]>([])

// ── Focus Degradation (per-student) ──
interface DegradationPt { minutesIntoSession: number; avgFocusScore: number; sampleSize: number }
const degradationLoading = ref(true)
const degradationCurve = ref<DegradationPt[]>([])

// ── Engagement ──
interface EngagementData {
  currentStreak: number
  longestStreak: number
  lastActivityDate: string | null
  totalXp: number
  xpByDifficulty: { difficultyLevel: string; totalXp: number; attemptCount: number }[]
  badges: { badgeId: string; badgeName: string; badgeCategory: string; earnedAt: string }[]
}

const engagementLoading = ref(true)
const engagement = ref<EngagementData | null>(null)

// ── Error Types ──
interface ErrorTypesData {
  totalAttempts: number
  totalErrors: number
  errorRate: number
  byErrorType: { errorType: string; count: number; percentage: number }[]
  byConceptTopErrors: { conceptId: string; errorCount: number; dominantErrorType: string }[]
}

const errorTypesLoading = ref(true)
const errorTypes = ref<ErrorTypesData | null>(null)

// ── Hint Usage ──
interface HintUsageData {
  totalHintRequests: number
  byLevel: { level: number; label: string; count: number }[]
  byConcept: { conceptId: string; hintCount: number }[]
  hintEffectivenessPercent: number
}

const hintUsageLoading = ref(true)
const hintUsage = ref<HintUsageData | null>(null)

// ── Stagnation ──
interface StagnationData {
  stagnatingConcepts: { conceptId: string; compositeScore: number; consecutiveStagnantSessions: number; attemptedMethodologies: string[]; lastDetected: string }[]
  totalStagnationEvents: number
}

const stagnationLoading = ref(true)
const stagnation = ref<StagnationData | null>(null)
const expandedStagnationConcept = ref<string | null>(null)

// ── Response Times ──
interface ResponseTimeData {
  medianRtMs: number
  meanRtMs: number
  stdDevMs: number
  trend: { date: string; avgRtMs: number; attemptCount: number }[]
  anomalies: { timestamp: string; responseTimeMs: number; conceptId: string; expectedRangeMs: string }[]
}

const rtLoading = ref(true)
const rtData = ref<ResponseTimeData | null>(null)

// ── Outreach History ──
interface OutreachEvent {
  messageId: string
  channel: string
  sentAt: string
  deliveredAt: string | null
  openedAt: string | null
  reEngagedAt: string | null
}

const outreachLoading = ref(true)
const outreachEvents = ref<OutreachEvent[]>([])

// ── Computed: Session time chart ──
const sessionTimeChartOptions = computed(() => ({
  chart: { type: 'bar' as const, parentHeightOffset: 0, toolbar: { show: false } },
  plotOptions: { bar: { borderRadius: 3, columnWidth: '60%' } },
  colors: ['#FFB400'],
  dataLabels: { enabled: false },
  xaxis: {
    categories: (sessionPatterns.value?.byHour ?? []).map(h => h.timeSlot),
    labels: { style: { colors: labelColor, fontSize: '11px' }, rotateAlways: true },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: { labels: { style: { colors: labelColor } } },
  grid: { strokeDashArray: 8, borderColor },
  tooltip: { y: { formatter: (v: number) => `${v} sessions` } },
}))

const sessionTimeSeries = computed(() => [{
  name: 'Sessions',
  data: (sessionPatterns.value?.byHour ?? []).map(h => h.sessionCount),
}])

// ── Computed: Error type donut ──
const errorDonutOptions = computed(() => ({
  chart: { type: 'donut' as const },
  labels: (errorTypes.value?.byErrorType ?? []).map(e => e.errorType),
  colors: ['#FF4C51', '#FFB400', '#56CA00', '#16B1FF', '#9055FD', '#8A8D93'],
  legend: { position: 'bottom' as const, labels: { colors: labelColor } },
  plotOptions: { pie: { donut: { labels: { show: true, total: { show: true, label: 'Total Errors' } } } } },
}))

const errorDonutSeries = computed(() => (errorTypes.value?.byErrorType ?? []).map(e => e.count))

// ── Computed: RT trend ──
const rtTrendOptions = computed(() => ({
  chart: { type: 'line' as const, parentHeightOffset: 0, toolbar: { show: false }, zoom: { enabled: false } },
  stroke: { curve: 'smooth' as const, width: 2 },
  colors: ['#FF4C51'],
  grid: { strokeDashArray: 8, borderColor },
  xaxis: {
    categories: (rtData.value?.trend ?? []).map(t => t.date),
    labels: { style: { colors: labelColor, fontSize: '10px' }, rotateAlways: true },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: { labels: { style: { colors: labelColor }, formatter: (v: number) => `${Math.round(v)}ms` } },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} ms` } },
}))

const rtTrendSeries = computed(() => [{
  name: 'Avg RT',
  data: (rtData.value?.trend ?? []).map(t => t.avgRtMs),
}])

// ── Computed: Degradation curve chart ──
const degradationChartOptions = computed(() => ({
  chart: { type: 'area' as const, parentHeightOffset: 0, toolbar: { show: false }, zoom: { enabled: false } },
  stroke: { curve: 'smooth' as const, width: 2 },
  colors: ['#16B1FF'],
  grid: { strokeDashArray: 8, borderColor },
  xaxis: {
    categories: degradationCurve.value.map(p => `${p.minutesIntoSession}m`),
    labels: { style: { colors: labelColor, fontSize: '11px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: { min: 0, max: 100, labels: { style: { colors: labelColor }, formatter: (v: number) => `${Math.round(v)}` } },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} / 100` } },
}))

const degradationSeries = computed(() => [{
  name: 'Focus Score',
  data: degradationCurve.value.map(p => Math.round(p.avgFocusScore)),
}])

// ── Helpers ──
const focusScoreColor = (score: number) => score >= 70 ? 'success' : score >= 40 ? 'warning' : 'error'
const masteryColor = (level: number) => level >= 80 ? 'success' : level >= 50 ? 'info' : level >= 30 ? 'warning' : 'error'
const decayColor = (risk: number) => risk >= 0.7 ? 'error' : risk >= 0.4 ? 'warning' : 'success'

const formatDate = (dateStr: string) => {
  try {
    return new Date(dateStr).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  }
  catch { return dateStr }
}

// ── Fetch All ──
const fetchFocus = async () => {
  focusLoading.value = true
  try {
    const [detail, timeline] = await Promise.all([
      $api(`/admin/focus/students/${props.userId}`),
      $api(`/admin/focus/students/${props.userId}/timeline?period=7d`),
    ])
    focusData.value = detail as FocusData
    timelineData.value = (Array.isArray(timeline) ? timeline : (timeline.points ?? [])) as TimelinePoint[]
    focusError.value = null
  }
  catch (err: any) {
    focusError.value = err.message ?? 'Failed to load focus data'
  }
  finally {
    focusLoading.value = false
  }
}

const fetchMastery = async () => {
  masteryLoading.value = true
  try {
    const data = await $api(`/admin/mastery/students/${props.userId}`)
    masteryData.value = data as MasteryData
    masteryError.value = null
  }
  catch (err: any) {
    masteryError.value = err.message ?? 'Failed to load mastery data'
  }
  finally {
    masteryLoading.value = false
  }
}

const fetchMethodology = async () => {
  methodologyLoading.value = true
  try {
    const data = await $api(`/admin/mastery/students/${props.userId}/methodology-profile`)
    methodologyData.value = data as typeof methodologyData.value
  }
  catch { /* non-critical */ }
  finally {
    methodologyLoading.value = false
  }
}

// ── ROW 6-9 Fetches ──
const fetchSessionPatterns = async () => {
  sessionPatternsLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/session-patterns`)
    sessionPatterns.value = data as SessionPatterns
  }
  catch { /* non-critical */ }
  finally { sessionPatternsLoading.value = false }
}

const fetchHeatmap = async () => {
  heatmapLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/focus-heatmap`)
    heatmapCells.value = (data as any).cells ?? []
  }
  catch { /* non-critical */ }
  finally { heatmapLoading.value = false }
}

const fetchDegradation = async () => {
  degradationLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/degradation`)
    degradationCurve.value = (data as any).curve ?? []
  }
  catch { /* non-critical */ }
  finally { degradationLoading.value = false }
}

const fetchEngagement = async () => {
  engagementLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/engagement`)
    engagement.value = data as EngagementData
  }
  catch { /* non-critical */ }
  finally { engagementLoading.value = false }
}

const fetchErrorTypes = async () => {
  errorTypesLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/error-types`)
    errorTypes.value = data as ErrorTypesData
  }
  catch { /* non-critical */ }
  finally { errorTypesLoading.value = false }
}

const fetchHintUsage = async () => {
  hintUsageLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/hint-usage`)
    hintUsage.value = data as HintUsageData
  }
  catch { /* non-critical */ }
  finally { hintUsageLoading.value = false }
}

const fetchStagnation = async () => {
  stagnationLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/stagnation`)
    stagnation.value = data as StagnationData
  }
  catch { /* non-critical */ }
  finally { stagnationLoading.value = false }
}

const fetchResponseTimes = async () => {
  rtLoading.value = true
  try {
    const data = await $api(`/admin/insights/students/${props.userId}/response-times`)
    rtData.value = data as ResponseTimeData
  }
  catch { /* non-critical */ }
  finally { rtLoading.value = false }
}

const fetchOutreach = async () => {
  outreachLoading.value = true
  try {
    const data = await $api(`/admin/outreach/students/${props.userId}/history`)
    outreachEvents.value = (data as any).events ?? []
  }
  catch { /* non-critical */ }
  finally { outreachLoading.value = false }
}

onMounted(() => {
  if (isStudent.value) {
    // ROW 1-5 (existing)
    fetchFocus()
    fetchMastery()
    fetchMethodology()
    // ROW 6-9 (new)
    fetchSessionPatterns()
    fetchHeatmap()
    fetchDegradation()
    fetchEngagement()
    fetchErrorTypes()
    fetchHintUsage()
    fetchStagnation()
    fetchResponseTimes()
    fetchOutreach()
  }
})
</script>

<template>
  <div v-if="!isStudent">
    <VAlert type="info" variant="tonal">
      Learning insights are only available for students.
    </VAlert>
  </div>

  <div v-else>
    <!-- ═══ ROW 1: Focus Summary Cards ═══ -->
    <VRow class="match-height mb-2">
      <VCol cols="6" sm="3">
        <VCard :loading="focusLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Focus (7d)</div>
            <div class="text-h4 font-weight-bold" :class="`text-${focusScoreColor(focusScore7d)}`">
              {{ focusScore7d }}
            </div>
            <div class="d-flex align-center justify-center gap-x-1 mt-1">
              <VIcon
                :icon="focusTrend >= 0 ? 'tabler-trending-up' : 'tabler-trending-down'"
                :color="focusTrend >= 0 ? 'success' : 'error'"
                size="16"
              />
              <span class="text-caption" :class="focusTrend >= 0 ? 'text-success' : 'text-error'">
                {{ focusTrend > 0 ? '+' : '' }}{{ focusTrend }} vs 30d
              </span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="6" sm="3">
        <VCard :loading="focusLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Mind Wandering</div>
            <div class="text-h4 font-weight-bold" :class="mindWanderingCount > 10 ? 'text-error' : mindWanderingCount > 3 ? 'text-warning' : 'text-success'">
              {{ mindWanderingCount }}
            </div>
            <div class="text-caption text-medium-emphasis">events (30d)</div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="6" sm="3">
        <VCard :loading="focusLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Microbreak Compliance</div>
            <div class="text-h4 font-weight-bold" :class="`text-${focusScoreColor(microbreakCompliance)}`">
              {{ microbreakCompliance }}%
            </div>
            <div class="text-caption text-medium-emphasis">of suggested taken</div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="6" sm="3">
        <VCard :loading="focusLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Best Study Time</div>
            <div class="text-h6 font-weight-bold text-primary-text">
              {{ focusData?.chronotype?.optimalStudyTime ?? '--' }}
            </div>
            <div class="text-caption text-medium-emphasis">
              {{ focusData?.chronotype?.detectedChronotype ?? '' }}
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 2: Performance by Topic + Mastery History ═══ -->
    <VRow class="match-height mb-2">
      <!-- Mastery by Subject -->
      <VCol cols="12" md="5">
        <VCard :loading="masteryLoading">
          <VCardItem title="Mastery by Subject" />
          <VCardText>
            <VAlert v-if="masteryError" type="error" variant="tonal" class="mb-4">
              {{ masteryError }}
            </VAlert>

            <VueApexCharts
              v-if="masteryBySubject.length > 0"
              type="bar"
              height="250"
              :options="subjectChartOptions"
              :series="subjectChartSeries"
            />

            <!-- Subject detail chips -->
            <div v-if="masteryBySubject.length > 0" class="d-flex flex-wrap gap-2 mt-4">
              <VChip
                v-for="s in masteryBySubject"
                :key="s.subject"
                :color="masteryColor(s.avgMastery)"
                label
                size="small"
              >
                {{ s.subject }}: {{ s.masteredCount }}/{{ s.conceptCount }} mastered
              </VChip>
            </div>

            <div v-else-if="!masteryLoading && !masteryError" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No mastery data yet</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Mastery History -->
      <VCol cols="12" md="7">
        <VCard :loading="masteryLoading">
          <VCardItem title="Learning Progress Over Time" />
          <VCardText>
            <VueApexCharts
              v-if="(masteryData?.masteryHistory ?? []).length > 0"
              type="area"
              height="250"
              :options="historyChartOptions"
              :series="historyChartSeries"
            />
            <div v-else-if="!masteryLoading" class="d-flex justify-center py-8">
              <span class="text-body-1 text-disabled">No history data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 3: Irregularities + Decay Risk ═══ -->
    <VRow class="match-height mb-2">
      <!-- Struggling Concepts -->
      <VCol cols="12" md="6">
        <VCard :loading="masteryLoading">
          <VCardItem title="Struggling Concepts">
            <template #subtitle>Low mastery, needs attention</template>
          </VCardItem>
          <VCardText>
            <VList v-if="strugglingConcepts.length > 0" lines="two" density="compact">
              <VListItem v-for="c in strugglingConcepts" :key="c.conceptId">
                <template #prepend>
                  <VAvatar :color="masteryColor(c.masteryLevel)" variant="tonal" size="36">
                    <span class="text-caption font-weight-bold">{{ Math.round(c.masteryLevel) }}</span>
                  </VAvatar>
                </template>
                <VListItemTitle class="text-body-1 font-weight-medium">
                  {{ c.conceptName }}
                </VListItemTitle>
                <VListItemSubtitle>
                  {{ c.subject }} &mdash; {{ c.status }}
                </VListItemSubtitle>
              </VListItem>
            </VList>
            <div v-else-if="!masteryLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No struggling concepts</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Decay Risk (Review Queue) -->
      <VCol cols="12" md="6">
        <VCard :loading="masteryLoading">
          <VCardItem title="Decay Risk">
            <template #subtitle>Concepts losing retention</template>
          </VCardItem>
          <VCardText>
            <VList v-if="decayRisks.length > 0" lines="two" density="compact">
              <VListItem v-for="r in decayRisks" :key="r.conceptId">
                <template #prepend>
                  <VProgressCircular
                    :model-value="r.decayRisk * 100"
                    :color="decayColor(r.decayRisk)"
                    :size="36"
                    :width="3"
                  >
                    <span class="text-caption">{{ Math.round(r.decayRisk * 100) }}</span>
                  </VProgressCircular>
                </template>
                <VListItemTitle class="text-body-1 font-weight-medium">
                  {{ r.conceptName }}
                </VListItemTitle>
                <VListItemSubtitle>
                  Last practiced {{ r.daysSinceAttempt }}d ago &mdash; was {{ Math.round(r.lastMasteryLevel) }}%
                </VListItemSubtitle>
              </VListItem>
            </VList>
            <div v-else-if="!masteryLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No concepts at risk</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 4: Methodology Effectiveness + Learning Frontier ═══ -->
    <VRow class="match-height mb-2">
      <!-- What Works for This Student -->
      <VCol cols="12" md="6">
        <VCard :loading="methodologyLoading">
          <VCardItem title="What Works">
            <template #subtitle>Methodology effectiveness by subject</template>
          </VCardItem>
          <VCardText>
            <VTable v-if="methodologyBySubject.length > 0" density="compact">
              <thead>
                <tr>
                  <th>Subject</th>
                  <th>Methodology</th>
                  <th>Success</th>
                  <th>Confidence</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="m in methodologyBySubject" :key="m.id">
                  <td class="text-body-2">{{ m.id }}</td>
                  <td>
                    <VChip label size="small" color="primary" variant="tonal">
                      {{ m.methodology }}
                    </VChip>
                  </td>
                  <td>
                    <VChip :color="focusScoreColor(m.successRate * 100)" label size="small">
                      {{ Math.round(m.successRate * 100) }}%
                    </VChip>
                  </td>
                  <td>
                    <VProgressLinear
                      :model-value="m.confidence * 100"
                      :color="m.confidence >= 0.7 ? 'success' : 'warning'"
                      height="6"
                      rounded
                    />
                  </td>
                </tr>
              </tbody>
            </VTable>
            <div v-else-if="!methodologyLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">Not enough data yet</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Learning Frontier: What's Next -->
      <VCol cols="12" md="6">
        <VCard :loading="masteryLoading">
          <VCardItem title="Ready to Learn Next">
            <template #subtitle>Concepts with prerequisites met</template>
          </VCardItem>
          <VCardText>
            <VList v-if="(masteryData?.learningFrontier ?? []).length > 0" lines="two" density="compact">
              <VListItem v-for="f in (masteryData?.learningFrontier ?? []).slice(0, 5)" :key="f.conceptId">
                <template #prepend>
                  <VAvatar color="info" variant="tonal" size="36">
                    <VIcon icon="tabler-bulb" size="18" />
                  </VAvatar>
                </template>
                <VListItemTitle class="text-body-1 font-weight-medium">
                  {{ f.conceptName }}
                </VListItemTitle>
                <VListItemSubtitle>
                  Readiness: {{ Math.round(f.readinessScore * 100) }}% &mdash; {{ f.reason.replace(/_/g, ' ') }}
                </VListItemSubtitle>
              </VListItem>
            </VList>
            <div v-else-if="!masteryLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No frontier data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 5: Scaffolding Recommendations ═══ -->
    <VRow v-if="(masteryData?.scaffolding ?? []).length > 0" class="mb-2">
      <VCol cols="12">
        <VCard :loading="masteryLoading">
          <VCardItem title="Scaffolding Recommendations">
            <template #subtitle>Concepts needing instructional support</template>
          </VCardItem>
          <VCardText>
            <VTable density="compact">
              <thead>
                <tr>
                  <th>Concept</th>
                  <th>Support Level</th>
                  <th>Rationale</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="s in masteryData!.scaffolding" :key="s.conceptId">
                  <td class="text-body-2 font-weight-medium">{{ s.conceptName }}</td>
                  <td>
                    <VChip
                      :color="s.recommendedLevel === 'extensive' ? 'error' : s.recommendedLevel === 'moderate' ? 'warning' : 'success'"
                      label
                      size="small"
                      class="text-capitalize"
                    >
                      {{ s.recommendedLevel }}
                    </VChip>
                  </td>
                  <td class="text-body-2 text-medium-emphasis">{{ s.rationale }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 6: Session Patterns ═══ -->
    <VRow class="match-height mb-2">
      <!-- Study Time Distribution -->
      <VCol cols="12" md="4">
        <VCard :loading="sessionPatternsLoading">
          <VCardItem title="Study Time Distribution" />
          <VCardText>
            <VueApexCharts
              v-if="(sessionPatterns?.byHour ?? []).length > 0"
              type="bar"
              height="220"
              :options="sessionTimeChartOptions"
              :series="sessionTimeSeries"
            />
            <div v-else-if="!sessionPatternsLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No session data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Focus Degradation Curve (per student) -->
      <VCol cols="12" md="4">
        <VCard :loading="degradationLoading">
          <VCardItem title="Focus Degradation Curve">
            <template #subtitle>How focus drops during sessions</template>
          </VCardItem>
          <VCardText>
            <VueApexCharts
              v-if="degradationCurve.length > 0"
              type="area"
              height="220"
              :options="degradationChartOptions"
              :series="degradationSeries"
            />
            <div v-else-if="!degradationLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">Not enough data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Session Stats Cards -->
      <VCol cols="12" md="4">
        <VCard :loading="sessionPatternsLoading">
          <VCardItem title="Session Overview" />
          <VCardText>
            <VList density="compact">
              <VListItem>
                <template #prepend><VIcon icon="tabler-player-play" color="primary" size="20" class="me-2" /></template>
                <VListItemTitle>Total Sessions: <strong>{{ sessionPatterns?.totalSessions ?? 0 }}</strong></VListItemTitle>
              </VListItem>
              <VListItem>
                <template #prepend><VIcon icon="tabler-clock" color="info" size="20" class="me-2" /></template>
                <VListItemTitle>Avg Duration: <strong>{{ sessionPatterns?.avgDurationMinutes ?? 0 }} min</strong></VListItemTitle>
              </VListItem>
              <VListItem>
                <template #prepend><VIcon icon="tabler-help" color="warning" size="20" class="me-2" /></template>
                <VListItemTitle>Avg Questions/Session: <strong>{{ sessionPatterns?.avgQuestionsPerSession ?? 0 }}</strong></VListItemTitle>
              </VListItem>
              <VListItem>
                <template #prepend><VIcon icon="tabler-door-exit" :color="(sessionPatterns?.abandonmentRate ?? 0) > 20 ? 'error' : 'success'" size="20" class="me-2" /></template>
                <VListItemTitle>Abandonment Rate: <strong :class="(sessionPatterns?.abandonmentRate ?? 0) > 20 ? 'text-error' : 'text-success'">{{ sessionPatterns?.abandonmentRate ?? 0 }}%</strong></VListItemTitle>
              </VListItem>
            </VList>

            <!-- End Reasons -->
            <div v-if="(sessionPatterns?.endReasons ?? []).length > 0" class="mt-4">
              <div class="text-caption text-medium-emphasis mb-2">End Reasons</div>
              <div class="d-flex flex-wrap gap-2">
                <VChip
                  v-for="r in sessionPatterns!.endReasons"
                  :key="r.reason"
                  :color="r.reason === 'completed' ? 'success' : r.reason === 'abandoned' ? 'error' : 'warning'"
                  label
                  size="small"
                >
                  {{ r.reason }}: {{ r.count }}
                </VChip>
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 7: Engagement & Motivation ═══ -->
    <VRow class="match-height mb-2">
      <VCol cols="6" sm="3">
        <VCard :loading="engagementLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Current Streak</div>
            <div class="text-h4 font-weight-bold text-primary-text">
              {{ engagement?.currentStreak ?? 0 }}
            </div>
            <div class="text-caption text-medium-emphasis">
              Best: {{ engagement?.longestStreak ?? 0 }}
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="6" sm="3">
        <VCard :loading="engagementLoading">
          <VCardText class="text-center">
            <div class="text-caption text-medium-emphasis mb-1">Total XP</div>
            <div class="text-h4 font-weight-bold text-warning">
              {{ (engagement?.totalXp ?? 0).toLocaleString() }}
            </div>
            <div class="text-caption text-medium-emphasis">experience points</div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="12" sm="6">
        <VCard :loading="engagementLoading">
          <VCardItem title="Badges Earned" />
          <VCardText>
            <div v-if="(engagement?.badges ?? []).length > 0" class="d-flex flex-wrap gap-2">
              <VChip
                v-for="b in engagement!.badges"
                :key="b.badgeId"
                :color="b.badgeCategory === 'mastery' ? 'success' : b.badgeCategory === 'streak' ? 'warning' : 'info'"
                label
                size="small"
              >
                <VIcon start icon="tabler-award" size="14" />
                {{ b.badgeName }}
              </VChip>
            </div>
            <div v-else-if="!engagementLoading" class="text-body-2 text-disabled">No badges yet</div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 8: Learning Irregularities ═══ -->
    <VRow class="match-height mb-2">
      <!-- Error Type Distribution -->
      <VCol cols="12" md="4">
        <VCard :loading="errorTypesLoading">
          <VCardItem title="Error Types">
            <template #subtitle>{{ errorTypes?.totalErrors ?? 0 }} errors in {{ errorTypes?.totalAttempts ?? 0 }} attempts ({{ errorTypes?.errorRate?.toFixed(1) ?? 0 }}%)</template>
          </VCardItem>
          <VCardText>
            <VueApexCharts
              v-if="(errorTypes?.byErrorType ?? []).length > 0"
              type="donut"
              height="250"
              :options="errorDonutOptions"
              :series="errorDonutSeries"
            />
            <div v-else-if="!errorTypesLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No error data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Hint Usage -->
      <VCol cols="12" md="4">
        <VCard :loading="hintUsageLoading">
          <VCardItem title="Hint Usage">
            <template #subtitle>Effectiveness: {{ hintUsage?.hintEffectivenessPercent ?? 0 }}%</template>
          </VCardItem>
          <VCardText>
            <VList v-if="(hintUsage?.byLevel ?? []).length > 0" density="compact">
              <VListItem v-for="h in hintUsage!.byLevel" :key="h.level">
                <template #prepend>
                  <VAvatar :color="h.level === 1 ? 'success' : h.level === 2 ? 'warning' : 'error'" variant="tonal" size="32">
                    <span class="text-caption font-weight-bold">{{ h.level }}</span>
                  </VAvatar>
                </template>
                <VListItemTitle>{{ h.label }}</VListItemTitle>
                <VListItemSubtitle>{{ h.count }} requests</VListItemSubtitle>
              </VListItem>
            </VList>
            <div v-else-if="!hintUsageLoading" class="d-flex justify-center py-6">
              <span class="text-body-1 text-disabled">No hint data</span>
            </div>

            <!-- Top concepts needing hints -->
            <div v-if="(hintUsage?.byConcept ?? []).length > 0" class="mt-4">
              <div class="text-caption text-medium-emphasis mb-2">Most-Hinted Concepts</div>
              <div class="d-flex flex-wrap gap-1">
                <VChip v-for="c in hintUsage!.byConcept.slice(0, 5)" :key="c.conceptId" size="small" label color="warning" variant="tonal">
                  {{ c.conceptId }}: {{ c.hintCount }}
                </VChip>
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Response Time + Stagnation -->
      <VCol cols="12" md="4">
        <VCard :loading="rtLoading">
          <VCardItem title="Response Time Trend">
            <template #subtitle>Median: {{ rtData?.medianRtMs ?? 0 }}ms | Anomalies: {{ rtData?.anomalies?.length ?? 0 }}</template>
          </VCardItem>
          <VCardText>
            <VueApexCharts
              v-if="(rtData?.trend ?? []).length > 0"
              type="line"
              height="180"
              :options="rtTrendOptions"
              :series="rtTrendSeries"
            />
            <div v-else-if="!rtLoading" class="d-flex justify-center py-4">
              <span class="text-body-1 text-disabled">Not enough RT data</span>
            </div>
          </VCardText>
        </VCard>

        <!-- Stagnation Alerts with Root-Cause Drill-Down -->
        <VCard v-if="(stagnation?.stagnatingConcepts ?? []).length > 0" :loading="stagnationLoading" class="mt-4">
          <VCardItem title="Stagnation Alerts">
            <template #subtitle>{{ stagnation!.stagnatingConcepts.length }} concepts stuck — click Analyze for root cause</template>
          </VCardItem>
          <VCardText>
            <VList density="compact">
              <template v-for="s in stagnation!.stagnatingConcepts.slice(0, 5)" :key="s.conceptId">
                <VListItem>
                  <template #prepend>
                    <VAvatar color="error" variant="tonal" size="32">
                      <VIcon icon="tabler-alert-triangle" size="16" />
                    </VAvatar>
                  </template>
                  <VListItemTitle class="text-body-2 font-weight-medium">
                    {{ s.conceptId }}
                  </VListItemTitle>
                  <VListItemSubtitle>
                    {{ s.consecutiveStagnantSessions }} sessions stuck | Tried: {{ s.attemptedMethodologies?.join(', ') || 'none' }}
                  </VListItemSubtitle>
                  <template #append>
                    <VBtn
                      variant="text"
                      color="primary"
                      size="small"
                      @click="expandedStagnationConcept = expandedStagnationConcept === s.conceptId ? null : s.conceptId"
                    >
                      <VIcon :icon="expandedStagnationConcept === s.conceptId ? 'ri-arrow-up-s-line' : 'ri-search-eye-line'" class="me-1" />
                      {{ expandedStagnationConcept === s.conceptId ? 'Hide' : 'Analyze' }}
                    </VBtn>
                  </template>
                </VListItem>
                <!-- Inline root-cause analysis panel -->
                <div v-if="expandedStagnationConcept === s.conceptId" class="pa-4">
                  <StagnationInsightsPanel
                    :student-id="props.userId"
                    :concept-id="s.conceptId"
                  />
                </div>
              </template>
            </VList>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ ROW 9: Tutoring & Outreach ═══ -->
    <VRow v-if="outreachEvents.length > 0" class="mb-2">
      <VCol cols="12">
        <VCard :loading="outreachLoading">
          <VCardItem title="Outreach & Re-engagement History">
            <template #subtitle>Messages sent to bring student back</template>
          </VCardItem>
          <VCardText>
            <VTable density="compact">
              <thead>
                <tr>
                  <th>Channel</th>
                  <th>Sent</th>
                  <th>Delivered</th>
                  <th>Opened</th>
                  <th>Re-engaged</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="e in outreachEvents.slice(0, 10)" :key="e.messageId">
                  <td>
                    <VChip :color="e.channel === 'WhatsApp' ? 'success' : e.channel === 'Push' ? 'info' : 'warning'" label size="small">
                      {{ e.channel }}
                    </VChip>
                  </td>
                  <td class="text-body-2">{{ formatDate(e.sentAt) }}</td>
                  <td>
                    <VIcon v-if="e.deliveredAt" icon="tabler-check" color="success" size="18" />
                    <VIcon v-else icon="tabler-x" color="error" size="18" />
                  </td>
                  <td>
                    <VIcon v-if="e.openedAt" icon="tabler-check" color="success" size="18" />
                    <VIcon v-else icon="tabler-x" color="disabled" size="18" />
                  </td>
                  <td>
                    <VChip v-if="e.reEngagedAt" color="success" label size="small">Yes</VChip>
                    <span v-else class="text-disabled">--</span>
                  </td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- ═══ Quick Links ═══ -->
    <VRow>
      <VCol cols="12">
        <div class="d-flex gap-x-4">
          <VBtn
            variant="tonal"
            color="primary"
            prepend-icon="tabler-eye"
            :to="{ name: 'apps-focus-student-id', params: { id: props.userId } }"
          >
            Full Focus Detail
          </VBtn>
          <VBtn
            variant="tonal"
            color="info"
            prepend-icon="tabler-chart-dots-3"
            :to="{ path: `/apps/mastery/student/${props.userId}` }"
          >
            Full Mastery Detail
          </VBtn>
        </div>
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
