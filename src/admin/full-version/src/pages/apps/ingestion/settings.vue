<script setup lang="ts">
import CloudDirectoriesPanel from '@/views/apps/ingestion/settings/CloudDirectoriesPanel.vue'
import EmailIngestionPanel from '@/views/apps/ingestion/settings/EmailIngestionPanel.vue'
import MessagingChannelsPanel from '@/views/apps/ingestion/settings/MessagingChannelsPanel.vue'
import PipelineSettingsPanel from '@/views/apps/ingestion/settings/PipelineSettingsPanel.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'manage',
    subject: 'Content',
  },
})

interface IngestionSettings {
  id: string
  cloudDirectories: any[]
  email: any | null
  messagingChannels: any[]
  pipeline: {
    maxConcurrentIngestions: number
    maxFileSizeMb: number
    autoClassify: boolean
    autoDedup: boolean
    minQualityScore: number
    allowedFileTypes: string[]
    defaultLanguage: string
    defaultSubject: string
  }
  updatedAt: string
  updatedBy: string
}

const loading = ref(true)
const saving = ref(false)
const saveSuccess = ref(false)
const error = ref<string | null>(null)
const testingDir = ref(false)
const testingEmail = ref(false)
const dirTestResult = ref<boolean | null>(null)
const emailTestResult = ref<boolean | null>(null)

const openPanels = ref([0])

const defaultPipeline = () => ({
  maxConcurrentIngestions: 5,
  maxFileSizeMb: 20,
  autoClassify: true,
  autoDedup: true,
  minQualityScore: 0.6,
  allowedFileTypes: ['pdf', 'png', 'jpg', 'jpeg', 'webp', 'csv', 'xlsx'],
  defaultLanguage: 'he',
  defaultSubject: 'math',
})

const settings = ref<IngestionSettings>({
  id: 'ingestion-settings-singleton',
  cloudDirectories: [],
  email: null,
  messagingChannels: [],
  pipeline: defaultPipeline(),
  updatedAt: '',
  updatedBy: '',
})

const fetchSettings = async () => {
  loading.value = true
  try {
    const data = await $api<IngestionSettings>('/admin/ingestion-settings')
    settings.value = {
      ...data,
      pipeline: { ...defaultPipeline(), ...data.pipeline },
    }
    error.value = null
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load ingestion settings'
  }
  finally {
    loading.value = false
  }
}

const saveSettings = async () => {
  saving.value = true
  saveSuccess.value = false
  try {
    const updated = await $api<IngestionSettings>('/admin/ingestion-settings', {
      method: 'PUT',
      body: settings.value,
    })
    settings.value = {
      ...updated,
      pipeline: { ...defaultPipeline(), ...updated.pipeline },
    }
    saveSuccess.value = true
    error.value = null
    setTimeout(() => { saveSuccess.value = false }, 3000)
  }
  catch (err: any) {
    error.value = err.data?.error ?? err.message ?? 'Failed to save settings'
  }
  finally {
    saving.value = false
  }
}

const testCloudDir = async (dir: any) => {
  testingDir.value = true
  dirTestResult.value = null
  try {
    const data = await $api<{ connected: boolean }>('/admin/ingestion-settings/test-cloud-dir', {
      method: 'POST',
      body: dir,
    })
    dirTestResult.value = data.connected
  }
  catch {
    dirTestResult.value = false
  }
  finally {
    testingDir.value = false
  }
}

const testEmail = async (config: any) => {
  testingEmail.value = true
  emailTestResult.value = null
  try {
    const data = await $api<{ connected: boolean }>('/admin/ingestion-settings/test-email', {
      method: 'POST',
      body: config,
    })
    emailTestResult.value = data.connected
  }
  catch {
    emailTestResult.value = false
  }
  finally {
    testingEmail.value = false
  }
}

const lastSaved = computed(() => {
  if (!settings.value.updatedAt) return null
  const d = new Date(settings.value.updatedAt)
  return d.toLocaleString()
})

onMounted(fetchSettings)
</script>

