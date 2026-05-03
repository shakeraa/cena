<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'manage', subject: 'Settings' } })

interface ProviderConfig {
  provider: string
  displayName: string
  isEnabled: boolean
  hasApiKey: boolean
  modelId: string
  temperature: number
  baseUrl?: string
}

interface AiSettings {
  activeProvider: string
  providers: ProviderConfig[]
  defaults: {
    defaultLanguage: string
    defaultBloomsLevel: number
    defaultGrade: string
    questionsPerBatch: number
    autoRunQualityGate: boolean
  }
}

const loading = ref(true)
const saving = ref(false)
const saveSuccess = ref(false)
const testingProvider = ref<string | null>(null)

interface TestConnectionResult {
  provider: string
  ok: boolean
  // Human-readable message from the probe ("Invalid API key", "Model 'X' not found...").
  error?: string
  // Stable category code from the probe (AUTH_FAILED, MODEL_NOT_FOUND, etc.).
  // Used to look up an actionable hint for the operator.
  category?: string
}
const testResult = ref<TestConnectionResult | null>(null)
const error = ref<string | null>(null)

// Operator-facing remediation hints keyed by the probe's category code.
// AnthropicConnectionProbe.Categorize is the source of truth for these codes.
const testConnectionHints: Record<string, string> = {
  AUTH_FAILED: 'The API key Anthropic received is not valid. Paste a fresh key above and click Test Connection (no need to Save first).',
  INSUFFICIENT_CREDITS: 'Your Anthropic account has insufficient credit balance. Top up at console.anthropic.com → Plans & Billing.',
  MODEL_NOT_FOUND: 'The selected model is not accessible to this API key. Pick a different model or contact Anthropic to enable it.',
  RATE_LIMITED: 'Anthropic is rate-limiting this key. Wait ~1 minute and retry.',
  UPSTREAM_ERROR: 'Anthropic returned a 5xx. Usually transient — retry in a minute. If it persists check status.anthropic.com.',
  INVALID_REQUEST: 'Anthropic rejected the request shape (model, payload, or headers). See the message above for the upstream reason.',
  NETWORK_UNREACHABLE: 'The backend cannot reach api.anthropic.com. Check container DNS / firewall / proxy.',
  TIMEOUT: 'The probe timed out. Anthropic may be slow — retry in a minute.',
  CONFIG_MISSING_KEY: 'No API key is configured. Enter a key above and click Save Settings.',
  UNSUPPORTED_PROVIDER: 'This provider is not yet supported by the backend.',
  UNEXPECTED_ERROR: 'Something unexpected went wrong. Check admin-api container logs for the stack trace.',
}

const settings = ref<AiSettings>({
  activeProvider: 'Anthropic',
  providers: [],
  defaults: {
    defaultLanguage: 'he',
    defaultBloomsLevel: 3,
    defaultGrade: '4 Units',
    questionsPerBatch: 5,
    autoRunQualityGate: true,
  },
})

// Per-provider API key inputs (not sent back from server for security)
// FIND-arch-005: only Anthropic is supported; secondary providers were removed
// because their server-side implementations were stubs that threw
// NotImplementedException.
const apiKeys = ref<Record<string, string>>({
  Anthropic: '',
})

const activeProviderConfig = computed(() =>
  settings.value.providers.find(p => p.provider === settings.value.activeProvider),
)

const providerIcons: Record<string, string> = {
  Anthropic: 'tabler-brand-react',
}

const modelOptions: Record<string, { title: string; value: string }[]> = {
  Anthropic: [
    { title: 'Claude Opus 4.6', value: 'claude-opus-4-6' },
    { title: 'Claude Sonnet 4.6', value: 'claude-sonnet-4-6' },
    { title: 'Claude Haiku 4.5', value: 'claude-haiku-4-5-20251001' },
  ],
}

const languageOptions = [
  { title: 'Hebrew', value: 'he' },
  { title: 'Arabic', value: 'ar' },
  { title: 'English', value: 'en' },
]

