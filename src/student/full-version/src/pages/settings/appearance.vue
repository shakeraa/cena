<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTheme } from 'vuetify'
import LanguageSwitcher from '@/components/common/LanguageSwitcher.vue'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settingsAppearance',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const vuetifyTheme = useTheme()

const selectedTheme = ref<'light' | 'dark'>(vuetifyTheme.global.name.value as 'light' | 'dark')

function setTheme(mode: 'light' | 'dark') {
  selectedTheme.value = mode
  vuetifyTheme.global.name.value = mode
  if (typeof localStorage !== 'undefined')
    localStorage.setItem('cena-student-theme', mode)
}
</script>

<template>
  <div
    class="settings-appearance-page pa-4"
    data-testid="settings-appearance-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.appearance.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.appearance.subtitle') }}
    </p>

    <VCard
      variant="outlined"
      class="pa-5 mb-4"
      data-testid="theme-picker"
    >
      <div class="text-h6 mb-3">
        {{ t('settingsPage.appearance.themeLabel') }}
      </div>
      <div class="d-flex ga-3">
        <VCard
          :variant="selectedTheme === 'light' ? 'flat' : 'outlined'"
          :color="selectedTheme === 'light' ? 'primary' : undefined"
          class="pa-4 cursor-pointer flex-grow-1 text-center"
          data-testid="theme-light"
          @click="setTheme('light')"
        >
          <VIcon
            icon="tabler-sun"
            size="32"
            aria-hidden="true"
          />
          <div class="text-subtitle-1 mt-2">
            {{ t('settingsPage.appearance.themeLight') }}
          </div>
        </VCard>
        <VCard
          :variant="selectedTheme === 'dark' ? 'flat' : 'outlined'"
          :color="selectedTheme === 'dark' ? 'primary' : undefined"
          class="pa-4 cursor-pointer flex-grow-1 text-center"
          data-testid="theme-dark"
          @click="setTheme('dark')"
        >
          <VIcon
            icon="tabler-moon"
            size="32"
            aria-hidden="true"
          />
          <div class="text-subtitle-1 mt-2">
            {{ t('settingsPage.appearance.themeDark') }}
          </div>
        </VCard>
      </div>
    </VCard>

    <VCard
      variant="outlined"
      class="pa-5"
      data-testid="language-section"
    >
      <div class="text-h6 mb-3">
        {{ t('settingsPage.appearance.languageLabel') }}
      </div>
      <LanguageSwitcher />
    </VCard>
  </div>
</template>

<style scoped>
.settings-appearance-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
