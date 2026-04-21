<script setup lang="ts">
/**
 * ExamTargetsStep.vue — prr-221 onboarding step 3 (MVP wiring).
 *
 * Flat multi-select over the catalog returned by
 * GET /api/v1/catalog/exam-targets. MVP scope:
 *   - One list of cards, no region grouping (follow-up task).
 *   - Each card shows localized display name + track + family chip.
 *   - Multi-select with min 1, max MAX_EXAM_TARGETS.
 *   - Emits @complete once selection is valid.
 *
 * Deferred to follow-ups (file notes in prr-221 close-out):
 *   - Region/family grouping (persona-a11y nested headings)
 *   - aria-live cap-hit announcement
 *   - Search box
 *   - item_bank_status "reference-only" badge
 *   - Debounced fetch + locale-aware fallback banner
 *
 * Accessibility MVP:
 *   - role="group" wraps the card list
 *   - role="checkbox" + aria-checked per card
 *   - Track/code tokens wrapped <bdi dir="ltr"> so RTL pages still show
 *     Ministry codes LTR (CLAUDE.md "Math always LTR").
 */
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  DEFAULT_WEEKLY_HOURS,
  MAX_EXAM_TARGETS,
  useOnboardingStore,
  type ExamSittingDraft,
  type ExamTargetDraft,
} from '@/stores/onboardingStore'
import { $api } from '@/utils/api'

// ─── Wire types (mirror Cena.Api.Contracts.Catalog.ExamTargetCatalogDto) ──
interface LocalizedDisplay {
  name: string
  shortDescription: string | null
}
interface SittingWire {
  code: string
  academicYear: string
  season: string
  moed: string
  canonicalDate: string
}
interface ExamTargetWire {
  examCode: string
  family: string
  region: string
  track?: string | null
  units?: number | null
  regulator: string
  ministrySubjectCode?: string | null
  ministryQuestionPaperCodes: string[]
  availability: string
  itemBankStatus: string
  passbackEligible: boolean
  defaultLeadDays: number
  sittings: SittingWire[]
  display: LocalizedDisplay
}
interface ExamTargetGroupWire {
  family: string
  targets: ExamTargetWire[]
}
interface ExamTargetCatalogWire {
  catalogVersion: string
  locale: string
  localeFallbackUsed: string
  familyOrder: string[]
  groups: ExamTargetGroupWire[]
}

// ─── Season / Moed ordinal maps (sync with SittingSeason / SittingMoed) ──
const SEASON_ORDINAL: Record<string, number> = {
  summer: 0, winter: 1, spring: 2, autumn: 3,
}
const MOED_ORDINAL: Record<string, number> = {
  A: 0, B: 1, C: 2, Special: 3,
}

function toSittingDraft(s: SittingWire): ExamSittingDraft {
  return {
    sittingCode: s.code,
    academicYear: s.academicYear,
    season: SEASON_ORDINAL[s.season.toLowerCase()] ?? 0,
    moed: MOED_ORDINAL[s.moed] ?? 0,
    canonicalDate: s.canonicalDate,
  }
}

const emit = defineEmits<{
  (e: 'complete'): void
}>()

const { t, locale: i18nLocale } = useI18n()
const onboarding = useOnboardingStore()

const loading = ref(true)
const fetchError = ref<string | null>(null)
const catalog = ref<ExamTargetCatalogWire | null>(null)

onMounted(async () => {
  await loadCatalog()
})

async function loadCatalog() {
  loading.value = true
  fetchError.value = null
  try {
    const locale = i18nLocale.value || 'en'
    const url = `/api/v1/catalog/exam-targets?locale=${encodeURIComponent(locale)}`
    catalog.value = await $api<ExamTargetCatalogWire>(url)
  }
  catch (err) {
    fetchError.value = (err as Error).message
      ?? t('onboarding.examTargets.fetchFailed')
  }
  finally {
    loading.value = false
  }
}

// Flattened list of targets for MVP rendering (no grouping yet).
const flatTargets = computed<ExamTargetWire[]>(() => {
  if (!catalog.value)
    return []
  return catalog.value.groups.flatMap(g => g.targets)
})

const selectedCodes = computed(() =>
  new Set(onboarding.examTargets.map(t => t.examCode)),
)

