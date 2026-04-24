<!-- =============================================================================
     Cena Platform — Student PDF / Image Upload (RDY-056)

     Drives POST /api/student/photo/upload — the Phase 2.2 endpoint that
     handles both images AND PDFs (20 MB cap).
       Status values:
         processed_ocr            → extracted LaTeX + CTA
         processed_text_shortcut  → "text layer extracted" banner
         processed_empty          → "no math detected" retake CTA
         queued_for_review        → tutor-review ETA banner
       And the standard HTTP outcomes:
         403 moderation, 422 low-conf / encrypted_pdf, 503 circuit-open.
============================================================================= -->
<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { $api } from '@/utils/api'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.pdfUpload',
    breadcrumbs: true,
  },
})

const MAX_SIZE_BYTES = 20 * 1024 * 1024   // mirrors server cap (Phase 2.2)
const ACCEPT = 'application/pdf,image/jpeg,image/png,image/webp'

interface PhotoUploadResponse {
  photoId: string
  originalSizeBytes: number
  processedSizeBytes: number
  exifStripped: boolean
  contentType: string
  status: string
  moderationVerdict?: string | null
  pdfTriage?: string | null
  extractedLatex?: string[] | null
  overallConfidence?: number | null
  warnings?: string[] | null
}

type Outcome =
  | { kind: 'idle' }
  | { kind: 'uploading' }
  | { kind: 'ok'; data: PhotoUploadResponse }
  | { kind: 'blocked' }
  | { kind: 'encrypted' }
  | { kind: 'review' }
  | { kind: 'retry_later' }
  | { kind: 'error'; message: string }

const { t } = useI18n()
const router = useRouter()
const fileInput = ref<HTMLInputElement | null>(null)
const dragOver = ref(false)
const outcome = ref<Outcome>({ kind: 'idle' })

async function handleFiles(files: FileList | null) {
  if (!files || files.length === 0) return
  const file = files[0]

  if (!ACCEPT.split(',').includes(file.type)) {
    outcome.value = { kind: 'error', message: t('pdfUpload.errors.badType', 'Only PDF, JPG, PNG, or WebP are accepted.') }
    return
  }
  if (file.size > MAX_SIZE_BYTES) {
    outcome.value = { kind: 'error', message: t('pdfUpload.errors.tooLarge', 'File is larger than 20 MB.') }
    return
  }

  outcome.value = { kind: 'uploading' }
  const form = new FormData()
  form.append('photo', file, file.name)

  try {
    const res = await $api<PhotoUploadResponse>('/api/student/photo/upload', { method: 'POST', body: form })
    outcome.value = { kind: 'ok', data: res }
  }
  catch (e: any) {
    const status = e?.response?.status ?? e?.status
    const code = e?.data?.error ?? e?.data?.code
    if (status === 403) outcome.value = { kind: 'blocked' }
    else if (status === 422 && code === 'encrypted_pdf') outcome.value = { kind: 'encrypted' }
    else if (status === 422) outcome.value = { kind: 'review' }
    else if (status === 503) outcome.value = { kind: 'retry_later' }
    else outcome.value = { kind: 'error', message: e?.data?.message ?? e?.message ?? 'upload_failed' }
  }
}

function onDrop(e: DragEvent) {
  e.preventDefault()
  dragOver.value = false
  handleFiles(e.dataTransfer?.files ?? null)
}

function onDragOver(e: DragEvent) {
  e.preventDefault()
  dragOver.value = true
}

function startTutorSession(photoId: string, latex?: string[]) {
  router.push({
    path: '/tutor',
    query: {
      fromImage: photoId,
      latex: latex?.[0],
    },
  })
}

function statusColor(status: string): string {
  switch (status) {
    case 'processed_ocr':            return 'success'
    case 'processed_text_shortcut':  return 'success'
    case 'processed_empty':          return 'info'
    case 'queued_for_review':        return 'warning'
    default:                         return 'default'
  }
}
</script>

