<script setup lang="ts">
/**
 * STEP-002: MathInput.vue — MathLive wrapper for mathematical expression input.
 *
 * Wraps cortexjs.io/mathlive as a Vue 3 component.
 * MathLive handles math LTR internally; wrapper sets dir="ltr" on the math-field.
 * Separate verbal textarea for non-math input (RTL, 200-char soft limit).
 *
 * Props:
 * - modelValue: LaTeX string (v-model)
 * - locale: 'ar' | 'he' | 'en'
 * - disabled: boolean
 * - placeholder: string
 * - showVerbalInput: boolean (enables the verbal explanation textarea)
 *
 * Emits:
 * - update:modelValue: LaTeX string
 * - update:verbalInput: verbal explanation string
 * - submit: Enter key pressed
 */

import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = withDefaults(defineProps<{
  modelValue?: string
  verbalInput?: string
  locale?: 'ar' | 'he' | 'en'
  disabled?: boolean
  placeholder?: string
  showVerbalInput?: boolean
  verbalPlaceholder?: string
  verbalMaxLength?: number
}>(), {
  modelValue: '',
  verbalInput: '',
  locale: 'en',
  disabled: false,
  placeholder: '',
  showVerbalInput: false,
  verbalPlaceholder: '',
  verbalMaxLength: 200,
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'update:verbalInput', value: string): void
  (e: 'submit'): void
}>()

const { t } = useI18n()

const mathFieldRef = ref<HTMLElement | null>(null)
const localVerbal = ref(props.verbalInput)
const isMathLiveLoaded = ref(false)

// RTL locale detection
const isRtl = ['ar', 'he'].includes(props.locale)

onMounted(async () => {
  // Dynamically import MathLive to avoid SSR issues
  try {
    await import('mathlive')
    isMathLiveLoaded.value = true

    // Configure the math-field after MathLive is loaded
    if (mathFieldRef.value) {
      const mf = mathFieldRef.value as any

      // Set initial value
      if (props.modelValue)
        mf.setValue(props.modelValue)

      // Listen for input changes
      mf.addEventListener('input', () => {
        emit('update:modelValue', mf.value)
      })

      // Listen for Enter key (commit/submit)
      mf.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'Enter' && !e.shiftKey) {
          e.preventDefault()
          emit('submit')
        }
      })
    }
  }
  catch (err) {
    console.warn('MathLive failed to load:', err)
  }
})

// Sync external modelValue changes
watch(() => props.modelValue, newVal => {
  if (mathFieldRef.value && isMathLiveLoaded.value) {
    const mf = mathFieldRef.value as any
    if (mf.value !== newVal)
      mf.setValue(newVal || '')
  }
})

watch(localVerbal, val => {
  emit('update:verbalInput', val)
})

watch(() => props.verbalInput, val => {
  localVerbal.value = val
})

onBeforeUnmount(() => {
  // MathLive cleanup happens automatically when element is removed
})
</script>

<template>
  <div class="math-input-wrapper">
    <!-- Math expression input (always LTR) -->
    <div
      class="math-input-field"
      dir="ltr"
    >
      <label
        v-if="placeholder"
        class="math-input-label"
      >
        {{ placeholder }}
      </label>
      <MathField
        ref="mathFieldRef"
        class="math-input"
        :disabled="disabled"
        :aria-label="placeholder || t('session.mathInput.enterExpression')"
        virtual-keyboard-mode="manual"
        smart-mode
      />
      <p
        v-if="!isMathLiveLoaded"
        class="math-input-fallback"
      >
        <bdi dir="ltr">
          <input
            type="text"
            :value="modelValue"
            :disabled="disabled"
            :placeholder="placeholder || t('session.mathInput.enterLatex')"
            @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
            @keydown.enter="emit('submit')"
          >
        </bdi>
      </p>
    </div>

    <!-- Verbal explanation textarea (RTL for Arabic/Hebrew) -->
    <div
      v-if="showVerbalInput"
      class="verbal-input-field"
      :dir="isRtl ? 'rtl' : 'ltr'"
    >
      <label class="verbal-input-label">
        {{ verbalPlaceholder || t('session.mathInput.verbalExplanation') }}
      </label>
      <textarea
        v-model="localVerbal"
        class="verbal-textarea"
        :disabled="disabled"
        :maxlength="verbalMaxLength"
        :placeholder="verbalPlaceholder || t('session.mathInput.verbalPlaceholder')"
        :aria-label="t('session.mathInput.verbalExplanation')"
        rows="3"
      />
      <span
        class="verbal-char-count"
        :class="{ 'verbal-char-limit': localVerbal.length >= verbalMaxLength }"
      >
        {{ localVerbal.length }}/{{ verbalMaxLength }}
      </span>
    </div>
  </div>
</template>
