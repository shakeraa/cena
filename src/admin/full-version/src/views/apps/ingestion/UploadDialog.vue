<script setup lang="ts">
import { $api } from '@/utils/api'
import { firebaseAuth } from '@/plugins/firebase'

interface Emit {
  (e: 'update:isOpen', value: boolean): void
  (e: 'uploaded'): void
}

interface Props {
  isOpen: boolean
}

interface CloudFileEntry {
  Key: string
  Filename: string
  SizeBytes: number
  ContentType: string
  LastModified: string
  AlreadyIngested: boolean
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const activeTab = ref<'file' | 'url' | 'cloud'>('file')
const files = ref<File[]>([])
const urlInput = ref('')
const uploading = ref(false)
const uploadProgress = ref(0)
const uploadError = ref('')

// Cloud directory state
const cloudProvider = ref<'local' | 's3'>('local')
const cloudPath = ref('')
const cloudPrefix = ref('')
const cloudScanning = ref(false)
const cloudIngesting = ref(false)
const cloudFiles = ref<CloudFileEntry[]>([])
const cloudSelected = ref<string[]>([])

const cloudProviderOptions = [
  { title: 'Local Directory', value: 'local' },
  { title: 'S3 Bucket', value: 's3' },
]

const cloudHeaders = [
  { title: 'Filename', key: 'Filename', sortable: true },
  { title: 'Size', key: 'SizeBytes', sortable: true },
  { title: 'Modified', key: 'LastModified', sortable: true },
  { title: 'Status', key: 'AlreadyIngested', sortable: false },
]

const formatFileSize = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

const formatDate = (dateStr: string): string => {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

const selectableCloudFiles = computed(() =>
  cloudFiles.value.filter(f => !f.AlreadyIngested).map(f => f.Key),
)

const scanCloudDir = async () => {
  if (!cloudPath.value.trim()) return

  cloudScanning.value = true
  uploadError.value = ''
  cloudFiles.value = []
  cloudSelected.value = []

  try {
    const response = await $api<{ Files: CloudFileEntry[]; TotalCount: number }>('/admin/ingestion/cloud-dir/list', {
      method: 'POST',
      body: {
        Provider: cloudProvider.value,
        BucketOrPath: cloudPath.value.trim(),
        Prefix: cloudPrefix.value.trim() || null,
        ContinuationToken: null,
      },
    })
    cloudFiles.value = response.Files ?? []
  }
  catch (error: any) {
    uploadError.value = error?.data?.message ?? error?.message ?? 'Directory scan failed'
  }
  finally {
    cloudScanning.value = false
  }
}

const ingestCloudFiles = async () => {
  if (!cloudSelected.value.length) return

  cloudIngesting.value = true
  uploadError.value = ''

  try {
    await $api('/admin/ingestion/cloud-dir/ingest', {
      method: 'POST',
      body: {
        Provider: cloudProvider.value,
        BucketOrPath: cloudPath.value.trim(),
        FileKeys: cloudSelected.value,
        Prefix: cloudPrefix.value.trim() || null,
      },
    })

    emit('uploaded')
    handleClose()
  }
  catch (error: any) {
    uploadError.value = error?.data?.message ?? error?.message ?? 'Cloud ingest failed'
  }
  finally {
    cloudIngesting.value = false
  }
}

const handleClose = () => {
  emit('update:isOpen', false)
  resetForm()
}

const resetForm = () => {
  files.value = []
  urlInput.value = ''
  uploading.value = false
  uploadProgress.value = 0
  uploadError.value = ''
  cloudPath.value = ''
  cloudPrefix.value = ''
  cloudFiles.value = []
  cloudSelected.value = []
  cloudScanning.value = false
  cloudIngesting.value = false
}

const submitFile = async () => {
  if (!files.value.length)
    return

  uploading.value = true
  uploadError.value = ''
  uploadProgress.value = 0

  try {
    const formData = new FormData()
    for (const file of files.value) {
      formData.append('files', file)
    }

    const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api'
    const user = firebaseAuth.currentUser
    const token = user ? await user.getIdToken() : ''

    await new Promise<void>((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (event: ProgressEvent) => {
        if (event.lengthComputable)
          uploadProgress.value = Math.round((event.loaded / event.total) * 100)
      })

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300)
          resolve()
        else
          reject(new Error(xhr.responseText || `Upload failed with status ${xhr.status}`))
      })

      xhr.addEventListener('error', () => reject(new Error('Network error during upload')))
      xhr.addEventListener('abort', () => reject(new Error('Upload aborted')))

      xhr.open('POST', `${baseUrl}/admin/ingestion/upload`)
      if (token)
        xhr.setRequestHeader('Authorization', `Bearer ${token}`)

      xhr.send(formData)
    })

    emit('uploaded')
    handleClose()
  }
  catch (error: any) {
    uploadError.value = error?.message ?? 'Upload failed'
  }
  finally {
    uploading.value = false
  }
}

const submitUrl = async () => {
  if (!urlInput.value.trim())
    return

  uploading.value = true
  uploadError.value = ''

  try {
    await $api('/admin/ingestion/url', {
      method: 'POST',
      body: { url: urlInput.value.trim() },
    })

    emit('uploaded')
    handleClose()
  }
  catch (error: any) {
    uploadError.value = error?.data?.message ?? error?.message ?? 'URL submission failed'
  }
  finally {
    uploading.value = false
  }
}
</script>

