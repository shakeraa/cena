<script setup lang="ts">
import MethodologyOverrideDialog from '@/views/apps/mastery/student/MethodologyOverrideDialog.vue'
import ConceptCard from '@/views/apps/mastery/ConceptCard.vue'
import ConceptGraph from '@/views/apps/mastery/ConceptGraph.vue'
import MethodologyHierarchyPanel from '@/views/apps/pedagogy/MethodologyHierarchyPanel.vue'
import { $api } from '@/utils/api'

const router = useRouter()

definePage({
  meta: {
    action: 'read',
    subject: 'Mastery',
  },
})

const route = useRoute('apps-mastery-student-id')
const studentId = computed(() => route.params.id)

// --- Student overview ---
interface StudentMastery {
  studentId: string
  studentName: string
  avgMastery: number
  totalConcepts: number
  masteredCount: number
  learningCount: number
  notStartedCount: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const student = ref<StudentMastery | null>(null)

const showOverrideDialog = ref(false)
const overrideSnackbar = ref(false)

const onOverrideApplied = () => {
  overrideSnackbar.value = true
  // Refresh methodology data
  fetchStudent()
}

const fetchStudent = async () => {
  loading.value = true
  error.value = null
  try {
    student.value = await $api<StudentMastery>(`/admin/mastery/students/${studentId.value}`)
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load student mastery data'
    console.error('Failed to fetch student mastery:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchStudent)

// --- Knowledge Map ---
interface ConceptNode {
  conceptId: string
  name: string
  mastery: number
  status: 'mastered' | 'proficient' | 'developing' | 'introduced' | 'not-started'
  subject: string
}

interface KnowledgeMapData {
  concepts: ConceptNode[]
}

const mapLoading = ref(true)
const mapError = ref<string | null>(null)
const conceptsBySubject = ref<Record<string, ConceptNode[]>>({})

const fetchKnowledgeMap = async () => {
  mapLoading.value = true
  mapError.value = null
  try {
    const data = await $api<KnowledgeMapData>(`/admin/mastery/students/${studentId.value}/knowledge-map`)
    const grouped: Record<string, ConceptNode[]> = {}
    for (const concept of data.concepts ?? []) {
      if (!grouped[concept.subject]) grouped[concept.subject] = []
      grouped[concept.subject].push(concept)
    }
    conceptsBySubject.value = grouped
  }
  catch (err: any) {
    mapError.value = err.message ?? 'Failed to load knowledge map'
    console.error('Failed to fetch knowledge map:', err)
  }
  finally {
    mapLoading.value = false
  }
}

onMounted(fetchKnowledgeMap)

// --- Learning Frontier ---
interface FrontierConcept {
  conceptId: string
  name: string
  prerequisitesMet: number
  prerequisitesTotal: number
}

const frontierLoading = ref(true)
const frontier = ref<FrontierConcept[]>([])

const fetchFrontier = async () => {
  frontierLoading.value = true
  try {
    const data = await $api<{ concepts: FrontierConcept[] }>(`/admin/mastery/students/${studentId.value}/frontier`)
    frontier.value = data.concepts ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch learning frontier:', err)
  }
  finally {
    frontierLoading.value = false
  }
}

onMounted(fetchFrontier)

// --- Mastery History ---
interface HistoryPoint {
  date: string
  mastery: number
}

interface HistorySeries {
  conceptName: string
  points: HistoryPoint[]
}

const historyLoading = ref(true)
const historySeries = ref<HistorySeries[]>([])

const fetchHistory = async () => {
  historyLoading.value = true
  try {
    const data = await $api<{ series: HistorySeries[] }>(`/admin/mastery/students/${studentId.value}/history`)
    historySeries.value = data.series ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch mastery history:', err)
  }
  finally {
    historyLoading.value = false
  }
}

onMounted(fetchHistory)

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const historyChartOptions = computed(() => {
  const allDates = new Set<string>()
  for (const s of historySeries.value) {
    for (const p of s.points) allDates.add(p.date)
  }
  const sortedDates = Array.from(allDates).sort()

  return {
    chart: {
      type: 'line' as const,
      parentHeightOffset: 0,
      toolbar: { show: false },
      zoom: { enabled: false },
    },
    stroke: { curve: 'smooth' as const, width: 2 },
    markers: { size: 3, hover: { size: 5 } },
    xaxis: {
      categories: sortedDates,
      labels: {
        style: { colors: labelColor, fontSize: '11px' },
        rotate: -45,
        rotateAlways: sortedDates.length > 10,
      },
      axisBorder: { show: false },
      axisTicks: { show: false },
    },
    yaxis: {
      min: 0,
      max: 1,
      tickAmount: 5,
      labels: {
        style: { colors: labelColor, fontSize: '13px' },
        formatter(val: number) { return val.toFixed(1) },
      },
    },
    grid: { strokeDashArray: 8, borderColor },
    legend: {
      position: 'bottom' as const,
      fontSize: '13px',
      labels: { colors: labelColor },
      itemMargin: { horizontal: 12, vertical: 4 },
    },
    dataLabels: { enabled: false },
    tooltip: {
      y: { formatter(val: number) { return val.toFixed(3) } },
    },
  }
})

const historyChartSeries = computed(() => {
  const allDates = new Set<string>()
  for (const s of historySeries.value) {
    for (const p of s.points) allDates.add(p.date)
  }
  const sortedDates = Array.from(allDates).sort()

  return historySeries.value.map(s => {
    const pointMap = new Map(s.points.map(p => [p.date, p.mastery]))
    return {
      name: s.conceptName,
      data: sortedDates.map(d => pointMap.get(d) ?? null),
    }
  })
})

// --- Review Priority ---
interface ReviewItem {
  conceptId: string
  conceptName: string
  currentMastery: number
  decayRisk: number
  lastPracticed: string
}

const reviewLoading = ref(true)
const reviewItems = ref<ReviewItem[]>([])

const fetchReviewPriority = async () => {
  reviewLoading.value = true
  try {
    const data = await $api<{ items: ReviewItem[] }>(`/admin/mastery/students/${studentId.value}/review-priority`)
    reviewItems.value = data.items ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch review priority:', err)
  }
  finally {
    reviewLoading.value = false
  }
}

onMounted(fetchReviewPriority)

const decayColor = (risk: number): string => {
  if (risk >= 0.7) return 'error'
  if (risk >= 0.4) return 'warning'
  return 'info'
}
</script>

<template>
  <div>
    <!-- Header -->
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <div class="d-flex align-center gap-2 mb-1">
          <VBtn
            icon
            variant="text"
            size="small"
            @click="router.back()"
          >
            <VIcon icon="tabler-arrow-left" />
          </VBtn>
          <h4 class="text-h4">
            {{ student?.studentName ?? 'Student Detail' }}
          </h4>
        </div>
        <p
          v-if="student"
          class="text-body-1 text-medium-emphasis mb-0 ms-10"
        >
          {{ student.masteredCount }} mastered, {{ student.learningCount }} learning, {{ student.notStartedCount }} not started
          &mdash; Avg mastery: {{ (student.avgMastery * 100).toFixed(0) }}%
        </p>
      </div>

      <div class="d-flex gap-x-3">
        <VBtn
          variant="tonal"
          color="primary"
          prepend-icon="tabler-brain"
          :to="{ name: 'apps-user-view-id', params: { id: studentId }, query: { tab: 'insights' } }"
        >
          Full Insights
        </VBtn>

        <VBtn
          color="warning"
          variant="tonal"
          @click="showOverrideDialog = true"
        >
          <VIcon
            icon="tabler-switch-horizontal"
            start
          />
          Override Methodology
        </VBtn>
      </div>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
    >
      {{ error }}
    </VAlert>

    <VProgressLinear
      v-if="loading"
      indeterminate
      color="primary"
      class="mb-6"
    />

    <!-- Concept Graph (interactive D3 force graph) -->
    <div class="mb-6">
      <ConceptGraph :student-id="String(studentId)" />
    </div>

    <!-- Knowledge Map -->
    <VCard class="mb-6">
      <VCardItem title="Knowledge Map">
        <template #subtitle>
          Concept mastery grouped by subject
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="mapLoading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <VAlert
          v-if="mapError"
          type="error"
          variant="tonal"
          class="mb-4"
        >
          {{ mapError }}
        </VAlert>

        <div v-if="!mapLoading && !Object.keys(conceptsBySubject).length">
          <span class="text-disabled">No concept data available</span>
        </div>

        <VExpansionPanels
          v-else-if="Object.keys(conceptsBySubject).length"
          multiple
          variant="accordion"
        >
          <VExpansionPanel
            v-for="(concepts, subject) in conceptsBySubject"
            :key="subject"
            :title="String(subject)"
          >
            <VExpansionPanelText>
              <VRow>
                <VCol
                  v-for="concept in concepts"
                  :key="concept.conceptId"
                  cols="6"
                  sm="4"
                  md="3"
                  lg="2"
                >
                  <ConceptCard
                    :concept-id="concept.conceptId"
                    :name="concept.name"
                    :mastery="concept.mastery"
                    :status="concept.status"
                  />
                </VCol>
              </VRow>
            </VExpansionPanelText>
          </VExpansionPanel>
        </VExpansionPanels>
      </VCardText>
    </VCard>

    <!-- Learning Frontier -->
    <VCard class="mb-6">
      <VCardItem title="Learning Frontier">
        <template #subtitle>
          Concepts the student is ready for next
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="frontierLoading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <div
          v-else-if="!frontier.length"
          class="text-disabled"
        >
          No frontier concepts available
        </div>

        <VChipGroup
          v-else
          column
        >
          <VChip
            v-for="concept in frontier"
            :key="concept.conceptId"
            color="primary"
            variant="tonal"
            label
          >
            <VIcon
              icon="tabler-sparkles"
              size="16"
              start
            />
            {{ concept.name }}
            <VTooltip activator="parent">
              Prerequisites met: {{ concept.prerequisitesMet }}/{{ concept.prerequisitesTotal }}
            </VTooltip>
          </VChip>
        </VChipGroup>
      </VCardText>
    </VCard>

    <!-- Mastery History -->
    <VCard class="mb-6">
      <VCardItem title="Mastery History">
        <template #subtitle>
          Mastery over time for top concepts
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="historyLoading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <VueApexCharts
          v-if="historyChartSeries.length"
          type="line"
          height="350"
          :options="historyChartOptions"
          :series="historyChartSeries"
        />

        <div
          v-else-if="!historyLoading"
          class="d-flex align-center justify-center"
          style="min-height: 200px;"
        >
          <span class="text-disabled">No history data available</span>
        </div>
      </VCardText>
    </VCard>

    <!-- Methodology Hierarchy -->
    <div class="mb-6">
      <MethodologyHierarchyPanel :student-id="String(studentId)" />
    </div>

    <!-- Review Priority -->
    <VCard>
      <VCardItem title="Review Priority">
        <template #subtitle>
          Concepts at risk of decay, sorted by urgency
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="reviewLoading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <div
          v-else-if="!reviewItems.length"
          class="text-disabled"
        >
          No concepts at risk of decay
        </div>

        <VList
          v-else
          lines="two"
        >
          <VListItem
            v-for="item in reviewItems"
            :key="item.conceptId"
          >
            <template #prepend>
              <VAvatar
                variant="tonal"
                :color="decayColor(item.decayRisk)"
                rounded
              >
                <VIcon
                  icon="tabler-alert-circle"
                  size="24"
                />
              </VAvatar>
            </template>

            <VListItemTitle class="font-weight-medium">
              {{ item.conceptName }}
            </VListItemTitle>
            <VListItemSubtitle>
              Current: {{ (item.currentMastery * 100).toFixed(0) }}% &middot;
              Last practiced: {{ item.lastPracticed }}
            </VListItemSubtitle>

            <template #append>
              <VChip
                :color="decayColor(item.decayRisk)"
                label
                size="small"
              >
                {{ (item.decayRisk * 100).toFixed(0) }}% decay risk
              </VChip>
            </template>
          </VListItem>
        </VList>
      </VCardText>
    </VCard>

    <MethodologyOverrideDialog
      v-model="showOverrideDialog"
      :student-id="String(studentId)"
      :student-name="student?.studentName ?? ''"
      @override-applied="onOverrideApplied"
    />

    <VSnackbar
      v-model="overrideSnackbar"
      color="success"
      :timeout="3000"
    >
      Methodology override applied successfully
    </VSnackbar>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
