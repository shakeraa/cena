<script setup lang="ts">
import { $api } from '@/utils/api'

interface CausalFactors {
  difficultyMismatchScore: number
  focusDegradationScore: number
  prerequisiteGapScore: number
  methodologyIneffectivenessScore: number
  errorRepetitionScore: number
  primaryFactor: string
  explanation: string
}

interface Recommendation {
  action: string
  reason: string
  confidence: number
}

interface AttemptSummary {
  totalAttempts: number
  correctCount: number
  accuracyRate: number
  stretchAttempts: number
  regressionAttempts: number
  focusDegradedAttempts: number
  avgDifficultyGap: number
  avgResponseTimeMs: number
  methodologiesUsed: string[]
  errorTypes: string[]
}

interface StagnationInsights {
  studentId: string
  conceptId: string
  factors: CausalFactors
  recommendations: Recommendation[]
  summary: AttemptSummary
}

interface TimelineEntry {
  timestamp: string
  questionId: string
  isCorrect: boolean
  priorMastery: number
  posteriorMastery: number
  questionDifficulty: number
  difficultyGap: number
  difficultyFrame: string | null
  focusState: string | null
  methodology: string
  errorType: string
  responseTimeMs: number
  hintsUsed: number
}

const props = defineProps<{
  studentId: string
  conceptId: string
}>()

const loading = ref(true)
const insights = ref<StagnationInsights | null>(null)
const timeline = ref<TimelineEntry[]>([])
const error = ref<string | null>(null)
const showTimeline = ref(false)

const factorColors: Record<string, string> = {
  difficulty_mismatch: 'error',
  focus: 'warning',
  prerequisites: 'info',
  methodology: 'secondary',
  error_repetition: 'primary',
}

const factorIcons: Record<string, string> = {
  difficulty_mismatch: 'ri-bar-chart-2-line',
  focus: 'ri-eye-off-line',
  prerequisites: 'ri-git-branch-line',
  methodology: 'ri-route-line',
  error_repetition: 'ri-repeat-line',
}

const factorLabels: Record<string, string> = {
  difficulty_mismatch: 'Difficulty Mismatch',
  focus: 'Focus Degradation',
  prerequisites: 'Prerequisite Gaps',
  methodology: 'Methodology Ineffective',
  error_repetition: 'Repeated Errors',
}

const actionIcons: Record<string, string> = {
  reduce_difficulty: 'ri-arrow-down-line',
  suggest_break: 'ri-time-line',
  review_prerequisites: 'ri-git-branch-line',
  switch_methodology: 'ri-route-line',
  investigate_errors: 'ri-search-line',
}

const frameColors: Record<string, string> = {
  Stretch: 'error',
  Challenge: 'warning',
  Appropriate: 'success',
  Expected: 'info',
  Regression: 'primary',
}

const focusColors: Record<string, string> = {
  Strong: 'success',
  Stable: 'success',
  Declining: 'warning',
  Degrading: 'error',
  Critical: 'error',
}