<template>
  <VDialog
    :model-value="props.isOpen"
    max-width="720"
    @update:model-value="(val: boolean) => { if (!val) handleClose() }"
  >
    <VCard>
      <VCardTitle class="d-flex align-center justify-space-between pa-4">
        <span>Upload Content</span>
        <VBtn
          icon
          variant="text"
          size="small"
          @click="handleClose"
        >
          <VIcon icon="ri-close-line" />
        </VBtn>
      </VCardTitle>

      <VDivider />

      <VCardText>
        <VTabs
          v-model="activeTab"
          class="mb-4"
        >
          <VTab value="file">
            <VIcon
              icon="ri-upload-2-line"
              start
            />
            File Upload
          </VTab>
          <VTab value="url">
            <VIcon
              icon="ri-link"
              start
            />
            URL
          </VTab>
          <VTab value="cloud">
            <VIcon
              icon="ri-cloud-line"
              start
            />
            Cloud Dir
          </VTab>
        </VTabs>

        <VWindow v-model="activeTab">
          <VWindowItem value="file">
            <VFileInput
              v-model="files"
              accept=".pdf,.jpg,.png,.docx"
              label="Select files"
              placeholder="Drop files here or click to browse"
              prepend-icon=""
              prepend-inner-icon="ri-upload-cloud-2-line"
              multiple
              show-size
              counter
              :disabled="uploading"
              variant="outlined"
            />

            <VProgressLinear
              v-if="uploading"
              :model-value="uploadProgress"
              color="primary"
              rounded
              height="6"
              class="mt-3"
            />

            <VBtn
              block
              color="primary"
              class="mt-4"
              :loading="uploading"
              :disabled="!files.length"
              @click="submitFile"
            >
              <VIcon
                icon="ri-upload-2-line"
                start
              />
              Upload Files
            </VBtn>
          </VWindowItem>

          <VWindowItem value="url">
            <AppTextField
              v-model="urlInput"
              label="Content URL"
              placeholder="https://example.com/document.pdf"
              prepend-inner-icon="ri-link"
              :disabled="uploading"
            />

            <VBtn
              block
              color="primary"
              class="mt-4"
              :loading="uploading"
              :disabled="!urlInput.trim()"
              @click="submitUrl"
            >
              <VIcon
                icon="ri-send-plane-line"
                start
              />
              Submit URL
            </VBtn>
          </VWindowItem>

          <VWindowItem value="cloud">
            <VSelect
              v-model="cloudProvider"
              :items="cloudProviderOptions"
              label="Provider"
              variant="outlined"
              density="compact"
              class="mb-3"
              :disabled="cloudScanning || cloudIngesting"
            />

            <AppTextField
              v-model="cloudPath"
              :label="cloudProvider === 's3' ? 'Bucket Name' : 'Directory Path'"
              :placeholder="cloudProvider === 's3' ? 'my-ingest-bucket' : '/Users/shaker/edu-apps/cena/data/ingest'"
              prepend-inner-icon="ri-folder-line"
              :disabled="cloudScanning || cloudIngesting"
              class="mb-3"
            />

            <AppTextField
              v-model="cloudPrefix"
              label="Prefix / Subfolder (optional)"
              placeholder="exams/2026"
              prepend-inner-icon="ri-folder-open-line"
              :disabled="cloudScanning || cloudIngesting"
              class="mb-3"
            />

            <VBtn
              block
              color="primary"
              variant="outlined"
              :loading="cloudScanning"
              :disabled="!cloudPath.trim() || cloudIngesting"
              @click="scanCloudDir"
            >
              <VIcon
                icon="ri-search-line"
                start
              />
              Scan Directory
            </VBtn>

            <template v-if="cloudFiles.length > 0">
              <VDataTable
                v-model="cloudSelected"
                :headers="cloudHeaders"
                :items="cloudFiles"
                item-value="Key"
                show-select
                density="compact"
                class="mt-4"
                :items-per-page="10"
              >
                <template #item.SizeBytes="{ item }">
                  {{ formatFileSize(item.SizeBytes) }}
                </template>
                <template #item.LastModified="{ item }">
                  {{ formatDate(item.LastModified) }}
                </template>
                <template #item.AlreadyIngested="{ item }">
                  <VChip
                    :color="item.AlreadyIngested ? 'success' : 'default'"
                    size="small"
                    variant="tonal"
                  >
                    {{ item.AlreadyIngested ? 'Ingested' : 'New' }}
                  </VChip>
                </template>
                <template #item.data-table-select="{ item, isSelected, toggleSelect }">
                  <VCheckboxBtn
                    :model-value="isSelected({ value: item.Key, selectable: !item.AlreadyIngested })"
                    :disabled="item.AlreadyIngested"
                    @update:model-value="toggleSelect({ value: item.Key, selectable: !item.AlreadyIngested })"
                  />
                </template>
              </VDataTable>

              <VBtn
                block
                color="primary"
                class="mt-4"
                :loading="cloudIngesting"
                :disabled="!cloudSelected.length"
                @click="ingestCloudFiles"
              >
                <VIcon
                  icon="ri-upload-cloud-2-line"
                  start
                />
                Ingest Selected ({{ cloudSelected.length }})
              </VBtn>
            </template>

            <div
              v-else-if="!cloudScanning && cloudPath.trim()"
              class="text-center text-medium-emphasis mt-4 pa-4"
            >
              Click "Scan Directory" to discover files
            </div>
          </VWindowItem>
        </VWindow>

        <VAlert
          v-if="uploadError"
          color="error"
          variant="tonal"
          density="compact"
          class="mt-4"
          closable
          @click:close="uploadError = ''"
        >
          {{ uploadError }}
        </VAlert>
      </VCardText>
    </VCard>
  </VDialog>
</template>
