<script setup lang="ts">
// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) entry page.
//
// Lets the student pick a Bagrut format (806 / 807 / 036) and optionally
// pin a Ministry שאלון code (display-only today). Shows the real-world
// structure preview (time limit, Part A count, Part B "choose K of M",
// total expected duration) so the student knows what they're signing up
// for, then starts a run via POST /api/me/exam-prep/runs and routes to
// the runner page.
//
// Constraints:
//   - All Hebrew/Arabic copy via i18n; math LTR-isolated in <bdi>.
//   - No streak / loss-aversion copy (GD-004 ship-gate scanner enforces).
//   - Time-presentation framing is honest: "Real exam time: 180 min".
// =============================================================================

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { startMockExamRun } from '@/api/exam-prep'
import type { ExamCode } from '@/api/types/exam-prep'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'examPrep.title',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

interface FormatPreview {
  examCode: ExamCode
  labelKey: string
  timeLimitMinutes: number
  partA: number
  partB: number
  partBRequired: number
}

const FORMATS: FormatPreview[] = [
  { examCode: '806', labelKey: 'examPrep.formats.806', timeLimitMinutes: 180, partA: 5, partB: 4, partBRequired: 2 },
  { examCode: '807', labelKey: 'examPrep.formats.807', timeLimitMinutes: 180, partA: 5, partB: 4, partBRequired: 2 },
  { examCode: '036', labelKey: 'examPrep.formats.036', timeLimitMinutes: 180, partA: 4, partB: 5, partBRequired: 3 },
]

const selected = ref<ExamCode>('806')
const paperCode = ref('')
const starting = ref(false)
const error = ref<string | null>(null)

const previewItem = computed(() =>
  FORMATS.find(f => f.examCode === selected.value) ?? FORMATS[0],
)

const totalQuestions = computed(() =>
  previewItem.value.partA + previewItem.value.partBRequired,
)

async function start() {
  error.value = null
  starting.value = true
  try {
    const res = await startMockExamRun({
      examCode: selected.value,
      paperCode: paperCode.value.trim() || undefined,
    })
    await router.push(`/exam-prep/${res.runId}`)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.startFailed')
  }
  finally {
    starting.value = false
  }
}
</script>

<template>
  <VContainer>
    <VRow>
      <VCol cols="12">
        <h1 class="text-h4 mb-2">{{ t('examPrep.title') }}</h1>
        <p class="text-body-1 mb-6">{{ t('examPrep.subtitle') }}</p>
      </VCol>
    </VRow>

    <VRow>
      <VCol cols="12" md="7">
        <VCard data-testid="exam-prep-format-card">
          <VCardTitle>{{ t('examPrep.format.title') }}</VCardTitle>
          <VCardText>
            <VRadioGroup v-model="selected" data-testid="exam-prep-format-radio">
              <VRadio
                v-for="f in FORMATS"
                :key="f.examCode"
                :value="f.examCode"
                :data-testid="`exam-prep-format-${f.examCode}`"
              >
                <template #label>
                  <span><bdi dir="ltr">{{ f.examCode }}</bdi> — {{ t(f.labelKey) }}</span>
                </template>
              </VRadio>
            </VRadioGroup>

            <VTextField
              v-model="paperCode"
              :label="t('examPrep.paperCodeLabel')"
              :placeholder="t('examPrep.paperCodePlaceholder')"
              variant="outlined"
              density="comfortable"
              class="mt-4"
              data-testid="exam-prep-paper-code"
            >
              <template #append-inner>
                <VTooltip activator="parent" location="top">
                  {{ t('examPrep.paperCodeHelp') }}
                </VTooltip>
              </template>
            </VTextField>
          </VCardText>
        </VCard>
      </VCol>

      <VCol cols="12" md="5">
        <VCard data-testid="exam-prep-preview-card">
          <VCardTitle>{{ t('examPrep.preview.title') }}</VCardTitle>
          <VCardText>
            <VList density="compact">
              <VListItem>
                <VListItemTitle>{{ t('examPrep.preview.timeLimit') }}</VListItemTitle>
                <VListItemSubtitle>
                  <bdi dir="ltr">{{ previewItem.timeLimitMinutes }}</bdi> {{ t('examPrep.preview.minutes') }}
                </VListItemSubtitle>
              </VListItem>
              <VListItem>
                <VListItemTitle>{{ t('examPrep.preview.partACount') }}</VListItemTitle>
                <VListItemSubtitle>
                  <bdi dir="ltr">{{ previewItem.partA }}</bdi> {{ t('examPrep.preview.questions') }}
                </VListItemSubtitle>
              </VListItem>
              <VListItem>
                <VListItemTitle>{{ t('examPrep.preview.partBCount') }}</VListItemTitle>
                <VListItemSubtitle>
                  {{ t('examPrep.preview.chooseOf', { required: previewItem.partBRequired, total: previewItem.partB }) }}
                </VListItemSubtitle>
              </VListItem>
              <VListItem>
                <VListItemTitle>{{ t('examPrep.preview.totalQuestions') }}</VListItemTitle>
                <VListItemSubtitle>
                  <bdi dir="ltr">{{ totalQuestions }}</bdi>
                </VListItemSubtitle>
              </VListItem>
            </VList>

            <VAlert
              type="info"
              variant="tonal"
              density="compact"
              class="mt-4"
              data-testid="exam-prep-mode-notice"
            >
              {{ t('examPrep.preview.modeNotice') }}
            </VAlert>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <VRow>
      <VCol cols="12" class="d-flex justify-end">
        <VBtn
          color="primary"
          size="large"
          :loading="starting"
          data-testid="exam-prep-start-btn"
          @click="start"
        >
          {{ t('examPrep.startButton') }}
        </VBtn>
      </VCol>
    </VRow>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mt-4"
      data-testid="exam-prep-start-error"
    >
      {{ error }}
    </VAlert>
  </VContainer>
</template>
