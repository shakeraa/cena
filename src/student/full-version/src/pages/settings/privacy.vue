<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settingsPrivacy',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const prefs = ref({
  showProgressToClass: true,
  allowPeerComparison: true,
  shareAnalytics: false,
})

if (typeof localStorage !== 'undefined') {
  const stored = localStorage.getItem('cena-privacy-prefs')
  if (stored) {
    try {
      Object.assign(prefs.value, JSON.parse(stored))
    }
    catch {
      // ignore
    }
  }
}

function persist() {
  if (typeof localStorage !== 'undefined')
    localStorage.setItem('cena-privacy-prefs', JSON.stringify(prefs.value))
}
</script>

<template>
  <div
    class="settings-privacy-page pa-4"
    data-testid="settings-privacy-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.privacy.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.privacy.subtitle') }}
    </p>

    <VCard
      variant="outlined"
      class="pa-5"
    >
      <VSwitch
        v-model="prefs.showProgressToClass"
        :label="t('settingsPage.privacy.showProgressToClass')"
        color="primary"
        data-testid="privacy-show-progress"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.allowPeerComparison"
        :label="t('settingsPage.privacy.allowPeerComparison')"
        color="primary"
        data-testid="privacy-peer-comparison"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.shareAnalytics"
        :label="t('settingsPage.privacy.shareAnalytics')"
        color="primary"
        data-testid="privacy-analytics"
        @update:model-value="persist"
      />
    </VCard>
  </div>
</template>

<style scoped>
.settings-privacy-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
