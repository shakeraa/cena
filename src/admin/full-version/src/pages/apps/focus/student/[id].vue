<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Focus' } })

const route = useRoute('apps-focus-student-id')
const studentId = computed(() => route.params.id)

// --- Student profile data ---
interface StudentFocusProfile {
  studentName: string
  avgFocusScore: number
  classAvg: number
  gradeAvg: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<StudentFocusProfile>({ studentName: '', avgFocusScore: 0, classAvg: 0, gradeAvg: 0 })

// --- Focus timeline ---
const timelineRange = ref<'7d' | '30d'>('7d')

interface TimelinePoint {
  date: string
  focusScore: number
}

const timelineData = ref<TimelinePoint[]>([])
const timelineLoading = ref(false)

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const timelineChartOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
    zoom: { enabled: false },
  },
  stroke: { curve: 'smooth' as const, width: 3 },
  colors: ['#9055FD'],
  markers: { size: 3, colors: '#fff', strokeColors: '#9055FD' },
  grid: { strokeDashArray: 8, borderColor },
  xaxis: {
    categories: timelineData.value.map(p => p.date),
    labels: { style: { colors: labelColor, fontSize: '12px' }, rotateAlways: timelineData.value.length > 14 },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    min: 0,
    max: 100,
    tickAmount: 5,
    labels: { style: { colors: labelColor, fontSize: '13px' }, formatter: (v: number) => `${Math.round(v)}` },
  },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} / 100` } },
}))

const timelineChartSeries = computed(() => [
  { name: 'Focus Score', data: timelineData.value.map(p => p.focusScore) },
])

// --- Sessions table ---
interface FocusSession {
  sessionId: string
  date: string
  durationMinutes: number
  focusScore: number
  subject: string
}

const sessions = ref<FocusSession[]>([])
const sessionHeaders = [
  { title: 'Date', key: 'date' },
  { title: 'Subject', key: 'subject' },
  { title: 'Duration (min)', key: 'durationMinutes' },
  { title: 'Focus Score', key: 'focusScore' },
]

// --- Mind wandering events ---
interface MindWanderingEvent {
  timestamp: string
  durationSeconds: number
  context: string
}

const wanderingEvents = ref<MindWanderingEvent[]>([])

// --- Microbreak history ---
interface MicrobreakRecord {
  suggestedAt: string
  takenAt: string | null
  durationSeconds: number
}

const microbreaks = ref<MicrobreakRecord[]>([])
const microbreakHeaders = [
  { title: 'Suggested At', key: 'suggestedAt' },
  { title: 'Taken At', key: 'takenAt' },
  { title: 'Duration (s)', key: 'durationSeconds' },
]

// --- Comparison bar chart ---
const comparisonChartOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: { horizontal: true, barHeight: '50%', borderRadius: 4 },
  },
  colors: ['#9055FD', '#56CA00', '#FFB400'],
  dataLabels: { enabled: true, formatter: (v: number) => `${Math.round(v)}` },
  xaxis: {
    min: 0,
    max: 100,
    labels: { style: { colors: labelColor, fontSize: '13px' } },
  },
  yaxis: {
    labels: { style: { colors: labelColor, fontSize: '13px' } },
  },
  grid: { strokeDashArray: 8, borderColor },
  legend: { position: 'bottom' as const },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} / 100` } },
}))

const comparisonChartSeries = computed(() => [
  { name: 'This Student', data: [profile.value.avgFocusScore] },
  { name: 'Class Average', data: [profile.value.classAvg] },
  { name: 'Grade Average', data: [profile.value.gradeAvg] },
])

// --- Fetch ---
const fetchStudentData = async () => {
  loading.value = true
  try {
    const data = await $api(`/admin/focus/students/${studentId.value}`)

    profile.value = {
      studentName: data.studentName ?? '',
      avgFocusScore: data.avgFocusScore ?? 0,
      classAvg: data.classAvg ?? 0,
      gradeAvg: data.gradeAvg ?? 0,
    }

    sessions.value = (data.sessions ?? []).map((s: any) => ({
      sessionId: s.sessionId,
      date: s.date,
      durationMinutes: s.durationMinutes ?? 0,
      focusScore: s.focusScore ?? 0,
      subject: s.subject ?? '',
    }))

    wanderingEvents.value = (data.mindWanderingEvents ?? []).map((e: any) => ({
      timestamp: e.timestamp,
      durationSeconds: e.durationSeconds ?? 0,
      context: e.context ?? '',
    }))

    microbreaks.value = (data.microbreaks ?? []).map((m: any) => ({
      suggestedAt: m.suggestedAt,
      takenAt: m.takenAt ?? null,
      durationSeconds: m.durationSeconds ?? 0,
    }))

    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch student focus data:', err)
    error.value = err.message ?? 'Failed to load student data'
  }
  finally {
    loading.value = false
  }
}

