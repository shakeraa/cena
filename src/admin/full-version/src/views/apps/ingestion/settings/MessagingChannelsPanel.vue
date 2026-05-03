<script setup lang="ts">
interface MessagingChannelConfig {
  id: string
  type: string
  name: string
  enabled: boolean
  webhookUrl?: string | null
  botToken?: string | null
  phoneNumberId?: string | null
  allowedSenders?: string | null
}

const props = defineProps<{
  modelValue: MessagingChannelConfig[]
}>()

const emit = defineEmits<{
  'update:modelValue': [channels: MessagingChannelConfig[]]
}>()

const editing = ref(false)
const editIndex = ref(-1)
const confirmDeleteIndex = ref(-1)

const blankChannel = (): MessagingChannelConfig => ({
  id: '',
  type: 'telegram',
  name: '',
  enabled: false,
  webhookUrl: '',
  botToken: '',
  phoneNumberId: '',
  allowedSenders: '',
})

const form = ref<MessagingChannelConfig>(blankChannel())

const typeOptions = [
  { title: 'WhatsApp', value: 'whatsapp' },
  { title: 'Telegram', value: 'telegram' },
  { title: 'Slack', value: 'slack' },
]

const channelIcon = (type: string) => {
  const map: Record<string, string> = {
    whatsapp: 'tabler-brand-whatsapp',
    telegram: 'tabler-brand-telegram',
    slack: 'tabler-brand-slack',
  }
  return map[type] ?? 'tabler-message'
}

const channelColor = (type: string) => {
  const map: Record<string, string> = {
    whatsapp: 'success',
    telegram: 'info',
    slack: 'warning',
  }
  return map[type] ?? 'default'
}

const openAdd = () => {
  form.value = blankChannel()
  editIndex.value = -1
  editing.value = true
}

const openEdit = (idx: number) => {
  form.value = { ...props.modelValue[idx] }
  editIndex.value = idx
  editing.value = true
}

const cancel = () => {
  editing.value = false
  editIndex.value = -1
}

const save = () => {
  const channels = [...props.modelValue]
  if (editIndex.value >= 0) {
    channels[editIndex.value] = { ...form.value }
  }
  else {
    form.value.id = crypto.randomUUID().replace(/-/g, '').slice(0, 8)
    channels.push({ ...form.value })
  }
  emit('update:modelValue', channels)
  cancel()
}

const remove = (idx: number) => {
  const channels = props.modelValue.filter((_, i) => i !== idx)
  emit('update:modelValue', channels)
  confirmDeleteIndex.value = -1
}

const toggleEnabled = (idx: number) => {
  const channels = [...props.modelValue]
  channels[idx] = { ...channels[idx], enabled: !channels[idx].enabled }
  emit('update:modelValue', channels)
}
</script>

