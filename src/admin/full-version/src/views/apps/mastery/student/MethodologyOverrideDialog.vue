<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  studentId: string
  studentName: string
  modelValue: boolean
}

interface ConceptOption {
  title: string
  value: string
  conceptId: string
  level: string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  'overrideApplied': []
}>()

const isOpen = computed({
  get: () => props.modelValue,
  set: val => emit('update:modelValue', val),
})

const methodologies = [
  { title: 'Socratic', value: 'Socratic' },
  { title: 'Direct Instruction', value: 'DirectInstruction' },
  { title: 'Feynman Technique', value: 'Feynman' },
  { title: 'Worked Example', value: 'WorkedExample' },
]

const durations = [
  { title: 'Until mastery', value: 0 },
  { title: '7 days', value: 7 },
  { title: '14 days', value: 14 },
  { title: '30 days', value: 30 },
  { title: 'Permanent', value: -1 },
]

// Concept levels loaded from the student's knowledge map
const conceptOptions = ref<ConceptOption[]>([])
const conceptsLoading = ref(false)

const loadConcepts = async () => {
  if (!props.studentId) return
  conceptsLoading.value = true
  try {
    const data = await $api<{ concepts: { conceptId: string; name: string; subject: string }[] }>(
      `/admin/mastery/students/${props.studentId}/knowledge-map`,
    )
    conceptOptions.value = (data.concepts ?? []).map(c => ({
      title: `${c.name} (${c.subject})`,
      value: c.conceptId,
      conceptId: c.conceptId,
      level: c.name,
    }))
  }
  catch (err: any) {
    console.error('Failed to load knowledge map concepts:', err)
  }
  finally {
    conceptsLoading.value = false
  }
}

interface FormState {
  methodology: string
  conceptId: string
  durationDays: number
  reason: string
}

const form = ref<FormState>({
  methodology: '',
  conceptId: '',
  durationDays: 0,
  reason: '',
})

const isSubmitting = ref(false)
const errorMsg = ref<string | null>(null)

const isValid = computed(() =>
  !!form.value.methodology && form.value.reason.trim().length >= 20,
)

const selectedConcept = computed(() =>
  conceptOptions.value.find(c => c.value === form.value.conceptId) ?? null,
)

const durationLabel = computed(() =>
  durations.find(d => d.value === form.value.durationDays)?.title ?? '',
)

const previewText = computed(() => {
  if (!form.value.methodology) return ''
  const conceptPart = selectedConcept.value ? ` for ${selectedConcept.value.level}` : ''
  return `${props.studentName} will use ${form.value.methodology}${conceptPart} (${durationLabel.value})`
})

const submit = async () => {
  if (!isValid.value) return
  isSubmitting.value = true
  errorMsg.value = null
  try {
    const concept = selectedConcept.value
    await $api(`/admin/mastery/students/${props.studentId}/methodology-override`, {
      method: 'POST',
      body: {
        Level: concept?.level ?? '',
        LevelId: concept?.conceptId ?? '',
        Methodology: form.value.methodology,
      },
    })
    isOpen.value = false
    emit('overrideApplied')
  }
  catch (err: any) {
    errorMsg.value = err.data?.message ?? err.message ?? 'Failed to apply override'
  }
  finally {
    isSubmitting.value = false
  }
}

const reset = () => {
  form.value = { methodology: '', conceptId: '', durationDays: 0, reason: '' }
  errorMsg.value = null
}

watch(isOpen, val => {
  if (val) {
    reset()
    loadConcepts()
  }
})
</script>

<template>
  <VDialog
    v-model="isOpen"
    max-width="560"
    persistent
  >
    <VCard>
      <VCardTitle class="text-h5 pa-5 pb-3">
        Override Methodology
      </VCardTitle>

      <VDivider />

      <VCardText class="pa-5">
        <VForm @submit.prevent="submit">
          <VSelect
            v-model="form.methodology"
            :items="methodologies"
            label="Methodology"
            placeholder="Select methodology"
            class="mb-4"
          />

          <VAutocomplete
            v-model="form.conceptId"
            :items="conceptOptions"
            :loading="conceptsLoading"
            label="Concept / Level (optional)"
            placeholder="Search concepts from student's knowledge map"
            clearable
            class="mb-4"
          />

          <VSelect
            v-model="form.durationDays"
            :items="durations"
            label="Duration"
            class="mb-4"
          />

          <VTextarea
            v-model="form.reason"
            label="Reason"
            placeholder="Explain why this override is needed (min 20 characters)..."
            :counter="200"
            hint="Minimum 20 characters — recorded in the audit trail"
            persistent-hint
            rows="3"
            class="mb-4"
          />

          <VAlert
            v-if="previewText"
            type="info"
            variant="tonal"
            class="mb-4"
          >
            {{ previewText }}
          </VAlert>

          <VAlert
            v-if="errorMsg"
            type="error"
            variant="tonal"
            class="mb-0"
          >
            {{ errorMsg }}
          </VAlert>
        </VForm>
      </VCardText>

      <VDivider />

      <VCardActions class="pa-4">
        <VSpacer />
        <VBtn
          variant="text"
          :disabled="isSubmitting"
          @click="isOpen = false"
        >
          Cancel
        </VBtn>
        <VBtn
          color="warning"
          :loading="isSubmitting"
          :disabled="!isValid"
          @click="submit"
        >
          Apply Override
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