const fetchTimeline = async () => {
  timelineLoading.value = true
  try {
    const days = timelineRange.value === '7d' ? 7 : 30
    const data = await $api(`/admin/focus/students/${studentId.value}/timeline?days=${days}`)

    timelineData.value = (Array.isArray(data) ? data : (data.points ?? [])).map((p: any) => ({
      date: p.date,
      focusScore: p.focusScore ?? 0,
    }))
  }
  catch (err: any) {
    console.error('Failed to fetch focus timeline:', err)
  }
  finally {
    timelineLoading.value = false
  }
}

watch(timelineRange, fetchTimeline)

onMounted(async () => {
  await Promise.all([fetchStudentData(), fetchTimeline()])
})

const focusScoreColor = (score: number): string => {
  if (score >= 70) return 'success'
  if (score >= 40) return 'warning'
  return 'error'
}
</script>

<template>
  <div>
    <!-- Header -->
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <div class="d-flex align-center gap-x-2 mb-1">
          <VBtn
            icon
            variant="text"
            size="small"
            :to="{ name: 'apps-focus-dashboard' }"
          >
            <VIcon icon="tabler-arrow-left" />
          </VBtn>
          <h4 class="text-h4">
            {{ profile.studentName || `Student #${studentId}` }}
          </h4>
        </div>
        <div class="text-body-1 ms-10">
          Individual focus & attention detail
        </div>
      </div>

      <VBtn
        variant="tonal"
        color="primary"
        prepend-icon="tabler-brain"
        :to="{ name: 'apps-user-view-id', params: { id: studentId }, query: { tab: 'insights' } }"
      >
        Full Insights
      </VBtn>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
    >
      {{ error }}
    </VAlert>

    <VRow
      v-if="!error"
      class="match-height"
    >
      <!-- Focus Timeline -->
      <VCol
        cols="12"
        md="8"
      >
        <VCard :loading="timelineLoading">
          <VCardItem title="Focus Timeline">
            <template #append>
              <VBtnToggle
                v-model="timelineRange"
                mandatory
                density="compact"
                variant="outlined"
              >
                <VBtn
                  value="7d"
                  size="small"
                >
                  7 Days
                </VBtn>
                <VBtn
                  value="30d"
                  size="small"
                >
                  30 Days
                </VBtn>
              </VBtnToggle>
            </template>
          </VCardItem>
          <VCardText>
            <VueApexCharts
              v-if="timelineData.length > 0"
              type="line"
              height="300"
              :options="timelineChartOptions"
              :series="timelineChartSeries"
            />
            <div
              v-else-if="!timelineLoading"
              class="d-flex justify-center py-8"
            >
              <span class="text-body-1 text-disabled">No timeline data</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Comparison -->
      <VCol
        cols="12"
        md="4"
      >
        <VCard :loading="loading">
          <VCardItem title="Comparison (Anonymized)" />
          <VCardText>
            <VueApexCharts
              type="bar"
              height="200"
              :options="comparisonChartOptions"
              :series="comparisonChartSeries"
            />
          </VCardText>
        </VCard>
      </VCol>

      <!-- Session List -->
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem title="Recent Sessions" />
          <VCardText>
            <VDataTable
              :headers="sessionHeaders"
              :items="sessions"
              :items-per-page="10"
              density="compact"
            >
              <template #item.focusScore="{ item }">
                <VChip
                  :color="focusScoreColor(item.focusScore)"
                  label
                  size="small"
                >
                  {{ item.focusScore }}
                </VChip>
              </template>
            </VDataTable>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Mind Wandering Events -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem title="Mind Wandering Events" />
          <VCardText>
            <VTimeline
              v-if="wanderingEvents.length > 0"
              density="compact"
              side="end"
              truncate-line="both"
            >
              <VTimelineItem
                v-for="(evt, idx) in wanderingEvents"
                :key="idx"
                dot-color="warning"
                size="small"
              >
                <div class="text-body-2 font-weight-medium">
                  {{ evt.timestamp }}
                </div>
                <div class="text-caption text-medium-emphasis">
                  Duration: {{ evt.durationSeconds }}s — {{ evt.context }}
                </div>
              </VTimelineItem>
            </VTimeline>
            <div
              v-else-if="!loading"
              class="d-flex justify-center py-6"
            >
              <span class="text-body-1 text-disabled">No mind wandering events recorded</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Microbreak History -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem title="Microbreak History" />
          <VCardText>
            <VDataTable
              :headers="microbreakHeaders"
              :items="microbreaks"
              :items-per-page="10"
              density="compact"
            >
              <template #item.takenAt="{ item }">
                <span v-if="item.takenAt">{{ item.takenAt }}</span>
                <VChip
                  v-else
                  color="error"
                  label
                  size="small"
                >
                  Skipped
                </VChip>
              </template>
            </VDataTable>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
