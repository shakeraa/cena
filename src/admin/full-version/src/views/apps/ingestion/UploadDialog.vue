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

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const activeTab = ref<'file' | 'url'>('file')
const files = ref<File[]>([])
const urlInput = ref('')
const uploading = ref(false)
const uploadProgress = ref(0)
const uploadError = ref('')

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
    max-width="560"
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
