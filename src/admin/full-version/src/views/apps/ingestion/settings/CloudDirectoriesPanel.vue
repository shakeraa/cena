<script setup lang="ts">
interface CloudDirConfig {
  id: string
  name: string
  provider: string
  path: string
  prefix?: string | null
  enabled: boolean
  autoWatch: boolean
  watchIntervalMinutes?: number | null
  accessKeyId?: string | null
  region?: string | null
}

const props = defineProps<{
  modelValue: CloudDirConfig[]
  testing: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [dirs: CloudDirConfig[]]
  'test-dir': [dir: CloudDirConfig]
}>()

const editing = ref(false)
const editIndex = ref(-1)

const blankDir = (): CloudDirConfig => ({
  id: '',
  name: '',
  provider: 'local',
  path: '',
  prefix: null,
  enabled: true,
  autoWatch: false,
  watchIntervalMinutes: 5,
  accessKeyId: null,
  region: null,
})

const form = ref<CloudDirConfig>(blankDir())
const confirmDeleteIndex = ref(-1)

const providerOptions = [
  { title: 'Local Filesystem', value: 'local' },
  { title: 'Amazon S3', value: 's3' },
  { title: 'Google Cloud Storage', value: 'gcs' },
  { title: 'Azure Blob Storage', value: 'azure' },
]

const regionOptions = [
  { title: 'us-east-1', value: 'us-east-1' },
  { title: 'us-west-2', value: 'us-west-2' },
  { title: 'eu-west-1', value: 'eu-west-1' },
  { title: 'eu-central-1', value: 'eu-central-1' },
  { title: 'me-south-1', value: 'me-south-1' },
  { title: 'il-central-1', value: 'il-central-1' },
  { title: 'ap-southeast-1', value: 'ap-southeast-1' },
]

const providerColor = (p: string) => {
  const map: Record<string, string> = { local: 'default', s3: 'warning', gcs: 'info', azure: 'primary' }
  return map[p] ?? 'default'
}

const providerLabel = (p: string) => {
  const map: Record<string, string> = { local: 'Local', s3: 'S3', gcs: 'GCS', azure: 'Azure' }
  return map[p] ?? p
}

const isCloudProvider = computed(() => ['s3', 'gcs', 'azure'].includes(form.value.provider))

const openAdd = () => {
  form.value = blankDir()
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
  const dirs = [...props.modelValue]
  if (editIndex.value >= 0) {
    dirs[editIndex.value] = { ...form.value }
  }
  else {
    form.value.id = crypto.randomUUID().replace(/-/g, '').slice(0, 8)
    dirs.push({ ...form.value })
  }
  emit('update:modelValue', dirs)
  cancel()
}

const remove = (idx: number) => {
  const dirs = props.modelValue.filter((_, i) => i !== idx)
  emit('update:modelValue', dirs)
  confirmDeleteIndex.value = -1
}

const toggleEnabled = (idx: number) => {
  const dirs = [...props.modelValue]
  dirs[idx] = { ...dirs[idx], enabled: !dirs[idx].enabled }
  emit('update:modelValue', dirs)
}
</script>

<template>
  <div>
    <!-- Existing directories list -->
    <div
      v-if="!editing && modelValue.length === 0"
      class="text-center pa-6 text-medium-emphasis"
    >
      No cloud directories configured. Click "Add Directory" to get started.
    </div>

    <VCard
      v-for="(dir, idx) in modelValue"
      v-show="!editing"
      :key="dir.id || idx"
      class="mb-3"
      variant="outlined"
    >
      <VCardText class="d-flex align-center gap-4">
        <VIcon
          :icon="dir.provider === 'local' ? 'tabler-folder' : 'tabler-cloud'"
          size="24"
        />
        <div class="flex-grow-1">
          <div class="d-flex align-center gap-2 mb-1">
            <span class="text-body-1 font-weight-semibold">{{ dir.name }}</span>
            <VChip
              :color="providerColor(dir.provider)"
              size="x-small"
              label
            >
              {{ providerLabel(dir.provider) }}
            </VChip>
            <VChip
              v-if="dir.autoWatch"
              color="success"
              size="x-small"
              variant="tonal"
            >
              Auto-watch
            </VChip>
          </div>
          <span class="text-body-2 text-medium-emphasis">{{ dir.path }}{{ dir.prefix ? `/${dir.prefix}` : '' }}</span>
        </div>
        <VSwitch
          :model-value="dir.enabled"
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
        <VCardTitle>Delete Directory</VCardTitle>
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
        {{ editIndex >= 0 ? 'Edit Directory' : 'Add Directory' }}
      </VCardTitle>
      <VCardText>
        <VRow>
          <VCol
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.name"
              label="Name"
              placeholder="e.g. Main Ingest Folder"
            />
          </VCol>
          <VCol
            cols="12"
            sm="6"
          >
            <VSelect
              v-model="form.provider"
              :items="providerOptions"
              label="Provider"
            />
          </VCol>
          <VCol
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.path"
              :label="isCloudProvider ? 'Bucket Name' : 'Directory Path'"
              :placeholder="isCloudProvider ? 'my-bucket' : '/data/ingest'"
            />
          </VCol>
          <VCol
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.prefix"
              label="Prefix / Subfolder (optional)"
              placeholder="uploads/"
            />
          </VCol>
          <VCol
            v-if="isCloudProvider"
            cols="12"
            sm="6"
          >
            <AppTextField
              v-model="form.accessKeyId"
              label="Access Key ID"
            />
          </VCol>
          <VCol
            v-if="isCloudProvider"
            cols="12"
            sm="6"
          >
            <VSelect
              v-model="form.region"
              :items="regionOptions"
              label="Region"
            />
          </VCol>
          <VCol
            cols="12"
            sm="4"
          >
            <VSwitch
              v-model="form.enabled"
              label="Enabled"
              color="success"
              hide-details
            />
          </VCol>
          <VCol
            cols="12"
            sm="4"
          >
            <VSwitch
              v-model="form.autoWatch"
              label="Auto-Watch"
              color="success"
              hide-details
            />
          </VCol>
          <VCol
            v-if="form.autoWatch"
            cols="12"
            sm="4"
          >
            <AppTextField
              v-model.number="form.watchIntervalMinutes"
              label="Poll Interval (min)"
              type="number"
              :min="1"
              :max="1440"
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions class="pa-4 pt-0">
        <VBtn
          variant="tonal"
          :loading="testing"
          @click="$emit('test-dir', form)"
        >
          <VIcon
            icon="tabler-plug"
            start
          />
          Test Connection
        </VBtn>
        <VSpacer />
        <VBtn
          variant="text"
          @click="cancel"
        >
          Cancel
        </VBtn>
        <VBtn
          color="primary"
          :disabled="!form.name || !form.path"
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
      Add Directory
    </VBtn>
  </div>
</template>
