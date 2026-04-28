# PRR-250 — Pre-implementation verification sweep findings

**Date**: 2026-04-28
**Investigator**: claude-1
**Task**: [TASK-PRR-250](../TASK-PRR-250-pre-implementation-verification-sweep.md)
**Source docs verified against**: ADR-0059, PRR-245, EPIC-PRR-N
**Verdict scale**: VERIFIED | MISMATCH | MISSING | BLOCKER

Pure read-only investigation. No code changed. Live dev stack (Postgres
running, student-api up 9h healthy) consulted for §2 corpus presence.

---

## §1 IInstitutePricingResolver

**Verdict**: VERIFIED interface exists; **MISMATCH** — no rate-limit override surface.

- **Location**: [src/actors/Cena.Actors/Pricing/IInstitutePricingResolver.cs](../../../src/actors/Cena.Actors/Pricing/IInstitutePricingResolver.cs) (NOT in `Cena.Api.Contracts/Subscriptions/` as ADR-0059 implied)
- **Interface shape**: a single method
  - `Task<ResolvedPricing> ResolveAsync(string instituteId, CancellationToken ct = default)`
- **ResolvedPricing record** (`src/actors/Cena.Actors/Pricing/ResolvedPricing.cs:41`):
  ```
  ResolvedPricing(
      decimal StudentMonthlyPriceUsd,
      decimal InstitutionalPerSeatPriceUsd,
      int MinSeatsForInstitutional,
      int FreeTierSessionCap,
      PricingSource Source,
      DateTimeOffset EffectiveFromUtc)
  ```
- **Companion contracts**: `IInstitutePricingOverrideStore` (find/upsert) — Marten-backed in prod, in-memory in tests.
- **Gap vs ADR-0059 §5 expectation** ("variant rate-limit overrides per institute"):
  - ResolvedPricing has zero rate-limit fields. Source/EffectiveFromUtc and seat-pricing only.
  - The override document on the read-side store carries the same shape — no rate-limit override exists in the contract today.

**Follow-up task suggested**: extend `ResolvedPricing` (and `InstitutePricingOverrideDocument`)
with `int? VariantRateLimitPerHour` (or similar) so PRR-245's §5 has a
canonical resolver path. Alternatively, ADR-0059 §5 should be amended
to call out a SEPARATE per-institute rate-limit resolver and the YAML
defaults shape it would consume.

---

## §2 BagrutCorpusItemDocument

**Verdict**: PARTIAL — class exists but corpus is not ingested in dev; field names differ from ADR-0059.

- **Location**: [src/shared/Cena.Infrastructure/Documents/BagrutCorpusItemDocument.cs](../../../src/shared/Cena.Infrastructure/Documents/BagrutCorpusItemDocument.cs)
  (NOT in `src/actors/Cena.Actors/Corpus/` as ADR-0059 implied)
- **Actual fields** (sealed class, public mutable properties):
  - `Id` (deterministic: `bagrut-corpus:{subject}:{paper_code}:{question_number}`)
  - `MinistrySubjectCode` (e.g. "035")
  - `MinistryQuestionPaperCode` (e.g. "035581") — this is what ADR-0059 calls `paperCode`
  - `Units` (3, 4, 5)
  - `TrackKey` ("3U" | "4U" | "5U")
  - `Year`
  - `Season` (`BagrutCorpusSeason` enum)
  - `Moed` (string: A | B | C | Special)
  - `QuestionNumber`
  - `TopicId` (e.g. "algebra.quadratics")
  - `Stream` (`BagrutCorpusStream` enum: Hebrew, Arab, etc)
  - Raw OCR'd question text (internal — never on student DTO per ADR-0043)
  - More fields exist below truncation point.
- **ADR-0059 vs reality field-name table**:
  | ADR-0059 | reality | match? |
  | --- | --- | --- |
  | `paperCode` | `MinistryQuestionPaperCode` | renamed |
  | `year` | `Year` | yes (case) |
  | `moed` | `Moed` | yes (case) |
  | `questionNumber` | `QuestionNumber` | yes (case) |
  | `subject` | `MinistrySubjectCode` | renamed |
  | `stream` | `Stream` (enum) | yes (case) |

  None are blockers, but ADR-0059 §3 should adopt the canonical names to
  prevent variable-name drift in implementation.

