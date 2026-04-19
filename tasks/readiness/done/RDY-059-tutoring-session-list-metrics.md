# RDY-059: Accuracy + Focus-Score on the Tutoring Session List

- **Status**: Requested — not started
- **Priority**: Medium — Live Monitor page shows honest but sparse data
  until this lands
- **Source**: Shaker walkthrough 2026-04-18; surfaced after fixing the NaN%
  bug in admin-spa tutoring/monitor (commit `1134749`)
- **Depends on**: RDY-058 (admin /me + honest-field monitor, landed)
- **Effort**: 2-3 days
- **Tier**: 3 (polish — doesn't block pilot)

## Problem

`GET /api/admin/tutoring/sessions` returns `TutoringSessionSummaryDto`:

```csharp
public sealed record TutoringSessionSummaryDto(
    string Id, string StudentId, string StudentName, string SessionId,
    string ConceptId, string Subject, string Methodology, string Status,
    int TurnCount, int DurationSeconds, int TokensUsed,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt);
```

No **accuracy** (correct-answer ratio) and no **focus score**. The Live Monitor
page (`/apps/sessions/monitor`) ships with those two metrics removed — the
session cards now show Turns / Duration / Tokens, which is honest but
misses the two signals a teacher actually uses to decide "who do I check in
on next?"

Both signals exist elsewhere in the system:
- **Accuracy**: `AnswerSubmitted` events emit `IsCorrect`. The
  `LearningSessionQueueProjection` already projects `IsCorrect` on the
  `SessionQueueItem` rows. Tutoring sessions themselves don't carry a
  direct rollup — the answers belong to the sibling `LearningSession`.
- **Focus score**: `FocusAnalyticsDocuments.cs` + `FocusExperimentCollector`
  already compute focus per student per session. The tutoring session
  monitor just doesn't join on them.

## Scope

### 1. Extend `TutoringSessionSummaryDto`

Add three optional fields (nullable so old clients/tests don't break):

```csharp
public sealed record TutoringSessionSummaryDto(
    ...existing fields...,
    int? QuestionsAnswered,     // count of AnswerSubmitted events scoped to this session
    float? AccuracyPercent,     // correct / answered * 100, rounded to int
    float? FocusScore);         // latest FocusRollup for the student-session pair (0..100)
```

### 2. Server-side join

In `TutoringAdminService.GetSessionsAsync`:

- Batch-fetch answer rows once: for all `studentId`s in the returned page,
  pull `SessionQueueItem` documents where `Subject == doc.Subject` + session
  timestamp between `doc.StartedAt` and `doc.EndedAt ?? now`.
  - `QuestionsAnswered = count(rows)`.
  - `AccuracyPercent = count(IsCorrect == true) / count(rows) * 100`.
- Batch-fetch the student's latest focus rollup:
  - Query `FocusRollupDocument` where `StudentId in (...) && Subject == doc.Subject`,
    ordered by timestamp DESC, take 1 per student.
  - `FocusScore = rollup.OverallScore`.

Both queries are bounded by the already-paged session list (≤100 rows per
page), so the N+1 risk is low. Batch via `WhereIn` not per-row joins.

### 3. Wire Live Monitor UI back

In `src/admin/full-version/src/pages/apps/sessions/monitor.vue`:

- Re-add Accuracy and Focus Score fields — reading the new DTO fields.
  Keep Turns / Duration / Tokens too (still useful).
- When the backend returns null (data not yet computed for very new
  sessions), render "—" rather than "NaN%" — no regression to the
  2026-04-18 bug.
- Summary bar restores the `Avg Focus Score` card + adds an
  `Avg Accuracy` card.

### 4. Tests

- `TutoringAdminServiceTests.cs`: seed a session + 3 `SessionQueueItem`
  rows (2 correct, 1 wrong) + a FocusRollupDocument at 82.5. Assert the
  DTO returns `QuestionsAnswered=3, AccuracyPercent=66, FocusScore=82.5`.
- Edge cases: zero-answer session returns null-null, no-focus-rollup
  returns null focus, multi-student batch returns per-student
  aggregation.

## Acceptance Criteria

- [ ] `TutoringSessionSummaryDto` carries optional QuestionsAnswered,
  AccuracyPercent, FocusScore fields.
- [ ] `GET /api/admin/tutoring/sessions?status=active` returns the three
  new fields populated for sessions with answer / focus data.
- [ ] Fresh session (0 answers) returns all three as null, not 0, so
  the UI can render "—" without false-zero signal.
- [ ] Live Monitor renders the new fields; zero-answer sessions show
  "—", not "NaN%".
- [ ] Tests cover the three join paths (answers only, focus only, both,
  neither).
- [ ] No N+1 — batched `WhereIn` queries confirmed by a test that
  counts `_store.QuerySession()` opens for a page of 100 sessions and
  asserts ≤ constant count (5, say).

## Out of scope

- Real-time push updates via SignalR — the poll every 3s from the SPA
  is fine for now.
- Per-concept accuracy breakdown inside a session — the detail page
  already has the conversation view.
- Mastery-velocity / improvement-rate surfaces — those belong to the
  student profile Insights tab (tracked separately).

## Data sources confirmed

| Field | Source doc / event | Location |
|---|---|---|
| QuestionsAnswered | `SessionQueueItem` rows (from `AnswerSubmitted_*`) | `LearningSessionQueueProjection.cs:106` emits `IsCorrect` |
| AccuracyPercent | derived from the above | — |
| FocusScore | `FocusRollupDocument` | `src/shared/Cena.Infrastructure/Documents/FocusAnalyticsDocuments.cs` |

## Links

- [src/admin/full-version/src/pages/apps/sessions/monitor.vue](../../src/admin/full-version/src/pages/apps/sessions/monitor.vue) — consumer
- [src/api/Cena.Admin.Api/TutoringAdminService.cs:46](../../src/api/Cena.Admin.Api/TutoringAdminService.cs) — where the join goes
- [src/api/Cena.Api.Contracts/Admin/Tutoring/TutoringDtos.cs:16](../../src/api/Cena.Api.Contracts/Admin/Tutoring/TutoringDtos.cs) — DTO to extend
- Commit `1134749` — honest-fields fix that surfaced this gap