const gradeOptions = [
  { title: '3 Units', value: '3 Units' },
  { title: '4 Units', value: '4 Units' },
  { title: '5 Units', value: '5 Units' },
]

const bloomLevels = [
  { title: '1 - Remember', value: 1 },
  { title: '2 - Understand', value: 2 },
  { title: '3 - Apply', value: 3 },
  { title: '4 - Analyze', value: 4 },
  { title: '5 - Evaluate', value: 5 },
  { title: '6 - Create', value: 6 },
]

const fetchSettings = async () => {
  loading.value = true
  try {
    const data = await $api<AiSettings>('/admin/ai/settings')
    settings.value = data
    error.value = null
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load AI settings'
  }
  finally {
    loading.value = false
  }
}

const saveSettings = async () => {
  saving.value = true
  saveSuccess.value = false
  try {
    const activeKey = apiKeys.value[settings.value.activeProvider]
    const activeConfig = activeProviderConfig.value

    await $api('/admin/ai/settings', {
      method: 'PUT',
      body: {
        activeProvider: settings.value.activeProvider,
        apiKey: activeKey || undefined,
        modelId: activeConfig?.modelId,
        temperature: activeConfig?.temperature,
        baseUrl: activeConfig?.baseUrl || undefined,
        defaultLanguage: settings.value.defaults.defaultLanguage,
        defaultBloomsLevel: settings.value.defaults.defaultBloomsLevel,
        defaultGrade: settings.value.defaults.defaultGrade,
        questionsPerBatch: settings.value.defaults.questionsPerBatch,
        autoRunQualityGate: settings.value.defaults.autoRunQualityGate,
      },
    })

    saveSuccess.value = true
    error.value = null
    setTimeout(() => { saveSuccess.value = false }, 3000)
    await fetchSettings()
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to save settings'
  }
  finally {
    saving.value = false
  }
}

const testConnection = async (provider: string) => {
  testingProvider.value = provider
  testResult.value = null
  try {
    // Body is wrapped { provider, apiKey?, modelId? } — sending a raw string
    // makes ofetch send Content-Type: text/plain which the .NET binder rejects
    // with 415 before the probe ever runs. See TestConnectionRequest in
    // AiGenerationService.cs.
    //
    // Pass the typed key + selected model as overrides so the operator can
    // verify a fresh key BEFORE clicking Save Settings — otherwise Test
    // Connection would silently probe whatever cipher happens to be in
    // Marten, which may be stale or never-updated, producing AUTH_FAILED
    // even when the field-value key is fine. Backend treats both fields as
    // request-scope only; nothing is persisted from this call.
    const typedKey = apiKeys.value[provider]?.trim() || undefined
    const selectedModel = activeProviderConfig.value?.modelId || undefined

    const data = await $api<{ connected: boolean; error?: string; details?: string }>(
      '/admin/ai/test-connection',
      {
        method: 'POST',
        body: {
          provider,
          apiKey: typedKey,
          modelId: selectedModel,
        },
      },
    )

    testResult.value = {
      provider,
      ok: data.connected,
      error: data.error ?? undefined,
      category: data.details ?? undefined,
    }
  }
  catch (err: any) {
    // Surface server-side error envelope when present so transport-layer
    // failures (401, 415, 5xx) carry actionable detail instead of "Failed".
    testResult.value = {
      provider,
      ok: false,
      error: err?.data?.error ?? err?.data?.message ?? err?.message ?? 'Request failed',
      category: err?.data?.details ?? `HTTP_${err?.response?.status ?? 'ERROR'}`,
    }
  }
  finally {
    testingProvider.value = null
  }
}

// ── Per-task model overrides (2026-05-03 multi-model) ──────────────────────
// Loaded lazily when the Advanced accordion expands so a curator who never
// opens it pays no extra GET round-trip on settings load.

interface SupportedModel {
  modelId: string
  displayName: string
  inputUsdPerMtok: number
  outputUsdPerMtok: number
  tier: string
}

interface TaskModelRow {
  task: string
  currentModelId: string
  source: string
  isOverridden: boolean
  overrideModelId: string | null
  description: string | null
}

