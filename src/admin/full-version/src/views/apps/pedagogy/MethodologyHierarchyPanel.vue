<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  studentId: string
}

const props = defineProps<Props>()

interface MethodologyLevelEntry {
  id: string
  level: 'Subject' | 'Topic' | 'Concept'
  methodology: string
  source: string
  attemptCount: number
  successRate: number
  confidence: number
  hasSufficientData: boolean
  confidenceReached: boolean
}

interface MethodologyProfileResponse {
  studentId: string
  subjects: MethodologyLevelEntry[]
  topics: MethodologyLevelEntry[]
  concepts: MethodologyLevelEntry[]
}

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<MethodologyProfileResponse | null>(null)

const fetchProfile = async () => {
  loading.value = true
  error.value = null
  try {
    profile.value = await $api<MethodologyProfileResponse>(
      `/admin/mastery/students/${props.studentId}/methodology-profile`,
    )
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load methodology profile'
    console.error('Failed to fetch methodology profile:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchProfile)
watch(() => props.studentId, fetchProfile)

// Override dialog
const overrideDialog = ref(false)
const overrideTarget = ref<MethodologyLevelEntry | null>(null)
const overrideMethodology = ref('')
const overrideLoading = ref(false)

const allMethodologies = [
  'Socratic', 'SpacedRepetition', 'Feynman', 'ProjectBased',
  'BloomsProgression', 'WorkedExample', 'Analogy', 'RetrievalPractice', 'DrillAndPractice',
]

const openOverride = (entry: MethodologyLevelEntry) => {
  overrideTarget.value = entry
  overrideMethodology.value = entry.methodology
  overrideDialog.value = true
}

const submitOverride = async () => {
  if (!overrideTarget.value) return
  overrideLoading.value = true
  try {
    await $api(`/admin/mastery/students/${props.studentId}/methodology-override`, {
      method: 'POST',
      body: {
        level: overrideTarget.value.level,
        levelId: overrideTarget.value.id,
        methodology: overrideMethodology.value,
      },
    })
    overrideDialog.value = false
    await fetchProfile()
  }
  catch (err: any) {
    console.error('Override failed:', err)
  }
  finally {
    overrideLoading.value = false
  }
}

// Helpers
const sourceColor = (source: string) => {
  switch (source) {
    case 'DataDriven': return 'success'
    case 'TeacherOverride': return 'info'
    case 'McmRouted': return 'primary'
    case 'BloomsDefault': return 'secondary'
    case 'Inherited': return 'warning'
    default: return 'secondary'
  }
}

const sourceLabel = (source: string) => {
  switch (source) {
    case 'DataDriven': return 'Data-Driven'
    case 'TeacherOverride': return 'Teacher Override'
    case 'McmRouted': return 'MCM Routed'
    case 'BloomsDefault': return 'Blooms Default'
    case 'Inherited': return 'Inherited'
    default: return source
  }
}

const confidenceColor = (entry: MethodologyLevelEntry) => {
  if (entry.hasSufficientData) return 'success'
  if (entry.attemptCount >= 15) return 'warning'
  return 'secondary'
}

const confidenceLabel = (entry: MethodologyLevelEntry) => {
  if (entry.hasSufficientData) return 'Sufficient Data'
  const threshold = entry.level === 'Subject' ? 50 : 30
  return `${entry.attemptCount}/${threshold} attempts`
}

const levelIcon = (level: string) => {
  switch (level) {
    case 'Subject': return 'tabler-book-2'
    case 'Topic': return 'tabler-category'
    case 'Concept': return 'tabler-atom'
    default: return 'tabler-point'
  }
}

const headers = [
  { title: 'Level', key: 'level', width: '80px' },
  { title: 'ID', key: 'id' },
  { title: 'Methodology', key: 'methodology' },
  { title: 'Source', key: 'source' },
  { title: 'Confidence', key: 'confidence' },
  { title: 'Success Rate', key: 'successRate' },
  { title: 'Attempts', key: 'attemptCount', align: 'center' as const },
  { title: '', key: 'actions', sortable: false, width: '80px' },
]

const allEntries = computed(() => {
  if (!profile.value) return []
  return [
    ...profile.value.subjects,
    ...profile.value.topics,
    ...profile.value.concepts,
  ]
})

defineExpose({ refresh: fetchProfile })
</script>

<template>
  <VCard>
    <VCardItem>
      <template #title>
        <div class="d-flex align-center gap-2">
          <VIcon
            icon="tabler-hierarchy-3"
            color="primary"
          />
          Methodology Hierarchy
        </div>
      </template>
      <template #subtitle>
        Subject &rarr; Topic &rarr; Concept cascade with confidence gates
      </template>
      <template #append>
        <VBtn
          icon
          variant="text"
          size="small"
          @click="fetchProfile"
        >
          <VIcon icon="tabler-refresh" />
        </VBtn>
      </template>
    </VCardItem>

    <VDivider />

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="ma-4"
    >
      {{ error }}
    </VAlert>

    <!-- Summary chips -->
    <VCardText
      v-if="profile && !loading"
      class="pb-0"
    >
      <div class="d-flex gap-3 flex-wrap">
        <VChip
          color="primary"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            icon="tabler-book-2"
            size="14"
            start
          />
          {{ profile.subjects.length }} Subjects
        </VChip>
        <VChip
          color="info"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            icon="tabler-category"
            size="14"
            start
          />
          {{ profile.topics.length }} Topics
        </VChip>
        <VChip
          color="secondary"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            icon="tabler-atom"
            size="14"
            start
          />
          {{ profile.concepts.length }} Concepts
        </VChip>
        <VChip
          color="success"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            icon="tabler-circle-check"
            size="14"
            start
          />
          {{ allEntries.filter(e => e.hasSufficientData).length }} Data-Validated
        </VChip>
      </div>
    </VCardText>

    <!-- Hierarchy Table -->
    <VDataTable
      :headers="headers"
      :items="allEntries"
      :loading="loading"
      item-value="id"
      class="text-no-wrap"
      :sort-by="[{ key: 'level', order: 'asc' }]"
      :items-per-page="25"
    >
      <template #item.level="{ item }">
        <VChip
          :color="item.level === 'Subject' ? 'primary' : item.level === 'Topic' ? 'info' : 'secondary'"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            :icon="levelIcon(item.level)"
            size="14"
            start
          />
          {{ item.level }}
        </VChip>
      </template>

      <template #item.id="{ item }">
        <span class="text-body-2 font-weight-medium">{{ item.id }}</span>
      </template>

      <template #item.methodology="{ item }">
        <VChip
          color="primary"
          variant="flat"
          label
          size="small"
        >
          {{ item.methodology }}
        </VChip>
      </template>

      <template #item.source="{ item }">
        <VChip
          :color="sourceColor(item.source)"
          variant="tonal"
          label
          size="small"
        >
          {{ sourceLabel(item.source) }}
        </VChip>
      </template>

      <template #item.confidence="{ item }">
        <div class="d-flex align-center gap-2">
          <VProgressLinear
            :model-value="item.hasSufficientData ? item.confidence * 100 : (item.attemptCount / (item.level === 'Subject' ? 50 : 30)) * 100"
            :color="confidenceColor(item)"
            rounded
            :height="6"
            style="min-inline-size: 80px; max-inline-size: 120px;"
          />
          <VChip
            :color="confidenceColor(item)"
            variant="tonal"
            label
            size="x-small"
          >
            {{ confidenceLabel(item) }}
          </VChip>
        </div>
      </template>

      <template #item.successRate="{ item }">
        <span class="text-body-2 font-weight-medium">
          {{ item.attemptCount > 0 ? `${(item.successRate * 100).toFixed(0)}%` : '--' }}
        </span>
      </template>

      <template #item.attemptCount="{ item }">
        <span class="text-body-2">{{ item.attemptCount }}</span>
      </template>

      <template #item.actions="{ item }">
        <VBtn
          icon
          variant="text"
          size="small"
          color="primary"
          @click="openOverride(item)"
        >
          <VIcon
            icon="tabler-edit"
            size="18"
          />
          <VTooltip activator="parent">
            Override methodology
          </VTooltip>
        </VBtn>
      </template>

      <template #no-data>
        <div class="text-center pa-4 text-disabled">
          No methodology data available for this student
        </div>
      </template>
    </VDataTable>

    <!-- Override Dialog -->
    <VDialog
      v-model="overrideDialog"
      max-width="480"
    >
      <VCard>
        <VCardItem title="Override Methodology">
          <template #subtitle>
            {{ overrideTarget?.level }}: {{ overrideTarget?.id }}
          </template>
        </VCardItem>

        <VDivider />

        <VCardText>
          <VAlert
            type="info"
            variant="tonal"
            class="mb-4"
          >
            This override takes immediate effect and bypasses cooldown. The student will use this methodology until enough data suggests a better option.
          </VAlert>

          <VSelect
            v-model="overrideMethodology"
            :items="allMethodologies"
            label="Select Methodology"
            variant="outlined"
          />
        </VCardText>

        <VDivider />

        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            @click="overrideDialog = false"
          >
            Cancel
          </VBtn>
          <VBtn
            color="primary"
            :loading="overrideLoading"
            @click="submitOverride"
          >
            Apply Override
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </VCard>
</template>
