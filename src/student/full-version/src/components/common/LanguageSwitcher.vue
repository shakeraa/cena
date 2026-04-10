<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLocale } from 'vuetify'

interface LocaleOption {
  code: 'en' | 'ar' | 'he'
  label: string
  dir: 'ltr' | 'rtl'
}

const LOCALES: LocaleOption[] = [
  { code: 'en', label: 'English', dir: 'ltr' },
  { code: 'ar', label: 'العربية', dir: 'rtl' },
  { code: 'he', label: 'עברית', dir: 'rtl' },
]

const STORAGE_KEY = 'cena-student-locale'

const { locale: i18nLocale, t } = useI18n()
const vuetifyLocale = useLocale()

const selected = ref<LocaleOption['code']>(i18nLocale.value as LocaleOption['code'])

function applyLocale(code: LocaleOption['code']) {
  const found = LOCALES.find(l => l.code === code)
  if (!found)
    return

  i18nLocale.value = code

  // vuetify's locale adapter already knows ar + he are RTL, so flipping
  // `current` auto-derives `isRtl`. Keeping both in sync avoids the
  // double-source-of-truth trap.
  vuetifyLocale.current.value = code

  if (typeof document !== 'undefined') {
    document.documentElement.lang = code
    document.documentElement.dir = found.dir
  }

  if (typeof localStorage !== 'undefined')
    localStorage.setItem(STORAGE_KEY, code)
}

watch(selected, next => {
  applyLocale(next)
})

onMounted(() => {
  if (typeof localStorage === 'undefined')
    return
  const stored = localStorage.getItem(STORAGE_KEY) as LocaleOption['code'] | null
  if (stored && LOCALES.some(l => l.code === stored)) {
    selected.value = stored
    applyLocale(stored)
  }
  else {
    applyLocale(selected.value)
  }
})

defineExpose({ selected, applyLocale })
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
        {{ LOCALES.find(l => l.code === selected)?.label }}
      </VBtn>
    </template>
    <VList
      density="compact"
      role="listbox"
      :aria-label="t('language.switchLanguage')"
    >
      <VListItem
        v-for="option in LOCALES"
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
