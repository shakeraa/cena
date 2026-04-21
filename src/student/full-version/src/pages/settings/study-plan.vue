<script setup lang="ts">
// PRR-227: /settings/study-plan. Three actions wire to PRR-218 endpoints —
// POST /api/me/exam-targets (add), PUT {id} (edit), POST {id}/archive.
// Neutral copy; no streak/celebration mechanics.
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiMutation } from '@/composables/useApiMutation'
import { useApiQuery } from '@/composables/useApiQuery'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'settingsPage.studyPlan.title',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

// Types mirror Cena.Student.Api.Host ExamTargetResponseDto.
interface SittingCodeDto {
  academicYear: string
  season: 'Summer' | 'Winter'
  moed: 'A' | 'B' | 'C' | 'Special'
}

interface ExamTargetDto {
  id: string
  examCode: string
  track?: string | null
  sitting: SittingCodeDto
  weeklyHours: number
  reasonTag?: string | null
  isActive: boolean
  archivedAt?: string | null
  questionPaperCodes: string[]
  parentVisibility: 'Visible' | 'Hidden'
}

interface ListResponseDto {
  items: ExamTargetDto[]
  includeArchived: boolean
}

interface TargetFormValue {
  examCode: string
  track: string
  sitting: SittingCodeDto
  weeklyHours: number
  reasonTag: string | null
  questionPaperCodes: string[]
}

const { t } = useI18n()

const listQuery = useApiQuery<ListResponseDto>(
  '/api/me/exam-targets?includeArchived=true',
)

const active = computed(() =>
  (listQuery.data.value?.items ?? []).filter(t => t.isActive))
const archived = computed(() =>
  (listQuery.data.value?.items ?? []).filter(t => !t.isActive))

const totalWeeklyHours = computed(() =>
  active.value.reduce((sum, t) => sum + t.weeklyHours, 0))

const overLimit = computed(() => totalWeeklyHours.value > 40)

const showForm = ref(false)
const editingId = ref<string | null>(null) // null = add; non-null = edit
const form = ref<TargetFormValue>(emptyForm())
const formError = ref<string | null>(null)
const saveLoading = ref(false)

function emptyForm(): TargetFormValue {
  return {
    examCode: '',
    track: '',
    sitting: { academicYear: 'תשפ״ו', season: 'Summer', moed: 'A' },
    weeklyHours: 5,
    reasonTag: null,
    questionPaperCodes: [],
  }
}

function openAdd() {
  editingId.value = null
  form.value = emptyForm()
  formError.value = null
  showForm.value = true
}

function openEdit(target: ExamTargetDto) {
  editingId.value = target.id
  form.value = {
    examCode: target.examCode,
    track: target.track ?? '',
    sitting: { ...target.sitting },
    weeklyHours: target.weeklyHours,
    reasonTag: target.reasonTag ?? null,
    questionPaperCodes: [...target.questionPaperCodes],
  }
  formError.value = null
  showForm.value = true
}

const addMutation = useApiMutation<ExamTargetDto, unknown>(
  '/api/me/exam-targets', 'POST')

async function addTarget(body: TargetFormValue) {
  return addMutation.execute({
    examCode: body.examCode,
    track: body.track || null,
    sitting: body.sitting,
    weeklyHours: body.weeklyHours,
    reasonTag: body.reasonTag,
    questionPaperCodes: body.questionPaperCodes.length > 0
      ? body.questionPaperCodes
      : null,
  })
}

async function editTarget(id: string, body: TargetFormValue) {
  // PUT /api/me/exam-targets/{id}
  const mut = useApiMutation<ExamTargetDto, unknown>(
    `/api/me/exam-targets/${encodeURIComponent(id)}`, 'PUT')

  return mut.execute({
    track: body.track || null,
    sitting: body.sitting,
    weeklyHours: body.weeklyHours,
    reasonTag: body.reasonTag,
  })
}

async function archiveTarget(id: string) {
  // POST /api/me/exam-targets/{id}/archive — 204 on success.
  const mut = useApiMutation<null, unknown>(
    `/api/me/exam-targets/${encodeURIComponent(id)}/archive`, 'POST')

  return mut.execute({})
}

async function handleSave() {
  formError.value = null
  if (overLimit.value && editingId.value === null) {
    formError.value = t('settingsPage.studyPlan.overLimitWarning')
    return
  }

  saveLoading.value = true
  try {
    if (editingId.value === null)
      await addTarget(form.value)

    else
      await editTarget(editingId.value, form.value)
    showForm.value = false
    await listQuery.refresh()
  }
  catch {
    formError.value = editingId.value === null
      ? t('settingsPage.studyPlan.errors.addFailed')
      : t('settingsPage.studyPlan.errors.updateFailed')
  }
  finally {
    saveLoading.value = false
  }
}