interface ModelOverridesResponse {
  supportedModels: SupportedModel[]
  tasks: TaskModelRow[]
  lastChangedBy: string | null
  lastChangedAt: string | null
  globalDefaultModelId: string | null
}

const overridesPanelExpanded = ref<string[]>([])
const overridesLoading = ref(false)
const overridesSaving = ref<Record<string, boolean>>({})
const overridesError = ref<string | null>(null)
const overridesData = ref<ModelOverridesResponse | null>(null)

const fetchOverrides = async () => {
  overridesLoading.value = true
  try {
    overridesData.value = await $api<ModelOverridesResponse>(
      '/admin/ai/settings/model-overrides',
    )
    overridesError.value = null
  }
  catch (err: any) {
    overridesError.value = err?.data?.error
      ?? err?.message
      ?? 'Failed to load per-task model overrides'
  }
  finally {
    overridesLoading.value = false
  }
}

// Build the per-row dropdown items so each option label includes the
// per-Mtok cost (curators see the cost impact at decision time).
const overrideDropdownItems = computed(() =>
  (overridesData.value?.supportedModels ?? []).map(m => ({
    title: `${m.displayName} — $${m.inputUsdPerMtok} input / $${m.outputUsdPerMtok} output per Mtok`,
    value: m.modelId,
  })),
)

const setTaskOverride = async (taskName: string, modelId: string | null) => {
  overridesSaving.value = { ...overridesSaving.value, [taskName]: true }
  // Optimistic update: flip the row immediately so the curator sees their
  // pick reflected without a GET round-trip; revert on error below.
  const previousRow = overridesData.value?.tasks.find(t => t.task === taskName)
  if (overridesData.value && previousRow) {
    overridesData.value = {
      ...overridesData.value,
      tasks: overridesData.value.tasks.map(t => t.task === taskName
        ? {
            ...t,
            currentModelId: modelId ?? '',
            isOverridden: modelId != null,
            overrideModelId: modelId,
            source: modelId != null ? 'override' : 'routing-config-task-default',
          }
        : t),
    }
  }
  try {
    await $api(`/admin/ai/settings/model-overrides/${encodeURIComponent(taskName)}`, {
      method: 'PUT',
      body: { modelId },
    })
    // Re-fetch so currentModelId / source / lastChangedBy reflect server truth.
    await fetchOverrides()
    overridesError.value = null
  }
  catch (err: any) {
    // Revert the optimistic flip.
    if (overridesData.value && previousRow) {
      overridesData.value = {
        ...overridesData.value,
        tasks: overridesData.value.tasks.map(t => t.task === taskName ? previousRow : t),
      }
    }
    overridesError.value = err?.data?.error
      ?? err?.message
      ?? `Failed to update override for ${taskName}`
  }
  finally {
    overridesSaving.value = { ...overridesSaving.value, [taskName]: false }
  }
}

const onOverrideDropdownChange = (taskName: string, value: string | null) => {
  setTaskOverride(taskName, value)
}

const onResetOverride = (taskName: string) => {
  setTaskOverride(taskName, null)
}

// Load the overrides panel data on first expand (lazy, per the comment above).
watch(overridesPanelExpanded, async (newVal) => {
  if (newVal.length > 0 && overridesData.value === null && !overridesLoading.value) {
    await fetchOverrides()
  }
})

onMounted(fetchSettings)
</script>