const atCap = computed(() => onboarding.examTargets.length >= MAX_EXAM_TARGETS)

function isSelected(code: string): boolean {
  return selectedCodes.value.has(code)
}

function toggle(target: ExamTargetWire) {
  if (isSelected(target.examCode)) {
    onboarding.removeExamTarget(target.examCode)
    return
  }
  if (atCap.value) return
  const draft: ExamTargetDraft = {
    examCode: target.examCode,
    displayName: target.display.name,
    family: target.family,
    track: target.track ?? undefined,
    questionPaperCodes: [...target.ministryQuestionPaperCodes],
    availableQuestionPaperCodes: [...target.ministryQuestionPaperCodes],
    // Default-pick the earliest sitting; student refines on next step.
    sitting: target.sittings.length > 0 ? toSittingDraft(target.sittings[0]) : null,
    availableSittings: target.sittings.map(toSittingDraft),
    weeklyHours: DEFAULT_WEEKLY_HOURS,
  }
  onboarding.addExamTarget(draft)
}

function handleContinue() {
  if (onboarding.examTargets.length >= 1)
    emit('complete')
}
</script>

<template>
  <section
    data-testid="onboarding-step-exam-targets"
    role="form"
    :aria-label="t('onboarding.examTargets.title')"
  >
    <h2 class="text-h5 mb-1">
      {{ t('onboarding.examTargets.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('onboarding.examTargets.subtitle', { max: MAX_EXAM_TARGETS }) }}
    </p>

    <div
      v-if="loading"
      class="text-center py-8"
      data-testid="exam-targets-loading"
    >
      <VProgressCircular
        indeterminate
        color="primary"
        size="40"
      />
      <p class="text-body-2 mt-3 text-medium-emphasis">
        {{ t('onboarding.examTargets.loading') }}
      </p>
    </div>

    <VAlert
      v-else-if="fetchError"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="exam-targets-error"
    >
      {{ fetchError }}
    </VAlert>

    <template v-else>
      <div
        role="group"
        :aria-label="t('onboarding.examTargets.groupLabel')"
        class="d-flex flex-column ga-2 mb-4"
      >
        <VCard
          v-for="target in flatTargets"
          :key="target.examCode"
          :variant="isSelected(target.examCode) ? 'flat' : 'outlined'"
          :color="isSelected(target.examCode) ? 'primary' : undefined"
          :disabled="!isSelected(target.examCode) && atCap"
          class="pa-3 cursor-pointer"
          role="checkbox"
          :aria-checked="isSelected(target.examCode)"
          tabindex="0"
          :data-testid="`exam-target-card-${target.examCode}`"
          @click="toggle(target)"
          @keydown.enter.prevent="toggle(target)"
          @keydown.space.prevent="toggle(target)"
        >
          <div class="d-flex align-center ga-3">
            <div class="flex-grow-1">
              <div class="text-body-1 font-weight-medium">
                {{ target.display.name }}
              </div>
              <div class="text-caption text-medium-emphasis d-flex flex-wrap ga-2">
                <VChip
                  size="x-small"
                  variant="tonal"
                  :data-testid="`exam-target-family-${target.examCode}`"
                >
                  {{ target.family }}
                </VChip>
                <span v-if="target.track">
                  <bdi dir="ltr">{{ target.track }}</bdi>
                </span>
                <span>
                  <bdi dir="ltr">{{ target.examCode }}</bdi>
                </span>
              </div>
            </div>
            <VIcon
              v-if="isSelected(target.examCode)"
              icon="tabler-check"
              color="primary"
              aria-hidden="true"
            />
          </div>
        </VCard>
      </div>

      <p
        v-if="atCap"
        class="text-caption text-medium-emphasis mb-2"
        data-testid="exam-targets-cap-notice"
      >
        {{ t('onboarding.examTargets.capReached', { max: MAX_EXAM_TARGETS }) }}
      </p>

      <div class="d-flex justify-end">
        <VBtn
          color="primary"
          :disabled="onboarding.examTargets.length < 1"
          data-testid="exam-targets-continue"
          @click="handleContinue"
        >
          {{ t('onboarding.next') }}
        </VBtn>
      </div>
    </template>
  </section>
</template>

<style scoped>
.cursor-pointer {
  cursor: pointer;
}
</style>
