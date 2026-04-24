<script setup lang="ts">
import { $api } from '@/utils/api'
import { useAbility } from '@casl/vue'

definePage({ meta: { action: 'manage', subject: 'Settings' } })

const { can } = useAbility()
const isSuperAdmin = computed(() => can('manage', 'all'))

interface FeatureFlag {
  key: string
  label: string
  enabled: boolean
  description: string
}

interface FocusEngineConfig {
  degradationThresholdMs: number
  microbreakIntervalMs: number
  microbreakDurationMs: number
  mindWanderingThreshold: number
}

interface MasteryEngineConfig {
  masteryThreshold: number
  nearMasteryThreshold: number
  decayRatePerDay: number
  minItemsForMastery: number
}

interface OrgSettings {
  organizationName: string
  logoUrl: string
  timezone: string
  defaultLanguage: string
}

interface SettingsResponse {
  organization: OrgSettings
  featureFlags: FeatureFlag[]
  focusEngine: FocusEngineConfig
  masteryEngine: MasteryEngineConfig
}

const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const saveSuccess = ref(false)

// Reseed database (SUPER_ADMIN only)
const reseeding = ref(false)
const reseedSuccess = ref(false)
const reseedConfirmDialog = ref(false)

const reseedDatabase = async () => {
  reseedConfirmDialog.value = false
  reseeding.value = true
  reseedSuccess.value = false
  try {
    await $api('/admin/system/reseed', { method: 'POST' })
    reseedSuccess.value = true
    setTimeout(() => { reseedSuccess.value = false }, 5000)
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to reseed database'
  }
  finally {
    reseeding.value = false
  }
}

const organization = ref<OrgSettings>({
  organizationName: '',
  logoUrl: '',
  timezone: 'UTC',
  defaultLanguage: 'en',
})

const featureFlags = ref<FeatureFlag[]>([])

const focusEngine = ref<FocusEngineConfig>({
  degradationThresholdMs: 1200000,
  microbreakIntervalMs: 1500000,
  microbreakDurationMs: 120000,
  mindWanderingThreshold: 0.65,
})

const masteryEngine = ref<MasteryEngineConfig>({
  masteryThreshold: 0.85,
  nearMasteryThreshold: 0.6,
  decayRatePerDay: 0.02,
  minItemsForMastery: 5,
})

const timezoneOptions = [
  'UTC',
  'Asia/Dubai',
  'Asia/Riyadh',
  'Asia/Kuwait',
  'Asia/Bahrain',
  'Asia/Qatar',
  'Africa/Cairo',
  'Europe/London',
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
]

const languageOptions = [
  { title: 'English', value: 'en' },
  { title: 'Arabic', value: 'ar' },
  { title: 'French', value: 'fr' },
]

const fetchSettings = async () => {
  loading.value = true
  try {
    const data = await $api<SettingsResponse>('/admin/system/settings')

    if (data.organization) {
      organization.value = {
        organizationName: data.organization.organizationName ?? '',
        logoUrl: data.organization.logoUrl ?? '',
        timezone: data.organization.timezone ?? 'UTC',
        defaultLanguage: data.organization.defaultLanguage ?? 'en',
      }
    }

    if (data.featureFlags)
      featureFlags.value = data.featureFlags

    if (data.focusEngine) {
      focusEngine.value = {
        degradationThresholdMs: data.focusEngine.degradationThresholdMs ?? 1200000,
        microbreakIntervalMs: data.focusEngine.microbreakIntervalMs ?? 1500000,
        microbreakDurationMs: data.focusEngine.microbreakDurationMs ?? 120000,
        mindWanderingThreshold: data.focusEngine.mindWanderingThreshold ?? 0.65,
      }
    }

    if (data.masteryEngine) {
      masteryEngine.value = {
        masteryThreshold: data.masteryEngine.masteryThreshold ?? 0.85,
        nearMasteryThreshold: data.masteryEngine.nearMasteryThreshold ?? 0.6,
        decayRatePerDay: data.masteryEngine.decayRatePerDay ?? 0.02,
        minItemsForMastery: data.masteryEngine.minItemsForMastery ?? 5,
      }
    }

    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch settings:', err)
    error.value = err.message ?? 'Failed to load settings'
  }
  finally {
    loading.value = false
  }
}

const saveSettings = async () => {
  saving.value = true
  saveSuccess.value = false
  try {
    await $api('/admin/system/settings', {
      method: 'PUT',
      body: {
        organization: organization.value,
        featureFlags: featureFlags.value,
        focusEngine: focusEngine.value,
        masteryEngine: masteryEngine.value,
      },
    })

    saveSuccess.value = true
    error.value = null
    setTimeout(() => { saveSuccess.value = false }, 3000)
  }
  catch (err: any) {
    console.error('Failed to save settings:', err)
    error.value = err.message ?? 'Failed to save settings'
  }
  finally {
    saving.value = false
  }
}

