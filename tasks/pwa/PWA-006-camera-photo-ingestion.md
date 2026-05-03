# PWA-006: Camera Access + Photo Ingestion Pipeline (PWA)

## Goal
Implement the photo capture → upload → LaTeX pipeline for the PWA student app using browser APIs (`getUserMedia` and `<input capture>`). The student photographs a math problem from a textbook or worksheet, and the system extracts LaTeX, feeds it through the CAS pipeline, and creates a solvable question. Must work on iOS Safari 16+ and Android Chrome.

## Context
- Architecture doc: `docs/research/cena-question-engine-architecture-2026-04-12.md` §27-34 (photo ingestion security)
- PWA approach doc: `docs/research/cena-mobile-pwa-approach.md` §2.3
- Depends on: PWA-001 (Service Worker for upload queuing), PWA-004 (offline queue)
- The ephemeral image pipeline (1.5s volatile, no disk) is server-side — this task handles the client capture and upload
- COPPA 2025 / GDPR-K compliant: no client-side image storage, EXIF stripping before upload, no face detection client-side

## Scope of Work

### 1. Camera Capture Component
Create `src/student/full-version/src/components/PhotoCapture.vue`:

Two capture methods (user chooses):

**Method A: Live Camera (`getUserMedia`)**
```typescript
const stream = await navigator.mediaDevices.getUserMedia({
  video: {
    facingMode: 'environment',    // Rear camera
    width: { ideal: 1920 },
    height: { ideal: 1080 },
    aspectRatio: { ideal: 4/3 }
  }
});
```
- Show live preview in a `<video>` element
- Overlay: alignment guide rectangle (dashed border, ~70% of viewport) with text "Position the problem inside the frame"
- Capture button: large, centered, 64×64px, accessible
- After capture: show preview with "Use this photo" / "Retake" buttons
- Auto-close camera stream after capture (release hardware)

**Method B: File Input (`<input capture>`)**
```html
<input type="file" accept="image/*" capture="environment" />
```
- Fallback for browsers that don't support `getUserMedia` (rare but possible)
- Opens native camera app, returns the photo
- Less control (no alignment guide) but universally supported

**Auto-detect**: Try `getUserMedia` first. If `NotAllowedError` or `NotFoundError`, fall back to file input. Store preference in `localStorage`.

### 2. Client-Side Preprocessing (Before Upload)
Create `src/student/full-version/src/services/imagePreprocess.ts`:

1. **EXIF stripping**: Remove ALL EXIF metadata (location, device info, timestamps) using a lightweight EXIF parser. This is a COPPA/GDPR-K requirement — no metadata leaves the device.
2. **Resize**: If image > 1920px on longest edge, resize down. Use `<canvas>` for resize.
3. **Compression**: JPEG quality 85% (balance between OCR accuracy and upload size). Target: < 500KB per image.
4. **Client-side quality check**:
   - Reject if image is too dark (average luminance < 40) → "Photo is too dark — try better lighting"
   - Reject if image is too blurry (Laplacian variance < threshold) → "Photo is blurry — hold steady and try again"
   - Reject if image is too small (< 200×200px) → "Photo is too small — move closer"
5. **No face detection client-side** — this is done server-side (§30, tier 2: Cloud Vision). The client does not inspect image content beyond quality metrics.

### 3. Upload with Progress
Create `src/student/full-version/src/services/photoUpload.ts`:

```typescript
async function uploadPhoto(
  imageBlob: Blob,
  sessionId: string,
  onProgress: (percent: number) => void
): Promise<PhotoUploadResult> {
  const formData = new FormData();
  formData.append('image', imageBlob, 'capture.jpg');
  formData.append('sessionId', sessionId);

  const response = await fetch('/api/photo/upload', {
    method: 'POST',
    body: formData,
    headers: { Authorization: `Bearer ${token}` },
    // Progress via ReadableStream or XMLHttpRequest
  });
  // ...
}
```

- Show upload progress bar (important on slow mobile networks)
- Timeout: 30 seconds (Gemini processing can take 1-2s, but upload is the bottleneck)
- **Offline handling**: If offline, queue in IndexedDB (from PWA-004 offline queue), show "Will process when online"
- **Retry**: On network error, retry once after 3 seconds. On second failure, queue for later.