<template>
  <section>
    <!-- Header -->
    <VCard class="mb-6">
      <VCardItem>
        <template #prepend>
          <VAvatar
            color="primary"
            variant="tonal"
            rounded
          >
            <VIcon icon="tabler-sparkles" />
          </VAvatar>
        </template>
        <VCardTitle>AI Question Generation Settings</VCardTitle>
        <VCardSubtitle>Configure LLM providers, API keys, and generation defaults for AI-powered question creation.</VCardSubtitle>
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

      <VRow>
        <!-- Left column: Provider selection & config -->
        <VCol
          cols="12"
          md="8"
        >
          <!-- Active Provider -->
          <VCard class="mb-6">
            <VCardItem>
              <VCardTitle>AI Provider</VCardTitle>
              <VCardSubtitle>Select which LLM provider to use for question generation.</VCardSubtitle>
            </VCardItem>
            <VCardText>
              <VRow>
                <VCol
                  v-for="provider in settings.providers"
                  :key="provider.provider"
                  cols="12"
                  sm="6"
                >
                  <VCard
                    :variant="settings.activeProvider === provider.provider ? 'outlined' : 'flat'"
                    :color="settings.activeProvider === provider.provider ? 'primary' : undefined"
                    :class="{ 'border-primary': settings.activeProvider === provider.provider }"
                    class="cursor-pointer pa-4"
                    @click="settings.activeProvider = provider.provider"
                  >
                    <div class="d-flex align-center gap-3">
                      <VAvatar
                        :color="settings.activeProvider === provider.provider ? 'primary' : 'default'"
                        variant="tonal"
                        size="40"
                      >
                        <VIcon :icon="providerIcons[provider.provider] ?? 'tabler-robot'" />
                      </VAvatar>
                      <div class="flex-grow-1">
                        <div class="text-body-1 font-weight-medium">
                          {{ provider.displayName }}
                        </div>
                        <div class="text-body-2 text-disabled">
                          {{ provider.modelId }}
                        </div>
                      </div>
                      <div class="d-flex flex-column align-end gap-1">
                        <VChip
                          v-if="provider.hasApiKey"
                          size="x-small"
                          color="success"
                          label
                        >
                          Key Set
                        </VChip>
                        <VChip
                          v-else
                          size="x-small"
                          color="warning"
                          label
                        >
                          No Key
                        </VChip>
                        <VChip
                          v-if="testResult?.provider === provider.provider"
                          size="x-small"
                          :color="testResult.ok ? 'success' : 'error'"
                          label
                        >
                          {{ testResult.ok ? 'Connected' : 'Failed' }}
                        </VChip>
                      </div>
                    </div>
                  </VCard>
                </VCol>
              </VRow>
            </VCardText>
          </VCard>

          <!-- Provider Configuration — gated on activeProviderConfig
               so the `v-model="activeProviderConfig!.modelId"` etc.
               below don't dereference undefined when the API returns
               an empty providers array (e.g. fresh install / mis-seed).
               Without this guard the page throws "Cannot read
               properties of undefined (reading 'modelId')" on mount. -->
          <!-- 2026-05-03: anchor for IntegrationStatusBanner's "Configure
               API key" CTA. The banner appends #anthropic-api-key to the
               settings route so loading the page scrolls past the lead-in
               and lands the API-Key field above the fold. -->
          <VCard
            v-if="activeProviderConfig"
            id="anthropic-api-key"
            class="mb-6"
          >
            <VCardItem>
              <VCardTitle>{{ activeProviderConfig.displayName }} Configuration</VCardTitle>
            </VCardItem>
            <VCardText>
              <VRow>
                <VCol
                  cols="12"
                  sm="6"
                >
                  <AppTextField
                    v-model="apiKeys[settings.activeProvider]"
                    label="API Key"
                    :placeholder="activeProviderConfig?.hasApiKey ? '••••••••••••••••' : 'Enter API key'"
                    type="password"
                    persistent-hint
                    :hint="activeProviderConfig?.hasApiKey ? 'Key is set. Enter a new value to replace it.' : 'Required for generation.'"
                  />
                </VCol>

                <VCol
                  cols="12"
                  sm="6"
                >
                  <AppSelect
                    v-model="activeProviderConfig!.modelId"
                    label="Model"
                    :items="modelOptions[settings.activeProvider] ?? []"
                  />
                </VCol>

                <VCol
                  cols="12"
                  sm="6"
                >
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">
                    Temperature: {{ activeProviderConfig?.temperature?.toFixed(2) }}
                  </label>
                  <VSlider
                    v-model="activeProviderConfig!.temperature"
                    :min="0"
                    :max="1"
                    :step="0.05"
                    thumb-label
                    color="primary"
                  />
                </VCol>

                <VCol cols="12">
                  <VBtn
                    variant="tonal"
                    color="info"
                    prepend-icon="tabler-plug"
                    :loading="testingProvider === settings.activeProvider"
                    @click="testConnection(settings.activeProvider)"
                  >
                    Test Connection
                  </VBtn>

                  <!-- Probe result envelope: when the test fails, render the
                       human message from the probe + the category-mapped
                       remediation hint so the operator knows whether to
                       re-paste the key, wait it out, or fix container DNS. -->
                  <VAlert
                    v-if="testResult && testResult.provider === settings.activeProvider && !testResult.ok"
                    color="error"
                    variant="tonal"
                    icon="tabler-plug-x"
                    class="mt-4"
                    closable
                    data-test="ai-test-connection-error"
                    @click:close="testResult = null"
                  >
                    <template #title>
                      <span data-test="ai-test-connection-error-message">
                        {{ testResult.error ?? 'Connection test failed.' }}
                      </span>
                      <VChip
                        v-if="testResult.category"
                        size="x-small"
                        color="error"
                        variant="outlined"
                        class="ml-2"
                        label
                        data-test="ai-test-connection-error-category"
                      >
                        {{ testResult.category }}
                      </VChip>
                    </template>
                    <div
                      v-if="testResult.category && testConnectionHints[testResult.category]"
                      class="text-body-2"
                      data-test="ai-test-connection-error-hint"
                    >
                      {{ testConnectionHints[testResult.category] }}
                    </div>
                  </VAlert>

                  <!-- Success envelope: confirms which model acknowledged the
                       probe so the operator sees they tested the model they
                       actually configured. -->
                  <VAlert
                    v-if="testResult && testResult.provider === settings.activeProvider && testResult.ok"
                    color="success"
                    variant="tonal"
                    icon="tabler-plug-connected"
                    class="mt-4"
                    closable
                    data-test="ai-test-connection-success"
                    @click:close="testResult = null"
                  >
                    {{ testResult.error ?? 'Connection successful.' }}
                  </VAlert>
                </VCol>
              </VRow>
            </VCardText>
          </VCard>

          <!-- Advanced — per-task model overrides (2026-05-03 multi-model)
               Default collapsed. Lets a curator route a single task (e.g.
               quality_gate) to a stronger or cheaper model without touching
               the primary "Model" dropdown above. Empty map = use the
               routing-config defaults. -->
          <VCard
            class="mb-6"
            data-test="ai-advanced-overrides-card"
          >
            <VExpansionPanels v-model="overridesPanelExpanded" variant="accordion">
              <VExpansionPanel value="overrides">
                <VExpansionPanelTitle>
                  <div class="d-flex align-center gap-2">
                    <VIcon icon="tabler-adjustments" size="20" />
                    <span class="font-weight-medium">Advanced — Per-Task Model Overrides</span>
                    <VChip size="x-small" color="info" variant="tonal" label>
                      Curator-configurable
                    </VChip>
                  </div>
                </VExpansionPanelTitle>
                <VExpansionPanelText>
                  <p class="text-body-2 text-medium-emphasis mb-4">
                    Route specific LLM tasks (concept extraction, quality gate, segmentation, &hellip;)
                    to a stronger or cheaper model than the routing-config default. Changes are audited
                    and visible across every host within 60 seconds.
                  </p>

                  <VAlert
                    v-if="overridesError"
                    color="error"
                    variant="tonal"
                    class="mb-4"
                    closable
                    data-test="ai-overrides-error"
                    @click:close="overridesError = null"
                  >
                    {{ overridesError }}
                  </VAlert>

                  <div
                    v-if="overridesLoading"
                    class="d-flex justify-center py-8"
                  >
                    <VProgressCircular indeterminate size="32" />
                  </div>

                  <template v-else-if="overridesData">
                    <div
                      v-if="overridesData.tasks.length === 0"
                      class="text-center text-medium-emphasis py-8"
                    >
                      No tasks configured. Add rows under
                      <code>contracts/llm/routing-config.yaml § default_model_by_task:</code>.
                    </div>

                    <VTable
                      v-else
                      density="compact"
                      data-test="ai-overrides-table"
                    >
                      <thead>
                        <tr>
                          <th>Task</th>
                          <th>Current model</th>
                          <th>Source</th>
                          <th>Override</th>
                          <th />
                        </tr>
                      </thead>
                      <tbody>
                        <tr
                          v-for="row in overridesData.tasks"
                          :key="row.task"
                          :data-test="`ai-overrides-row-${row.task}`"
                        >
                          <td>
                            <div class="font-weight-medium">
                              {{ row.task }}
                            </div>
                            <div
                              v-if="row.description"
                              class="text-caption text-medium-emphasis"
                            >
                              {{ row.description }}
                            </div>
                          </td>
                          <td>
                            <code class="text-body-2">{{ row.currentModelId }}</code>
                          </td>
                          <td>
                            <VChip
                              size="x-small"
                              :color="row.isOverridden ? 'warning' : 'default'"
                              variant="tonal"
                              label
                            >
                              {{ row.isOverridden ? 'Override' : 'Routing-config default' }}
                            </VChip>
                          </td>
                          <td style="min-width: 320px">
                            <AppSelect
                              :model-value="row.overrideModelId"
                              :items="overrideDropdownItems"
                              :placeholder="`Use default (${row.currentModelId})`"
                              :loading="overridesSaving[row.task]"
                              :disabled="overridesSaving[row.task]"
                              clearable
                              density="compact"
                              @update:model-value="(value: string | null) => onOverrideDropdownChange(row.task, value)"
                            />
                          </td>
                          <td>
                            <VBtn
                              v-if="row.isOverridden"
                              size="small"
                              variant="text"
                              color="primary"
                              :loading="overridesSaving[row.task]"
                              @click="onResetOverride(row.task)"
                            >
                              Reset to default
                            </VBtn>
                          </td>
                        </tr>
                      </tbody>
                    </VTable>

                    <div
                      v-if="overridesData.lastChangedBy"
                      class="text-caption text-medium-emphasis mt-4"
                      data-test="ai-overrides-last-changed"
                    >
                      Last changed by
                      <span class="font-weight-medium">{{ overridesData.lastChangedBy }}</span>
                      <span v-if="overridesData.lastChangedAt">
                        on {{ new Date(overridesData.lastChangedAt).toLocaleString() }}
                      </span>
                    </div>
                  </template>
                </VExpansionPanelText>
              </VExpansionPanel>
            </VExpansionPanels>
          </VCard>
        </VCol>

        <!-- Right column: Generation defaults -->
        <VCol
          cols="12"
          md="4"
        >
          <VCard class="mb-6">
            <VCardItem>
              <VCardTitle>Generation Defaults</VCardTitle>
              <VCardSubtitle>Default values when creating AI-generated questions.</VCardSubtitle>
            </VCardItem>
            <VCardText>
              <div class="d-flex flex-column gap-4">
                <AppSelect
                  v-model="settings.defaults.defaultLanguage"
                  label="Default Language"
                  :items="languageOptions"
                />

                <AppSelect
                  v-model="settings.defaults.defaultBloomsLevel"
                  label="Default Bloom's Level"
                  :items="bloomLevels"
                />

                <AppSelect
                  v-model="settings.defaults.defaultGrade"
                  label="Default Grade"
                  :items="gradeOptions"
                />

                <AppTextField
                  v-model.number="settings.defaults.questionsPerBatch"
                  label="Questions per Batch"
                  type="number"
                  :min="1"
                  :max="20"
                />

                <VSwitch
                  v-model="settings.defaults.autoRunQualityGate"
                  label="Auto-run quality gate after generation"
                  color="primary"
                />
              </div>
            </VCardText>
          </VCard>

          <!-- Save -->
          <VBtn
            color="primary"
            block
            size="large"
            :loading="saving"
            @click="saveSettings"
          >
            Save Settings
          </VBtn>
        </VCol>
      </VRow>
    </template>
  </section>
</template>
