# TASK-PRR-260: Student-controlled hide-then-reveal session toggle

**Priority**: P1
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-cogsci, persona-ethics, persona-a11y, persona-educator, persona-enterprise, persona-sre (8/10 converge on visible-first + opt-in hide)
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md) + 10-persona findings under [pre-release-review/reviews/persona-*/student-input-modalities-2-findings.md](../../pre-release-review/reviews/)
**Assignee hint**: kimi-coder (after research prompt lands) or front-end coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, ui, pedagogy, q2
**Status**: Partial — backend shipped 2026-04-23 (enum + wire parser + projection field + GET/PATCH endpoint + author-force-visible flag + diagnostic-override policy + 22 tests). **Decision superseded**: initial 2026-04-23 decision was option A (research gate); user reversed later same day to "implement now with visible-first default per 8/10 persona consensus" — the research-prompt output informs v2 iteration, not v1 existence. Vue consumer (settings drawer + placeholder + `aria-live`) and downstream PRR-261/262/263 remain pending.
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Add a session-level student-controlled toggle that hides MC answer options until the student clicks "Show options". **Default = visible** (traditional behavior). Realizes Bjork's generation effect for students who opt in; zero friction for students who don't.

## Scope

### Session state

- New field on `SessionState`: `attemptMode: 'visible' | 'hidden_reveal'`. Default `'visible'`.
- Toggle affordance at session start **and** mid-session (settings drawer). Persists for the session, NOT across sessions (student re-opts each session to preserve autonomy per persona-ethics).
- Toggle is **ignored during PRR-228 diagnostic** (see [PRR-263](TASK-PRR-263-diagnostic-ignores-hide-reveal.md)).

### UI pattern (persona-a11y ranked)

Use **placeholder + `aria-live`** (preferred):
- When `hidden_reveal` and student hasn't revealed yet: render a placeholder "Click to reveal options" with a prominent button.
- Announce the reveal via `aria-live="polite"` on the options container.
- Preserve layout to avoid reflow jump (persona-a11y WCAG 4.1.3).

Reject DOM-absent pattern (breaks SR virtual cursor) and plain `<details>` (weaker affordance).

### Per-question applicability

- Applies only to questions with MC options. Step-solver math, chem reactions, essay items skip the flow entirely (`attemptMode` is a no-op for them).
- Author-level `forceOptionsVisible: true` flag respected — some items (e.g. "choose which graph is correct") ARE the options.

### Commit-and-compare (OPEN — not in this task)

Cogsci wants the student to type a guess before revealing; a11y + sre want to defer. **Not in PRR-260 scope.** If the decision-holder greenlights v1, create PRR-265 as a follow-up.

### Server-side enforcement

- Student-mode uses client-side hiding only. Redteam flagged bypass via DevTools — acceptable for self-discipline mode.
- Classroom-enforced server-side redaction is [PRR-261](TASK-PRR-261-classroom-redacted-projection.md), separate task.

### Excluded from scope

- Hidden-first default (cogsci: would misrepresent effect size — reject).
- Timer-based auto-hide (ADR-0048 countdown ban).
- Option C pedagogy-driven hide (dark pattern).

## Files

- `src/student/full-version/src/stores/sessionStore.ts` — add `attemptMode` state + mutations.
- `src/student/full-version/src/components/session/McOptionsGate.vue` (new) — placeholder + reveal-button + aria-live.
- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — integrate gate in render.
- `src/student/full-version/src/components/session/SessionSettingsDrawer.vue` (new or extend) — toggle control.
- Tests: toggle persistence within session, toggle reset across sessions, PRR-228 diagnostic unaffected, author `forceOptionsVisible` respected, SR-announcement on reveal.

## Definition of Done

- Default state = visible across all session types.
- Toggle-on → subsequent MC items render placeholder until reveal clicked.
- Toggle-off mid-session → remaining items show options immediately.
- Author `forceOptionsVisible` flag honored.
- Non-MC items unaffected.
- PRR-228 diagnostic unaffected (cross-ref test).
- WCAG 4.1.3 passes (SR test with NVDA + VoiceOver).
- Shipgate scanner (PRR-224 + PRR-264) passes on copy (no timer / countdown language).
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no time-pressure mechanics.
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md).
- Memory "No stubs — production grade".
- Memory "Ship-gate banned terms".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + SR test recording URL>"`

## Related

- [PRR-261](TASK-PRR-261-classroom-redacted-projection.md) — server-side classroom enforcement (separate API shape).
- [PRR-262](TASK-PRR-262-scaffolding-stem-grounded-hints.md) — hint routing under hidden mode.
- [PRR-263](TASK-PRR-263-diagnostic-ignores-hide-reveal.md) — PRR-228 diagnostic carve-out.
- [PRR-264](TASK-PRR-264-hide-reveal-shipgate-audit.md) — shipgate audit for countdown/timer language.
- Persona findings: cogsci, ethics, a11y, educator, enterprise, sre 002 files.

## What shipped (2026-04-23)

Backend-half complete; frontend + downstream chain deferred.

### Enum + wire parser