### 4. Result Display
After server processes the image and returns LaTeX:

```typescript
interface PhotoUploadResult {
  success: boolean;
  latex?: string;           // Extracted LaTeX expression
  confidence: number;       // 0-1, from Gemini/Mathpix
  questionCreated?: boolean;// Whether a question was auto-created
  error?: string;           // If failed: 'too_dark' | 'no_math_found' | 'moderation_blocked' | 'processing_error'
}
```

- Show extracted LaTeX rendered via KaTeX for student confirmation: "Is this what you photographed?"
- If confidence < 0.8: "I'm not sure I read this correctly — you can edit the expression or retake"
- Provide a MathLive/text input for the student to correct the extracted LaTeX
- On confirmation: proceed to step-solver flow with this question

### 5. Permission Handling
- Request camera permission with a pre-permission dialog: "Cena needs your camera to photograph math problems. We never store your photos."
- If permission denied: show instructions to enable in browser settings
- If permission previously granted: skip pre-permission dialog
- Track permission state in `localStorage` to avoid re-asking

### 6. Accessibility
- Camera button: `aria-label="Capture photo of math problem"`
- Alignment guide: `aria-hidden="true"` (visual-only)
- Preview: `aria-label="Preview of captured photo"`
- Progress: `aria-live="polite"`, `role="progressbar"`
- All error messages: announced via `aria-live="assertive"`

## Files to Create/Modify
- `src/student/full-version/src/components/PhotoCapture.vue`
- `src/student/full-version/src/components/PhotoPreview.vue`
- `src/student/full-version/src/components/LaTeXConfirmation.vue`
- `src/student/full-version/src/services/imagePreprocess.ts`
- `src/student/full-version/src/services/photoUpload.ts`
- `src/student/full-version/src/composables/useCameraPermission.ts`

## Non-Negotiables
- **EXIF stripping is mandatory** — no metadata must reach the server. This is a legal requirement (COPPA 2025, GDPR-K, Israeli PPL Amendment 13)
- **No client-side image storage** — the Blob exists in memory only, never in IndexedDB or localStorage. The offline queue stores the upload request metadata, not the image data. If offline, the student must retake when online.
- **Camera stream must be released** — `stream.getTracks().forEach(t => t.stop())` after capture. Leaving the camera active drains battery and shows the recording indicator.
- **Pre-permission dialog before browser permission prompt** — cold permission prompts have high denial rates
- **Confidence threshold visible to student** — don't silently accept low-confidence OCR results

## Acceptance Criteria
- [ ] Camera opens with rear-facing default on iOS Safari and Android Chrome
- [ ] Alignment guide overlay visible during capture
- [ ] EXIF metadata stripped (verify with EXIF reader tool on the uploaded file server-side)
- [ ] Images resized to ≤1920px and compressed to <500KB
- [ ] Too-dark / too-blurry / too-small images rejected with user-friendly message
- [ ] Upload progress shown during upload
- [ ] Extracted LaTeX rendered for student confirmation
- [ ] Student can edit extracted LaTeX before proceeding
- [ ] Camera stream released after capture (no recording indicator)
- [ ] Offline: "Will process when online" message (not an error)
- [ ] Permission denied: clear instructions to re-enable
- [ ] All strings i18n'd (Arabic + Hebrew)
- [ ] All interactive elements accessible

## Testing Requirements
- **Unit**: `imagePreprocess.ts` — test resize, compression, quality checks (use test images)
- **Unit**: `photoUpload.ts` — test progress, timeout, retry, offline detection
- **Unit**: `useCameraPermission.ts` — mock permission states
- **Integration**: Playwright — mock `getUserMedia`, test capture flow, verify EXIF stripping
- **Manual (REQUIRED)**: Real device camera testing on iOS Safari + Android Chrome — camera behavior is radically different between browsers

## DoD
- PR merged to `main`
- EXIF stripping verification screenshot (before/after metadata comparison)
- Real device camera capture video (iOS + Android)
- File size distribution chart (10 test images: original vs processed)

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-camera-photo,avg_file_size_kb=<n>,exif_stripped=<yes>,devices_tested=<n>`