<template>
  <div>
    <div
      v-if="!editing && modelValue.length === 0"
      class="text-center pa-6 text-medium-emphasis"
    >
      No messaging channels configured. Click "Add Channel" to get started.
    </div>

    <VCard
      v-for="(ch, idx) in modelValue"
      v-show="!editing"
      :key="ch.id || idx"
      class="mb-3"
      variant="outlined"
    >
      <VCardText class="d-flex align-center gap-4">
        <VIcon
          :icon="channelIcon(ch.type)"
          :color="channelColor(ch.type)"
          size="24"
        />
        <div class="flex-grow-1">
          <div class="d-flex align-center gap-2 mb-1">
            <span class="text-body-1 font-weight-semibold">{{ ch.name }}</span>
            <VChip
              :color="channelColor(ch.type)"
              size="x-small"
              label
            >
              {{ ch.type }}
            </VChip>
          </div>
          <span
            v-if="ch.webhookUrl"
            class="text-body-2 text-medium-emphasis"
          >{{ ch.webhookUrl }}</span>
        </div>
        <VChip
          :color="ch.enabled ? 'success' : 'default'"
          size="x-small"
          variant="tonal"
        >
          {{ ch.enabled ? 'Active' : 'Inactive' }}
        </VChip>
        <VSwitch
          :model-value="ch.enabled"
          density="compact"
          hide-details
          color="success"
          @update:model-value="toggleEnabled(idx)"
        />
        <VBtn
          icon
          variant="text"
          size="small"
          @click="openEdit(idx)"
        >
          <VIcon icon="tabler-edit" />
        </VBtn>
        <VBtn
          icon
          variant="text"
          size="small"
          color="error"
          @click="confirmDeleteIndex = idx"
        >
          <VIcon icon="tabler-trash" />
        </VBtn>
      </VCardText>
    </VCard>

    <!-- Delete confirmation -->
    <VDialog
      :model-value="confirmDeleteIndex >= 0"
      max-width="400"
      @update:model-value="confirmDeleteIndex = -1"
    >
      <VCard>
        <VCardTitle>Delete Channel</VCardTitle>
        <VCardText>
          Are you sure you want to remove
          <strong>{{ confirmDeleteIndex >= 0 ? modelValue[confirmDeleteIndex]?.name : '' }}</strong>?
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            @click="confirmDeleteIndex = -1"
          >
            Cancel
          </VBtn>
          <VBtn
            color="error"
            @click="remove(confirmDeleteIndex)"
          >
            Delete
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <!-- Add / Edit form -->
    <VCard
      v-if="editing"
      variant="outlined"
      class="mb-3"
    >
      <VCardTitle class="text-body-1 font-weight-semibold pa-4 pb-2">
        {{ editIndex >= 0 ? 'Edit Channel' : 'Add Channel' }}
      </VCardTitle>
      <VCardText>
        <VRow>
          <VCol
            cols="12"
            sm="6"
          >
            <VSelect
              v-model="form.type"
              :items="typeOptions"
              label="Channel Type"
            />
          </VCol>
          <VCol
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.name"
              label="Display Name"
              placeholder="e.g. Math Teachers Group"
            />
          </VCol>
          <VCol cols="12">
            <VSwitch
              v-model="form.enabled"
              label="Enabled"
              color="success"
              hide-details
            />
          </VCol>
          <!-- Telegram fields -->
          <VCol
            v-if="form.type === 'telegram'"
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.botToken"
              label="Bot Token"
              placeholder="123456:ABC-DEF..."
              type="password"
            />
          </VCol>
          <!-- WhatsApp fields -->
          <VCol
            v-if="form.type === 'whatsapp'"
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.phoneNumberId"
              label="Phone Number ID"
              placeholder="Business API Phone ID"
            />
          </VCol>
          <VCol
            v-if="form.type === 'whatsapp'"
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.webhookUrl"
              label="Access Token"
              type="password"
            />
          </VCol>
          <!-- Slack fields -->
          <VCol
            v-if="form.type === 'slack'"
            cols="12"
          >
            <AppTextField
              v-model="form.webhookUrl"
              label="Webhook URL"
              placeholder="https://hooks.slack.com/services/..."
            />
          </VCol>
          <VCol cols="12">
            <AppTextField
              v-model="form.allowedSenders"
              label="Allowed Senders"
              placeholder="Comma-separated identifiers"
              hint="Leave empty to allow all"
              persistent-hint
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions class="pa-4 pt-0">
        <VSpacer />
        <VBtn
          variant="text"
          @click="cancel"
        >
          Cancel
        </VBtn>
        <VBtn
          color="primary"
          :disabled="!form.name || !form.type"
          @click="save"
        >
          {{ editIndex >= 0 ? 'Update' : 'Add' }}
        </VBtn>
      </VCardActions>
    </VCard>

    <VBtn
      v-if="!editing"
      variant="tonal"
      color="primary"
      class="mt-2"
      @click="openAdd"
    >
      <VIcon
        icon="tabler-plus"
        start
      />
      Add Channel
    </VBtn>
  </div>
</template>