onMounted(fetchSettings)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          System Settings
        </h4>
        <div class="text-body-1">
          Manage organization, feature flags, and engine configuration
        </div>
      </div>

      <VBtn
        color="primary"
        :loading="saving"
        :disabled="loading"
        prepend-icon="tabler-device-floppy"
        @click="saveSettings"
      >
        Save All Settings
      </VBtn>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <VAlert
      v-if="saveSuccess"
      type="success"
      variant="tonal"
      class="mb-6"
    >
      Settings saved successfully
    </VAlert>

    <VRow>
      <!-- Organization Settings -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="primary"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-building" />
              </VAvatar>
            </template>
            <VCardTitle>Organization</VCardTitle>
            <VCardSubtitle>General organization settings</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VRow>
              <VCol cols="12">
                <AppTextField
                  v-model="organization.organizationName"
                  label="Organization Name"
                  placeholder="Enter organization name"
                />
              </VCol>
              <VCol cols="12">
                <AppTextField
                  v-model="organization.logoUrl"
                  label="Logo URL"
                  placeholder="https://example.com/logo.png"
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppSelect
                  v-model="organization.timezone"
                  :items="timezoneOptions"
                  label="Timezone"
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppSelect
                  v-model="organization.defaultLanguage"
                  :items="languageOptions"
                  label="Default Language"
                />
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Feature Flags -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="info"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-toggle-right" />
              </VAvatar>
            </template>
            <VCardTitle>Feature Flags</VCardTitle>
            <VCardSubtitle>Enable or disable platform features</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VList
              v-if="featureFlags.length"
              density="compact"
            >
              <VListItem
                v-for="flag in featureFlags"
                :key="flag.key"
              >
                <template #prepend>
                  <VSwitch
                    v-model="flag.enabled"
                    :aria-label="flag.label"
                    class="me-4"
                    hide-details
                    density="compact"
                  />
                </template>
                <VListItemTitle class="text-body-1 font-weight-medium">
                  {{ flag.label }}
                </VListItemTitle>
                <VListItemSubtitle class="text-body-2">
                  {{ flag.description }}
                </VListItemSubtitle>
              </VListItem>
            </VList>
            <div
              v-else-if="!loading"
              class="text-body-2 text-disabled text-center py-6"
            >
              No feature flags configured
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Focus Engine Config -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="warning"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-brain" />
              </VAvatar>
            </template>
            <VCardTitle>Focus Engine</VCardTitle>
            <VCardSubtitle>Degradation and microbreak parameters</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VRow>
              <VCol cols="12">
                <AppTextField
                  v-model.number="focusEngine.degradationThresholdMs"
                  label="Degradation Threshold (ms)"
                  type="number"
                  :min="0"
                  hint="Time in milliseconds before focus degradation is triggered"
                  persistent-hint
                />
              </VCol>
              <VCol cols="12">
                <AppTextField
                  v-model.number="focusEngine.microbreakIntervalMs"
                  label="Microbreak Interval (ms)"
                  type="number"
                  :min="0"
                  hint="Time between microbreak prompts"
                  persistent-hint
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="focusEngine.microbreakDurationMs"
                  label="Microbreak Duration (ms)"
                  type="number"
                  :min="0"
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="focusEngine.mindWanderingThreshold"
                  label="Mind Wandering Threshold"
                  type="number"
                  :min="0"
                  :max="1"
                  :step="0.01"
                  hint="0.0 - 1.0 probability threshold"
                  persistent-hint
                />
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Mastery Engine Config -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="loading">
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="success"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-chart-dots-3" />
              </VAvatar>
            </template>
            <VCardTitle>Mastery Engine</VCardTitle>
            <VCardSubtitle>Mastery thresholds and decay configuration</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VRow>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="masteryEngine.masteryThreshold"
                  label="Mastery Threshold"
                  type="number"
                  :min="0"
                  :max="1"
                  :step="0.01"
                  hint="Probability for full mastery (e.g. 0.85)"
                  persistent-hint
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="masteryEngine.nearMasteryThreshold"
                  label="Near-Mastery Threshold"
                  type="number"
                  :min="0"
                  :max="1"
                  :step="0.01"
                  hint="Probability for near-mastery (e.g. 0.60)"
                  persistent-hint
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="masteryEngine.decayRatePerDay"
                  label="Decay Rate / Day"
                  type="number"
                  :min="0"
                  :max="1"
                  :step="0.001"
                  hint="Daily mastery decay factor"
                  persistent-hint
                />
              </VCol>
              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="masteryEngine.minItemsForMastery"
                  label="Min Items for Mastery"
                  type="number"
                  :min="1"
                  hint="Minimum responses before mastery can be declared"
                  persistent-hint
                />
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Database Management (SUPER_ADMIN only) -->
    <VRow
      v-if="isSuperAdmin"
      class="mt-6"
    >
      <VCol cols="12">
        <VCard>
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="error"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-database-cog" />
              </VAvatar>
            </template>
            <VCardTitle>Database Management</VCardTitle>
            <VCardSubtitle>Seed and reset development data (Super Admin only)</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="reseedSuccess"
              type="success"
              variant="tonal"
              class="mb-4"
            >
              Database reseeded successfully. Roles, users, simulated students, and questions have been refreshed.
            </VAlert>

            <div class="d-flex align-center gap-4 flex-wrap">
              <div class="flex-grow-1">
                <div class="text-body-1 font-weight-medium mb-1">
                  Reseed Database
                </div>
                <div class="text-body-2 text-medium-emphasis">
                  Re-populate all demo data: 6 roles, 24 staff users, 100 simulated students (8 archetypes, 60-day history), and 15 Bagrut questions. This is idempotent and safe to run multiple times.
                </div>
              </div>

              <VBtn
                color="warning"
                variant="elevated"
                :loading="reseeding"
                prepend-icon="tabler-database-import"
                @click="reseedConfirmDialog = true"
              >
                Reseed Now
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Reseed Confirmation Dialog -->
    <VDialog
      v-model="reseedConfirmDialog"
      max-width="450"
    >
      <VCard>
        <VCardTitle class="text-h5 pa-6">
          Confirm Database Reseed
        </VCardTitle>
        <VCardText>
          This will re-run all seed data scripts. Existing demo data will be updated (upsert).
          Real user data will not be affected.
        </VCardText>
        <VCardActions class="pa-6 pt-0">
          <VSpacer />
          <VBtn
            variant="text"
            @click="reseedConfirmDialog = false"
          >
            Cancel
          </VBtn>
          <VBtn
            color="warning"
            variant="elevated"
            @click="reseedDatabase"
          >
            Reseed
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>
</template>
