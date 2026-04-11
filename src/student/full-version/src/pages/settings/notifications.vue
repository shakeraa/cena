<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settingsNotifications',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

// Phase A: local toggles persist to localStorage only.
// STU-W-14b will wire /api/me/settings when STB-00b settings writes land.
const prefs = ref({
  emailNotifications: true,
  pushNotifications: false,
  dailyReminder: true,
  weeklyProgress: true,
  streakAlerts: true,
  newContentAlerts: false,
})

// Seed from localStorage
if (typeof localStorage !== 'undefined') {
  const stored = localStorage.getItem('cena-notification-prefs')
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
    localStorage.setItem('cena-notification-prefs', JSON.stringify(prefs.value))
}
</script>

<template>
  <div
    class="settings-notif-page pa-4"
    data-testid="settings-notifications-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.notifications.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.notifications.subtitle') }}
    </p>

    <VCard
      variant="outlined"
      class="pa-5"
    >
      <VSwitch
        v-model="prefs.emailNotifications"
        :label="t('settingsPage.notifications.email')"
        color="primary"
        data-testid="pref-email"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.pushNotifications"
        :label="t('settingsPage.notifications.push')"
        color="primary"
        data-testid="pref-push"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.dailyReminder"
        :label="t('settingsPage.notifications.dailyReminder')"
        color="primary"
        data-testid="pref-daily"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.weeklyProgress"
        :label="t('settingsPage.notifications.weekly')"
        color="primary"
        data-testid="pref-weekly"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.streakAlerts"
        :label="t('settingsPage.notifications.streakAlerts')"
        color="primary"
        data-testid="pref-streak"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.newContentAlerts"
        :label="t('settingsPage.notifications.newContent')"
        color="primary"
        data-testid="pref-content"
        @update:model-value="persist"
      />
    </VCard>
  </div>
</template>

<style scoped>
.settings-notif-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
