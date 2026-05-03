# 2026-04-30 — `QuestionPaperCodes` backfill (PRR-246 + ADR-0043)

## What changes

| Surface | Before | After |
|---|---|---|
| `BagrutAlignment` (in `QuestionDocument`) | no `QuestionPaperCodes` field | `List<string> QuestionPaperCodes` (defaults to empty) |
| `QuestionReadModel` | no `QuestionPaperCodes` field | `List<string> QuestionPaperCodes` (defaults to empty) |
| `QuestionBagrutAlignmentSet_V1` | 9 positional params | 10 positional params (last optional, defaults to `null` → empty list) |
| `MartenQuestionPool.LoadAsync` | `(store, subjects, logger, ct)` | adds explicit-policy overload `(store, subjects, policy, logger, ct)` and a back-compat 4-arg overload that uses `QuestionPoolPolicy.Strict` |
| `MartenQuestionPool` filter | `Subject == s && Status == Published` | adds default `SourceType != "BagrutReference"` filter (closes ADR-0043 P0) and an opt-in `QuestionPaperCodes.Any(c => policy.QuestionPaperCodes.Contains(c))` filter |

## Why

1. **ADR-0043 enforcement** (P0 ship-gate hold): the prior pool leaked any item published with `SourceType="BagrutReference"` straight into student-facing pools. The default-strict policy closes the leak immediately on every existing call site without code changes (the 4-arg overload now delegates to the strict path).
2. **PRR-246 multi-paper exam-target binding**: students with `ExamTarget = Bagrut Math 5U {035581, 035582}` previously got the same pool as freestyle math students. The new filter restricts the pool to items aligned to the chosen Ministry שאלון set, making the `ExamTarget.QuestionPaperCodes` plumbing (populated by PRR-243 onboarding) actually load-bearing.

## Backfill — operational steps

The new `QuestionReadModel.QuestionPaperCodes` field is populated by `QuestionListProjection.Apply(QuestionBagrutAlignmentSet_V1)`. Existing `QuestionDocument` rows with a `BagrutAlignment` but no historical `QuestionBagrutAlignmentSet_V1` event will need backfill before the multi-paper filter can do useful work for them.

Three backfill modes (pick whichever the deploy environment can run):

### Mode A — replay (preferred when projection rebuild is acceptable)

```bash
# Online, idempotent. Marten replays all events through QuestionListProjection.
dotnet run --project src/api/Cena.Admin.Api -- projection rebuild QuestionListProjection
```

Caveat: if no `QuestionBagrutAlignmentSet_V1` events exist for a question (because alignment was set on the doc directly, not as an event), the projection rebuild will not populate `QuestionPaperCodes` for that row. Use Mode B for those.

### Mode B — direct doc-side backfill (one-shot script)

For QuestionDocument rows whose `BagrutAlignment.QuestionPaperCodes` is non-empty but the read model is still empty:

```sql
-- Inspect drift
SELECT qd.id,
       jsonb_array_length(coalesce(qd.data->'BagrutAlignment'->'QuestionPaperCodes', '[]'::jsonb)) AS doc_codes,
       jsonb_array_length(coalesce(qrm.data->'QuestionPaperCodes', '[]'::jsonb)) AS read_model_codes
FROM cena.mt_doc_questiondocument qd
LEFT JOIN cena.mt_doc_questionreadmodel qrm ON qrm.id = qd.id
WHERE qd.data->'BagrutAlignment'->'QuestionPaperCodes' IS NOT NULL
  AND jsonb_array_length(qrm.data->'QuestionPaperCodes') = 0;
```

Then either (a) emit a `QuestionBagrutAlignmentSet_V1` event for each drifted question via an admin one-shot script (preferred — keeps the projection authoritative), or (b) `UPDATE` the read model directly.

### Mode C — accept-zero (do nothing)

If the production corpus has no `BagrutAlignment.QuestionPaperCodes` populated yet (greenfield), no backfill is needed. The pool defaults to no exam-target restriction when `QuestionPaperCodes` is empty on the policy — Freestyle behaviour remains unchanged.

## Rollback

The migration is additive. To roll back:

1. Revert the `MartenQuestionPool.LoadAsync` policy overload (callers fall back to the 4-arg overload that uses `QuestionPoolPolicy.Strict`). Note: this **leaves the ADR-0043 SourceType filter ON** — the rollback does not re-open the ship-gate hold.
2. The added `QuestionPaperCodes` fields on `QuestionDocument`/`QuestionReadModel`/`QuestionBagrutAlignmentSet_V1` are forward-compat — they can stay even if the filter logic is reverted (zero-cost columns/JSON fields).

## Verification

After deploy, `MartenQuestionPoolPolicyTests` (in `Cena.Actors.Tests/Serving/`) exercises:

- Default policy excludes `BagrutReference` items (ADR-0043 closure).
- Strict policy explicit overload behaves identically.
- `AllowReferenceItems=true` includes them (admin opt-in).
- `QuestionPaperCodes` filter restricts to aligned items.
- Empty `QuestionPaperCodes` = no restriction (freestyle).

The `LearningSessionQueueProjection` does NOT yet carry `ExamScope`/`ActiveExamTargetId` — mid-session refill paths (lines 679 + 1065 of `SessionEndpoints.cs`) call the strict default and therefore close ADR-0043 but do not yet enforce the exam-target paper-code filter on refill. Filed as **PRR-246-followup** to plumb `ExamScope` through the queue projection.
