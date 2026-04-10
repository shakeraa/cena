<script setup lang="ts">
import { useTheme } from 'vuetify'
import { useI18n } from 'vue-i18n'

const theme = useTheme()
const { locale } = useI18n()

const toggleTheme = () => {
  theme.global.name.value = theme.global.current.value.dark ? 'light' : 'dark'
}

const locales = ['en', 'ar', 'he']
</script>

<template>
  <div class="d-flex align-center justify-center pa-8" style="min-height: 100vh">
    <VCard max-width="600" class="pa-6">
      <VCardTitle class="text-h4">Hello student</VCardTitle>
      <VCardText>
        <p>Cena — Student Web scaffold (STU-W-00). Placeholder page.</p>
        <p class="mt-4">Theme: {{ theme.global.name.value }}</p>
        <p>Locale: {{ locale }}</p>
      </VCardText>
      <VCardActions class="d-flex gap-2">
        <VBtn @click="toggleTheme">Toggle theme</VBtn>
        <VSelect v-model="locale" :items="locales" label="Language" density="compact" hide-details style="max-width: 200px" />
      </VCardActions>
    </VCard>
  </div>
</template>
