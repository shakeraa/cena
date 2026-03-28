<script setup lang="ts">
definePage({ meta: { action: 'read', subject: 'Questions' } })

const route = useRoute('apps-questions-edit-id')
const router = useRouter()
const questionId = computed(() => route.params.id as string)

// Data
const question = ref<any>(null)
const performance = ref<any>(null)
const history = ref<any[]>([])
const isLoading = ref(true)
const activeTab = ref('overview')

// Edit state
const isEditing = ref(false)
const isSaving = ref(false)
const editForm = ref({
  stem: '',
  difficulty: 0.5,
  options: [] as Array<{ id: string; text: string; isCorrect: boolean; distractorRationale: string }>,
  conceptIds: [] as string[],
})

// Lifecycle actions
const isApproving = ref(false)
const isPublishing = ref(false)
const isDeprecating = ref(false)
const deprecateReason = ref('')
const showDeprecateDialog = ref(false)

// Fetch all data
const fetchQuestion = async () => {
  isLoading.value = true
  try {
    const [questionRes, performanceRes, historyRes] = await Promise.all([
      $api(`/admin/questions/${questionId.value}`),
      $api(`/admin/questions/${questionId.value}/performance`).catch(() => null),
      $api(`/admin/questions/${questionId.value}/history`).catch(() => []),
    ])
    question.value = questionRes
    performance.value = performanceRes
    history.value = Array.isArray(historyRes) ? historyRes : []

    // Populate edit form
    if (questionRes) {
      editForm.value = {
        stem: questionRes.stem,
        difficulty: questionRes.difficulty,
        options: (questionRes.options ?? []).map((o: any) => ({
          id: o.id,
          text: o.text,
          isCorrect: o.isCorrect,
          distractorRationale: o.distractorRationale ?? '',
        })),
        conceptIds: questionRes.conceptIds ?? [],
      }
    }
  }
  catch (err) {
    console.error('Failed to load question', err)
  }
  finally {
    isLoading.value = false
  }
}

await fetchQuestion()

// Resolvers
const resolveStatusColor = (status: string) => {
  const map: Record<string, string> = {
    Draft: 'secondary', draft: 'secondary',
    InReview: 'warning', 'in-review': 'warning',
    Approved: 'info', approved: 'info',
    Published: 'success', published: 'success',
    Deprecated: 'error', deprecated: 'error',
  }
  return map[status] ?? 'primary'
}

const resolveQualityColor = (score: number) => {
  if (score >= 80) return 'success'
  if (score >= 60) return 'info'
  if (score >= 40) return 'warning'
  return 'error'
}

const resolveGateDecisionColor = (decision: string) => {
  const map: Record<string, string> = { AutoApproved: 'success', NeedsReview: 'warning', AutoRejected: 'error' }
  return map[decision] ?? 'secondary'
}

const qualityDimensions = [
  { key: 'structuralValidity', label: 'Structural Validity' },
  { key: 'stemClarity', label: 'Stem Clarity' },
  { key: 'distractorQuality', label: 'Distractor Quality' },
  { key: 'bloomAlignment', label: "Bloom's Alignment" },
  { key: 'factualAccuracy', label: 'Factual Accuracy' },
  { key: 'languageQuality', label: 'Language Quality' },
  { key: 'pedagogicalQuality', label: 'Pedagogical Quality' },
  { key: 'culturalSensitivity', label: 'Cultural Sensitivity' },
]

const bloomLabel = (level: number) => {
  const map: Record<number, string> = { 1: 'Remember', 2: 'Understand', 3: 'Apply', 4: 'Analyze', 5: 'Evaluate', 6: 'Create' }
  return map[level] ?? `Level ${level}`
}

const langLabel = (lang: string) => {
  const map: Record<string, string> = { he: 'Hebrew', ar: 'Arabic', en: 'English' }
  return map[lang] ?? lang
}

