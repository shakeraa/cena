<script setup lang="ts">
// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) result / mark sheet.
// Phase 1E: Ministry-style section breakdown + per-question points.
//
// Renders:
//   * Headline: percentage based on weighted points (PointsAwarded /
//     TotalPoints), exam code + paper code (display).
//   * Section A breakdown — pts awarded / pts total + correct / attempted.
//   * Section B breakdown — same shape.
//   * Per-question table — points + awarded + grading engine.
//   * Honest note: percentage is over GRADABLE attempted answers; items
//     without a canonical answer appear in the table but are excluded
//     from the percentage.
//
// Constraints:
//   * No streak / loss-aversion copy (GD-004).
//   * All math LTR-isolated in <bdi>.
//   * "Time taken vs. limit" is shown but not framed as "you ran out".
// =============================================================================

import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { getMockExamHistory, getMockExamRunResult } from '@/api/exam-prep'
import type { MockExamResultResponse, MockExamRunSummary } from '@/api/types/exam-prep'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'examPrep.result.title',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const result = ref<MockExamResultResponse | null>(null)
const history = ref<MockExamRunSummary[]>([])
const error = ref<string | null>(null)

const priorRuns = computed(() =>
  history.value.filter(r => r.runId !== result.value?.runId).slice(0, 3))

function parseTimeSpan(ts: string): string {
  const parts = ts.split(':')
  if (parts.length < 2) return ts
  const last = parts.length - 1
  const hours = parseInt(parts[last - 2] ?? '0', 10) || 0
  const minutes = parseInt(parts[last - 1] ?? '0', 10) || 0
  const seconds = Math.floor(parseFloat(parts[last] ?? '0')) || 0
  return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
}

const partABreakdown = computed(() => result.value?.perQuestion.filter(q => q.section === 'A') ?? [])
const partBBreakdown = computed(() => result.value?.perQuestion.filter(q => q.section === 'B') ?? [])
const sectionA = computed(() => result.value?.perSection.find(s => s.sectionLabel === 'A'))
const sectionB = computed(() => result.value?.perSection.find(s => s.sectionLabel === 'B'))

onMounted(async () => {
  try {
    result.value = await getMockExamRunResult(String(route.params.runId))
    // PRR-294 — fetch trend data alongside the mark sheet.
    if (result.value) {
      try {
        const hist = await getMockExamHistory(
          result.value.examCode,
          result.value.paperCode ?? undefined,
          5,
        )
        history.value = hist.runs ?? []
      }
      catch { /* history is best-effort; result page works without it */ }
    }
  }
  catch {
    error.value = t('examPrep.errors.notFound')
  }
})

function startAnotherRun() {
  router.push('/exam-prep')
}
</script>