- **Marten storage status** (live dev DB — Postgres user=`cena`, db=`cena`):
  ```
  SELECT count(*) FROM mt_doc_bagrutcorpusitemdocument
    → ERROR: relation "mt_doc_bagrutcorpusitemdocument" does not exist
  SELECT tablename FROM pg_tables WHERE tablename ILIKE '%bagrut%' OR ILIKE '%corpus%'
    → 0 rows
  ```
  **No `mt_doc_*` table for BagrutCorpusItemDocument has been created.**
  The Marten schema-creation hooks materialize tables on first write —
  this means **no document has ever been ingested in this dev env**.

**BLOCKER**: ADR-0059 §3 + §4 (reference-page filter scope) is moot
until corpus ingestion runs. PRR-245 implementation must either:
  (a) include a corpus-ingestion seeding step in dev/staging stand-up, or
  (b) gate the reference-library UI behind an "empty state" path that
      surfaces when the corpus is empty (existing pattern e.g.
      EPIC-G admin pages).

**Follow-up task suggested**: PRR-242 (corpus OCR pipeline) wire-up to
dev stack; or a synthetic test-fixture seeder under
`scripts/seed-bagrut-corpus.sh` that mints N items per Track × Year ×
Moed for e2e-flow specs.

---

## §3 PRR-218 + PRR-221 status

**Verdict**: VERIFIED both DONE.

- **PRR-218 (StudentPlan aggregate Marten store)**: shipped at commit
  `7de1e388` — "feat(prr-218): MartenStudentPlanAggregateStore replaces
  InMemory as prod binding". The aggregate is Marten-backed in prod.
- **PRR-221 (onboarding `exam-targets` + `per-target-plan` steps)**:
  shipped at commit `d31e2984` — "feat(prr-221): extend onboardingStore
  with exam-targets + per-target-plan steps (MVP wiring)". Onboarding
  store carries the new step types.
- **ExamTarget structural code**: present at
  [src/actors/Cena.Actors/StudentPlan/ExamTarget.cs](../../../src/actors/Cena.Actors/StudentPlan/ExamTarget.cs)
  + `src/actors/Cena.Actors/ExamTargets/{ExamTargetEvents.cs,ExamTargetCode.cs}`
  + retention pipeline (`Retention/ExamTargetRetentionExtensionDocument.cs`,
    `Retention/ExamTargetRetentionWorker.cs`,
    `Retention/ExamTargetShredLedgerDocument.cs`)
  + RTBF cascade (`Rtbf/ExamTargetErasureCascade.cs`)
- **`ExamTarget.QuestionPaperCodes` populated for real students?**
  Not directly verified — would require querying StudentActor for a
  seeded student in this env. The retention worker DI exists
  (`MartenArchivedExamTargetSource` at commit `6b66f92f`) which means
  the field is indexable and persisted.

**No blocker.** ADR-0059's §4 filter scope can rely on `ExamTarget`
being shipped.

---

## §4 Feature-flag plumbing

**Verdict**: VERIFIED — real event-sourced feature-flag system exists.

- **Implementation pattern**: NOT LaunchDarkly / OpenFeature / a vendor
  service. It's a Marten event-sourced in-house system:
  - [src/actors/Cena.Actors/Events/FeatureFlagEvents.cs](../../../src/actors/Cena.Actors/Events/FeatureFlagEvents.cs) — events
  - [src/actors/Cena.Actors/Projections/FeatureFlagProjection.cs](../../../src/actors/Cena.Actors/Projections/FeatureFlagProjection.cs) — Marten projection
    (`FeatureFlagDocument`, multi-stream projection)
  - [src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs](../../../src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs) — Proto.Actor instance
  - Wired in `Cena.Actors.Host/Program.cs` and `Configuration/MartenConfiguration.cs`
- **Plug-in path for `reference_library_enabled`**: emit a
  `FeatureFlagSet_V*` event, the projection materializes the
  `FeatureFlagDocument` row, callers read it. The actor is Proto-style
  (`IActor.ReceiveAsync`); endpoints reading the flag would either
  consult the projection directly (Marten doc read) or message the actor.
