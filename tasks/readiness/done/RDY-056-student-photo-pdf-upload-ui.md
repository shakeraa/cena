# RDY-056: Student Photo + PDF Upload UI (PWA)

**Parent**: [RDY-019-ocr-spike.md](done/RDY-019-ocr-spike.md)
**Related**: [RDY-OCR-PORT](done/RDY-OCR-PORT.md), Phase 2.1 `fa74e46`, Phase 2.2 `f20a897`
**Priority**: High — closes the single remaining student-side gap on the
end-to-end OCR pipeline
**Complexity**: Mid frontend
**Effort**: 1–2 days
**Blocker status**: None — every backend dependency is on main and green.

## Problem

Phase 2.1 shipped `POST /api/student/photo/capture` (8 tests) and
Phase 2.2 shipped `POST /api/student/photo/upload` (PDF + image, 13
tests). The student PWA has:

- A `useCamera.ts` composable (PWA-006) — camera access, blob capture,
  resize/compress to ≤ 500 KB JPEG 0.8, EXIF-auto-correct.
- …and **zero components that import it**.

Result: a student using `src/student/full-version/` today cannot reach
the photo/PDF ingestion flow through the UI. The backend is wired to
the cascade, CAS-validates every math block, and handles CSAM / low
confidence / circuit-open / encrypted-PDF — all unreachable from the
PWA.

## Scope

### 1. Photo capture page

New file: `src/student/full-version/src/pages/tutor/photo-capture.vue`.

- Uses `useCamera()` for live camera + file-input fallback.
- Preview captured blob + retake / confirm actions.
- On confirm, POSTs to `/api/student/photo/capture` as multipart-form
  (field name: `photo`).
- Hebrew/Arabic-aware error messages; math always LTR via `<bdi
  dir="ltr">` (per memory:math_always_ltr).
- Renders the four backend outcomes the endpoint tests verify:
  - 200 → recognised LaTeX + session-start CTA
  - 422 (low confidence / CAS fail) → human-review queued banner
  - 403 (moderation block) → friendly "this image can't be used"
  - 503 (circuit open) → retry later

### 2. PDF upload page

New file: `src/student/full-version/src/pages/tutor/pdf-upload.vue`.

- File picker + drag-drop.
- Accept `application/pdf`, `image/jpeg`, `image/png`, `image/webp`.
- 20 MB client-side cap matching the Phase 2.2 server cap.
- POSTs to `/api/student/photo/upload` as multipart-form.
- Renders the PDF triage outcomes:
  - `encrypted_pdf` → "please remove the password and retry"
  - `processed_text_shortcut` → green "text layer extracted" badge
  - `processed_empty` → "no math detected" banner with retake CTA
  - `queued_for_review` → reassure + ETA
- Shows extracted LaTeX (sanitized by the backend) under an LTR `<bdi>`.

### 3. Route + navigation

- Add routes `tutor/photo-capture` and `tutor/pdf-upload` to the
  student router.
- Add entry in the tutor nav (or the existing "ask a question" flow).

### 4. Tests

- Component tests: Vitest + @testing-library/vue for the three error
  states + the happy path (mock `$api`).
- Optional: Playwright E2E covering camera-fallback → upload → confirm.

## Files to Modify

- New: `src/student/full-version/src/pages/tutor/photo-capture.vue`
- New: `src/student/full-version/src/pages/tutor/pdf-upload.vue`
- Edit: `src/student/full-version/src/navigation/...` (add nav entries)
- Edit: `src/student/full-version/src/router/...` (add routes)
- New: `src/student/full-version/src/pages/tutor/__tests__/photo-capture.test.ts`

## Acceptance Criteria

- [ ] Student can open the PWA, navigate to photo-capture, grant camera,
      capture, confirm → backend receives the image + returns LaTeX
- [ ] PDF upload accepts password-protected PDFs and renders the
      `encrypted_pdf` banner
- [ ] All four backend outcomes render distinct UI states
- [ ] Math in all rendered LaTeX is wrapped in `<bdi dir="ltr">`
- [ ] Component tests green
- [ ] No new backend work — this is UI-only

## Coordination notes

- Camera composable exists; do NOT rewrite. Import from
  `@/composables/useCamera`.
- Match the existing Vuexy design tokens in `@/plugins/vuetify`.
- Per the design non-negotiable in CLAUDE.md: no streaks, no loss-
  aversion copy, no variable-ratio rewards on the upload / result
  flows.
