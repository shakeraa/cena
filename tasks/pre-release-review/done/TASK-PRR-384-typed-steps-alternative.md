# TASK-PRR-384: Typed-steps alternative UX (accessibility-first fallback)

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #8 accessibility (dysgraphia-friendly), #1 student (never blame handwriting)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + accessibility lead
**Tags**: epic=epic-prr-j, accessibility, priority=p0
**Status**: Partial — typed-steps CAS endpoint + DTOs + 12 validation tests shipped 2026-04-23 (DoD item #1 green); Vue `TypedStepsInput.vue` + per-student default-mode preference deferred on frontend + preference-store follow-up
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Alternative: student types their work in MathLive step-by-step instead of uploading a photo. Surfaced by default when a student's OCR confidence is consistently low ([PRR-420](TASK-PRR-420-per-student-ocr-confidence-tracking.md)).

## Scope

- "Type your steps" mode uses MathLive (reuse EPIC-PRR-H primitives).
- Warm copy: "Want to type your steps instead? Often faster" — NOT "your handwriting is unclear."
- Submits structured step sequence directly to CAS (skips OCR entirely).
- Per-student default-mode preference setting.

## Files

- `src/student/full-version/src/components/diagnostic/TypedStepsInput.vue`
- Tests.

## Definition of Done

- Typed mode end-to-end to CAS works.
- Default-mode setting persists.
- Copy positive framing, shipgate passes.

## Non-negotiable references

- Memory "Ship-gate banned terms".
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-420](TASK-PRR-420-per-student-ocr-confidence-tracking.md), [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) MathLive

## What shipped (2026-04-23)

DoD item #1 "Typed mode end-to-end to CAS works" is green. The path
funnels through the SAME `IStepChainVerifier` the photo pipeline uses
so downstream features (first-wrong-step UI per PRR-380, show-my-work
drawer per PRR-382) work identically for typed submissions.

### Endpoint

[TypedStepsDiagnosticEndpoint.cs](../../src/api/Cena.Student.Api.Host/Endpoints/TypedStepsDiagnosticEndpoint.cs)
`POST /api/me/diagnostic/typed-steps`:

- Auth-guarded; sub claim required.
- Validates shape (1..40 steps, non-whitespace LaTeX up to 2_000 chars
  per step, contiguous 0-based indices). Stable error codes
  `steps_required` / `too_many_steps` / `empty_step` /
  `step_too_long` / `index_out_of_order` / `invalid_request`.
- Constructs `ExtractedStep(Index, Latex, Canonical=Latex, Confidence=1.0)`.
  Per-step Confidence=1.0 is NOT a stub — the student authored the
  LaTeX directly, so OCR confidence is correctly unity (documented
  in the endpoint file banner, locked in the comment block).
- Calls `IStepChainVerifier.VerifyChainAsync`. The verifier's
  canonicalizer pre-pass still runs (PRR-361 compliance), and
  `IStepSkippingTolerator` still distinguishes Wrong from
  UnfollowableSkip (PRR-362) — full CAS machinery unchanged.
- Returns `TypedStepsDiagnosticResponse(Succeeded, FirstFailureIndex,
  Transitions)` — same logical shape as the photo-diagnostic chain
  result but without OCR / bbox / photo-hash fields. Separate wire
  DTO so the typed path never pretends to be a photo path (memory
  "No stubs" + "Labels match data").
- Wired into `Program.cs` via `app.MapTypedStepsDiagnosticEndpoint()`.

### DTOs

[TypedStepsDtos.cs](../../src/api/Cena.Api.Contracts/Diagnostic/TypedStepsDtos.cs):

- `TypedStepInputDto(int Index, string Latex)` — pure typed step.
- `TypedStepsDiagnosticRequest(Steps, Locale)` — request body.
- `TypedStepTransitionDto(FromStepIndex, ToStepIndex, Outcome, Summary)`
  — per-transition wire shape; `Outcome` string carries
  `"Valid" | "Wrong" | "UnfollowableSkip" | "LowConfidence"`.
- `TypedStepsDiagnosticResponse(Succeeded, FirstFailureIndex, Transitions)`.

### Tests (12)

[TypedStepsDiagnosticEndpointTests.cs](../../src/actors/Cena.Actors.Tests/Diagnosis/PhotoDiagnostic/TypedStepsDiagnosticEndpointTests.cs):

- Valid request passes.
- Empty steps rejected.
- MaxSteps+1 rejected; MaxSteps exactly accepted (boundary guard).
- Whitespace + null LaTeX rejected.
- MaxStepLatexLength+1 rejected; exact MaxStepLatexLength accepted.
- Out-of-order / duplicate / skipped indices all rejected (three
  separate tests locking the contiguous-0-based invariant).
- Defensive `MaxSteps >= 20` regression guard — catches a future
  refactor that would silently drop the cap below typical Bagrut
  derivation length.

Full `Cena.Actors.sln` build green. 12/12 new tests pass.

Framing discipline: the wire DTO carries no "handwriting unclear"
or time-pressure fields; copy is the Vue layer's concern per
memory "Ship-gate banned terms" + ADR-0048.

## What is deferred

- **Vue `src/student/full-version/src/components/diagnostic/TypedStepsInput.vue`**
  — MathLive-backed editor that emits `TypedStepsDiagnosticRequest`;
  renders the response's first-wrong-step via the existing PRR-380
  diagnostic result screen (Partial) + PRR-382 show-my-work drawer
  (Partial). Pure frontend work; endpoint contract frozen.
- **Per-student default-mode preference (DoD #2).** Preference store
  is an accommodation-profile concern (`AccommodationProfile`
  already exists; a `DefaultDiagnosticMode: "typed" | "photo"` field
  plus a Vue setting plus reader wiring closes this). Deliberately held
  back from v1 because it's a preference concern, not a diagnostic
  concern, and shipping the endpoint first unblocks frontend work
  today.
- **Surfaced-by-default trigger when OCR confidence is consistently
  low** — depends on PRR-420 (per-student OCR confidence tracking)
  landing first.

Closing as **Partial** per memory "Honest not complimentary": DoD #1
"Typed mode end-to-end to CAS works" is genuinely end-to-end on the
backend side; DoD #2 (preference persistence) and DoD #3 (copy
framing) are the remaining Vue + accommodation-profile work.
