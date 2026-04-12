<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SupportedLocale } from '@/stores/onboardingStore'
import { useAvailableLocales } from '@/composables/useAvailableLocales'

interface Props {
  modelValue: SupportedLocale
}

const props = defineProps<Props>()
const emit = defineEmits<{
  'update:modelValue': [value: SupportedLocale]
}>()

const { t } = useI18n()

// FIND-pedagogy-010: use the shared Hebrew gate instead of a hardcoded
// 3-locale array. When VITE_ENABLE_HEBREW is false the 'he' entry is
// excluded, closing the onboarding picker bypass.
const { locales: availableLocales } = useAvailableLocales()

interface LocaleOption {
  code: SupportedLocale
  nativeLabel: string
  dir: 'ltr' | 'rtl'
  sampleKey: string
  testId: string
}

const ALL_LOCALES: LocaleOption[] = [
  {
    code: 'en',
    nativeLabel: 'English',
    dir: 'ltr',
    sampleKey: 'onboarding.language.sampleEn',
    testId: 'locale-en',
  },
  {
    code: 'ar',
    nativeLabel: 'العربية',
    dir: 'rtl',
    sampleKey: 'onboarding.language.sampleAr',
    testId: 'locale-ar',
  },
  {
    code: 'he',
    nativeLabel: 'עברית',
    dir: 'rtl',
    sampleKey: 'onboarding.language.sampleHe',
    testId: 'locale-he',
  },
]

// Filter the locale options through the Hebrew gate
const LOCALES = computed<LocaleOption[]>(() => {
  const allowedCodes = new Set(availableLocales.value.map(l => l.code))

  return ALL_LOCALES.filter(opt => allowedCodes.has(opt.code))
})

function select(code: SupportedLocale) {
  emit('update:modelValue', code)
}
</script>

<template>
  <div
    class="language-picker"
    data-testid="language-picker"
  >
    <VCard
      v-for="opt in LOCALES"
      :key="opt.code"
      :variant="props.modelValue === opt.code ? 'flat' : 'outlined'"
      :color="props.modelValue === opt.code ? 'primary' : undefined"
      class="language-picker__tile pa-5 cursor-pointer"
      :data-testid="opt.testId"
      tabindex="0"
      role="button"
      :aria-pressed="props.modelValue === opt.code"
      @click="select(opt.code)"
      @keydown.enter="select(opt.code)"
      @keydown.space.prevent="select(opt.code)"
    >
      <div class="d-flex align-center">
        <VIcon
          icon="tabler-language"
          size="24"
          class="me-3"
          aria-hidden="true"
        />
        <div class="text-subtitle-1 font-weight-medium">
          {{ opt.nativeLabel }}
        </div>
      </div>
      <div
        class="text-body-2 text-medium-emphasis mt-2"
        :dir="opt.dir"
      >
        {{ t(opt.sampleKey) }}
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.language-picker {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.75rem;
}

.language-picker__tile {
  transition: transform 0.15s ease-out, border-color 0.15s ease-out;
}

.language-picker__tile:hover {
  transform: translateY(-2px);
}

.language-picker__tile:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}
</style>
