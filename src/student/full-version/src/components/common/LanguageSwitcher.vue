<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLocale } from 'vuetify'
import type { LocaleDescriptor } from '@/composables/useAvailableLocales'
import { useAvailableLocales } from '@/composables/useAvailableLocales'

const STORAGE_KEY = 'cena-student-locale'

const { locale: i18nLocale, t } = useI18n()
const vuetifyLocale = useLocale()

// FIND-ux-014: honor the Hebrew gate. `locales` already filters he out
// when VITE_ENABLE_HEBREW is not set to 'true'.
const { locales: availableLocales, hebrewEnabled } = useAvailableLocales()

// Track what the i18n runtime currently thinks is active. Falls back to
// 'en' if for some reason the runtime is initialized to a hidden locale
// (stale localStorage, build-time flag flip).
const selected = ref<LocaleDescriptor['code']>(
  normalize(i18nLocale.value as LocaleDescriptor['code']),
)

function normalize(code: LocaleDescriptor['code']): LocaleDescriptor['code'] {
  // If the current build hides Hebrew but the user previously picked it,
  // gracefully fall back to English. Crash-free by design.
  if (code === 'he' && !hebrewEnabled)
    return 'en'

  return code
}

function applyLocale(code: LocaleDescriptor['code']) {
  const normalized = normalize(code)
  const found = availableLocales.value.find(l => l.code === normalized)
  if (!found)
    return

  i18nLocale.value = normalized

  // Vuetify's locale adapter already knows ar + he are RTL, so flipping
  // `current` auto-derives `isRtl`. Keeping both in sync avoids the
  // double-source-of-truth trap.
  vuetifyLocale.current.value = normalized

  if (typeof document !== 'undefined') {
    document.documentElement.lang = normalized
    document.documentElement.dir = found.dir
  }

  if (typeof localStorage !== 'undefined')
    localStorage.setItem(STORAGE_KEY, normalized)
}

watch(selected, next => {
  applyLocale(next)
})

onMounted(() => {
  if (typeof localStorage === 'undefined') {
    applyLocale(selected.value)

    return
  }
  const stored = localStorage.getItem(STORAGE_KEY) as LocaleDescriptor['code'] | null
  if (stored && availableLocales.value.some(l => l.code === stored)) {
    selected.value = stored
    applyLocale(stored)
  }
  else if (stored === 'he' && !hebrewEnabled) {
    // Graceful fallback: user was on Hebrew but the build no longer
    // exposes it. Switch to English without crashing and update the
    // persisted locale so we don't keep trying on every reload.
    selected.value = 'en'
    applyLocale('en')
  }
  else {
    applyLocale(selected.value)
  }
})

const currentLabel = computed(() =>
  availableLocales.value.find(l => l.code === selected.value)?.label
  ?? 'English',
)

defineExpose({ selected, applyLocale, hebrewEnabled })
</script>

<template>
  <VMenu close-on-content-click>
    <template #activator="{ props: activator }">
      <VBtn
        v-bind="activator"
        variant="text"
        prepend-icon="tabler-language"
        :aria-label="t('language.switchLanguage')"
      >
        {{ currentLabel }}
      </VBtn>
    </template>
    <VList
      density="compact"
      role="listbox"
      :aria-label="t('language.switchLanguage')"
    >
      <VListItem
        v-for="option in availableLocales"
        :key="option.code"
        :active="selected === option.code"
        role="option"
        :aria-selected="selected === option.code"
        @click="selected = option.code"
      >
        <VListItemTitle>{{ option.label }}</VListItemTitle>
      </VListItem>
    </VList>
  </VMenu>
</template>
