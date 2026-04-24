<!-- =============================================================================
     Cena Platform — Student Photo Capture (RDY-056)

     Drives POST /api/student/photo/capture through useCamera.
     Renders all four backend outcomes:
       200 → recognised LaTeX + "start tutor session" CTA
       403 → friendly "this image can't be used" (CSAM / moderation)
       422 → human-review queued banner (low confidence / CAS fail)
       503 → retry-later banner (circuit open)

     Math is always wrapped in <bdi dir="ltr"> even inside RTL locales.
============================================================================= -->
<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useCamera, type CaptureResult } from '@/composables/useCamera'
import { $api } from '@/utils/api'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.photoCapture',
    breadcrumbs: true,
  },
})

interface PhotoCaptureResponse {
  recognizedLatex: string
  confidence: number
  originalImageId: string
  warnings: string[]
  moderationVerdict?: string | null
  boundingBoxes: Array<{ x: number; y: number; width: number; height: number; extractedLatex: string }>
}

type Outcome =
  | { kind: 'idle' }
  | { kind: 'capturing' }
  | { kind: 'previewing'; capture: CaptureResult }
  | { kind: 'uploading' }
  | { kind: 'ok'; data: PhotoCaptureResponse }
  | { kind: 'blocked'; reason: string }
  | { kind: 'review'; warnings: string[] }
  | { kind: 'retry_later' }
  | { kind: 'error'; message: string }

const { t } = useI18n()
const router = useRouter()
const cam = useCamera()
const videoEl = ref<HTMLVideoElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const outcome = ref<Outcome>({ kind: 'idle' })

async function startCamera() {
  outcome.value = { kind: 'capturing' }
  const stream = await cam.startCamera('environment')
  if (stream && videoEl.value) {
    videoEl.value.srcObject = stream
    await videoEl.value.play().catch(() => {})
  }
  else if (!stream) {
    outcome.value = { kind: 'error', message: cam.error.value ?? 'camera_error' }
  }
}

async function capture() {
  if (!videoEl.value) return
  const result = await cam.captureFromVideo(videoEl.value)
  if (result) {
    cam.stopCamera()
    outcome.value = { kind: 'previewing', capture: result }
  }
}

function retake() {
  outcome.value = { kind: 'idle' }
  startCamera()
}

async function handleFilePick(ev: Event) {
  const input = ev.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  const result = await cam.processFile(file)
  if (result) outcome.value = { kind: 'previewing', capture: result }
  else outcome.value = { kind: 'error', message: cam.error.value ?? 'file_error' }
}

async function upload(capture: CaptureResult) {
  outcome.value = { kind: 'uploading' }
  const form = new FormData()
  form.append('photo', capture.blob, `capture-${Date.now()}.jpg`)

  try {
    const res = await $api<PhotoCaptureResponse>('/api/student/photo/capture', {
      method: 'POST',
      body: form,
    })
    outcome.value = { kind: 'ok', data: res }
  }
  catch (e: any) {
    const status = e?.response?.status ?? e?.status
    if (status === 403) outcome.value = { kind: 'blocked', reason: e?.data?.error ?? 'blocked' }
    else if (status === 422) outcome.value = { kind: 'review', warnings: e?.data?.warnings ?? [] }
    else if (status === 503) outcome.value = { kind: 'retry_later' }
    else outcome.value = { kind: 'error', message: e?.data?.message ?? e?.message ?? 'upload_failed' }
  }
}

function startTutorSession(imageId: string, latex: string) {
  router.push({ path: '/tutor', query: { fromImage: imageId, latex } })
}

onMounted(() => {
  if (cam.isSupported.value) startCamera()
})
onBeforeUnmount(() => cam.stopCamera())
</script>

