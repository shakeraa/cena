<script setup lang="ts">
interface EmailIngestionConfig {
  enabled: boolean
  imapHost?: string | null
  imapPort: number
  useSsl: boolean
  emailAddress?: string | null
  allowedSenders?: string | null
  pollIntervalMinutes: number
  subjectFilter?: string | null
}

const props = defineProps<{
  modelValue: EmailIngestionConfig | null
  testing: boolean
  testResult: boolean | null
}>()

const emit = defineEmits<{
  'update:modelValue': [config: EmailIngestionConfig]
  'test-email': [config: EmailIngestionConfig]
}>()

const defaultConfig = (): EmailIngestionConfig => ({
  enabled: false,
  imapHost: '',
  imapPort: 993,
  useSsl: true,
  emailAddress: '',
  allowedSenders: '',
  pollIntervalMinutes: 5,
  subjectFilter: '',
})

const config = computed({
  get: () => props.modelValue ?? defaultConfig(),
  set: val => emit('update:modelValue', val),
})

const update = <K extends keyof EmailIngestionConfig>(key: K, val: EmailIngestionConfig[K]) => {
  emit('update:modelValue', { ...config.value, [key]: val })
}

const statusColor = computed(() => {
  if (props.testResult === true) return 'success'
  if (props.testResult === false) return 'error'
  if (!config.value.enabled) return 'default'
  if (config.value.imapHost) return 'warning'
  return 'default'
})

const statusText = computed(() => {
  if (props.testResult === true) return 'Connected'
  if (props.testResult === false) return 'Connection failed'
  if (!config.value.enabled) return 'Disabled'
  if (!config.value.imapHost) return 'Not configured'
  return 'Not tested'
})
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-4">
      <VSwitch
        :model-value="config.enabled"
        label="Enable Email Ingestion"
        color="success"
        hide-details
        @update:model-value="update('enabled', $event as boolean)"
      />
      <VChip
        :color="statusColor"
        variant="tonal"
        size="small"
      >
        {{ statusText }}
      </VChip>
    </div>

    <template v-if="config.enabled">
      <VRow>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            :model-value="config.imapHost"
            label="IMAP Host"
            placeholder="imap.gmail.com"
            @update:model-value="update('imapHost', $event)"
          />
        </VCol>
        <VCol
          cols="12"
          sm="3"
        >
          <AppTextField
            :model-value="config.imapPort"
            label="Port"
            type="number"
            @update:model-value="update('imapPort', Number($event))"
          />
        </VCol>
        <VCol
          cols="12"
          sm="3"
          class="d-flex align-center"
        >
          <VSwitch
            :model-value="config.useSsl"
            label="SSL"
            color="success"
            hide-details
            @update:model-value="update('useSsl', $event as boolean)"
          />
        </VCol>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            :model-value="config.emailAddress"
            label="Email Address"
            placeholder="ingest@school.edu"
            type="email"
            @update:model-value="update('emailAddress', $event)"
          />
        </VCol>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            model-value=""
            label="Password"
            type="password"
            placeholder="Stored securely"
            persistent-hint
            hint="Password stored in secure vault"
          />
        </VCol>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            :model-value="config.allowedSenders"
            label="Allowed Senders"
            placeholder="*@school.edu, teacher@example.com"
            hint="Comma-separated email patterns"
            persistent-hint
            @update:model-value="update('allowedSenders', $event)"
          />
        </VCol>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            :model-value="config.subjectFilter"
            label="Subject Filter (optional)"
            placeholder="[CENA]"
            hint="Only process emails matching this subject"
            persistent-hint
            @update:model-value="update('subjectFilter', $event)"
          />
        </VCol>
        <VCol
          cols="12"
          sm="6"
        >
          <AppTextField
            :model-value="config.pollIntervalMinutes"
            label="Poll Interval (minutes)"
            type="number"
            :min="1"
            :max="60"
            @update:model-value="update('pollIntervalMinutes', Number($event))"
          />
        </VCol>
      </VRow>
      <div class="mt-4">
        <VBtn
          variant="tonal"
          :loading="testing"
          :disabled="!config.imapHost || !config.emailAddress"
          @click="$emit('test-email', config)"
        >
          <VIcon
            icon="tabler-plug"
            start
          />
          Test Connection
        </VBtn>
      </div>
    </template>
  </div>
</template>
