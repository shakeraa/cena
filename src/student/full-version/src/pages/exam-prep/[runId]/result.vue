<script setup lang="ts">
// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) result / mark sheet.
//
// Renders the post-submit grading breakdown: total score, per-question
// pass/fail, grading engine attribution, time taken vs. limit. Read-only;
// no engagement copy (GD-004 — no streak, no loss-aversion). Counts
// itself as honest signal: percentage is over GRADABLE attempted items.
// =============================================================================

import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { getMockExamRunResult } from '@/api/exam-prep'
import type { MockExamResultResponse } from '@/api/types/exam-prep'

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
const error = ref<string | null>(null)

function parseTimeSpan(ts: string): string {
  // C# TimeSpan serializes as "hh:mm:ss" or "d.hh:mm:ss".
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

onMounted(async () => {
  try {
    result.value = await getMockExamRunResult(String(route.params.runId))
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

    <VRow>
      <VCol cols="12" md="6">
        <VCard data-testid="exam-prep-score-card">
          <VCardTitle>{{ t('examPrep.result.scoreTitle') }}</VCardTitle>
          <VCardText>
            <div class="d-flex align-center">
              <div class="text-h2 font-weight-bold" data-testid="exam-prep-score-percent">
                <bdi dir="ltr">{{ Math.round(result.scorePercent) }}%</bdi>
              </div>
              <div class="ms-4 text-body-2 text-medium-emphasis">
                {{ t('examPrep.result.scoreSummary', {
                  correct: result.questionsCorrect,
                  attempted: result.questionsAttempted,
                  total: result.totalQuestions,
                }) }}
              </div>
            </div>
            <VDivider class="my-3" />
            <p class="text-body-2">
              {{ t('examPrep.result.timeTaken') }}:
              <bdi dir="ltr">{{ parseTimeSpan(result.timeTaken) }}</bdi>
              / <bdi dir="ltr">{{ parseTimeSpan(result.timeLimit) }}</bdi>
            </p>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="12" md="6">
        <VCard>
          <VCardTitle>{{ t('examPrep.result.gradingTitle') }}</VCardTitle>
          <VCardText>
            <p class="text-body-2 mb-2">{{ t('examPrep.result.gradingHonest') }}</p>
            <VAlert
              type="info"
              variant="tonal"
              density="compact"
            >
              {{ t('examPrep.result.gradingNote') }}
            </VAlert>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <VCard class="mt-4">
      <VCardTitle>{{ t('examPrep.result.partAHeading') }}</VCardTitle>
      <VCardText>
        <VTable density="compact">
          <thead>
            <tr>
              <th>{{ t('examPrep.result.col.question') }}</th>
              <th>{{ t('examPrep.result.col.attempted') }}</th>
              <th>{{ t('examPrep.result.col.correct') }}</th>
              <th>{{ t('examPrep.result.col.engine') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="q in partABreakdown"
              :key="q.questionId"
              :data-testid="`exam-prep-result-row-${q.questionId}`"
            >
              <td><bdi dir="ltr">{{ q.questionId }}</bdi></td>
              <td>
                <VIcon v-if="q.attempted" icon="ri-check-line" color="success" size="small" />
                <VIcon v-else icon="ri-close-line" color="error" size="small" />
              </td>
              <td>
                <span v-if="q.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                <span v-else-if="q.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                <span v-else class="text-medium-emphasis">{{ t('examPrep.result.col.notGraded') }}</span>
              </td>
              <td><bdi dir="ltr" class="text-caption">{{ q.gradingEngine }}</bdi></td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>

    <VCard class="mt-4">
      <VCardTitle>{{ t('examPrep.result.partBHeading') }}</VCardTitle>
      <VCardText>
        <VTable density="compact">
          <thead>
            <tr>
              <th>{{ t('examPrep.result.col.question') }}</th>
              <th>{{ t('examPrep.result.col.attempted') }}</th>
              <th>{{ t('examPrep.result.col.correct') }}</th>
              <th>{{ t('examPrep.result.col.engine') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="q in partBBreakdown"
              :key="q.questionId"
              :data-testid="`exam-prep-result-row-${q.questionId}`"
            >
              <td><bdi dir="ltr">{{ q.questionId }}</bdi></td>
              <td>
                <VIcon v-if="q.attempted" icon="ri-check-line" color="success" size="small" />
                <VIcon v-else icon="ri-close-line" color="error" size="small" />
              </td>
              <td>
                <span v-if="q.correct === true" class="text-success">{{ t('examPrep.result.col.yes') }}</span>
                <span v-else-if="q.correct === false" class="text-error">{{ t('examPrep.result.col.no') }}</span>
                <span v-else class="text-medium-emphasis">{{ t('examPrep.result.col.notGraded') }}</span>
              </td>
              <td><bdi dir="ltr" class="text-caption">{{ q.gradingEngine }}</bdi></td>
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
