<script setup lang="ts">
/**
 * FIND-privacy-001: AgeGateStep.vue
 *
 * Collects the student's date of birth via a date picker and evaluates
 * the consent tier required. This is NOT a trivially bypassable checkbox —
 * the DOB is validated, the age is computed server-side on submission,
 * and the backend independently enforces the same age thresholds.
 *
 * Compliance: COPPA §312.5, GDPR Art 8, ICO Children's Code Std 7+11,
 * Israel PPL §11.
 */

import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { validateDateOfBirth, evaluateAgeGate } from '@/composables/useAgeGate'
import type { AgeGateResult } from '@/composables/useAgeGate'

interface Props {
  modelValue: string | null  // ISO date string YYYY-MM-DD or null
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
  'age-evaluated': [result: AgeGateResult | null]
}>()

const { t } = useI18n()

const dobInput = ref(props.modelValue ?? '')
const dobError = ref('')
const ageResult = ref<AgeGateResult | null>(null)

// Date constraints for the picker
const today = new Date()
const maxDate = new Date(today.getFullYear() - 4, today.getMonth(), today.getDate())
  .toISOString()
  .split('T')[0]
const minDate = new Date(today.getFullYear() - 120, 0, 1)
  .toISOString()
  .split('T')[0]

watch(() => props.modelValue, (val) => {
  if (val !== dobInput.value)
    dobInput.value = val ?? ''
})

const formattedAge = computed(() => {
  if (!ageResult.value)
    return ''
  return ageResult.value.age.toString()
})

function handleDateChange(val: string) {
  dobInput.value = val
  dobError.value = ''
  ageResult.value = null

  const validationError = validateDateOfBirth(val)
  if (validationError) {
    dobError.value = t(validationError)
    emit('update:modelValue', null)
    emit('age-evaluated', null)
    return
  }

  const parsed = new Date(val)
  const result = evaluateAgeGate(parsed)
  ageResult.value = result

  emit('update:modelValue', val)
  emit('age-evaluated', result)
}
</script>

<template>
  <div
    class="age-gate-step"
    data-testid="age-gate-step"
  >
    <h2 class="text-h5 mb-1">
      {{ t('auth.ageGate.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('auth.ageGate.subtitle') }}
    </p>

    <VTextField
      :model-value="dobInput"
      type="date"
      :label="t('auth.ageGate.dobLabel')"
      :error-messages="dobError"
      :max="maxDate"
      :min="minDate"
      data-testid="age-gate-dob"
      autocomplete="bday"
      class="mb-3"
      @update:model-value="handleDateChange"
    />

    <!-- Age confirmation display -->
    <VAlert
      v-if="ageResult && !ageResult.requiresParentalConsent"
      type="success"
      variant="tonal"
      density="compact"
      class="mb-3"
      data-testid="age-gate-adult"
    >
      {{ t('auth.ageGate.ageConfirmed', { age: formattedAge }) }}
    </VAlert>

    <!-- Parental consent required notice -->
    <VAlert
      v-if="ageResult && ageResult.requiresParentalConsent"
      type="warning"
      variant="tonal"
      density="compact"
      class="mb-3"
      data-testid="age-gate-minor"
    >
      {{ t('auth.ageGate.parentRequired') }}
    </VAlert>

    <!-- Privacy notice -->
    <p class="text-caption text-medium-emphasis mt-2">
      {{ t('auth.ageGate.privacyNotice') }}
    </p>
  </div>
</template>