const fetchInsights = async () => {
  loading.value = true
  error.value = null
  try {
    const [insightsData, timelineData] = await Promise.all([
      $api<StagnationInsights>(`/admin/stagnation/${props.studentId}/${props.conceptId}/insights`),
      $api<{ timeline: TimelineEntry[] }>(`/admin/stagnation/${props.studentId}/${props.conceptId}/timeline?limit=30`),
    ])
    insights.value = insightsData
    timeline.value = timelineData.timeline ?? []
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load stagnation insights'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchInsights)

const factorBars = computed(() => {
  if (!insights.value) return []
  const f = insights.value.factors
  return [
    { label: 'Difficulty', value: f.difficultyMismatchScore, key: 'difficulty_mismatch' },
    { label: 'Focus', value: f.focusDegradationScore, key: 'focus' },
    { label: 'Prerequisites', value: f.prerequisiteGapScore, key: 'prerequisites' },
    { label: 'Methodology', value: f.methodologyIneffectivenessScore, key: 'methodology' },
    { label: 'Error Pattern', value: f.errorRepetitionScore, key: 'error_repetition' },
  ].sort((a, b) => b.value - a.value)
})

const timelineHeaders = [
  { title: 'Time', key: 'timestamp' },
  { title: 'Correct', key: 'isCorrect' },
  { title: 'Mastery', key: 'posteriorMastery' },
  { title: 'Q Difficulty', key: 'questionDifficulty' },
  { title: 'Gap', key: 'difficultyGap' },
  { title: 'Frame', key: 'difficultyFrame' },
  { title: 'Focus', key: 'focusState' },
  { title: 'Method', key: 'methodology' },
  { title: 'Error', key: 'errorType' },
  { title: 'RT (ms)', key: 'responseTimeMs' },
]

function formatTime(ts: string) {
  return new Date(ts).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
}
</script>

<template>
  <VCard :loading="loading">
    <VCardTitle class="d-flex align-center gap-2">
      <VIcon icon="ri-search-eye-line" />
      Stagnation Root-Cause Analysis
    </VCardTitle>

    <VCardText v-if="error">
      <VAlert type="error" variant="tonal">
        {{ error }}
      </VAlert>
    </VCardText>

    <VCardText v-else-if="insights">
      <!-- Primary Factor Banner -->
      <VAlert
        :type="factorColors[insights.factors.primaryFactor] as any ?? 'info'"
        variant="tonal"
        class="mb-4"
      >
        <template #prepend>
          <VIcon :icon="factorIcons[insights.factors.primaryFactor] ?? 'ri-information-line'" />
        </template>
        <strong>Primary factor: {{ factorLabels[insights.factors.primaryFactor] ?? insights.factors.primaryFactor }}</strong>
        <br>
        {{ insights.factors.explanation }}
      </VAlert>

      <!-- Factor Bars -->
      <div class="mb-4">
        <div v-for="bar in factorBars" :key="bar.key" class="mb-2">
          <div class="d-flex justify-space-between mb-1">
            <span class="text-body-2">{{ bar.label }}</span>
            <span class="text-body-2 font-weight-medium">{{ (bar.value * 100).toFixed(0) }}%</span>
          </div>
          <VProgressLinear
            :model-value="bar.value * 100"
            :color="factorColors[bar.key] ?? 'primary'"
            height="8"
            rounded
          />
        </div>
      </div>

      <!-- Summary Stats -->
      <VRow class="mb-4">
        <VCol cols="3">
          <div class="text-center">
            <div class="text-h5">
              {{ insights.summary.totalAttempts }}
            </div>
            <div class="text-caption">
              Attempts
            </div>
          </div>
        </VCol>
        <VCol cols="3">
          <div class="text-center">
            <div class="text-h5" :class="insights.summary.accuracyRate < 0.5 ? 'text-error' : 'text-success'">
              {{ (insights.summary.accuracyRate * 100).toFixed(0) }}%
            </div>
            <div class="text-caption">
              Accuracy
            </div>
          </div>
        </VCol>
        <VCol cols="3">
          <div class="text-center">
            <div class="text-h5 text-warning">
              {{ insights.summary.stretchAttempts }}
            </div>
            <div class="text-caption">
              Stretch Q's
            </div>
          </div>
        </VCol>
        <VCol cols="3">
          <div class="text-center">
            <div class="text-h5 text-info">
              {{ insights.summary.focusDegradedAttempts }}
            </div>
            <div class="text-caption">
              Low Focus
            </div>
          </div>
        </VCol>
      </VRow>

      <!-- Recommendations -->
      <div v-if="insights.recommendations.length" class="mb-4">
        <div class="text-subtitle-1 font-weight-medium mb-2">
          Recommendations
        </div>
        <VList density="compact">
          <VListItem
            v-for="(rec, i) in insights.recommendations"
            :key="i"
            :prepend-icon="actionIcons[rec.action] ?? 'ri-lightbulb-line'"
          >
            <VListItemTitle>{{ rec.reason }}</VListItemTitle>
            <template #append>
              <VChip size="x-small" :color="rec.confidence > 0.7 ? 'error' : 'warning'">
                {{ (rec.confidence * 100).toFixed(0) }}%
              </VChip>
            </template>
          </VListItem>
        </VList>
      </div>

      <!-- Timeline Toggle -->
      <VBtn
        variant="outlined"
        size="small"
        @click="showTimeline = !showTimeline"
      >
        <VIcon :icon="showTimeline ? 'ri-arrow-up-s-line' : 'ri-arrow-down-s-line'" class="me-1" />
        {{ showTimeline ? 'Hide' : 'Show' }} Attempt Timeline ({{ timeline.length }})
      </VBtn>

      <!-- Timeline Table -->
      <VDataTable
        v-if="showTimeline && timeline.length"
        :headers="timelineHeaders"
        :items="timeline"
        density="compact"
        class="mt-3"
        :items-per-page="10"
      >
        <template #item.timestamp="{ item }">
          {{ formatTime(item.timestamp) }}
        </template>
        <template #item.isCorrect="{ item }">
          <VIcon
            :icon="item.isCorrect ? 'ri-check-line' : 'ri-close-line'"
            :color="item.isCorrect ? 'success' : 'error'"
            size="18"
          />
        </template>
        <template #item.posteriorMastery="{ item }">
          {{ (item.posteriorMastery * 100).toFixed(0) }}%
        </template>
        <template #item.questionDifficulty="{ item }">
          {{ item.questionDifficulty > 0 ? item.questionDifficulty.toFixed(2) : '--' }}
        </template>
        <template #item.difficultyGap="{ item }">
          <span :class="item.difficultyGap > 0.25 ? 'text-error' : item.difficultyGap < -0.25 ? 'text-info' : ''">
            {{ item.difficultyGap !== 0 ? (item.difficultyGap > 0 ? '+' : '') + item.difficultyGap.toFixed(2) : '--' }}
          </span>
        </template>
        <template #item.difficultyFrame="{ item }">
          <VChip v-if="item.difficultyFrame" size="x-small" :color="frameColors[item.difficultyFrame] ?? 'default'">
            {{ item.difficultyFrame }}
          </VChip>
          <span v-else>--</span>
        </template>
        <template #item.focusState="{ item }">
          <VChip v-if="item.focusState" size="x-small" :color="focusColors[item.focusState] ?? 'default'">
            {{ item.focusState }}
          </VChip>
          <span v-else>--</span>
        </template>
        <template #item.errorType="{ item }">
          {{ item.errorType && item.errorType !== 'None' ? item.errorType : '--' }}
        </template>
      </VDataTable>
    </VCardText>
  </VCard>
</template>
