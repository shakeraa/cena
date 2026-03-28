<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  studentId: string
  studentName: string
  modelValue: boolean
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

const form = ref({
  methodology: '',
  level: '',
  reason: '',
  durationDays: 0,
})

const isSubmitting = ref(false)
const errorMsg = ref<string | null>(null)

const isValid = computed(() =>
  form.value.methodology && form.value.reason.length >= 20,
)

const previewText = computed(() => {
  if (!form.value.methodology) return ''
  const dur = durations.find(d => d.value === form.value.durationDays)?.title ?? ''

  return `${props.studentName} will use ${form.value.methodology} ${form.value.level ? `for ${form.value.level} ` : ''}for ${dur}`
})

const submit = async () => {
  if (!isValid.value) return
  isSubmitting.value = true
  errorMsg.value = null
  try {
    await $api(`/admin/mastery/students/${props.studentId}/methodology-override`, {
      method: 'POST',
      body: {
        methodology: form.value.methodology,
        level: form.value.level || null,
        reason: form.value.reason,
        durationDays: form.value.durationDays === -1 ? null : form.value.durationDays || null,
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
  form.value = { methodology: '', level: '', reason: '', durationDays: 0 }
  errorMsg.value = null
}

watch(isOpen, val => { if (val) reset() })
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

          <VTextField
            v-model="form.level"
            label="Level (optional)"
            placeholder="e.g. Algebra, Grade 5"
            hint="Scope the override to a specific level or subject"
            persistent-hint
            class="mb-4"
          />

          <VTextarea
            v-model="form.reason"
            label="Reason"
            placeholder="Explain why this override is needed..."
            :counter="200"
            hint="Minimum 20 characters"
            persistent-hint
            rows="3"
            class="mb-4"
          />

          <VSelect
            v-model="form.durationDays"
            :items="durations"
            label="Duration"
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
