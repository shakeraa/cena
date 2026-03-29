<script setup lang="ts">
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

onMounted(() => {
  if (isStudent.value) {
    fetchFocus()
    fetchMastery()
    fetchMethodology()
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
            <div class="text-h6 font-weight-bold text-primary">
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
