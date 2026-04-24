# TASK-PRR-413: Face + name + school-logo redaction before OCR

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #5 compliance (incidental PII minimization)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + ML-engineer
**Tags**: epic=epic-prr-j, privacy, priority=p0
**Status**: Partial — heuristic-only slice shipped 2026-04-23; face + logo + structured-image decoding deferred (see "Progress log" below)
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Before the photo goes to the OCR vendor, detect + redact faces (if photo includes student's face), handwritten names in margins, school-logo / letterhead. Reduces incidental PII exposure to vendor.

## Scope

- Face detection (local model, not vendor-dependent).
- Handwritten-name heuristics (corner / margin text near top-right or top-left, especially if Hebrew / Arabic name pattern).
- Logo/letterhead detection (top-of-page banner regions).
- Redaction = solid-color blur overlay before vendor submission.
- Audit log of what was redacted.

## Files

- `src/backend/Cena.Diagnostic/Intake/PhotoRedactor.cs`
- Tests.

## Definition of Done

- Faces redacted when present.
- Top-margin name-patterns redacted.
- Logos/letterhead regions redacted.
- Math content preserved.

## Non-negotiable references

- PPL Amendment 13 (minimize biometric exposure).
- Israeli Privacy Law.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-410](TASK-PRR-410-ocr-vendor-dpa.md), [PRR-412](TASK-PRR-412-photo-deletion-sla.md)

## Progress log

**2026-04-23 — Heuristic-only slice shipped** (branch `claude-subagent-prr413/prr-413-redaction-heuristic`).

Scope landed:

- `IPhotoRedactor` interface at `src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/Intake/IPhotoRedactor.cs` (note: path is `Cena.Actors/Diagnosis/PhotoDiagnostic/Intake/` rather than the placeholder `Cena.Diagnostic/Intake/` in the original Files section — matches the actual bounded-context home of every other PhotoDiagnostic file).
- `HeuristicPhotoRedactor` — byte-level zero-fill of top 10% + bottom 10% for raw-grayscale MIME (`application/octet-stream` or `image/x-cena-raw-gray8`). Math content (center) preserved; this bit of DoD ✔ for raw inputs.
- `NoopPhotoRedactor` — null-object default (mirrors `NoopPhotoBlobStore`); emits `redaction_not_configured` tag so misconfigured environments are visible in the audit log.
- `IPhotoRedactionAuditLog` + `LoggingPhotoRedactionAuditLog` — structured per-diagnostic audit trail (applied + deferred kinds + method histogram). Marten-backed persistence is a clean follow-up; log signal is the seam.
- DI wiring in `PhotoDiagnosticServiceRegistration.AddSharedServices` (TryAddSingleton both ports so hosts opt in via `Replace`).
- 16 unit tests (5 Noop, 11 heuristic) — `dotnet test` green under the full `Cena.Actors.sln` build.

Explicitly deferred (each surfaces as a `DeferredRedactions` tag so the audit log is honest):

1. `face_detection_not_implemented` — needs ONNX model + license review + tuning corpus. Re-binding the same interface is a one-line DI change when the model lands.
2. `logo_template_matcher_not_implemented` — needs curated school-letterhead corpus + feature-matcher library.
3. `structured_image_decoder_required` — margin redaction on `image/jpeg` / `image/png` / `image/webp` / `image/heic` needs an image decoder (ImageSharp / Magick.NET). Byte-range zeroing a compressed file corrupts rather than redacts, so the heuristic returns input unchanged + logs the gap.

DoD status per original bullets:

- Faces redacted when present → **deferred** (follow-up).
- Top-margin name-patterns redacted → **partial** (raw-grayscale only; structured-image deferred).
- Logos/letterhead regions redacted → **deferred** (follow-up).
- Math content preserved → **✔** (center rows untouched on raw path; all bytes untouched on structured path).
- Audit log → **✔** (log-based implementation shipped; Marten-backed persistence follow-up).

Follow-up tasks to queue:

- **PRR-413-F1** (Faces via ML): ship ONNX-backed face detector, wire as a replacement `IPhotoRedactor` that composes `HeuristicPhotoRedactor` + face-region overlay. Needs model-licensing review.
- **PRR-413-F2** (Structured decoder): add `ImageSharp` dependency (or equivalent) + ship `DecoderBackedPhotoRedactor` that decodes → blurs → re-encodes, superseding the heuristic's byte-level path for JPEG/PNG.
- **PRR-413-F3** (Logos): curated letterhead corpus + `TemplateMatchingLogoRedactor`.
- **PRR-413-F4** (Marten audit): persist `PhotoRedactionResult` rows onto a `PhotoRedactionAuditDocument` stream for the incidental-PII compliance dashboard.