<template>
  <section>
    <!-- Header Card -->
    <VCard class="mb-6">
      <VCardItem>
        <template #prepend>
          <VAvatar
            color="primary"
            variant="tonal"
            rounded
          >
            <VIcon icon="tabler-settings-automation" />
          </VAvatar>
        </template>
        <VCardTitle>Ingestion Settings</VCardTitle>
        <VCardSubtitle>
          Configure content sources, cloud directories, email ingestion, messaging channels, and pipeline defaults.
          <template v-if="lastSaved">
            <br>
            <span class="text-caption text-disabled">Last saved: {{ lastSaved }} by {{ settings.updatedBy }}</span>
          </template>
        </VCardSubtitle>
      </VCardItem>
    </VCard>

    <!-- Loading -->
    <div
      v-if="loading"
      class="d-flex justify-center py-16"
    >
      <VProgressCircular indeterminate />
    </div>

    <template v-else>
      <!-- Error alert -->
      <VAlert
        v-if="error"
        color="error"
        variant="tonal"
        class="mb-6"
        closable
        @click:close="error = null"
      >
        {{ error }}
      </VAlert>

      <!-- Success alert -->
      <VAlert
        v-if="saveSuccess"
        color="success"
        variant="tonal"
        class="mb-6"
      >
        Settings saved successfully.
      </VAlert>

      <!-- Dir test result -->
      <VAlert
        v-if="dirTestResult !== null"
        :color="dirTestResult ? 'success' : 'error'"
        variant="tonal"
        class="mb-4"
        closable
        @click:close="dirTestResult = null"
      >
        {{ dirTestResult ? 'Directory connection successful.' : 'Directory connection failed. Check path and permissions.' }}
      </VAlert>

      <!-- Expansion Panels -->
      <VExpansionPanels
        v-model="openPanels"
        multiple
      >
        <!-- Panel 1: Cloud Directories -->
        <VExpansionPanel value="0">
          <VExpansionPanelTitle>
            <VIcon
              icon="tabler-cloud-upload"
              class="me-3"
            />
            <span class="text-body-1 font-weight-semibold">Cloud Directories</span>
            <VSpacer />
            <VChip
              size="x-small"
              variant="tonal"
              class="me-2"
            >
              {{ settings.cloudDirectories.length }} configured
            </VChip>
          </VExpansionPanelTitle>
          <VExpansionPanelText>
            <CloudDirectoriesPanel
              v-model="settings.cloudDirectories"
              :testing="testingDir"
              @test-dir="testCloudDir"
            />
          </VExpansionPanelText>
        </VExpansionPanel>

        <!-- Panel 2: Email Ingestion -->
        <VExpansionPanel value="1">
          <VExpansionPanelTitle>
            <VIcon
              icon="tabler-mail"
              class="me-3"
            />
            <span class="text-body-1 font-weight-semibold">Email Ingestion</span>
            <VSpacer />
            <VChip
              :color="settings.email?.enabled ? 'success' : 'default'"
              size="x-small"
              variant="tonal"
              class="me-2"
            >
              {{ settings.email?.enabled ? 'Enabled' : 'Disabled' }}
            </VChip>
          </VExpansionPanelTitle>
          <VExpansionPanelText>
            <EmailIngestionPanel
              v-model="settings.email"
              :testing="testingEmail"
              :test-result="emailTestResult"
              @test-email="testEmail"
            />
          </VExpansionPanelText>
        </VExpansionPanel>

        <!-- Panel 3: Messaging Channels -->
        <VExpansionPanel value="2">
          <VExpansionPanelTitle>
            <VIcon
              icon="tabler-message-circle"
              class="me-3"
            />
            <span class="text-body-1 font-weight-semibold">Messaging Channels</span>
            <VSpacer />
            <VChip
              size="x-small"
              variant="tonal"
              class="me-2"
            >
              {{ settings.messagingChannels.length }} configured
            </VChip>
          </VExpansionPanelTitle>
          <VExpansionPanelText>
            <MessagingChannelsPanel v-model="settings.messagingChannels" />
          </VExpansionPanelText>
        </VExpansionPanel>

        <!-- Panel 4: Pipeline Settings -->
        <VExpansionPanel value="3">
          <VExpansionPanelTitle>
            <VIcon
              icon="tabler-adjustments"
              class="me-3"
            />
            <span class="text-body-1 font-weight-semibold">Pipeline Settings</span>
          </VExpansionPanelTitle>
          <VExpansionPanelText>
            <PipelineSettingsPanel v-model="settings.pipeline" />
          </VExpansionPanelText>
        </VExpansionPanel>
      </VExpansionPanels>

      <!-- Save Button -->
      <VBtn
        block
        color="primary"
        size="large"
        class="mt-6"
        :loading="saving"
        @click="saveSettings"
      >
        <VIcon
          icon="tabler-device-floppy"
          start
        />
        Save Settings
      </VBtn>
    </template>
  </section>
</template>
