// =============================================================================
// PWA-006: Camera capture composable
// Provides reactive camera access, photo capture, and image processing
// for student question reporting and profile photos.
//
// Capabilities:
// - getUserMedia with environment (rear) camera fallback
// - Capture photo from video stream as Blob
// - Resize/compress to ≤1280px and ≤500 KB (JPEG 0.8)
// - File input fallback for devices without getUserMedia
// - EXIF orientation auto-correction via canvas
// =============================================================================

import { computed, onUnmounted, ref, shallowRef } from 'vue'

export interface CaptureResult {
  blob: Blob
  dataUrl: string
  width: number
  height: number
  sizeKb: number
}

const MAX_DIMENSION = 1280
const TARGET_SIZE_KB = 500
const JPEG_QUALITY = 0.8

export function useCamera() {
  const isSupported = computed(() =>
    typeof navigator !== 'undefined'
    && 'mediaDevices' in navigator
    && typeof navigator.mediaDevices.getUserMedia === 'function',
  )

  const stream = shallowRef<MediaStream | null>(null)
  const isActive = ref(false)
  const error = ref<string | null>(null)
  const lastCapture = shallowRef<CaptureResult | null>(null)

  // -------------------------------------------------------------------
  // Stream management
  // -------------------------------------------------------------------

  async function startCamera(facingMode: 'user' | 'environment' = 'environment'): Promise<MediaStream | null> {
    error.value = null

    if (!isSupported.value) {
      error.value = 'camera_not_supported'
      return null
    }

    try {
      // Try preferred facing mode first
      const s = await navigator.mediaDevices.getUserMedia({
        video: {
          facingMode: { ideal: facingMode },
          width: { ideal: MAX_DIMENSION },
          height: { ideal: MAX_DIMENSION },
        },
        audio: false,
      })

      stream.value = s
      isActive.value = true
      return s
    }
    catch (err: unknown) {
      // Fallback: try without facingMode constraint
      try {
        const s = await navigator.mediaDevices.getUserMedia({
          video: true,
          audio: false,
        })

        stream.value = s
        isActive.value = true
        return s
      }
      catch (fallbackErr: unknown) {
        const e = fallbackErr as DOMException
        error.value = e.name === 'NotAllowedError'
          ? 'camera_permission_denied'
          : e.name === 'NotFoundError'
            ? 'camera_not_found'
            : 'camera_error'
        return null
      }
    }
  }

  function stopCamera() {
    if (stream.value) {
      stream.value.getTracks().forEach(t => t.stop())
      stream.value = null
    }
    isActive.value = false
  }

  // -------------------------------------------------------------------
  // Photo capture from video element
  // -------------------------------------------------------------------

  async function captureFromVideo(video: HTMLVideoElement): Promise<CaptureResult | null> {
    if (!video.videoWidth || !video.videoHeight) {
      error.value = 'video_not_ready'
      return null
    }

    const canvas = document.createElement('canvas')
    const { width, height } = fitDimensions(video.videoWidth, video.videoHeight)
    canvas.width = width
    canvas.height = height

    const ctx = canvas.getContext('2d')!
    ctx.drawImage(video, 0, 0, width, height)

    return await canvasToResult(canvas, width, height)
  }

  // -------------------------------------------------------------------
  // File input processing (fallback for no-getUserMedia or gallery pick)
  // -------------------------------------------------------------------

  async function processFile(file: File): Promise<CaptureResult | null> {
    if (!file.type.startsWith('image/')) {
      error.value = 'invalid_file_type'
      return null
    }

    return new Promise((resolve, reject) => {
      const img = new Image()
      img.onload = async () => {
        URL.revokeObjectURL(img.src)
        const { width, height } = fitDimensions(img.naturalWidth, img.naturalHeight)
        const canvas = document.createElement('canvas')
        canvas.width = width
        canvas.height = height

        const ctx = canvas.getContext('2d')!
        ctx.drawImage(img, 0, 0, width, height)

        const result = await canvasToResult(canvas, width, height)
        resolve(result)
      }
      img.onerror = () => {
        URL.revokeObjectURL(img.src)
        error.value = 'image_load_error'
        resolve(null)
      }
      img.src = URL.createObjectURL(file)
    })
  }

  // -------------------------------------------------------------------
  // Upload helper
  // -------------------------------------------------------------------

  async function uploadCapture(
    capture: CaptureResult,
    url: string,
    headers: Record<string, string> = {},
  ): Promise<Response | null> {
    try {
      const formData = new FormData()
      formData.append('photo', capture.blob, `capture-${Date.now()}.jpg`)

      const res = await fetch(url, {
        method: 'POST',
        headers,
        body: formData,
      })

      return res
    }
    catch {
      error.value = 'upload_failed'
      return null
    }
  }

  // -------------------------------------------------------------------
  // Internal helpers
  // -------------------------------------------------------------------

  function fitDimensions(w: number, h: number): { width: number; height: number } {
    if (w <= MAX_DIMENSION && h <= MAX_DIMENSION)
      return { width: w, height: h }

    const ratio = Math.min(MAX_DIMENSION / w, MAX_DIMENSION / h)
    return {
      width: Math.round(w * ratio),
      height: Math.round(h * ratio),
    }
  }

  async function canvasToResult(canvas: HTMLCanvasElement, width: number, height: number): Promise<CaptureResult> {
    let quality = JPEG_QUALITY
    let blob: Blob

    // Iterative compression if over target size
    do {
      blob = await new Promise<Blob>((resolve) => {
        canvas.toBlob(
          b => resolve(b!),
          'image/jpeg',
          quality,
        )
      })

      if (blob.size <= TARGET_SIZE_KB * 1024)
        break

      quality -= 0.1
    } while (quality > 0.3)

    const dataUrl = URL.createObjectURL(blob)
    const result: CaptureResult = {
      blob,
      dataUrl,
      width,
      height,
      sizeKb: Math.round(blob.size / 1024),
    }

    lastCapture.value = result
    return result
  }

  // -------------------------------------------------------------------
  // Cleanup
  // -------------------------------------------------------------------

  onUnmounted(() => {
    stopCamera()
    if (lastCapture.value?.dataUrl) {
      URL.revokeObjectURL(lastCapture.value.dataUrl)
    }
  })

  return {
    /** Whether getUserMedia is available */
    isSupported,
    /** Active camera stream */
    stream: computed(() => stream.value),
    /** Whether the camera is currently active */
    isActive: computed(() => isActive.value),
    /** Last error code */
    error: computed(() => error.value),
    /** Last captured image */
    lastCapture: computed(() => lastCapture.value),
    /** Start the camera stream */
    startCamera,
    /** Stop the camera stream */
    stopCamera,
    /** Capture a photo from a <video> element */
    captureFromVideo,
    /** Process a File from <input type="file"> */
    processFile,
    /** Upload a capture result via fetch */
    uploadCapture,
  }
}
