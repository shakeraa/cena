<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'

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

// ---- Types matching backend SettingsDto.Notifications ----
interface NotificationSettings {
  emailNotifications: boolean
  pushNotifications: boolean
  dailyReminder: boolean
  dailyReminderTime: string | null
  weeklyProgress: boolean
  streakAlerts: boolean
  newContentAlerts: boolean
}

interface SettingsDto {
  appearance: unknown
  notifications: NotificationSettings
  privacy: unknown
  learning: unknown
  homeLayout: unknown
}

interface SettingsPatch {
  notifications: Partial<NotificationSettings>
}

// FIND-privacy-010: ICO Children's Code Std 3+7 — all notification defaults OFF
// (high-privacy by default for minors; Std 13 prohibits default-on engagement nudges)
const prefs = ref<NotificationSettings>({
  emailNotifications: false,
  pushNotifications: false,
  dailyReminder: false,
  dailyReminderTime: null,
  weeklyProgress: false,
  streakAlerts: false,
  newContentAlerts: false,
})

// Seed from localStorage as a fast cache while the API call resolves
if (typeof localStorage !== 'undefined') {
  const stored = localStorage.getItem('cena-notification-prefs')
  if (stored) {
    try {
      Object.assign(prefs.value, JSON.parse(stored))
    }
    catch {
      // ignore corrupt cache
    }
  }
}

/** Write current prefs to localStorage as a client-side cache. */
function cacheToStorage() {
  if (typeof localStorage !== 'undefined')
    localStorage.setItem('cena-notification-prefs', JSON.stringify(prefs.value))
}

// ---- Load from server (source of truth) ----
const { data: settingsData, loading: settingsLoading } = useApiQuery<SettingsDto>('/api/me/settings')

// When server settings arrive, overwrite local state + cache
watch(settingsData, (val) => {
  if (val?.notifications) {
    Object.assign(prefs.value, val.notifications)
    cacheToStorage()
  }
})

// ---- Save to server on each toggle ----
const settingsMutation = useApiMutation<void, SettingsPatch>('/api/me/settings', 'PATCH')

const saveError = ref(false)
/** Tracks the pref key currently being saved, for optimistic revert. */
let pendingSaveKey: keyof NotificationSettings | null = null

async function persistToggle(key: keyof NotificationSettings) {
  // Optimistic: the UI already shows the new value via v-model.
  // Cache optimistically so refresh in the same tab stays consistent.
  cacheToStorage()
  pendingSaveKey = key

  try {
    await settingsMutation.execute({
      notifications: { [key]: prefs.value[key] },
    })
  }
  catch {
    // FIND-ux-032: structured log so production monitoring detects regressions.
    console.error('[FIND-ux-032]', JSON.stringify({
      event: 'notification_pref_save_failed',
      key,
      value: prefs.value[key],
    }))

    // Revert the toggle on failure
    ;(prefs.value as Record<string, unknown>)[key] = !prefs.value[key]
    cacheToStorage()
    saveError.value = true
  }
  finally {
    pendingSaveKey = null
  }
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
        :loading="settingsLoading"
        data-testid="pref-email"
        @update:model-value="persistToggle('emailNotifications')"
      />
      <VSwitch
        v-model="prefs.pushNotifications"
        :label="t('settingsPage.notifications.push')"
        color="primary"
        :loading="settingsLoading"
        data-testid="pref-push"
        @update:model-value="persistToggle('pushNotifications')"
      />
      <VSwitch
        v-model="prefs.dailyReminder"
        :label="t('settingsPage.notifications.dailyReminder')"
        color="primary"
        :loading="settingsLoading"
        data-testid="pref-daily"
        @update:model-value="persistToggle('dailyReminder')"
      />
      <VSwitch
        v-model="prefs.weeklyProgress"
        :label="t('settingsPage.notifications.weekly')"
        color="primary"
        :loading="settingsLoading"
        data-testid="pref-weekly"
        @update:model-value="persistToggle('weeklyProgress')"
      />
      <VSwitch
        v-model="prefs.streakAlerts"
        :label="t('settingsPage.notifications.streakAlerts')"
        color="primary"
        :loading="settingsLoading"
        data-testid="pref-streak"
        @update:model-value="persistToggle('streakAlerts')"
      />
      <VSwitch
        v-model="prefs.newContentAlerts"
        :label="t('settingsPage.notifications.newContent')"
        color="primary"
        :loading="settingsLoading"
        data-testid="pref-content"
        @update:model-value="persistToggle('newContentAlerts')"
      />
    </VCard>

    <VSnackbar
      v-model="saveError"
      color="error"
      :timeout="4000"
      data-testid="notif-save-error-snackbar"
    >
      {{ t('settingsPage.notifications.saveError') }}
    </VSnackbar>
  </div>
</template>

<style scoped>
.settings-notif-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