- **Gap**: I did not find an `IFeatureFlagService` synchronous read
  service for endpoints. Endpoints currently must Marten-load the
  `FeatureFlagDocument` directly. PRR-245's "default-off at Launch"
  flag will work, but a thin `IFeatureFlagReader` facade with caching
  would reduce per-request DB hits if the flag is checked on a hot
  path (variant-creation endpoint hit ≥ once per student variant).

**Follow-up task suggested**: nice-to-have, not blocker. Add
`IFeatureFlagReader` with TTL'd cache backed by FeatureFlagDocument.
Defer to v2 of PRR-245 unless the variant-creation latency budget
shows DB read dominance.

---

## §5 RateLimitedEndpoint decorator

**Verdict**: MISSING as named — but the underlying primitive is the
ASP.NET Core 7+ rate limiter, used everywhere as `RequireRateLimiting`.

- **Search**: `RateLimitedEndpoint` class/decorator does not exist
  anywhere under `src/`. Zero hits.
- **Real primitive in use** (`src/api/Cena.Student.Api.Host/Program.cs`):
  - `builder.Services.AddRateLimiter(options => { ... })` — configures
    multiple named policies via `FixedWindowRateLimiterOptions` and at
    least one `SlidingWindowRateLimiterOptions`.
  - Endpoint groups apply a policy via
    `.RequireRateLimiting("api")` (e.g. `SessionEndpoints.cs:55`) or
    similar named policies.
- **Plug-in for PRR-245 variant-creation endpoint**:
  add a new policy in `AddRateLimiter(...)` (e.g. `"variant-create"`,
  PartitionedRateLimiter.Create(httpContext => ... by studentId)),
  then `.RequireRateLimiting("variant-create")` on the variant-create
  endpoint group.
- **Plug-in for institute-overrideable rate-limits** (the §1 gap):
  ASP.NET's policy registry is static at `AddRateLimiter` time. To
  honor PER-INSTITUTE overrides, you need a custom
  `PartitionedRateLimiter<HttpContext>` whose partition key incorporates
  `(studentId, instituteId)` and whose limit reads from the
  IInstitutePricingResolver (extended per §1) at partition-creation
  time. Rough effort: M (1 day), well-scoped.

**Follow-up**: ADR-0059 should drop the "RateLimitedEndpoint decorator"
language and reference `RequireRateLimiting` + a custom partitioned
limiter for the per-institute case.

---

## §6 CasGatedQuestionPersister — student-callable?

**Verdict**: NOT student-callable today. **MISMATCH** vs PRR-245 §6.

- **Location**: [src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs](../../../src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs:79-122) (interface) + (class :125)
- **Two `PersistAsync` overloads**:
  1. `(questionId, creationEvent, GatedPersistContext, ...)` — standalone session
  2. `(IDocumentSession, questionId, ...)` — session-aware (RDY-039), caller owns the unit-of-work
- **`GatedPersistContext`** record carries metadata (subject, locale,
  source, etc.) — does NOT carry caller role/auth. The persister
  itself does not gate by role; it always runs the CAS gate (RDY-041).
- **DI registration audit** — the persister is registered in EXACTLY
  one place:
  ```
  src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs:
    services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>();
  ```
  No `AddScoped<ICasGatedQuestionPersister, ...>` exists in
  `Cena.Student.Api.Host` (verified — single registration site).