<template>
  <VContainer v-if="result" data-testid="exam-prep-result">
    <VRow>
      <VCol cols="12">
        <h1 class="text-h4 mb-2">{{ t('examPrep.result.heading') }}</h1>
        <p class="text-body-1 text-medium-emphasis">
          <bdi dir="ltr">{{ result.examCode }}</bdi>
          <bdi v-if="result.paperCode" dir="ltr" class="ms-1">/ {{ result.paperCode }}</bdi>
        </p>
      </VCol>
    </VRow>

    <!-- Top score card + section summary -->
    <VRow>
      <VCol cols="12" md="5">
        <VCard data-testid="exam-prep-score-card">
          <VCardTitle>{{ t('examPrep.result.scoreTitle') }}</VCardTitle>
          <VCardText>
            <div class="d-flex align-center">
              <div class="text-h2 font-weight-bold" data-testid="exam-prep-score-percent">
                <bdi dir="ltr">{{ Math.round(result.scorePercent) }}%</bdi>
              </div>
              <div class="ms-4 text-body-2 text-medium-emphasis">
                <bdi dir="ltr">{{ result.pointsAwarded }}</bdi> /
                <bdi dir="ltr">{{ result.totalPoints }}</bdi>
                {{ t('examPrep.result.pointsLabel') }}
              </div>
            </div>
            <VDivider class="my-3" />
            <p class="text-body-2 mb-1">
              {{ t('examPrep.result.scoreSummary', {
                correct: result.questionsCorrect,
                attempted: result.questionsAttempted,
                total: result.totalQuestions,
              }) }}
            </p>
            <p class="text-body-2">
              {{ t('examPrep.result.timeTaken') }}:
              <bdi dir="ltr">{{ parseTimeSpan(result.timeTaken) }}</bdi>
              / <bdi dir="ltr">{{ parseTimeSpan(result.timeLimit) }}</bdi>
            </p>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="12" md="7">
        <VCard data-testid="exam-prep-section-summary">
          <VCardTitle>{{ t('examPrep.result.sectionSummary') }}</VCardTitle>
          <VCardText>
            <VTable density="compact">
              <thead>
                <tr>
                  <th>{{ t('examPrep.result.col.section') }}</th>
                  <th>{{ t('examPrep.result.col.attempted') }}</th>
                  <th>{{ t('examPrep.result.col.correct') }}</th>
                  <th>{{ t('examPrep.result.col.points') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="sectionA" data-testid="exam-prep-section-A">
                  <td><bdi dir="ltr">{{ t('examPrep.result.partALabel') }}</bdi></td>
                  <td><bdi dir="ltr">{{ sectionA.attempted }}</bdi></td>
                  <td><bdi dir="ltr">{{ sectionA.correct }}</bdi></td>
                  <td>
                    <bdi dir="ltr">{{ sectionA.pointsAwarded }} / {{ sectionA.totalPoints }}</bdi>
                  </td>
                </tr>
                <tr v-if="sectionB" data-testid="exam-prep-section-B">
                  <td><bdi dir="ltr">{{ t('examPrep.result.partBLabel') }}</bdi></td>
                  <td><bdi dir="ltr">{{ sectionB.attempted }}</bdi></td>
                  <td><bdi dir="ltr">{{ sectionB.correct }}</bdi></td>
                  <td>
                    <bdi dir="ltr">{{ sectionB.pointsAwarded }} / {{ sectionB.totalPoints }}</bdi>
                  </td>
                </tr>
              </tbody>
            </VTable>
            <VAlert
              type="info"
              variant="tonal"
              density="compact"
              class="mt-3"
            >
              {{ t('examPrep.result.gradingNote') }}
            </VAlert>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Per-question Part A -->
    <VCard class="mt-4">
      <VCardTitle>{{ t('examPrep.result.partAHeading') }}</VCardTitle>
      <VCardText>
        <VTable density="compact">
          <thead>
            <tr>
              <th>{{ t('examPrep.result.col.question') }}</th>
              <th>{{ t('examPrep.result.col.points') }}</th>
              <th>{{ t('examPrep.result.col.attempted') }}</th>
              <th>{{ t('examPrep.result.col.correct') }}</th>
              <th>{{ t('examPrep.result.col.engine') }}</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="q in partABreakdown" :key="q.questionId">
              <tr :data-testid="`exam-prep-result-row-${q.questionId}`">
                <td><bdi dir="ltr">{{ q.questionId }}</bdi></td>
                <td><bdi dir="ltr">{{ q.pointsAwarded }} / {{ q.points }}</bdi></td>
                <td>
                  <VIcon v-if="q.attempted" icon="tabler-check" color="success" size="small" />
                  <VIcon v-else icon="tabler-x" color="error" size="small" />
                </td>
                <td>
                  <span v-if="q.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                  <span v-else-if="q.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                  <span v-else class="text-medium-emphasis">{{ t('examPrep.result.col.notGraded') }}</span>
                </td>
                <td><bdi dir="ltr" class="text-caption">{{ q.gradingEngine }}</bdi></td>
              </tr>
              <tr
                v-for="sp in (q.subparts ?? [])"
                :key="`${q.questionId}:${sp.subpartId}`"
                :data-testid="`exam-prep-result-subrow-${q.questionId}-${sp.subpartId}`"
                class="text-caption"
              >
                <td class="ps-8"><bdi dir="ltr">└ ({{ sp.subpartId }})</bdi></td>
                <td><bdi dir="ltr">{{ sp.pointsAwarded }} / {{ sp.points }}</bdi></td>
                <td>
                  <VIcon v-if="sp.attempted" icon="tabler-check" color="success" size="x-small" />
                  <VIcon v-else icon="tabler-x" color="error" size="x-small" />
                </td>
                <td>
                  <span v-if="sp.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                  <span v-else-if="sp.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                  <span v-else class="text-medium-emphasis">—</span>
                </td>
                <td><bdi dir="ltr" class="text-caption">{{ sp.gradingEngine }}</bdi></td>
              </tr>
            </template>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>

    <!-- Per-question Part B -->
    <VCard class="mt-4">
      <VCardTitle>{{ t('examPrep.result.partBHeading') }}</VCardTitle>
      <VCardText>
        <VTable density="compact">
          <thead>
            <tr>
              <th>{{ t('examPrep.result.col.question') }}</th>
              <th>{{ t('examPrep.result.col.points') }}</th>
              <th>{{ t('examPrep.result.col.attempted') }}</th>
              <th>{{ t('examPrep.result.col.correct') }}</th>
              <th>{{ t('examPrep.result.col.engine') }}</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="q in partBBreakdown" :key="q.questionId">
              <tr :data-testid="`exam-prep-result-row-${q.questionId}`">
                <td><bdi dir="ltr">{{ q.questionId }}</bdi></td>
                <td><bdi dir="ltr">{{ q.pointsAwarded }} / {{ q.points }}</bdi></td>
                <td>
                  <VIcon v-if="q.attempted" icon="tabler-check" color="success" size="small" />
                  <VIcon v-else icon="tabler-x" color="error" size="small" />
                </td>
                <td>
                  <span v-if="q.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                  <span v-else-if="q.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                  <span v-else class="text-medium-emphasis">{{ t('examPrep.result.col.notGraded') }}</span>
                </td>
                <td><bdi dir="ltr" class="text-caption">{{ q.gradingEngine }}</bdi></td>
              </tr>
              <tr
                v-for="sp in (q.subparts ?? [])"
                :key="`${q.questionId}:${sp.subpartId}`"
                :data-testid="`exam-prep-result-subrow-${q.questionId}-${sp.subpartId}`"
                class="text-caption"
              >
                <td class="ps-8"><bdi dir="ltr">└ ({{ sp.subpartId }})</bdi></td>
                <td><bdi dir="ltr">{{ sp.pointsAwarded }} / {{ sp.points }}</bdi></td>
                <td>
                  <VIcon v-if="sp.attempted" icon="tabler-check" color="success" size="x-small" />
                  <VIcon v-else icon="tabler-x" color="error" size="x-small" />
                </td>
                <td>
                  <span v-if="sp.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                  <span v-else-if="sp.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                  <span v-else class="text-medium-emphasis">—</span>
                </td>
                <td><bdi dir="ltr" class="text-caption">{{ sp.gradingEngine }}</bdi></td>
              </tr>
            </template>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>

    <!-- PRR-294 — longitudinal trend card. Honest framing per ADR-0048:
         no "you improved!" copy; just side-by-side run history. -->
    <VCard v-if="priorRuns.length > 0" class="mt-4" data-testid="exam-prep-trend-card">
      <VCardTitle>{{ t('examPrep.result.trendTitle') }}</VCardTitle>
      <VCardText>
        <p class="text-body-2 mb-3">{{ t('examPrep.result.trendBody') }}</p>
        <VTable density="compact">
          <thead>
            <tr>
              <th>{{ t('examPrep.result.col.date') }}</th>
              <th>{{ t('examPrep.result.col.score') }}</th>
              <th>{{ t('examPrep.result.col.points') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="r in priorRuns"
              :key="r.runId"
              :data-testid="`exam-prep-trend-row-${r.runId}`"
            >
              <td><bdi dir="ltr">{{ new Date(r.submittedAt).toLocaleDateString() }}</bdi></td>
              <td><bdi dir="ltr">{{ Math.round(r.scorePercent) }}%</bdi></td>
              <td><bdi dir="ltr">{{ r.pointsAwarded }} / {{ r.totalPoints }}</bdi></td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>

    <VRow class="mt-4">
      <VCol cols="12" class="d-flex justify-end">
        <VBtn
          color="primary"
          variant="outlined"
          data-testid="exam-prep-result-another"
          @click="startAnotherRun"
        >
          {{ t('examPrep.result.startAnother') }}
        </VBtn>
      </VCol>
    </VRow>
  </VContainer>

  <VContainer v-else-if="error">
    <VAlert type="error" variant="tonal">{{ error }}</VAlert>
  </VContainer>

  <VContainer v-else>
    <VProgressLinear indeterminate color="primary" />
  </VContainer>
</template>
