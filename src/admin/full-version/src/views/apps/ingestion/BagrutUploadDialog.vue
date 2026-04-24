<!-- =============================================================================
     Cena Platform — Admin Bagrut PDF Upload Dialog (RDY-057)

     Drives POST /api/admin/ingestion/bagrut. Super-admin-only; exam-code
     regex matches the server's ExamCodeRx (^[a-z0-9\-_]{3,64}$). Renders
     the PdfIngestionResult warnings as severity-graded chips and lists
     the extracted drafts so the curator can jump into the review queue.
============================================================================= -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { $api } from '@/utils/api'

interface IngestionDraftQuestion {
  draftId: string
  sourcePage: number
  prompt: string
  latexContent: string | null
  extractionConfidence: number
  reviewNotes: string[]
  figureSpecJson: string | null
}

interface PdfIngestionResult {
  pdfId: string
  examCode: string
  totalPages: number
  questionsExtracted: number
  figuresExtracted: number
  drafts: IngestionDraftQuestion[]
  warnings: string[]
}

interface Props { modelValue: boolean }
const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'ingested', result: PdfIngestionResult): void
}>()

const EXAM_CODE_RX = /^[a-z0-9\-_]{3,64}$/
const MAX_SIZE_BYTES = 50 * 1024 * 1024

const examCode = ref('')
const file = ref<File | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)
const error = ref<string | null>(null)
const result = ref<PdfIngestionResult | null>(null)

const examCodeValid = computed(() => EXAM_CODE_RX.test(examCode.value.trim().toLowerCase()))
const canSubmit = computed(() =>
  examCodeValid.value && !!file.value && file.value.size <= MAX_SIZE_BYTES && !uploading.value,
)

function onFilePick(ev: Event) {
  const f = (ev.target as HTMLInputElement).files?.[0]
  if (!f) return
  error.value = null
  if (f.type !== 'application/pdf') {
    error.value = 'File must be a PDF.'
    file.value = null
    return
  }
  if (f.size > MAX_SIZE_BYTES) {
    error.value = `File exceeds ${MAX_SIZE_BYTES / (1024 * 1024)} MB.`
    file.value = null
    return
  }
  file.value = f
}

async function submit() {
  if (!canSubmit.value || !file.value) return
  uploading.value = true
  error.value = null
  result.value = null
  try {
    const form = new FormData()
    form.append('examCode', examCode.value.trim().toLowerCase())
    form.append('file', file.value, file.value.name)
    const res = await $api<PdfIngestionResult>('/admin/ingestion/bagrut', {
      method: 'POST',
      body: form,
    })
    result.value = res
    emit('ingested', res)
  }
  catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Upload failed'
  }
  finally {
    uploading.value = false
  }
}

function close() {
  emit('update:modelValue', false)
}

// Reset when opened
watch(() => props.modelValue, (open) => {
  if (open) {
    examCode.value = ''
    file.value = null
    result.value = null
    error.value = null
  }
})

function warningSeverity(w: string): 'error' | 'warning' | 'info' | 'success' {
  if (w.startsWith('encrypted_pdf')) return 'error'
  if (w.startsWith('cas_failed') || w === 'human_review_required') return 'error'
  if (w.startsWith('review:')) return 'warning'
  if (w === 'some_drafts_low_confidence') return 'warning'
  if (w.startsWith('fallback_used:')) return 'info'
  return 'info'
}
</script>

<template>
  <VDialog
    :model-value="props.modelValue"
    max-width="720"
    persistent
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
  >
    <VCard>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="ri-file-pdf-2-line" />
        <span>Upload Bagrut PDF (reference-only)</span>
        <VSpacer />
        <VBtn icon="ri-close-line" variant="text" size="small" @click="close" />
      </VCardTitle>

      <VCardText class="pt-3">
        <VAlert type="info" variant="tonal" density="compact" class="mb-3">
          Ministry exams are reference material only. Student-facing items are
          CAS-gated recreations — raw Bagrut text never reaches students.
        </VAlert>

        <VTextField
          v-model="examCode"
          label="Exam code (e.g. math-5u-2023-winter)"
          density="compact"
          :error-messages="examCode && !examCodeValid ? ['3-64 chars, lower-case alphanumerics + - + _'] : []"
          autofocus
          class="mb-3"
        />

        <div class="mb-3">
          <VBtn
            prepend-icon="ri-upload-2-line"
            variant="outlined"
            @click="fileInput?.click()"
          >
            {{ file ? file.name : 'Choose PDF' }}
          </VBtn>
          <div v-if="file" class="text-caption text-medium-emphasis mt-1">
            {{ (file.size / (1024 * 1024)).toFixed(2) }} MB
          </div>
          <input
            ref="fileInput"
            type="file"
            accept="application/pdf"
            class="d-none"
            data-testid="bagrut-file-input"
            @change="onFilePick"
          >
        </div>

        <VAlert v-if="error" type="error" variant="tonal" density="compact" class="mb-3">
          {{ error }}
        </VAlert>

        <!-- Result -->
        <div v-if="result" class="mt-4">
          <VDivider class="mb-3" />
          <div class="text-subtitle-2 mb-2">
            Ingestion complete: {{ result.pdfId }}
          </div>
          <div class="d-flex flex-wrap gap-1 mb-3">
            <VChip size="small" color="primary" variant="tonal">
              {{ result.questionsExtracted }} drafts
            </VChip>
            <VChip size="small" variant="tonal">
              {{ result.totalPages }} pages
            </VChip>
            <VChip v-if="result.figuresExtracted > 0" size="small" variant="tonal">
              {{ result.figuresExtracted }} figures
            </VChip>
          </div>

          <div v-if="result.warnings.length > 0" class="mb-3">
            <div class="text-caption text-medium-emphasis mb-1">
              Warnings
            </div>
            <div class="d-flex flex-wrap gap-1">
              <VChip
                v-for="w in result.warnings"
                :key="w"
                size="small"
                :color="warningSeverity(w)"
                variant="tonal"
              >
                {{ w }}
              </VChip>
            </div>
          </div>

          <div v-if="result.drafts.length > 0">
            <div class="text-caption text-medium-emphasis mb-1">
              Drafts
            </div>
            <VList density="compact">
              <VListItem
                v-for="d in result.drafts"
                :key="d.draftId"
                :subtitle="`Page ${d.sourcePage} · confidence ${Math.round(d.extractionConfidence * 100)}%`"
              >
                <template #title>
                  <code class="text-caption">{{ d.draftId }}</code>
                </template>
              </VListItem>
            </VList>
          </div>
        </div>
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="close">
          {{ result ? 'Close' : 'Cancel' }}
        </VBtn>
        <VBtn
          v-if="!result"
          color="primary"
          :disabled="!canSubmit"
          :loading="uploading"
          @click="submit"
        >
          Ingest
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