<template>
  <div class="pdf-upload-page pa-4" data-testid="pdf-upload-page">
    <h1 class="text-h5 mb-3">
      {{ t('pdfUpload.title', 'Upload a problem') }}
    </h1>
    <p class="text-body-2 text-medium-emphasis mb-4">
      {{ t('pdfUpload.subtitle', 'PDF, JPG, PNG, or WebP — up to 20 MB.') }}
    </p>

    <!-- Dropzone -->
    <VCard
      class="mb-4"
      :class="{ 'bg-grey-lighten-3': dragOver }"
      data-testid="dropzone"
      @drop="onDrop"
      @dragover="onDragOver"
      @dragleave="dragOver = false"
    >
      <VCardText class="text-center py-12">
        <VIcon icon="ri-upload-cloud-2-line" size="64" class="text-medium-emphasis" />
        <div class="mt-3 text-body-1">
          {{ t('pdfUpload.drop', 'Drop a file here, or') }}
        </div>
        <VBtn class="mt-3" color="primary" @click="fileInput?.click()">
          {{ t('pdfUpload.browse', 'Choose file') }}
        </VBtn>
        <input
          ref="fileInput"
          type="file"
          :accept="ACCEPT"
          class="d-none"
          data-testid="pdf-file-input"
          @change="handleFiles(($event.target as HTMLInputElement).files)"
        >
      </VCardText>
    </VCard>

    <template v-if="outcome.kind === 'uploading'">
      <VCard class="mb-3" data-testid="outcome-uploading">
        <VCardText class="text-center py-8">
          <VProgressCircular indeterminate color="primary" />
          <div class="mt-3">
            {{ t('pdfUpload.uploading', 'Processing…') }}
          </div>
        </VCardText>
      </VCard>
    </template>

    <VAlert
      v-if="outcome.kind === 'blocked'"
      type="error"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-blocked"
    >
      {{ t('pdfUpload.blocked', "This file can't be used. Please upload a problem sheet only.") }}
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'encrypted'"
      type="warning"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-encrypted"
    >
      {{ t('pdfUpload.encrypted', "This PDF is password-protected. Please unlock it and re-upload.") }}
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'review'"
      type="warning"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-review"
    >
      {{ t('pdfUpload.review', 'A tutor will review your file shortly.') }}
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'retry_later'"
      type="info"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-retry-later"
    >
      {{ t('pdfUpload.retryLater', 'Processing is temporarily unavailable. Please try again in a few minutes.') }}
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'error'"
      type="error"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-error"
    >
      {{ outcome.message }}
    </VAlert>

    <VCard v-if="outcome.kind === 'ok'" data-testid="outcome-ok" class="mb-3">
      <VCardTitle class="d-flex align-center gap-2">
        <span>{{ t('pdfUpload.ok.title', 'File processed') }}</span>
        <VChip :color="statusColor(outcome.data.status)" size="small">
          {{ outcome.data.status }}
        </VChip>
        <VChip v-if="outcome.data.pdfTriage" size="small">
          triage: {{ outcome.data.pdfTriage }}
        </VChip>
      </VCardTitle>
      <VCardText>
        <div v-if="outcome.data.extractedLatex?.length" class="mb-3">
          <div class="text-caption text-medium-emphasis mb-1">
            {{ t('pdfUpload.ok.extracted', 'Extracted math:') }}
          </div>
          <bdi
            v-for="(l, i) in outcome.data.extractedLatex"
            :key="i"
            dir="ltr"
            class="d-block rounded pa-2 mb-1"
            style="background: rgba(0,0,0,0.04); font-family: monospace;"
          >
            {{ l }}
          </bdi>
        </div>

        <div v-if="outcome.data.overallConfidence != null" class="text-caption text-medium-emphasis mb-2">
          {{ t('pdfUpload.ok.confidence', 'Confidence:') }}
          {{ Math.round(outcome.data.overallConfidence * 100) }}%
        </div>

        <div v-if="outcome.data.warnings?.length" class="d-flex flex-wrap gap-1 mb-3">
          <VChip
            v-for="w in outcome.data.warnings"
            :key="w"
            size="small"
            color="warning"
            variant="tonal"
          >
            {{ w }}
          </VChip>
        </div>

        <VBtn
          v-if="outcome.data.status === 'processed_ocr' && outcome.data.extractedLatex?.length"
          color="primary"
          @click="startTutorSession(outcome.data.photoId, outcome.data.extractedLatex ?? undefined)"
        >
          {{ t('pdfUpload.ok.startTutor', 'Start tutoring session') }}
        </VBtn>
      </VCardText>
    </VCard>
  </div>
</template>
