---
id: FIND-DATA-021
task_id: t_b224f213658a
severity: P0 — Critical
lens: data
tags: [reverify, data, cost, fake-data]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-021: token cost meter fabricated from 200-char preview; real tokens ignored

## Summary

token cost meter fabricated from 200-char preview; real tokens ignored

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

GOAL
Replace the fabricated cost meter (messagePreview.Length / 4) with the real
token count from TutorMessageDocument.TokensUsed. Persist daily/monthly limit
overrides per-school in a Marten document (TokenBudgetSettingsDocument). Add
an upcaster so the event stream carries token counts going forward.

ROOT CAUSE
TutoringMessageSent_V1 has only a 200-char MessagePreview field, no token
count. When TutorMessageDocument.TokensUsed was added (FIND-arch-004
hardening), the token field was NOT added to the event payload. The cost
meter services chose the event store as source of truth and silently
substituted preview-length/4 as a fake proxy.

EVIDENCE
  $ rg "preview.Length / 4" src/api/Cena.Admin.Api -n
    TokenBudgetAdminService.cs:59:   return preview.Length / 4 * 2;
    TokenBudgetAdminService.cs:107:  return (long)(preview.Length / 4 * 2);
    TutoringAdminService.cs:146:     return preview.Length / 4;
    TutoringAdminService.cs:216:     return preview.Length / 4;
    TutoringAdminService.cs:348:     return preview.Length / 4;

  $ rg "TokensUsed" src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs -n
    324: int? totalTokensUsed = null;
    334: if (chunk.Finished && chunk.TokensUsed.HasValue)
    336:     totalTokensUsed = chunk.TokensUsed.Value;
    351: TokensUsed = totalTokensUsed // persisted on TutorMessageDocument

  $ rg "private static" src/api/Cena.Admin.Api/TokenBudgetAdminService.cs -n
    26: private static int _dailyLimitOverride = DefaultDailyLimit;
    27: private static long _monthlyLimitOverride = DefaultMonthlyLimit;

IMPACT
- A 4000-token assistant response counts as `200/4*2 = 100` in the admin
  dashboard. Real cost is ~40× higher than the meter shows.
- DailyLimitPerStudent enforcement lets a runaway tutor session burn
  through the real limit while admin UI shows "1.5% used".
- _dailyLimitOverride static is lost on restart, not per-tenant, not
  persisted.

FILES TO TOUCH
  - src/api/Cena.Admin.Api/TokenBudgetAdminService.cs (rewrite)
  - src/api/Cena.Admin.Api/TutoringAdminService.cs:200-236, 331-352
  - src/actors/Cena.Actors/Tutoring/TutoringEvents.cs (add V2 record with
    InputTokens, OutputTokens)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
    (register V2, register upcaster, register TokenBudgetSettingsDocument)
  - src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs
    (add TutoringMessageSentV1ToV2Upcaster)
  - src/shared/Cena.Infrastructure/Documents/TokenBudgetSettingsDocument.cs
    (NEW)

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-021
  - src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs
    (existing ConceptAttemptedV1ToV2Upcaster pattern)
  - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs:324-372
    (where real token count is captured from the LLM stream)
  - src/shared/Cena.Infrastructure/Documents/TutorDocuments.cs:44
    (TutorMessageDocument.TokensUsed field)

DEFINITION OF DONE
  - TutoringMessageSent_V2 added with InputTokens, OutputTokens.
  - V1→V2 upcaster registered. V1 events still load with null token fields.
  - TokenBudgetAdminService reads from TutorMessageDocument, NOT the event
    store. Group by StudentId, sum TokensUsed, group by Date.
  - Daily/monthly limits persist across restart via TokenBudgetSettingsDocument
    keyed by SchoolId. Static fields removed.
  - TokenBudgetAdminEndpoints.UpdateLimits requires SchoolId from JWT claim and
    writes to the per-school doc.
  - Pre-instrumentation events show "unknown — pre-instrumentation" not "0".
  - Integration test pumps a known 4321-token response through the LLM stream
    and asserts /api/admin/token-budget/status returns TokensUsedToday=4321
    (from the document), NOT 100 (from preview/4 fallback).
  - Restart test: UpdateLimitsAsync with a custom limit, restart TestServer,
    assert limit survives.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, test paths, paste of the integration
  test output showing the real token count flowing end-to-end.

TAGS: reverify, data, cost, fake-data
RELATED PRIOR FINDING: none (net-new v2 concern)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_b224f213658a`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
