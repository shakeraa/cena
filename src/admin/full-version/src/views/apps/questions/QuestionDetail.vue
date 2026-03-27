<script setup lang="ts">
import { PerfectScrollbar } from 'vue3-perfect-scrollbar'

interface Props {
  questionId: string | null
  isOpen: boolean
}

interface Emit {
  (e: 'update:isOpen', value: boolean): void
  (e: 'updated'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

// Question data
const question = ref<any>(null)
const performance = ref<any>(null)
const isLoading = ref(false)
const isDeprecating = ref(false)
const isEditing = ref(false)

// Fetch question detail when questionId changes
watch(() => props.questionId, async (id) => {
  if (!id) {
    question.value = null
    performance.value = null

    return
  }

  isLoading.value = true

  try {
    const [questionRes, performanceRes] = await Promise.all([
      $api(`/admin/questions/${id}`),
      $api(`/admin/questions/${id}/performance`).catch(() => null),
    ])

    question.value = questionRes
    performance.value = performanceRes
  }
  catch (err) {
    console.error('Failed to fetch question detail', err)
    question.value = null
    performance.value = null
  }
  finally {
    isLoading.value = false
  }
}, { immediate: true })

// Resolvers
const resolveStatusColor = (status: string) => {
  const map: Record<string, string> = {
    'draft': 'secondary',
    'in-review': 'warning',
    'approved': 'info',
    'published': 'success',
    'deprecated': 'error',
  }

  return map[status] ?? 'primary'
}

const resolveDifficultyColor = (difficulty: string) => {
  const map: Record<string, string> = {
    easy: 'success',
    medium: 'warning',
    hard: 'error',
  }

  return map[difficulty] ?? 'secondary'
}

const resolveDifficultyPercent = (difficulty: string) => {
  const map: Record<string, number> = {
    easy: 33,
    medium: 66,
    hard: 100,
  }

  return map[difficulty] ?? 50
}

// Quality gate dimensions
const qualityDimensions = [
  { key: 'structuralValidity', label: 'Structural' },
  { key: 'stemClarity', label: 'Stem Clarity' },
  { key: 'distractorQuality', label: 'Distractors' },
  { key: 'bloomAlignment', label: "Bloom's Alignment" },
  { key: 'factualAccuracy', label: 'Factual Accuracy' },
  { key: 'languageQuality', label: 'Language' },
  { key: 'pedagogicalQuality', label: 'Pedagogy' },
  { key: 'culturalSensitivity', label: 'Cultural' },
]

const resolveQualityColor = (score: number) => {
  if (score >= 80) return 'success'
  if (score >= 60) return 'info'
  if (score >= 40) return 'warning'
  return 'error'
}

const resolveGateDecisionColor = (decision: string) => {
  const map: Record<string, string> = {
    AutoApproved: 'success',
    NeedsReview: 'warning',
    AutoRejected: 'error',
  }

  return map[decision] ?? 'secondary'
}

// Actions
const deprecateQuestion = async () => {
  if (!props.questionId)
    return

  isDeprecating.value = true

  try {
    await $api(`/admin/questions/${props.questionId}/deprecate`, { method: 'POST' })
    emit('updated')
    emit('update:isOpen', false)
  }
  catch (err) {
    console.error('Failed to deprecate question', err)
  }
  finally {
    isDeprecating.value = false
  }
}

const closeDrawer = () => {
  emit('update:isOpen', false)
}
</script>

<template>
  <VNavigationDrawer
    data-allow-mismatch
    :model-value="props.isOpen"
    temporary
    location="end"
    width="480"
    border="none"
    @update:model-value="(val: boolean) => emit('update:isOpen', val)"
  >
    <!-- Header -->
    <AppDrawerHeaderSection
      title="Question Detail"
      @cancel="closeDrawer"
    />

    <VDivider />

    <VCard flat>
      <PerfectScrollbar
        :options="{ wheelPropagation: false }"
        class="h-100"
      >
        <VCardText style="block-size: calc(100vh - 5rem);">
          <!-- Loading state -->
          <div
            v-if="isLoading"
            class="d-flex justify-center align-center py-16"
          >
            <VProgressCircular
              indeterminate
              color="primary"
            />
          </div>

          <!-- Empty state -->
          <div
            v-else-if="!question"
            class="d-flex justify-center align-center py-16"
          >
            <span class="text-disabled">No question selected</span>
          </div>

          <!-- Question content -->
          <div v-else>
            <!-- Question stem -->
            <VCard
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Question Stem
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <div
                  class="text-body-1"
                  v-html="question.stem"
                />
              </VCardText>
            </VCard>

            <!-- Answer options -->
            <VCard
              v-if="question.options?.length"
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Answer Options
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <VRadioGroup
                  :model-value="question.correctAnswer"
                  disabled
                >
                  <VRadio
                    v-for="(option, idx) in question.options"
                    :key="idx"
                    :value="option.key"
                    :label="option.text"
                    :color="option.key === question.correctAnswer ? 'success' : undefined"
                    :class="{ 'bg-success-lighten': option.key === question.correctAnswer }"
                    class="mb-1 pa-2 rounded"
                  />
                </VRadioGroup>
              </VCardText>
            </VCard>

            <!-- Metadata card -->
            <VCard
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Metadata
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <VList density="compact">
                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Subject</span>
                    </template>
                    <VListItemTitle>
                      <VChip
                        size="small"
                        label
                        :color="question.subject === 'math' ? 'primary' : 'info'"
                        class="text-capitalize"
                      >
                        {{ question.subject }}
                      </VChip>
                    </VListItemTitle>
                  </VListItem>

                  <VListItem v-if="question.topic">
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Topic</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ question.topic }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Bloom's Level</span>
                    </template>
                    <VListItemTitle>
                      <VRating
                        :model-value="question.bloomLevel"
                        readonly
                        density="compact"
                        size="small"
                        :length="6"
                        color="warning"
                        active-color="warning"
                      />
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Difficulty</span>
                    </template>
                    <VListItemTitle>
                      <div class="d-flex align-center gap-2">
                        <VProgressLinear
                          :model-value="resolveDifficultyPercent(question.difficulty)"
                          :color="resolveDifficultyColor(question.difficulty)"
                          height="6"
                          rounded
                          style="max-inline-size: 100px;"
                        />
                        <span class="text-body-2 text-capitalize">{{ question.difficulty }}</span>
                      </div>
                    </VListItemTitle>
                  </VListItem>