const archiveConfirmFor = ref<string | null>(null)
const archiveError = ref<string | null>(null)

function promptArchive(id: string) {
  archiveConfirmFor.value = id
  archiveError.value = null
}

async function confirmArchive() {
  if (!archiveConfirmFor.value) return
  const id = archiveConfirmFor.value

  try {
    await archiveTarget(id)
    archiveConfirmFor.value = null
    await listQuery.refresh()
  }
  catch {
    archiveError.value = t('settingsPage.studyPlan.errors.archiveFailed')
  }
}

onMounted(() => listQuery.refresh())
</script>

<template>
  <div
    class="settings-study-plan-page pa-4"
    data-testid="settings-study-plan-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.studyPlan.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-4">
      {{ t('settingsPage.studyPlan.subtitle') }}
    </p>

    <div
      class="d-flex align-center justify-space-between mb-4"
      data-testid="study-plan-totals"
    >
      <div>
        <strong>{{ t('settingsPage.studyPlan.totalWeeklyHours', { hours: totalWeeklyHours }) }}</strong>
      </div>
      <VBtn
        color="primary"
        prepend-icon="tabler-plus"
        data-testid="btn-add-target"
        @click="openAdd"
      >
        {{ t('settingsPage.studyPlan.addTarget') }}
      </VBtn>
    </div>

    <VAlert
      v-if="overLimit"
      type="warning"
      variant="tonal"
      class="mb-4"
      role="alert"
      data-testid="over-limit-warning"
    >
      {{ t('settingsPage.studyPlan.overLimitWarning') }}
    </VAlert>

    <!-- Active targets -->
    <h2 class="text-h6 mb-3">
      {{ t('settingsPage.studyPlan.activeHeading') }}
    </h2>

    <VCard
      v-if="active.length === 0"
      variant="outlined"
      class="pa-4 mb-6"
      data-testid="no-active-targets"
    >
      <p class="text-body-2 text-medium-emphasis">
        {{ t('settingsPage.studyPlan.noActiveTargets') }}
      </p>
    </VCard>

    <VCard
      v-for="target in active"
      :key="target.id"
      variant="outlined"
      class="pa-4 mb-3"
      :data-testid="`target-row-${target.id}`"
    >
      <div class="d-flex align-start justify-space-between ga-3">
        <div class="flex-grow-1 min-w-0">
          <div class="text-subtitle-1 font-weight-medium">
            <bdi dir="ltr">{{ target.examCode }}</bdi>
            <span
              v-if="target.track"
              class="text-medium-emphasis ms-2"
            >
              <bdi dir="ltr">{{ target.track }}</bdi>
            </span>
          </div>
          <div class="text-body-2 text-medium-emphasis mt-1">
            <bdi dir="ltr">{{ target.sitting.academicYear }} · {{ target.sitting.season }} · {{ target.sitting.moed }}</bdi>
            &nbsp;·&nbsp;
            {{ target.weeklyHours }}h/wk
          </div>
          <div
            v-if="target.parentVisibility"
            class="text-caption mt-1"
            :data-testid="`target-visibility-${target.id}`"
          >
            {{ target.parentVisibility === 'Visible'
              ? t('settingsPage.studyPlan.parentVisibility.visible')
              : t('settingsPage.studyPlan.parentVisibility.hidden') }}
          </div>
        </div>
        <div class="d-flex ga-2">
          <VBtn
            variant="text"
            size="small"
            prepend-icon="tabler-edit"
            :data-testid="`btn-edit-${target.id}`"
            @click="openEdit(target)"
          >
            {{ t('settingsPage.studyPlan.editTarget') }}
          </VBtn>
          <VBtn
            variant="text"
            size="small"
            color="error"
            prepend-icon="tabler-archive"
            :data-testid="`btn-archive-${target.id}`"
            @click="promptArchive(target.id)"
          >
            {{ t('settingsPage.studyPlan.archiveTarget') }}
          </VBtn>
        </div>
      </div>
    </VCard>

    <!-- Archived targets -->
    <h2 class="text-h6 mt-6 mb-3">
      {{ t('settingsPage.studyPlan.archivedHeading') }}
    </h2>

    <VCard
      v-if="archived.length === 0"
      variant="outlined"
      class="pa-4"
      data-testid="no-archived-targets"
    >
      <p class="text-body-2 text-medium-emphasis">
        {{ t('settingsPage.studyPlan.noArchivedTargets') }}
      </p>
    </VCard>

    <VCard
      v-for="target in archived"
      :key="target.id"
      variant="outlined"
      class="pa-4 mb-3"
      :data-testid="`archived-row-${target.id}`"
    >
      <div class="text-subtitle-2">
        <bdi dir="ltr">{{ target.examCode }}</bdi>
      </div>
      <div class="text-caption text-medium-emphasis">
        <bdi dir="ltr">{{ target.sitting.academicYear }} · {{ target.sitting.season }} · {{ target.sitting.moed }}</bdi>
      </div>
    </VCard>

    <!-- Add/Edit form dialog -->
    <VDialog
      v-model="showForm"
      max-width="600"
    >
      <VCard data-testid="target-form-dialog">
        <VCardTitle>
          {{ editingId === null
            ? t('settingsPage.studyPlan.addTarget')
            : t('settingsPage.studyPlan.editTarget') }}
        </VCardTitle>
        <VCardText class="d-flex flex-column ga-3">
          <VTextField
            v-model="form.examCode"
            :label="t('settingsPage.studyPlan.fields.examCode')"
            :disabled="editingId !== null"
            data-testid="form-exam-code"
            autofocus
          />
          <VTextField
            v-model="form.track"
            :label="t('settingsPage.studyPlan.fields.track')"
            data-testid="form-track"
          />
          <div class="d-flex ga-3">
            <VTextField
              v-model="form.sitting.academicYear"
              :label="t('settingsPage.studyPlan.fields.sitting')"
              data-testid="form-academic-year"
            />
            <VSelect
              v-model="form.sitting.season"
              :items="['Summer', 'Winter']"
              data-testid="form-season"
            />
            <VSelect
              v-model="form.sitting.moed"
              :items="['A', 'B', 'C', 'Special']"
              data-testid="form-moed"
            />
          </div>
          <VTextField
            v-model.number="form.weeklyHours"
            type="number"
            :label="t('settingsPage.studyPlan.fields.weeklyHours')"
            :min="1"
            :max="40"
            data-testid="form-weekly-hours"
          />
          <VSelect
            v-model="form.reasonTag"
            :items="[
              { value: null, title: '—' },
              { value: 'Retake', title: 'Retake' },
              { value: 'NewSubject', title: 'New subject' },
              { value: 'ReviewOnly', title: 'Review only' },
              { value: 'Enrichment', title: 'Enrichment' },
            ]"
            :label="t('settingsPage.studyPlan.fields.reasonTag')"
            data-testid="form-reason-tag"
          />
          <VAlert
            v-if="formError"
            type="error"
            variant="tonal"
            role="alert"
            data-testid="form-error"
          >
            {{ formError }}
          </VAlert>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            data-testid="form-cancel"
            @click="showForm = false"
          >
            {{ t('settingsPage.studyPlan.actions.cancel') }}
          </VBtn>
          <VBtn
            color="primary"
            variant="flat"
            :loading="saveLoading"
            data-testid="form-save"
            @click="handleSave"
          >
            {{ t('settingsPage.studyPlan.actions.save') }}
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <!-- Archive confirm dialog -->
    <VDialog
      v-model:model-value="archiveConfirmFor"
      :model-value="archiveConfirmFor !== null"
      max-width="480"
      @update:model-value="archiveConfirmFor = null"
    >
      <VCard data-testid="archive-confirm-dialog">
        <VCardTitle>{{ t('settingsPage.studyPlan.archiveTarget') }}</VCardTitle>
        <VCardText>
          <p>{{ t('settingsPage.studyPlan.archiveConfirm') }}</p>
          <VAlert
            v-if="archiveError"
            type="error"
            variant="tonal"
            role="alert"
            data-testid="archive-error"
            class="mt-3"
          >
            {{ archiveError }}
          </VAlert>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            data-testid="archive-cancel"
            @click="archiveConfirmFor = null"
          >
            {{ t('settingsPage.studyPlan.actions.cancel') }}
          </VBtn>
          <VBtn
            color="error"
            variant="flat"
            data-testid="archive-confirm"
            @click="confirmArchive"
          >
            {{ t('settingsPage.studyPlan.archiveConfirmYes') }}
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <VAlert
      v-if="listQuery.error.value"
      type="error"
      variant="tonal"
      class="mt-6"
      role="alert"
      data-testid="load-error"
    >
      {{ t('settingsPage.studyPlan.errors.loadFailed') }}
    </VAlert>
  </div>
</template>

<style scoped>
.settings-study-plan-page {
  max-inline-size: 900px;
  margin-inline: auto;
}
</style>