<template>
  <div class="photo-capture-page pa-4" data-testid="photo-capture-page">
    <h1 class="text-h5 mb-3">
      {{ t('photoCapture.title', 'Snap a photo of your problem') }}
    </h1>

    <!-- Camera / preview surface -->
    <VCard class="mb-4">
      <VCardText>
        <template v-if="outcome.kind === 'capturing'">
          <video
            ref="videoEl"
            class="w-100 rounded"
            style="max-height: 480px; background: #000;"
            autoplay
            playsinline
            muted
          />
          <div class="d-flex justify-center mt-3 gap-3">
            <VBtn color="primary" size="large" @click="capture">
              {{ t('photoCapture.snap', 'Take photo') }}
            </VBtn>
            <VBtn variant="text" @click="fileInput?.click()">
              {{ t('photoCapture.pickFromGallery', 'Pick from gallery') }}
            </VBtn>
          </div>
        </template>

        <template v-else-if="outcome.kind === 'previewing'">
          <img
            :src="outcome.capture.dataUrl"
            class="w-100 rounded"
            style="max-height: 480px; object-fit: contain;"
            alt="captured preview"
          />
          <div class="d-flex justify-center mt-3 gap-3">
            <VBtn color="primary" :loading="false" @click="upload(outcome.capture)">
              {{ t('photoCapture.use', 'Use this photo') }}
            </VBtn>
            <VBtn variant="text" @click="retake">
              {{ t('photoCapture.retake', 'Retake') }}
            </VBtn>
          </div>
        </template>

        <template v-else-if="outcome.kind === 'uploading'">
          <div class="text-center py-10">
            <VProgressCircular indeterminate color="primary" />
            <div class="mt-3">
              {{ t('photoCapture.uploading', 'Analysing your photo…') }}
            </div>
          </div>
        </template>

        <template v-else>
          <div class="text-center py-10 text-medium-emphasis">
            {{ t('photoCapture.cameraNotReady', 'Camera not active') }}
          </div>
          <div class="d-flex justify-center gap-3">
            <VBtn v-if="cam.isSupported.value" color="primary" @click="startCamera">
              {{ t('photoCapture.start', 'Start camera') }}
            </VBtn>
            <VBtn variant="text" @click="fileInput?.click()">
              {{ t('photoCapture.pickFromGallery', 'Pick from gallery') }}
            </VBtn>
          </div>
        </template>

        <input
          ref="fileInput"
          type="file"
          accept="image/*"
          class="d-none"
          data-testid="photo-file-input"
          @change="handleFilePick"
        >
      </VCardText>
    </VCard>

    <!-- Outcome panels -->
    <VAlert
      v-if="outcome.kind === 'blocked'"
      type="error"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-blocked"
    >
      {{ t('photoCapture.blocked', "This image can't be used. Try a cleaner photo of just the problem.") }}
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'review'"
      type="warning"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-review"
    >
      <div class="font-weight-medium">
        {{ t('photoCapture.review.title', "We're not confident enough to solve this automatically.") }}
      </div>
      <div class="text-caption mt-1">
        {{ t('photoCapture.review.queued', 'A tutor will review it shortly — you can also retake the photo for a cleaner shot.') }}
      </div>
    </VAlert>

    <VAlert
      v-if="outcome.kind === 'retry_later'"
      type="info"
      variant="tonal"
      class="mb-3"
      data-testid="outcome-retry-later"
    >
      {{ t('photoCapture.retryLater', 'Math recognition is temporarily unavailable. Please try again in a few minutes.') }}
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
      <VCardTitle>{{ t('photoCapture.ok.title', 'Here is what we read:') }}</VCardTitle>
      <VCardText>
        <!-- Math ALWAYS LTR, even inside an RTL page. -->
        <bdi dir="ltr" class="d-inline-block rounded pa-2" style="background: rgba(0,0,0,0.04); font-family: monospace;">
          {{ outcome.data.recognizedLatex }}
        </bdi>
        <div class="text-caption mt-2 text-medium-emphasis">
          {{ t('photoCapture.ok.confidence', 'Confidence:') }}
          {{ Math.round(outcome.data.confidence * 100) }}%
        </div>
        <VBtn
          class="mt-3"
          color="primary"
          @click="startTutorSession(outcome.data.originalImageId, outcome.data.recognizedLatex)"
        >
          {{ t('photoCapture.ok.startTutor', 'Start tutoring session') }}
        </VBtn>
      </VCardText>
    </VCard>
  </div>
</template>
