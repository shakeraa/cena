# ADR-0060 — Session mode is a first-class discriminator: `exam-prep` vs `freestyle`

- **Status**: Accepted (2026-04-28 via coordinator delegation per user "do all" directive). Implementation deviation (additive ExamScope+ActiveExamTargetId, no Mode rename) merged at sha 8eadb079 — see §History. §Decision retained as historical-design record; §Invariants stay authoritative.
- **Date proposed**: 2026-04-28
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Related**:
  - [ADR-0050 (multi-target student exam plan)](0050-multi-target-student-exam-plan.md) — `ExamTarget` is the filter source for `exam-prep` mode.
  - [ADR-0048 (exam-prep time framing)](0048-exam-prep-time-framing.md) — banned UX patterns apply equally in both modes.
  - [ADR-0059 (reference browse)](0059-bagrut-reference-browse-and-variant-generation.md) — variant practice routes through `freestyle` mode.
- **Source**: User conversation 2026-04-27/28. Original gap: `MartenQuestionPool` filters only on `Subject`; the proposed "documented fallback to subject-only when active code is null" was rejected on the no-stubs rule.

---

## Context

Cena's `SessionStartRequest` (in `Cena.Api.Contracts.Sessions.SessionDtos.cs`) currently carries `Subjects[]`, `DurationMinutes`, `Mode` — where `Mode ∈ {practice, challenge, review, diagnostic}`. None of these capture the load-bearing distinction:

- A student preparing for a localized exam (Bagrut, PET, SAT) needs the question pool **scoped to their target's exam codes** (per ADR-0050: `ExamTarget.QuestionPaperCodes` for Bagrut; equivalent catalog code lists for PET / SAT).
- A student in freestyle ("I just want to study quadratics today, not Bagrut prep") needs the pool scoped only by subject + topic — exactly today's behavior in `MartenQuestionPool.LoadAsync`.

Without an explicit mode discriminator, the code path `MartenQuestionPool` takes for a student with active exam targets is ambiguous: either silently apply the filter (breaks freestyle for exam-prep students) or silently skip it (breaks exam-prep). Neither is acceptable under the "no stubs / production grade" memory.

Adjacent decisions that this discriminator unblocks:
- The reference library (ADR-0059) routes variant practice into a single-question session — that session must be `freestyle` (no exam-target weight) so the variant counts as concept practice, not target progress.
- The diagnostic at onboarding (per cogsci §14.2.9 of MULTI-TARGET-EXAM-PLAN-001) runs as `freestyle` regardless of whether the student has set targets yet.

---

## Decision

`SessionMode` becomes a first-class discriminator on `SessionStartRequest`:

```csharp
public sealed record SessionStartRequest(
    string[] Subjects,
    int DurationMinutes,
    SessionMode Mode,                  // exam-prep | freestyle
    SessionPedagogy Pedagogy,          // practice | challenge | review | diagnostic
    string? ActiveExamTargetId = null  // required iff Mode = exam-prep
);

public enum SessionMode
{
    ExamPrep = 1,    // pool filtered by ExamTarget.QuestionPaperCodes / PET section / SAT section codes
    Freestyle = 2,   // pool filtered by Subject only — today's behavior
}
```

The existing `Mode` field (practice|challenge|review|diagnostic) is renamed to `Pedagogy` to free the name. Backwards-compat: a 1-release shim accepts the old `Mode: "practice"` shape and maps to `Mode: ExamPrep, Pedagogy: Practice` only when `ActiveExamTargetId` is non-null; otherwise to `Mode: Freestyle, Pedagogy: Practice`.

### Invariants (server-enforced at endpoint)

1. `Mode == ExamPrep` ⇒ `ActiveExamTargetId` is non-null AND resolves to a non-archived `ExamTarget` owned by the calling student.
2. `Mode == Freestyle` ⇒ `ActiveExamTargetId` is null. Endpoint rejects 400 if a value is supplied.
3. `Mode == ExamPrep` AND `Pedagogy == Diagnostic` is allowed (per-target diagnostic, per PRR-228), but uses freestyle pool internally with target-aware item-weighting (covered in PRR-228, not this ADR).
4. `Pedagogy == Diagnostic` AND no `ExamTarget` exists ⇒ Mode must be Freestyle (onboarding case).

### Pool selection contract

`MartenQuestionPool.LoadAsync` and its three call sites in `SessionEndpoints.cs` (lines 179, 652, 1031) gain a mode-aware overload:

```csharp
public static async Task<MartenQuestionPool> LoadAsync(
    IDocumentStore store,
    string[] subjects,
    SessionMode mode,
    IReadOnlyList<string>? questionPaperCodes,    // non-null + non-empty when mode == ExamPrep
    ILogger logger,
    CancellationToken ct = default);
```

When `mode == ExamPrep`:
- The Marten query becomes `q.Subject == subject && q.Status == "Published" && q.QuestionPaperCodes.Any(code => questionPaperCodes.Contains(code))`.
- This requires `QuestionReadModel.QuestionPaperCodes: List<string>` field (added per PRR-246) — backfilled via projection rebuild.

When `mode == Freestyle`:
- Pool filter is unchanged — `q.Subject == subject && q.Status == "Published"`.

There is **no implicit fallback**. Endpoint rejects malformed combinations at validation, never silently degrades.

### Onboarding implication

Onboarding's exam-target step is allowed to be skipped (per the freestyle peer-mode decision 2026-04-27): a student with `StudentPlan.Targets = []` is a valid first-class state. Such students exclusively launch `Freestyle` sessions; the exam-prep-only surfaces (reference library per ADR-0059, exam-week scheduler lock per ADR-0048, etc.) gracefully no-op for them.

This locks the freestyle-as-peer-mode product call: not a staging area, not a gateway to exam-prep, but an equal first-class product surface. Students may switch between modes per session.

---

## Consequences

### Good

- The original `MartenQuestionPool` exam-target gap (traced 2026-04-27) becomes implementable without an ambiguous fallback.
- Reference library variant practice (ADR-0059) has a clean session route — single-question Freestyle session.
- Onboarding diagnostic + targetless students no longer require special-case code paths in pool selection.
- Preserves no-stubs / production-grade rule: the code always knows which filter to apply, deterministically.

### Costs

- API contract change: `SessionStartRequest` adds two fields, renames one. One-release back-compat shim required for any existing client.
- Three call sites in `SessionEndpoints.cs` updated; projection rebuild for `QuestionReadModel.QuestionPaperCodes`.
- Frontend `SessionSetupForm.vue` gets a mode toggle (today implicit) — small UX addition.

### Risks accepted

- **Mode-mode toggling friction**: students who set targets but want to do freestyle today must explicitly switch mode. Mitigation: default the mode toggle to ExamPrep when the student has ≥1 active target, Freestyle otherwise; user override is one tap.
- **Downstream surfaces that consume `SessionStartRequest`**: SignalR session-summary, replay export, history list. Each must accept the new shape. Inventory in PRR-247 implementation.

---

## Operational rules

### For code review

- Any PR introducing a `MartenQuestionPool.LoadAsync` call without passing `SessionMode` is rejected.
- Any PR where `ActiveExamTargetId` is supplied without `Mode == ExamPrep` is rejected at validator.
- Any PR introducing a "documented fallback" or "subject-only when null" path in pool selection is rejected — this ADR explicitly bans the pattern.

---

## History

- 2026-04-28 proposed: closes the original-bug architectural decision implied by the user's freestyle-OK confirmation 2026-04-27. Implementation in PRR-247 (wiring) + PRR-246 (filter behavior).
