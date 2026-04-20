# TASK-PRR-001: Fix EXIF stripping bug — stop lying in PhotoUploadResponse

**Priority**: P0 — ship-blocker (lens consensus: 2)
**Effort**: S — 1-2 days
**Lens consensus**: persona-redteam, persona-privacy
**Source docs**: `axis9_data_privacy_trust_mechanics.md:L39`
**Assignee hint**: kimi-coder
**Tags**: source=pre-release-review-2026-04-20, lens=redteam
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp

---

## Goal

Replace the current `StripExifMetadata` stub (at `src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs:241-248` — returns input bytes unchanged) with a real EXIF/IPTC/XMP/GPS scrub. Tie the `ExifStripped` flag to the **strip result**, not to file-type. Reject uploads where strip fails rather than persist leaked metadata. "Labels match data" (user memory) and "no stubs in production" (banned 2026-04-11) both apply.

### User decision 2026-04-20 — tightened DoD
- Implement via [Magick.NET](https://github.com/dlemstra/Magick.NET). `image.Strip()` removes all EXIF/IPTC/XMP tags including GPS in a single call. Handles JPEG/PNG/HEIC; well-maintained; MIT-friendly license.
- `ExifStripped` flag becomes `stripResult.Success` — never tied to file-type (`!isPdf`) again.
- On strip failure: return 422, do **not** persist the bytes. Better to reject than to persist a leaked-GPS photo.
- Integration test against a real EXIF-laden fixture — unit-testing the stripper in isolation is insufficient; the bug is at the endpoint seam.
- Architecture test: no outbound pipeline (OCR, LLM prompt assembly, analytics, RetentionWorker) may accept image bytes that haven't passed `ExifStripper.StripSucceeded()`.

## Files

- `src/shared/Cena.Infrastructure/Media/ExifStripper.cs` — new; Magick.NET wrapper returning `StripResult(byte[] scrubbed, bool success, string? failureReason)`
- `src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs` — replace `StripExifMetadata` stub (lines 241-248), change line 211 to tie `ExifStripped` to `stripResult.Success`, add 422 path on failure
- `src/actors/Cena.Actors/Services/PhotoIngestService.cs` — consume the new stripper; never accept raw bytes
- `tests/fixtures/exif-laden-sample.jpg` — real JPEG with known GPS/Make/Model/timestamp (commit in-repo so the test is deterministic)
- `tests/integration/PhotoUploadEndpoint.ExifStripping.Tests.cs` — upload the fixture through the full endpoint, read persisted bytes via `MetadataExtractor`, assert zero GPS/Make/Model/serial-number/owner tags remain; separate test asserts 422 on strip failure
- `tests/arch/NoUnstrippedImageBytesTest.cs` — architecture test: only `ExifStripper.StripResult.Scrubbed` may flow to persistence/LLM/OCR/analytics seams
- `Cena.Shared.Infrastructure.csproj` — add `Magick.NET-Q8-AnyCPU` NuGet dep (smaller Q8 build, sufficient for 8-bit image scrub)

## Definition of Done

1. `StripExifMetadata` stub removed from `PhotoUploadEndpoints.cs`; `ExifStripper` service exists and is injected.
2. `ExifStripped` response flag is `stripResult.Success` — architecture test enforces no other assignment path.
3. Integration test with the committed EXIF-laden fixture passes: upload → response asserts `ExifStripped=true` → read persisted bytes → `MetadataExtractor` finds zero GPS/Make/Model/owner tags.
4. Negative integration test: strip-failure path returns 422, persists nothing, response carries failure reason.
5. Architecture test green: `NoUnstrippedImageBytesTest` scans the type graph and asserts no persistence/LLM/OCR/analytics method accepts `byte[]` image payloads directly — only `StripResult.Scrubbed`.
6. Full `Cena.Actors.sln` builds cleanly (branch-only builds miss cross-project errors — user memory `feedback_full_sln_build_gate`).
7. All existing tests still pass.

### Downstream tasks blocked until this lands

- prr-010 (sandbox SymPy template evaluation — ingestion-adjacent)
- AXIS_6 assessment features that accept photo uploads
- AXIS_8 OCR/content-authoring work that consumes uploaded images
- Any prr-NNN whose Files list includes `PhotoUploadEndpoints.cs` or `PhotoIngestService.cs`

Annotate those tasks with `blocked-by: prr-001` when their implementer claims them.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
- #8: Event-sourced DDD, files <500 LOC, no stubs in production
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)

## Implementation Protocol — Senior Architect

Implementation of this task must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this task exist?** Read the source-doc lines cited above and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What's the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping — see user memory "Verify data E2E" and "Labels match data".
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the task as-scoped is wrong in light of what you find, **push back** and propose the correction via a task comment — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker, not 'hard'>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new task ID.

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-001)
