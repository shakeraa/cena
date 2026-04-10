<script setup lang="ts">
import { useTheme } from 'vuetify'
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'blank',
  },
})

const theme = useTheme()
const { t } = useI18n()

const toggleTheme = () => {
  theme.global.name.value = theme.global.current.value.dark ? 'light' : 'dark'
}
</script>

<template>
  <div
    class="d-flex align-center justify-center pa-8"
    style="min-height: 100vh"
  >
    <VCard
      max-width="640"
      class="pa-6"
    >
      <VCardTitle class="text-h4 mb-2">
        Cena — Student Web
      </VCardTitle>
      <VCardText>
        <p class="mb-2">
          STU-W-01 design system chassis. Theme, tokens, locales, and shared components wired up.
        </p>
        <div class="d-flex align-center gap-2 mb-4">
          <span class="text-body-2 text-medium-emphasis">Theme:</span>
          <VChip
            size="small"
            color="primary"
            variant="flat"
          >
            {{ theme.global.name.value }}
          </VChip>
        </div>
        <div class="d-flex gap-3 flex-wrap">
          <StudentEmptyState
            :title="t('empty.noSessions')"
            :subtitle="t('empty.noSessionsSubtitle')"
            icon="tabler-books"
          >
            <template #actions>
              <VBtn
                color="primary"
                data-testid="index-empty-cta"
              >
                {{ t('common.save') }}
              </VBtn>
            </template>
          </StudentEmptyState>
        </div>
      </VCardText>
      <VCardActions class="d-flex gap-2 flex-wrap">
        <VBtn
          data-testid="index-toggle-theme"
          @click="toggleTheme"
        >
          Toggle theme
        </VBtn>
        <LanguageSwitcher />
        <VBtn
          variant="text"
          to="/_dev/design-system"
        >
          Design system
        </VBtn>
        <VBtn
          variant="text"
          to="/_dev/flow-states"
        >
          Flow states
        </VBtn>
      </VCardActions>
    </VCard>
  </div>
</template>
