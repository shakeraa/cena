<script setup lang="ts">
import MethodologyOverrideDialog from '@/views/apps/mastery/student/MethodologyOverrideDialog.vue'
import ConceptCard from '@/views/apps/mastery/ConceptCard.vue'
import ConceptGraph from '@/views/apps/mastery/ConceptGraph.vue'
import MethodologyHierarchyPanel from '@/views/apps/pedagogy/MethodologyHierarchyPanel.vue'
import StagnationInsightsPanel from '@/views/apps/pedagogy/StagnationInsightsPanel.vue'
import { $api } from '@/utils/api'
import { useAbility } from '@casl/vue'

const router = useRouter()
const { can } = useAbility()

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
  fetchStudent()
  fetchOverrides()
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

// --- Methodology Overrides ---
interface MethodologyOverride {
  id: string
  studentId: string
  level: string
  levelId: string
  methodology: string
  teacherId: string
  createdAt: string
}

const overridesLoading = ref(false)
const overrides = ref<MethodologyOverride[]>([])
const removingOverrideId = ref<string | null>(null)
const removeErrorMsg = ref<string | null>(null)

const fetchOverrides = async () => {
  overridesLoading.value = true
  try {
    const data = await $api<{ overrides: MethodologyOverride[] }>(
      `/admin/mastery/students/${studentId.value}/methodology-overrides`,
    )
    overrides.value = data.overrides ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch methodology overrides:', err)
  }
  finally {
    overridesLoading.value = false
  }
}

const removeOverride = async (override: MethodologyOverride) => {
  removingOverrideId.value = override.id
  removeErrorMsg.value = null
  try {
    await $api(
      `/admin/mastery/students/${studentId.value}/methodology-overrides/${encodeURIComponent(override.id)}`,
      { method: 'DELETE' },
    )
    await fetchOverrides()
  }
  catch (err: any) {
    removeErrorMsg.value = err.data?.message ?? err.message ?? 'Failed to remove override'
  }
  finally {
    removingOverrideId.value = null
  }
}

const formatDate = (iso: string) => {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
  }
  catch {
    return iso
  }
}

onMounted(fetchOverrides)

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

// --- Stagnation Root-Cause Analysis ---
const selectedStagnationConcept = ref<string | null>(null)
const allConceptIds = computed(() => {
  const ids: string[] = []
  for (const concepts of Object.values(conceptsBySubject.value))
    for (const c of concepts)
      if (c.mastery > 0 && c.mastery < 0.7) ids.push(c.conceptId)
  return ids
})
const allConceptNames = computed(() => {
  const map: Record<string, string> = {}
  for (const concepts of Object.values(conceptsBySubject.value))
    for (const c of concepts) map[c.conceptId] = c.name
  return map
})
const conceptSelectItems = computed(() =>
  allConceptIds.value.map(id => ({ title: allConceptNames.value[id] ?? id, value: id })))

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
// prr-013 Phase 2 retirement 2026-04-20: the `decayRisk` point estimate was
// dropped from this view. Cross-session "risk of decay" framing violated
// ADR-0003 (session-scope) + RDY-080 (in-surface only). The review-priority
// list is kept as a plain queue of concepts-to-review ordered by server-
// side priority; the numeric decay badge is gone.
interface ReviewItem {
  conceptId: string
  conceptName: string
  currentMastery: number
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
          v-if="can('manage', 'Pedagogy')"
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

    <!-- Active override banners -->
    <template v-if="overrides.length">
      <VAlert
        v-for="ov in overrides"
        :key="ov.id"
        type="warning"
        variant="tonal"
        class="mb-4"
        border="start"
      >
        <div class="d-flex align-center justify-space-between flex-wrap gap-2">
          <span>
            <strong>Active override:</strong>
            {{ ov.methodology }}
            <template v-if="ov.level">
              for <em>{{ ov.level }}</em>
            </template>
            &mdash; set by {{ ov.teacherId }} on {{ formatDate(ov.createdAt) }}
          </span>
          <VBtn
            v-if="can('manage', 'Pedagogy')"
            size="small"
            color="warning"
            variant="tonal"
            :loading="removingOverrideId === ov.id"
            @click="removeOverride(ov)"
          >
            Remove Override
          </VBtn>
        </div>
      </VAlert>
      <VAlert
        v-if="removeErrorMsg"
        type="error"
        variant="tonal"
        class="mb-4"
        closable
        @click:close="removeErrorMsg = null"
      >
        {{ removeErrorMsg }}
      </VAlert>
    </template>

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

    <!-- Stagnation Root-Cause Analysis -->
    <VCard v-if="allConceptIds.length > 0" class="mb-6">
      <VCardItem title="Stagnation Root-Cause Analysis">
        <template #subtitle>
          Select a struggling concept to analyze why the student is stuck
        </template>
      </VCardItem>
      <VCardText>
        <VSelect
          v-model="selectedStagnationConcept"
          :items="conceptSelectItems"
          label="Select concept to analyze"
          density="compact"
          class="mb-4"
          clearable
        />
        <StagnationInsightsPanel
          v-if="selectedStagnationConcept"
          :student-id="String(studentId)"
          :concept-id="selectedStagnationConcept"
        />
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
          Concepts suggested for the next review session, in priority order
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
          No concepts queued for review
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
                color="info"
                rounded
              >
                <VIcon
                  icon="tabler-refresh"
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
          </VListItem>
        </VList>
      </VCardText>
    </VCard>

    <!-- Override History -->
    <VCard class="mb-6">
      <VCardItem title="Methodology Override History">
        <template #subtitle>
          Active and past overrides for this student
        </template>
        <template #append>
          <VProgressCircular
            v-if="overridesLoading"
            indeterminate
            size="20"
            width="2"
            color="primary"
          />
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <div
          v-if="!overridesLoading && !overrides.length"
          class="text-disabled"
        >
          No methodology overrides have been set for this student
        </div>

        <VTable v-else-if="overrides.length">
          <thead>
            <tr>
              <th>Methodology</th>
              <th>Concept / Level</th>
              <th>Set By</th>
              <th>Date</th>
              <th v-if="can('manage', 'Pedagogy')" />
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="ov in overrides"
              :key="ov.id"
            >
              <td>
                <VChip
                  color="warning"
                  label
                  size="small"
                >
                  {{ ov.methodology }}
                </VChip>
              </td>
              <td>{{ ov.level || '—' }}</td>
              <td>{{ ov.teacherId }}</td>
              <td>{{ formatDate(ov.createdAt) }}</td>
              <td v-if="can('manage', 'Pedagogy')">
                <VBtn
                  icon
                  variant="text"
                  size="small"
                  color="error"
                  :loading="removingOverrideId === ov.id"
                  @click="removeOverride(ov)"
                >
                  <VIcon icon="tabler-trash" />
                  <VTooltip activator="parent">
                    Remove override
                  </VTooltip>
                </VBtn>
              </td>
            </tr>
          </tbody>
        </VTable>
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