const eventTypeLabel = (type: string) => {
  const map: Record<string, { label: string; color: string; icon: string }> = {
    QuestionAuthored_V1: { label: 'Created (Authored)', color: 'primary', icon: 'tabler-pencil-plus' },
    QuestionAiGenerated_V1: { label: 'Created (AI Generated)', color: 'primary', icon: 'tabler-sparkles' },
    QuestionIngested_V1: { label: 'Created (Ingested)', color: 'primary', icon: 'tabler-file-import' },
    QuestionStemEdited_V1: { label: 'Stem Edited', color: 'info', icon: 'tabler-edit' },
    QuestionOptionChanged_V1: { label: 'Option Changed', color: 'info', icon: 'tabler-list-check' },
    QuestionMetadataUpdated_V1: { label: 'Metadata Updated', color: 'secondary', icon: 'tabler-settings' },
    QuestionQualityEvaluated_V1: { label: 'Quality Evaluated', color: 'warning', icon: 'tabler-shield-check' },
    QuestionApproved_V1: { label: 'Approved', color: 'success', icon: 'tabler-check' },
    QuestionPublished_V1: { label: 'Published', color: 'success', icon: 'tabler-world-upload' },
    QuestionDeprecated_V1: { label: 'Deprecated', color: 'error', icon: 'tabler-archive' },
    LanguageVersionAdded_V1: { label: 'Language Added', color: 'info', icon: 'tabler-language' },
  }
  return map[type] ?? { label: type.replace(/_V\d+$/, ''), color: 'default', icon: 'tabler-point' }
}

// Edit actions
const setCorrectOption = (idx: number) => {
  editForm.value.options.forEach((opt, i) => { opt.isCorrect = i === idx })
}

const saveEdits = async () => {
  isSaving.value = true
  try {
    await $api(`/admin/questions/${questionId.value}`, {
      method: 'PUT',
      body: {
        stem: editForm.value.stem,
        difficulty: editForm.value.difficulty,
        options: editForm.value.options,
        conceptIds: editForm.value.conceptIds,
      },
    })
    isEditing.value = false
    await fetchQuestion()
  }
  catch (err) {
    console.error('Save failed', err)
  }
  finally {
    isSaving.value = false
  }
}

const cancelEdit = () => {
  isEditing.value = false
  if (question.value) {
    editForm.value = {
      stem: question.value.stem,
      difficulty: question.value.difficulty,
      options: (question.value.options ?? []).map((o: any) => ({
        id: o.id, text: o.text, isCorrect: o.isCorrect,
        distractorRationale: o.distractorRationale ?? '',
      })),
      conceptIds: question.value.conceptIds ?? [],
    }
  }
}

// Lifecycle actions
const approveQuestion = async () => {
  isApproving.value = true
  try {
    await $api(`/admin/questions/${questionId.value}/approve`, { method: 'POST' })
    await fetchQuestion()
  }
  finally { isApproving.value = false }
}

const publishQuestion = async () => {
  isPublishing.value = true
  try {
    await $api(`/admin/questions/${questionId.value}/publish`, { method: 'POST' })
    await fetchQuestion()
  }
  finally { isPublishing.value = false }
}

const deprecateQuestion = async () => {
  isDeprecating.value = true
  try {
    await $api(`/admin/questions/${questionId.value}/deprecate`, {
      method: 'POST',
      body: { reason: deprecateReason.value || 'Deprecated by admin', removeFromServing: true },
    })
    showDeprecateDialog.value = false
    await fetchQuestion()
  }
  finally { isDeprecating.value = false }
}

const shortId = (id: string) => id?.length > 10 ? `${id.slice(0, 10)}...` : id
</script>