                  <VListItem v-if="question.grade">
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Grade</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ question.grade }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Language</span>
                    </template>
                    <VListItemTitle class="text-body-1 text-capitalize">
                      {{ question.language }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Status</span>
                    </template>
                    <VListItemTitle>
                      <VChip
                        :color="resolveStatusColor(question.status)"
                        size="small"
                        label
                        class="text-capitalize"
                      >
                        {{ question.status }}
                      </VChip>
                    </VListItemTitle>
                  </VListItem>
                </VList>
              </VCardText>
            </VCard>

            <!-- Concept chips -->
            <VCard
              v-if="question.concepts?.length"
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Curriculum Concepts
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <div class="d-flex flex-wrap gap-2">
                  <VChip
                    v-for="concept in question.concepts"
                    :key="concept"
                    size="small"
                    variant="tonal"
                    color="primary"
                  >
                    {{ concept }}
                  </VChip>
                </div>
              </VCardText>
            </VCard>

            <!-- Quality Gate Card (8-dimension breakdown) -->
            <VCard
              v-if="question.qualityGate"
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium d-flex align-center gap-2">
                  Quality Gate
                  <VChip
                    size="x-small"
                    :color="resolveGateDecisionColor(question.qualityGate.gateDecision)"
                    label
                  >
                    {{ question.qualityGate.gateDecision }}
                  </VChip>
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <!-- Composite score -->
                <div class="d-flex align-center gap-2 mb-3">
                  <span class="text-h4 font-weight-bold">{{ Math.round(question.qualityGate.compositeScore) }}</span>
                  <span class="text-body-2 text-disabled">/ 100</span>
                  <VSpacer />
                  <span
                    v-if="question.qualityGate.violationCount > 0"
                    class="text-body-2 text-error"
                  >
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

                <!-- 8-dimension bars -->
                <div class="d-flex flex-column gap-3">
                  <div
                    v-for="dim in qualityDimensions"
                    :key="dim.key"
                    class="d-flex align-center gap-2"
                  >
                    <span class="text-body-2 text-medium-emphasis" style="min-inline-size: 130px;">{{ dim.label }}</span>
                    <VProgressLinear
                      :model-value="question.qualityGate[dim.key]"
                      :color="resolveQualityColor(question.qualityGate[dim.key])"
                      height="6"
                      rounded
                      style="max-inline-size: 140px;"
                    />
                    <span class="text-body-2 font-weight-medium" style="min-inline-size: 30px;">
                      {{ question.qualityGate[dim.key] }}
                    </span>
                  </div>
                </div>

                <div
                  v-if="question.qualityGate.evaluatedAt"
                  class="text-caption text-disabled mt-3"
                >
                  Evaluated {{ new Date(question.qualityGate.evaluatedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) }}
                </div>
              </VCardText>
            </VCard>

            <!-- Performance card -->
            <VCard
              v-if="performance"
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Performance
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <VList density="compact">
                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 140px;">Times Served</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ performance.timesServed ?? 0 }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 140px;">Accuracy Rate</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ performance.accuracyRate != null ? `${performance.accuracyRate}%` : '--' }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 140px;">Avg Response Time</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ performance.avgResponseTime != null ? `${performance.avgResponseTime}s` : '--' }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem>
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 140px;">Discrimination Index</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ performance.discriminationIndex ?? '--' }}
                    </VListItemTitle>
                  </VListItem>
                </VList>
              </VCardText>
            </VCard>

            <!-- Provenance -->
            <VCard
              v-if="question.provenance"
              variant="outlined"
              class="mb-4"
            >
              <VCardItem>
                <VCardTitle class="text-body-1 font-weight-medium">
                  Provenance
                </VCardTitle>
              </VCardItem>
              <VCardText>
                <VList density="compact">
                  <VListItem v-if="question.provenance.source">
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Source</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ question.provenance.source }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem v-if="question.provenance.ingestedAt">
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Ingested At</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      {{ new Date(question.provenance.ingestedAt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' }) }}
                    </VListItemTitle>
                  </VListItem>

                  <VListItem v-if="question.provenance.importBatch">
                    <template #prepend>
                      <span class="text-body-2 text-medium-emphasis me-2" style="min-inline-size: 100px;">Batch</span>
                    </template>
                    <VListItemTitle class="text-body-1">
                      <code>{{ question.provenance.importBatch }}</code>
                    </VListItemTitle>
                  </VListItem>
                </VList>
              </VCardText>
            </VCard>

            <!-- Actions -->
            <div class="d-flex gap-4 mt-6 pb-10">
              <VBtn
                color="primary"
                prepend-icon="tabler-pencil"
                @click="isEditing = !isEditing"
              >
                {{ isEditing ? 'Cancel Edit' : 'Edit' }}
              </VBtn>
              <VBtn
                v-if="question.status !== 'deprecated'"
                color="error"
                variant="tonal"
                prepend-icon="tabler-archive"
                :loading="isDeprecating"
                @click="deprecateQuestion"
              >
                Deprecate
              </VBtn>
            </div>
          </div>
        </VCardText>
      </PerfectScrollbar>
    </VCard>
  </VNavigationDrawer>
</template>

<style lang="scss">
.v-navigation-drawer__content {
  overflow-y: hidden !important;
}

.bg-success-lighten {
  background-color: rgba(var(--v-theme-success), 0.08);
}
</style>