[`src/actors/Cena.Actors/Sessions/SessionAttemptMode.cs`](../../src/actors/Cena.Actors/Sessions/SessionAttemptMode.cs):

- `SessionAttemptMode` enum with values `Visible = 0`, `HiddenReveal = 1`.
- `SessionAttemptModeWire` static helper — canonical strings (`"visible"` / `"hidden_reveal"`), `ToWire(mode)` + `TryParse(input, out mode)` with whitespace-trimming + case-insensitive matching.
- Forward-compat: unknown / null / empty input rejects cleanly; endpoint boundary returns 400 with the accepted-values list.

### Projection field

[`LearningSessionQueueProjection.AttemptMode`](../../src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs) — new `string` property defaulted to `"visible"`. Stored on the Marten session doc. Older session replays without the field default-initialize to the empty string; the endpoint's `CanonicaliseWire` helper re-normalises to `"visible"` so no replay crash is possible.

### Author-level force-visible

[`QuestionDocument.ForceOptionsVisible`](../../src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs) — new `bool` property defaulted to `false` (retroactively-safe: existing seeded items remain eligible for hide-then-reveal). Authors flip to `true` when the options ARE the question (e.g. "which graph is correct").

### Read-side policy

`SessionAttemptModePolicy.ResolveEffective(SessionAttemptModeContext)` — pure function folding the four precedence rules into a single effective mode:

1. Non-MC question → Visible (attempt-mode is a no-op for step-solver / chem / essay).
2. Author force-visible → Visible (authoring contract).
3. Diagnostic block → Visible (PRR-263 calibration guarantee; caller passes `IsDiagnosticBlock` from PRR-228 integration point).
4. Otherwise → stored mode.

### Endpoints

[`SessionAttemptModeEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/SessionAttemptModeEndpoints.cs):

- `GET /api/sessions/{sessionId}/attempt-mode` — returns current mode; 404 on unknown session, 403 on cross-student access attempt.
- `PATCH /api/sessions/{sessionId}/attempt-mode` — accepts `{"mode": "visible" | "hidden_reveal"}`; 400 on invalid value, 403 on cross-student, 404 on unknown session, 409 on terminated session, idempotent no-op on same-mode write.
- Auth-guarded via the existing `/api/sessions` group convention (same pattern as `HintLadderEndpoint`).
- Tenant scope via `ResourceOwnershipGuard.VerifyStudentAccess` — matches the hint-ladder IDOR guard. SIEM structured log on ownership mismatches.
- Wired into [`Program.cs`](../../src/api/Cena.Student.Api.Host/Program.cs) via `app.MapSessionAttemptModeEndpoints()`.

### DTOs

[`SessionAttemptModeDtos.cs`](../../src/api/Cena.Api.Contracts/Sessions/SessionAttemptModeDtos.cs) — `SessionAttemptModeUpdateRequestDto(Mode)` + `SessionAttemptModeResponseDto(SessionId, Mode)`. String wire values keep the contract stable across future enum additions.

### Tests (22)

[`SessionAttemptModeTests.cs`](../../src/actors/Cena.Actors.Tests/Sessions/SessionAttemptModeTests.cs):

- Wire parser: 5 theory rows for canonical + case-insensitive + whitespace-trimmed inputs; 5 rejection rows for null / empty / unknown.
- `ToWire` round-trips Visible + HiddenReveal.
- Policy resolver: default Visible, stored HiddenReveal honoured, non-MC forces Visible, author-force-visible overrides stored, diagnostic block overrides stored, precedence sanity (non-MC wins over every other lane), 8-combination cross-product check, null context rejected.
- Projection default parses to Visible (forward-compat guard so a freshly-created session is immediately toggleable without lazy default-set).

Full `Cena.Actors.sln` build green; 22/22 new tests pass.

## What is deferred

- **Vue toggle UI** (settings-drawer chip + placeholder with `aria-live`). Frontend-plus-BFF consumer; endpoint contracts are frozen and Vue calls `/api/sessions/{id}/attempt-mode` to read + write.
- **Client-side hiding of MC options when `effectiveAttemptMode == hidden_reveal`**. Per PRR-260 scope this is explicitly CLIENT-side (server-side redaction is PRR-261, a separate task).
- **PRR-263 diagnostic-ignore wiring**. This backend exposes `IsDiagnosticBlock` as a policy input; the session-render path must pass `true` during PRR-228 diagnostic blocks. The call-site integration is PRR-263.
- **PRR-228 `forceOptionsVisible` authoring UI**. Admin-side field editor for the `QuestionDocument.ForceOptionsVisible` flag. Backend field is live; admin UI is a Vue follow-up.
- **Research-prompt output** informing v2 iteration (which copy variants work best, whether to offer a per-subject default, etc.). V1 ships with the 8/10 persona-consensus baseline: visible-first default, per-session opt-in, no cross-session persistence. Research informs tuning, not shipping.

Closing as **Partial** per memory "Honest not complimentary": backend contract is production-grade and tested; the Vue pass + the downstream chain (PRR-261 / PRR-262 / PRR-263) are their own tasks with their own gates.