<template>
  <section>
    <!-- Loading -->
    <div v-if="isLoading" class="d-flex justify-center align-center py-16">
      <VProgressCircular indeterminate color="primary" size="48" />
    </div>

    <template v-else-if="question">
      <!-- Header -->
      <div class="d-flex align-center flex-wrap gap-4 mb-6">
        <VBtn icon variant="text" size="small" @click="router.push('/apps/questions/list')">
          <VIcon icon="tabler-arrow-left" />
        </VBtn>
        <div>
          <h4 class="text-h4">Question Detail</h4>
          <span class="text-body-2 text-disabled">
            <code>{{ shortId(question.id) }}</code>
            · {{ question.sourceType }}
            · created {{ new Date(question.createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) }}
          </span>
        </div>
        <VSpacer />
        <VChip
          :color="resolveStatusColor(question.status)"
          label
          class="text-capitalize"
        >
          {{ question.status }}
        </VChip>
      </div>

      <!-- Tabs -->
      <VTabs v-model="activeTab" class="mb-6">
        <VTab value="overview">
          <VIcon icon="tabler-file-text" class="me-2" />
          Overview
        </VTab>
        <VTab value="edit">
          <VIcon icon="tabler-pencil" class="me-2" />
          Edit
        </VTab>
        <VTab value="history">
          <VIcon icon="tabler-history" class="me-2" />
          History
        </VTab>
        <VTab value="analytics">
          <VIcon icon="tabler-chart-bar" class="me-2" />
          Analytics
        </VTab>
      </VTabs>

      <VWindow v-model="activeTab">
        <!-- ═══════════════ OVERVIEW TAB ═══════════════ -->
        <VWindowItem value="overview">
          <VRow>
            <!-- Left column: Question content -->
            <VCol cols="12" md="8">
              <!-- Stem -->
              <VCard class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Question Stem</VCardTitle>
                </VCardItem>
                <VCardText>
                  <div class="text-body-1" v-html="question.stemHtml || question.stem" />
                </VCardText>
              </VCard>

              <!-- Options -->
              <VCard class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Answer Options</VCardTitle>
                </VCardItem>
                <VCardText>
                  <div
                    v-for="opt in question.options"
                    :key="opt.id"
                    class="d-flex align-center gap-3 pa-3 rounded mb-2"
                    :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                  >
                    <VChip
                      size="small"
                      :color="opt.isCorrect ? 'success' : 'default'"
                      label
                    >
                      {{ opt.label }}
                    </VChip>
                    <span class="text-body-1 flex-grow-1" v-html="opt.textHtml || opt.text" />
                    <VIcon v-if="opt.isCorrect" icon="tabler-check" color="success" size="20" />
                    <VTooltip v-if="opt.distractorRationale" location="start">
                      <template #activator="{ props: tp }">
                        <VIcon v-bind="tp" icon="tabler-info-circle" size="18" color="disabled" />
                      </template>
                      {{ opt.distractorRationale }}
                    </VTooltip>
                  </div>
                </VCardText>
              </VCard>

              <!-- Concepts -->
              <VCard v-if="question.conceptNames?.length" class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Curriculum Concepts</VCardTitle>
                </VCardItem>
                <VCardText>
                  <div class="d-flex flex-wrap gap-2">
                    <VChip
                      v-for="c in question.conceptNames"
                      :key="c"
                      size="small"
                      variant="tonal"
                      color="primary"
                    >
                      {{ c }}
                    </VChip>
                  </div>
                </VCardText>
              </VCard>

              <!-- Provenance -->
              <VCard v-if="question.provenance" class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Source Provenance</VCardTitle>
                </VCardItem>
                <VCardText>
                  <VList density="compact">
                    <VListItem v-if="question.provenance.sourceItemId">
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 120px;">Source Doc</span></template>
                      <VListItemTitle><code>{{ question.provenance.sourceItemId }}</code></VListItemTitle>
                    </VListItem>
                    <VListItem v-if="question.provenance.sourceUrl">
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 120px;">URL</span></template>
                      <VListItemTitle>{{ question.provenance.sourceUrl }}</VListItemTitle>
                    </VListItem>
                    <VListItem v-if="question.provenance.importedAt">
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 120px;">Imported</span></template>
                      <VListItemTitle>{{ new Date(question.provenance.importedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) }}</VListItemTitle>
                    </VListItem>
                  </VList>
                </VCardText>
              </VCard>
            </VCol>

            <!-- Right column: Metadata + Quality Gate -->
            <VCol cols="12" md="4">
              <!-- Metadata card -->
              <VCard class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Metadata</VCardTitle>
                </VCardItem>
                <VCardText>
                  <VList density="compact" class="py-0">
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Subject</span></template>
                      <VListItemTitle><VChip size="small" label color="primary">{{ question.subject }}</VChip></VListItemTitle>
                    </VListItem>
                    <VListItem v-if="question.topic">
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Topic</span></template>
                      <VListItemTitle>{{ question.topic }}</VListItemTitle>
                    </VListItem>
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Grade</span></template>
                      <VListItemTitle>{{ question.grade }}</VListItemTitle>
                    </VListItem>
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Bloom's</span></template>
                      <VListItemTitle>{{ question.bloomsLevel }} — {{ bloomLabel(question.bloomsLevel) }}</VListItemTitle>
                    </VListItem>
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Difficulty</span></template>
                      <VListItemTitle>
                        <div class="d-flex align-center gap-2">
                          <VProgressLinear
                            :model-value="question.difficulty * 100"
                            :color="question.difficulty > 0.7 ? 'error' : question.difficulty > 0.4 ? 'warning' : 'success'"
                            height="6"
                            rounded
                            style="max-inline-size: 80px;"
                          />
                          {{ question.difficulty?.toFixed(2) }}
                        </div>
                      </VListItemTitle>
                    </VListItem>
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Language</span></template>
                      <VListItemTitle>{{ langLabel(question.language ?? '') }}</VListItemTitle>
                    </VListItem>
                    <VListItem>
                      <template #prepend><span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">Created By</span></template>
                      <VListItemTitle>{{ question.createdBy }}</VListItemTitle>
                    </VListItem>
                  </VList>
                </VCardText>
              </VCard>

              <!-- Quality Gate -->
              <VCard v-if="question.qualityGate" class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium d-flex align-center gap-2">
                    Quality Gate
                    <VChip size="x-small" :color="resolveGateDecisionColor(question.qualityGate.gateDecision)" label>
                      {{ question.qualityGate.gateDecision }}
                    </VChip>
                  </VCardTitle>
                </VCardItem>
                <VCardText>
                  <div class="d-flex align-center gap-2 mb-3">
                    <span class="text-h4 font-weight-bold">{{ Math.round(question.qualityGate.compositeScore) }}</span>
                    <span class="text-body-2 text-disabled">/ 100</span>
                    <VSpacer />
                    <span v-if="question.qualityGate.violationCount > 0" class="text-body-2 text-error">
                      {{ question.qualityGate.violationCount }} violation{{ question.qualityGate.violationCount !== 1 ? 's' : '' }}
                    </span>
                  </div>
                  <VProgressLinear
                    :model-value="question.qualityGate.compositeScore"
                    :color="resolveQualityColor(Math.round(question.qualityGate.compositeScore))"
                    height="8"
                    rounded
                    class="mb-4"
                  />
                  <div class="d-flex flex-column gap-3">
                    <div v-for="dim in qualityDimensions" :key="dim.key" class="d-flex align-center gap-2">
                      <span class="text-body-2 text-medium-emphasis" style="min-inline-size: 130px;">{{ dim.label }}</span>
                      <VProgressLinear
                        :model-value="question.qualityGate[dim.key]"
                        :color="resolveQualityColor(question.qualityGate[dim.key])"
                        height="6"
                        rounded
                        class="flex-grow-1"
                      />
                      <span class="text-body-2 font-weight-medium" style="min-inline-size: 28px;">
                        {{ question.qualityGate[dim.key] }}
                      </span>
                    </div>
                  </div>
                </VCardText>
              </VCard>

              <!-- Actions -->
              <VCard class="mb-4">
                <VCardItem>
                  <VCardTitle class="text-body-1 font-weight-medium">Actions</VCardTitle>
                </VCardItem>
                <VCardText class="d-flex flex-column gap-3">
                  <VBtn
                    v-if="question.status === 'Draft'"
                    color="info"
                    prepend-icon="tabler-check"
                    :loading="isApproving"
                    block
                    @click="approveQuestion"
                  >
                    Approve
                  </VBtn>
                  <VBtn
                    v-if="question.status === 'Approved'"
                    color="success"
                    prepend-icon="tabler-world-upload"
                    :loading="isPublishing"
                    block
                    @click="publishQuestion"
                  >
                    Publish
                  </VBtn>
                  <VBtn
                    v-if="question.status !== 'Deprecated'"
                    color="error"
                    variant="tonal"
                    prepend-icon="tabler-archive"
                    block
                    @click="showDeprecateDialog = true"
                  >
                    Deprecate
                  </VBtn>
                </VCardText>
              </VCard>
            </VCol>
          </VRow>
        </VWindowItem>

        <!-- ═══════════════ EDIT TAB ═══════════════ -->
        <VWindowItem value="edit">
          <VCard>
            <VCardItem>
              <VCardTitle class="d-flex align-center gap-2">
                Edit Question
                <VChip v-if="isEditing" size="x-small" color="warning" label>Unsaved Changes</VChip>
              </VCardTitle>
            </VCardItem>
            <VCardText>
              <VRow>
                <VCol cols="12">
                  <AppTextarea
                    v-model="editForm.stem"
                    label="Question Stem"
                    rows="4"
                    @update:model-value="isEditing = true"
                  />
                </VCol>

                <VCol cols="12" sm="4">
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">
                    Difficulty: {{ editForm.difficulty.toFixed(2) }}
                  </label>
                  <VSlider
                    v-model="editForm.difficulty"
                    :min="0"
                    :max="1"
                    :step="0.05"
                    thumb-label
                    color="primary"
                    @update:model-value="isEditing = true"
                  />
                </VCol>

                <VCol cols="12">
                  <label class="text-body-2 text-medium-emphasis d-block mb-2">
                    Answer Options (select the correct one)
                  </label>
                  <div
                    v-for="(opt, idx) in editForm.options"
                    :key="opt.id"
                    class="d-flex align-center gap-3 mb-3"
                  >
                    <VRadio
                      :model-value="opt.isCorrect"
                      :value="true"
                      color="success"
                      @click="setCorrectOption(idx); isEditing = true"
                    />
                    <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                      {{ opt.id }}
                    </VChip>
                    <AppTextField
                      v-model="opt.text"
                      :placeholder="`Option ${opt.id} text`"
                      density="compact"
                      class="flex-grow-1"
                      @update:model-value="isEditing = true"
                    />
                    <AppTextField
                      v-if="!opt.isCorrect"
                      v-model="opt.distractorRationale"
                      placeholder="Why wrong?"
                      density="compact"
                      style="max-inline-size: 200px;"
                      @update:model-value="isEditing = true"
                    />
                  </div>
                </VCol>
              </VRow>
            </VCardText>
            <VDivider />
            <VCardActions class="pa-4">
              <VSpacer />
              <VBtn variant="tonal" color="secondary" :disabled="!isEditing" @click="cancelEdit">
                Cancel
              </VBtn>
              <VBtn color="primary" :loading="isSaving" :disabled="!isEditing" @click="saveEdits">
                Save & Re-evaluate
              </VBtn>
            </VCardActions>
          </VCard>
        </VWindowItem>

        <!-- ═══════════════ HISTORY TAB ═══════════════ -->
        <VWindowItem value="history">
          <VCard>
            <VCardItem>
              <VCardTitle>Event History</VCardTitle>
              <VCardSubtitle>Event-sourced version timeline</VCardSubtitle>
            </VCardItem>
            <VCardText>
              <VTimeline
                v-if="history.length"
                density="compact"
                side="end"
                truncate-line="both"
              >
                <VTimelineItem
                  v-for="evt in history"
                  :key="evt.sequence"
                  :dot-color="eventTypeLabel(evt.eventType).color"
                  size="small"
                >
                  <template #icon>
                    <VIcon :icon="eventTypeLabel(evt.eventType).icon" size="14" />
                  </template>
                  <div class="d-flex align-center flex-wrap gap-2 mb-1">
                    <VChip
                      size="x-small"
                      :color="eventTypeLabel(evt.eventType).color"
                      label
                    >
                      {{ eventTypeLabel(evt.eventType).label }}
                    </VChip>
                    <span class="text-caption text-disabled">
                      #{{ evt.sequence }} ·
                      {{ new Date(evt.timestamp).toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) }}
                    </span>
                  </div>
                  <!-- Show relevant detail for certain event types -->
                  <div v-if="evt.data?.Editor || evt.data?.UserId" class="text-caption text-disabled">
                    by {{ evt.data.Editor || evt.data.UserId }}
                  </div>
                  <div v-if="evt.data?.OldStem && evt.data?.NewStem" class="text-caption">
                    <span class="text-error text-decoration-line-through">{{ evt.data.OldStem?.slice(0, 80) }}</span>
                    → <span class="text-success">{{ evt.data.NewStem?.slice(0, 80) }}</span>
                  </div>
                  <div v-if="evt.data?.CompositeScore != null" class="text-caption">
                    Score: <strong>{{ Math.round(evt.data.CompositeScore) }}</strong> · {{ evt.data.GateDecision }}
                  </div>
                  <div v-if="evt.data?.Reason" class="text-caption text-error">
                    {{ evt.data.Reason }}
                  </div>
                </VTimelineItem>
              </VTimeline>

              <div v-else class="text-center py-8 text-disabled">
                No event history available
              </div>
            </VCardText>
          </VCard>
        </VWindowItem>

        <!-- ═══════════════ ANALYTICS TAB ═══════════════ -->
        <VWindowItem value="analytics">
          <VRow>
            <!-- Stats cards -->
            <VCol cols="12" sm="6" md="3">
              <VCard>
                <VCardText class="text-center">
                  <span class="text-h4 font-weight-bold">{{ performance?.timesServed ?? 0 }}</span>
                  <div class="text-body-2 text-disabled mt-1">Times Served</div>
                </VCardText>
              </VCard>
            </VCol>
            <VCol cols="12" sm="6" md="3">
              <VCard>
                <VCardText class="text-center">
                  <span class="text-h4 font-weight-bold">
                    {{ performance?.accuracyRate != null ? `${performance.accuracyRate}%` : '--' }}
                  </span>
                  <div class="text-body-2 text-disabled mt-1">Accuracy Rate</div>
                </VCardText>
              </VCard>
            </VCol>
            <VCol cols="12" sm="6" md="3">
              <VCard>
                <VCardText class="text-center">
                  <span class="text-h4 font-weight-bold">
                    {{ performance?.avgTimeSeconds != null ? `${performance.avgTimeSeconds}s` : '--' }}
                  </span>
                  <div class="text-body-2 text-disabled mt-1">Avg Response Time</div>
                </VCardText>
              </VCard>
            </VCol>
            <VCol cols="12" sm="6" md="3">
              <VCard>
                <VCardText class="text-center">
                  <span class="text-h4 font-weight-bold">
                    {{ performance?.discriminationIndex ?? '--' }}
                  </span>
                  <div class="text-body-2 text-disabled mt-1">Discrimination Index</div>
                </VCardText>
              </VCard>
            </VCol>

            <!-- Performance breakdown -->
            <VCol v-if="performance?.performanceBreakdown?.length" cols="12">
              <VCard>
                <VCardItem>
                  <VCardTitle>Performance by Difficulty</VCardTitle>
                </VCardItem>
                <VCardText>
                  <VTable density="compact">
                    <thead>
                      <tr>
                        <th>Difficulty</th>
                        <th>Attempts</th>
                        <th>Accuracy</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="row in performance.performanceBreakdown" :key="row.difficulty">
                        <td class="text-capitalize">{{ row.difficulty }}</td>
                        <td>{{ row.attempts }}</td>
                        <td>{{ row.accuracy }}%</td>
                      </tr>
                    </tbody>
                  </VTable>
                </VCardText>
              </VCard>
            </VCol>

            <!-- Empty state -->
            <VCol v-if="!performance" cols="12">
              <VCard>
                <VCardText class="text-center py-12 text-disabled">
                  <VIcon icon="tabler-chart-bar-off" size="48" class="mb-4" />
                  <div class="text-h6">No Analytics Yet</div>
                  <div class="text-body-2 mt-2">
                    Student performance data will appear here once this question has been served in practice sessions.
                  </div>
                </VCardText>
              </VCard>
            </VCol>
          </VRow>
        </VWindowItem>
      </VWindow>
    </template>

    <!-- Not found -->
    <VCard v-else>
      <VCardText class="text-center py-12 text-disabled">
        Question not found
      </VCardText>
    </VCard>

    <!-- Deprecate dialog -->
    <VDialog v-model="showDeprecateDialog" max-width="480">
      <VCard>
        <VCardTitle>Deprecate Question</VCardTitle>
        <VCardText>
          <AppTextarea
            v-model="deprecateReason"
            label="Reason for deprecation"
            placeholder="e.g., Outdated curriculum content, factual error..."
            rows="3"
          />
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="tonal" @click="showDeprecateDialog = false">Cancel</VBtn>
          <VBtn color="error" :loading="isDeprecating" @click="deprecateQuestion">Deprecate</VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </section>
</template>

<style lang="scss">
.bg-success-lighten {
  background-color: rgba(var(--v-theme-success), 0.08);
}
</style>
