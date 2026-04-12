<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { SessionAnswerResponseDto } from '@/api/types/common'

// FIND-pedagogy-005 — Tap-to-continue feedback
//
// The AnswerFeedback component previously rendered for exactly 1600ms via
// a hard-coded setTimeout on the parent page, then auto-dismissed the
// feedback mid-read. That contradicted the formative-feedback research:
//
//   Shute, V.J. (2008). "Focus on Formative Feedback." Review of
//   Educational Research, 78(1), 153-189.
//   DOI: 10.3102/0034654307313795
//
//   > "Presenting feedback too quickly may interrupt the student's
//   >  processing and reduce its effectiveness." (p.161)
//
//   Kulhavy, R.W. & Stock, W.A. (1989). "Feedback in Written Instruction:
//   The Place of Response Certitude." Educational Psychology Review,
//   1(4), 279-308. DOI: 10.1007/BF01320096
//
//   > Feedback on errors requires substantially more time to process than
//   >  feedback on correct responses.
//
// Fix: the component now emits a `continue` event when the student taps
// an explicit Continue button. The parent page uses this instead of a
// setTimeout. For correct answers, the parent may elect to auto-advance
// after a longer configurable delay (default 8s), but a manual Continue
// button is ALWAYS present. For wrong answers, auto-advance is disabled.

interface Props {
  feedback: SessionAnswerResponseDto

  /**
   * Whether the parent is busy loading the next question (disables the
   * Continue button so students can't double-tap).
   */
  loading?: boolean
}

withDefaults(defineProps<Props>(), { loading: false })

const emit = defineEmits<{
  continue: []
}>()

const { t, locale } = useI18n()

function handleContinue() {
  emit('continue')
}
</script>

<template>
  <VCard
    :class="feedback.correct ? 'answer-feedback--correct' : 'answer-feedback--wrong'"
    class="answer-feedback pa-5"
    variant="flat"
    data-testid="answer-feedback"
    :data-correct="feedback.correct"
  >
    <div class="d-flex align-center mb-2">
      <VIcon
        :icon="feedback.correct ? 'tabler-circle-check-filled' : 'tabler-circle-x-filled'"
        :color="feedback.correct ? 'success' : 'error'"
        size="36"
        class="me-3"
        aria-hidden="true"
      />
      <div>
        <div class="text-h6">
          {{ feedback.correct ? t('session.runner.correct') : t('session.runner.wrong') }}
        </div>
        <div
          v-if="feedback.xpAwarded > 0"
          class="text-caption text-success"
          data-testid="feedback-xp"
        >
          {{ t('session.runner.xpAwarded', { xp: feedback.xpAwarded }) }}
        </div>
      </div>
    </div>

    <!--
      FIND-pedagogy-017 — the `feedback.feedback` field (English short pill
      from the server: "Correct" / "Not quite") was rendered here as raw text.
      When the UI locale is ar/he, the translated heading above would be
      followed by an English string, creating a bilingual mash-up.

      Fix: removed. The translated heading (session.runner.correct / .wrong)
      is the ONLY correctness indicator. The server `Feedback` field is
      deprecated (kept for one release for backwards-compat, then removed).

      The explanation and distractorRationale below are authored content
      that ships in the question's original language. Until FIND-pedagogy-013
      delivers backend-localised explanations, we wrap them in `lang="en"`
      so screen readers announce the language switch, and show a translated
      label when the UI locale is not English.
    -->
    <div
      v-if="feedback.distractorRationale"
      class="text-body-2 mb-3"
      data-testid="feedback-distractor-rationale"
    >
      <div
        v-if="locale !== 'en'"
        class="text-caption text-medium-emphasis mb-1"
      >
        {{ t('session.runner.explanationLangNote') }}
      </div>
      <div lang="en" dir="ltr">
        {{ feedback.distractorRationale }}
      </div>
    </div>
    <div
      v-if="feedback.explanation"
      class="text-body-2 text-medium-emphasis mb-3"
      data-testid="feedback-explanation"
    >
      <div
        v-if="locale !== 'en' && !feedback.distractorRationale"
        class="text-caption text-medium-emphasis mb-1"
      >
        {{ t('session.runner.explanationLangNote') }}
      </div>
      <div lang="en" dir="ltr">
        {{ feedback.explanation }}
      </div>
    </div>

    <!--
      FIND-pedagogy-005: explicit Continue button. No auto-dismiss — the
      parent page listens to the `continue` event and advances only when
      the student taps. This satisfies Shute (2008) "learner-controlled
      pacing" and Kulhavy & Stock (1989) "mindful processing on errors".
    -->
    <div class="d-flex justify-end mt-2">
      <VBtn
        color="primary"
        size="large"
        :disabled="loading"
        :loading="loading"
        append-icon="tabler-arrow-right"
        data-testid="feedback-continue"
        @click="handleContinue"
      >
        {{ t('session.runner.continueWhenReady') }}
      </VBtn>
    </div>
  </VCard>
</template>

<style scoped>
.answer-feedback--correct {
  background-color: rgb(var(--v-theme-success) / 0.12);
  border-inline-start: 4px solid rgb(var(--v-theme-success));
}

.answer-feedback--wrong {
  background-color: rgb(var(--v-theme-error) / 0.12);
  border-inline-start: 4px solid rgb(var(--v-theme-error));
}
</style>