- **Current callers** (3 production paths, all admin):
  - `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — admin seed
  - `src/api/Cena.Admin.Api/QuestionBankService.cs` — admin question CRUD
  - `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` — ingestion pipeline (admin-driven)
- **Architecture invariant under test**: 
  `src/actors/Cena.Actors.Tests/Architecture/SeedLoaderMustUseQuestionBankServiceTest.cs`
  enforces "all question creation routes through the persister" — but
  this is a static-analysis check on writers, not a DI-availability check.

**PRR-245 implementation requirements**:
1. Add `services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>()`
   to `Cena.Student.Api.Host`'s DI configuration (the registration
   composition path — likely `Registration/CenaStudentApiServiceRegistration.cs`).
2. Add the auth gate at the variant-creation endpoint layer — the
   persister won't reject a student caller; the **endpoint** must
   enforce ownership/role checks. Pattern matches the cross-tenant
   guards just landed on `/current-question`/`/answer`/`/complete`
   (commit `5a030d24`).
3. Define a fresh `GatedPersistContext` shape for student-initiated
   variants — at minimum carrying the SOURCE Bagrut item id (so the
   variant can be linked back to its reference) and the caller's
   studentId for audit.

**Follow-up task suggested**: extract this into a sibling task
"PRR-245-pre-impl-1: Wire ICasGatedQuestionPersister into student-api
+ define GatedPersistContext for student-variants" — split out so
PRR-245 implementation can start unblocked on the auth-gate pattern.

---

## §7 EPIC numbering verification

**Verdict**: ALMOST CLEAN — one dangling reference remains.

- **EPIC-PRR-N is canonical**: [tasks/pre-release-review/EPIC-PRR-N-reference-library-and-variants.md](../EPIC-PRR-N-reference-library-and-variants.md) exists.
- **ADR-0059** explicitly notes the rename:
  > "EPIC-PRR-H letter was already taken by student-input-modalities."
  Reference link goes to EPIC-PRR-N. Clean.
- **TASK-PRR-250** references EPIC-PRR-N consistently. Clean.
- **TASK-PRR-245** has ONE dangling old-name reference:
  ```
  tasks/pre-release-review/TASK-PRR-245-reference-library-variant-generation.md:
    - `tasks/pre-release-review/EPIC-PRR-H-reference-library.md`
  ```
  This is the only `EPIC-PRR-H-reference-library` mention I found in
  `docs/` or `tasks/` (verified by grep). It's a dead link — that file
  does not exist on disk.

**Follow-up**: ADR-0059 + this findings file are both correct.
Coordinator should patch the single line in TASK-PRR-245 either to
remove the dangling link or to point at EPIC-PRR-N. 1-line fix; no
follow-up task needed if the coordinator does it on the next merge.

---

## Summary of follow-ups requested

| # | Section | Severity | Action |
| --- | --- | --- | --- |
| 1 | §1 | medium | Extend `ResolvedPricing`/`InstitutePricingOverrideDocument` with rate-limit override fields, OR amend ADR-0059 §5 to use a separate per-institute rate-limit resolver |
| 2 | §2 | **BLOCKER** for ADR-0059 §3 §4 | Wire BagrutCorpus ingestion into the dev stack OR ship variant-creation behind an empty-state path |
| 3 | §2 | low | Adopt canonical field names (`MinistryQuestionPaperCode` etc.) in ADR-0059 + PRR-245 |
| 4 | §4 | low (defer to v2) | Add `IFeatureFlagReader` facade with TTL'd cache |
| 5 | §5 | low | ADR-0059 swap "RateLimitedEndpoint decorator" for `RequireRateLimiting` + custom partitioned limiter |
| 6 | §6 | **HIGH** for PRR-245 unblock | Wire `ICasGatedQuestionPersister` into `Cena.Student.Api.Host` DI + define student-variant `GatedPersistContext` shape + add endpoint-layer auth gate. Suggest splitting into pre-impl task. |
| 7 | §7 | trivial | Patch dangling `EPIC-PRR-H-reference-library.md` reference in TASK-PRR-245 |

## Verdict matrix

| Section | Subject | Verdict |
| --- | --- | --- |
| 1 | IInstitutePricingResolver | EXISTS / MISMATCH (no rate-limit fields) |
| 2 | BagrutCorpusItemDocument | EXISTS / minor name mismatches / **CORPUS NOT INGESTED** |
| 3 | PRR-218 + PRR-221 | DONE |
| 4 | Feature-flag system | EXISTS (Marten event-sourced) |
| 5 | RateLimitedEndpoint | NAME MISSING / underlying primitive (`RequireRateLimiting`) exists |
| 6 | CasGatedQuestionPersister | EXISTS / NOT WIRED for student-api / no role gate |
| 7 | EPIC numbering | CLEAN except 1 dangling reference in TASK-PRR-245 |
