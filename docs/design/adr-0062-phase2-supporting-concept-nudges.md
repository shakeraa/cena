# ADR-0062 Phase 2 — Supporting-concept nudges + Marten-backed publication counter

- **Status**: Architecture brief — implementation-ready
- **Date**: 2026-05-03
- **Author**: claude-subagent-phase15-phase2-design
- **Scope**: Implements ADR-0062 §Phasing "Phase 2 — supporting-concept nudges"
- **Owning ADR**: [ADR-0062](../adr/0062-concept-extraction-and-multi-skill-mastery.md)
- **Constraint anchors**: [ADR-0039](../adr/0039-bkt-parameters-and-fading.md) — BKT keys mastery on `PrimaryConceptId` only. Phase 2 does NOT change BKT. [Ship-gate banned terms](../engineering/shipgate.md) — no streaks, no variable-ratio rewards, no loss-aversion framing.

## §1 Problem statement

ADR-0062 §3 says supporting concepts get a `MasterySignalEmitted_V1` "positive-only, ½ post-reflection delta, never decrements" nudge. Today the nudge channel exists but is silent: `IConceptItemPublicationCounter` is bound to `NullConceptItemPublicationCounter` which always returns 0, so `IsAboveStabilityFloorAsync` always returns false, so the gate stays closed. Even if a developer wanted to fan a CAS-correct attempt out across `QuestionDocument.ConceptIds` to nudge the supporting concepts, there is no service that would actually do it, and no fast way to ask "does this leaf have ≥10 published items?"

Pedagogical failure mode this fixes: a question that tests `[derivative-rules]` (primary) and exercises `[function-domain, polynomial-arithmetic]` (supporting) currently produces zero mastery signal on the supporting concepts when the student gets it right. The student's `polynomial-arithmetic` posterior never moves on Bagrut-Part-B–style problems because Bagrut-Part-B is rarely the primary tag for that skill — but those problems heavily exercise it. The 002 deep research validated this gap and tightened the design with a precondition: nudges only fire when a concept has been corroborated across ≥10 items in the corpus, matching the published BKT identifiability floor (van de Sande 2013).

## §2 Current state

- **`IConceptItemPublicationCounter`** at `src/actors/Cena.Actors/Mastery/Extraction/IConceptItemPublicationCounter.cs:48-58`. Default binding is `NullConceptItemPublicationCounter` (same file, lines 60–75) — `GetPublishedItemCountAsync` returns 0; `IsAboveStabilityFloorAsync` returns false. The interface is wired into nothing yet — there is no caller in the codebase that asks the counter.
- **`MasterySignalEmitted_V1`** at `src/actors/Cena.Actors/Mastery/MasterySignalEmitted_V1.cs:84-91`. Already defined and used by EPIC-PRR-J PRR-381 (post-reflection retry success). Default delta is `MasterySignalOptions.DefaultDelta = 0.05`. The Phase 2 supporting-concept nudge will reuse the SAME event with a different `TriggerSource` — no new event type.
- **`IMasterySignalEmitter`** at `src/actors/Cena.Actors/Mastery/IMasterySignalEmitter.cs:43-51`. Two implementations: `InMemoryMasterySignalEmitter` (default), `MartenMasterySignalEmitter` (writes to per-student stream `masterysignal-{studentAnonId}`). Both are append-only. Already wired in DI via `MasterySignalServiceRegistration.AddMasterySignalServices` / `AddMasterySignalServicesMarten`.
- **`PostReflectionMasteryService`** at `src/actors/Cena.Actors/Mastery/PostReflectionMasteryService.cs:74-153`. Today's only consumer of `IMasterySignalEmitter`. It emits exactly one `MasterySignalEmitted_V1` per CAS-verified retry success. We will NOT reuse this service for the Phase 2 fan-out because (a) its single-skill signature collapses the supporting set, (b) PRR-381 has its own pedagogical contract that we don't want to entangle.
- **Existing call site for primary-concept BKT**: `ConceptAttempted_V3` (`src/actors/Cena.Actors/Events/LearnerEvents.cs:118-143`). The student-session answer endpoint already appends this. Phase 2 needs to find the right hook to also fan supporting concepts out — see §3 for the chosen seam.
- **`QuestionDocument.ConceptIds`** at `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:99`. Populated by `QuestionListProjection.Apply(QuestionConceptsExtracted_V1, ...)` and `QuestionListProjection.Apply(QuestionConceptsConfirmed_V1, ...)` (`src/actors/Cena.Actors/Questions/QuestionListProjection.cs:104-128`). The list is `[primary, supporting1, supporting2, …]` — `[1..]` are the supporting set.
- **Marten projection patterns**: `MultiStreamProjection` is already used for cross-stream aggregates (e.g. `SessionAttemptHistoryProjection`). The `Identity<TEvent>(e => key)` pattern lets a single document accumulate from many streams, registered via `opts.Projections.Add<X>(ProjectionLifecycle.Inline | Async)`.

## §3 Design — interfaces, contracts, file layout

Two pieces ship together: a Marten-backed publication counter (the gate) and a fan-out service (the consumer). They are independent enough to test separately but go live in the same flag flip.

### File layout

```
src/actors/Cena.Actors/Mastery/Extraction/
  ConceptPublicationCountDocument.cs        # NEW — the projection's storage row
  ConceptPublicationCountProjection.cs       # NEW — Marten Inline projection
  MartenConceptItemPublicationCounter.cs     # NEW — IConceptItemPublicationCounter (Marten-backed)
src/actors/Cena.Actors/Mastery/
  ISupportingConceptNudgeService.cs          # NEW
  SupportingConceptNudgeService.cs           # NEW — composes counter + emitter
  MasterySignalServiceRegistration.cs        # EXTENDED — AddPhase2NudgeServices()
src/actors/Cena.Actors/Configuration/
  MartenConfiguration.cs                     # EXTENDED — register projection + count doc
src/api/Cena.Student.Api/<answer-endpoint>   # EXTENDED — call nudge service after BKT update
```

### Publication counter projection

The counter answers "how many *published* questions have this `SkillCode` in their `ConceptIds`?" — fast (indexed read), cohort-wide (cross-curator), no table-scan.

**Storage shape**:
```csharp
public sealed class ConceptPublicationCountDocument
{
    public string Id { get; set; } = "";          // = SkillCode.Value (canonical)
    public int PublishedCount { get; set; }       // monotonic non-negative
    public DateTimeOffset LastUpdatedAt { get; set; }
}
```

One row per `SkillCode`. ~73 leaves means ~73 rows total; trivially fits in memory if we want to cache it.

**Projection** (MultiStream, keyed on `SkillCode`):
```csharp
public sealed class ConceptPublicationCountProjection
    : MultiStreamProjection<ConceptPublicationCountDocument, string>
{
    public ConceptPublicationCountProjection()
    {
        // Phase 1 calibration corpus uses Confirmed events (curator gates publish).
        // Phase 1 post-calibration uses Extracted events that auto-confirm.
        // Both contribute; the projection treats them as "this question is in the corpus".
        Identity<QuestionConceptsConfirmed_V1>(e => e.QuestionId);
        // Note: we do NOT take a fan-out at projection time over `Concepts[]`.
        // Marten projections are keyed; we use a separate per-skill key projection
        // by emitting one row per (SkillCode, QuestionId) pair via a custom slicer.
        // See implementation note below.
    }
    // ... custom slicer fan-out + Apply methods ...
}
```

**Slicer**: `MultiStreamProjection<T, string>` keyed on `QuestionId` doesn't naturally fan one event into N keys. Use a custom `IEventSlicer` that takes one `QuestionConceptsConfirmed_V1` and emits one event-projection pair per `SkillCode` in the concept set. Rejected alternatives: a flat `QuestionConceptMembershipDocument` (every floor check becomes a SQL COUNT — slower than a keyed `LoadAsync`), and Marten's async document-change subscription (weaker guarantees than event projections).

**Idempotency under replay**. To handle "Confirm N adds X; Confirm N+1 removes X", the per-skill document carries a private `Dictionary<string, bool> MemberQuestions` (persisted as a JSON column on the same document). On Confirmed: set `MemberQuestions[QuestionId] = true` for every skill in the new list; set `false` for any previously-true skill now absent. Recompute `PublishedCount = MemberQuestions.Count(kv => kv.Value)`. Final state is a pure function of the latest Confirmed per `QuestionId` — replay-safe.

**Lifecycle**: Inline. Counter is consulted on every student answer; staleness would let a marginally-published skill stay below floor for hours. Inline adds ~1-3ms to draft-confirm writes (curator-action, not student-hot).

### Counter binding

```csharp
public sealed class MartenConceptItemPublicationCounter
    : IConceptItemPublicationCounter
{
    private readonly IDocumentStore _store;
    public int StabilityFloor => 10;       // ADR-0062 §Phase 2 precondition

    public async Task<int> GetPublishedItemCountAsync(SkillCode skill, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<ConceptPublicationCountDocument>(skill.Value, ct);
        return doc?.PublishedCount ?? 0;
    }

    public async Task<bool> IsAboveStabilityFloorAsync(SkillCode skill, CancellationToken ct)
    {
        try { return await GetPublishedItemCountAsync(skill, ct) >= StabilityFloor; }
        catch { return false; }                  // FAIL-CLOSED on any error
    }
}
```

**Performance math**: ~73 rows × ~80 bytes ≈ 6KB total. Marten `LoadAsync` by primary key = B-tree index lookup, 1-2 disk reads cold, all-RAM hot. One fan-out = N supporting × one `LoadAsync`. Typical N=2-3 → ≤15ms p99 on managed RDS, ≤3ms on local Postgres. Existing answer-endpoint budget (BKT update + event append) is ~50ms p99 → ≤30% added. Acceptable. Optional 60s in-memory cache wrapper bounds worst-case staleness at 1 min; not load-bearing.

### Supporting-concept nudge service

```csharp
public interface ISupportingConceptNudgeService
{
    /// <summary>
    /// Fan out a positive mastery nudge across the question's supporting concepts.
    /// Called AFTER the BKT update on the primary concept. Skips silently when:
    ///   - the answer was wrong (no nudge on incorrect attempts — Phase 2 is positive-only)
    ///   - the question has no supporting concepts (single-skill question)
    ///   - the supporting concept is below the publication floor (≥10 items per leaf)
    ///   - the feature flag is off
    /// </summary>
    Task<IReadOnlyList<MasterySignalEmitted_V1>> EmitNudgesAsync(
        string studentAnonId,
        string examTargetCode,
        string questionId,
        bool isCorrect,
        CancellationToken ct);
}

public sealed class SupportingConceptNudgeService : ISupportingConceptNudgeService
{
    private readonly IDocumentStore _store;
    private readonly IConceptItemPublicationCounter _counter;
    private readonly IMasterySignalEmitter _emitter;
    private readonly IOptions<SupportingConceptNudgeOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SupportingConceptNudgeService> _log;

    public async Task<IReadOnlyList<MasterySignalEmitted_V1>> EmitNudgesAsync(...)
    {
        if (!_options.Value.Enabled) return Array.Empty<MasterySignalEmitted_V1>();
        if (!isCorrect)               return Array.Empty<MasterySignalEmitted_V1>();
        // load QuestionDocument, take ConceptIds[1..] as supporting set
        // for each supporting skill: counter check; if pass, build event with delta = options.Delta
        // emit each event; collect into result
        // catch-all wrapper: log + return what we have so far; never throw
    }
}
```

### Constants and tunables

```csharp
public sealed class SupportingConceptNudgeOptions
{
    public const string TriggerSource = "supporting_concept_nudge_v1";
    /// Default delta: half of MasterySignalOptions.DefaultDelta (0.05) per ADR-0062 §3.
    public const double DefaultDelta = 0.025d;
    public bool Enabled { get; set; } = false;       // flag-default OFF
    public double Delta { get; set; } = DefaultDelta;
}
```

The nudge uses a NEW trigger string `supporting_concept_nudge_v1` so downstream BKT consumers can filter PRR-381 (`post_reflection_retry_success`, full delta) from Phase 2 supporting-concept nudges (`supporting_concept_nudge_v1`, half delta) without inspecting the magnitude.

### DI registration

Extends `MasterySignalServiceRegistration`:

```csharp
public static IServiceCollection AddPhase2SupportingConceptNudges(
    this IServiceCollection services)
{
    services.AddOptions<SupportingConceptNudgeOptions>()
        .BindConfiguration("Cena:Concepts:SupportingNudges");

    // Production binding: Marten-backed counter. Replaces the Null binding.
    services.Replace(ServiceDescriptor.Singleton<IConceptItemPublicationCounter,
        MartenConceptItemPublicationCounter>());

    services.TryAddSingleton<ISupportingConceptNudgeService, SupportingConceptNudgeService>();
    return services;
}
```

`MartenConfiguration.cs` adds:

```csharp
opts.Schema.For<ConceptPublicationCountDocument>()
    .Identity(x => x.Id);
opts.Projections.Add<ConceptPublicationCountProjection>(ProjectionLifecycle.Inline);
```

### The student-session call seam

The fan-out happens AFTER the BKT update on the primary concept, on the same code path that today appends `ConceptAttempted_V3`. Locating the seam is part of the implementer's first task — grep the answer-endpoint for `ConceptAttempted_V3` write site. The seam is:

```csharp
// existing path:
session.Events.Append(streamKey, new ConceptAttempted_V3(...));
// NEW:
var nudges = await supportingNudgeSvc.EmitNudgesAsync(
    studentAnonId, examTargetCode, questionId, isCorrect, ct);
// nudges have already been emitted via IMasterySignalEmitter (separate stream).
// The collection is returned only for response-shape diagnostics; the answer
// endpoint does NOT include it in the student-facing response (no "you got
// nudge X! also nudge Y!" — that would be the variable-ratio reward pattern
// the shipgate bans).
```

The nudges are emitted **after** the primary-event append BUT in their own stream (`masterysignal-{studentAnonId}`), so an answer-handler failure between the two writes is recoverable by re-running the nudge service idempotently — but in v1 we accept "nudge dropped on partial failure" because the loss is bounded (one half-delta on a supporting skill, not a correctness signal).

## §4 Acceptance criteria

A coder agent has shipped Phase 2 when ALL of these hold:

1. **`MartenConceptItemPublicationCounter.GetPublishedItemCountAsync(skill_with_no_published_items)`** returns 0 and the call completes in <10ms p99 against a fresh local Postgres.
2. **`MartenConceptItemPublicationCounter.IsAboveStabilityFloorAsync`** returns false when `PublishedCount < 10`, true when `>= 10`. Threshold sourced from the interface's `StabilityFloor`.
3. **Counter projection idempotency**: replaying a stream of (Confirm A → Confirm B → Confirm A) for the same question converges to the count produced by a single Confirm A. (Pin via `Marten.Daemon.Resiliency` integration test.)
4. **Counter projection cohort-wide**: confirming 10 questions across 10 different curators (10 different streams), each with `SkillCode=X` in their concept set, results in `count(X) == 10`. Cross-curator aggregation works.
5. **Counter projection track-agnostic**: a `SkillCode` that appears in 4yu and 5yu items both contributes — the count is across all tracks, matching ADR-0062's leaf-only granularity.
6. **`SupportingConceptNudgeService.EmitNudgesAsync` with `Enabled=false`**: returns empty, calls neither counter nor emitter.
7. **`SupportingConceptNudgeService.EmitNudgesAsync` with `isCorrect=false`**: returns empty, calls neither counter nor emitter (positive-only invariant).
8. **`SupportingConceptNudgeService.EmitNudgesAsync` with all supporting skills below floor**: returns empty, calls counter (one per skill), does NOT call emitter.
9. **`SupportingConceptNudgeService.EmitNudgesAsync` with one supporting skill above floor**: returns one event, the event's `MasteryDelta == options.Delta == 0.025`, `TriggerSource == "supporting_concept_nudge_v1"`.
10. **Magnitude invariant**: every emitted Phase-2 event has `MasteryDelta == 0.025` exactly (half of the 0.05 PRR-381 default), and the event's TriggerSource starts with `"supporting_concept_nudge_"`. (Property-test pin.)
11. **Negative invariant**: no emitted Phase-2 event ever has a negative `MasteryDelta`. (Architecture test that scans `MasterySignalEmitted_V1` payloads with a `supporting_concept_nudge_*` trigger.)
12. **Trigger discoverability**: `MasterySignalTrigger.SupportingConceptNudgeV1 = "supporting_concept_nudge_v1"` constant added to the same class that holds `PostReflectionRetrySuccess`.
13. **Ship-gate banned-terms scanner passes**: the new code introduces no string matching the banned set (streak / loss / variable-ratio terms). CI scanner pre-existing; just must not regress.
14. **End-to-end (real Postgres + real flag on)**: a student-answer flow on a question with two supporting concepts, both above floor, results in (a) one `ConceptAttempted_V3` on the student's session stream, (b) two `MasterySignalEmitted_V1` on the student's `masterysignal-` stream, (c) `IPostReflectionMasteryService` is NOT involved (Phase-2 nudges are a sibling channel, not a fork of the post-reflection path).
15. **No behavior change with flag off**: every existing test that doesn't touch Phase-2 wiring passes unchanged. Baseline: capture origin/main test pass count first; branch must equal it.

## §5 Test plan

| Test | Type | What it pins |
|---|---|---|
| `ConceptPublicationCountProjectionTests.SingleConfirm_IncrementsAllListedSkills` | unit (in-mem projection) | basic fan-out |
| `ConceptPublicationCountProjectionTests.SecondConfirmRemovesSkill_Decrements` | unit | curator removed-skill case |
| `ConceptPublicationCountProjectionTests.IdempotentReplay` | unit | replay convergence (memory rule `feedback_event_sourcing_replay_check`) |
| `ConceptPublicationCountProjectionTests.SameQuestionConfirmedTwice_StaysAtOne` | unit | duplicate-confirm idempotency |
| `MartenConceptItemPublicationCounterTests.NoDocument_ReturnsZero` | unit | new-skill case is 0, not error |
| `MartenConceptItemPublicationCounterTests.MartenError_FailsClosed` | unit | exception → `IsAboveStabilityFloorAsync` returns false |
| `MartenConceptItemPublicationCounterIntegrationTests.TenConfirmsUnlockGate` | integration (real Postgres) | end-to-end count via the projection |
| `SupportingConceptNudgeServiceTests.FlagOff_NoCalls` | unit | guard short-circuit |
| `SupportingConceptNudgeServiceTests.IncorrectAnswer_NoNudge` | unit | positive-only invariant |
| `SupportingConceptNudgeServiceTests.NoSupportingConcepts_NoNudge` | unit | single-skill question case |
| `SupportingConceptNudgeServiceTests.SupportingBelowFloor_Skipped` | unit | precondition gate |
| `SupportingConceptNudgeServiceTests.MixedFloor_OnlyAboveEmits` | unit | partial-fan-out: 2 supporting skills, only one above floor → one nudge |
| `SupportingConceptNudgeServiceTests.EmitterThrows_ServiceLogsAndContinues` | unit | failure-mode containment (§7) |
| `SupportingConceptNudgeServiceTests.MagnitudePinnedToHalfDelta` | unit | `MasteryDelta == 0.025` exact, no drift |
| `SupportingConceptNudgeServiceTests.NegativeDeltaImpossible` | property | random-input fuzz: emitted delta > 0 |
| `Phase2NudgeBannedTermsArchTest` | architecture | scans new files for banned ship-gate terms |
| `StudentAnswerEndpoint_Phase2_E2ETests.CorrectAnswer_FansOutToSupporting` | integration (real Postgres) | full path through the answer endpoint |
| `StudentAnswerEndpoint_Phase2_E2ETests.NoSpyEffectOnPRRRetry` | integration | a PRR-381 retry-success path emits exactly one nudge with `post_reflection_retry_success` trigger; the Phase-2 service is not invoked because the answer endpoint and the retry endpoint are separate seams |

Integration tests using real Postgres use the existing `RequiresPostgresFact` attribute (skips cleanly when docker stack isn't up; per `feedback_container_state_before_build.md`).

## §6 Migration / rollout plan

Two flags, four steps. Counter graduation is decoupled from gate activation so the projection can land + warm up before student-facing fan-out turns on.

**Flag 1**: `Cena:Concepts:Phase2:CounterEnabled` (default `false`).
**Flag 2**: `Cena:Concepts:SupportingNudges:Enabled` (default `false`).

**Step 1 (counter dark)**: merge with both flags `false`. Register `ConceptPublicationCountProjection` UNCONDITIONALLY — projections are storage and replay-safe; building it silently means there's a populated doc to query the moment Flag 1 flips.

**Step 2 (counter live, gate closed)**: `Cena:Concepts:Phase2:CounterEnabled=true` on staging → prod. The DI binding swaps to `MartenConceptItemPublicationCounter`; `SupportingConceptNudgeService` registered but `Enabled=false` so no nudges fire. Validates projection queryability in prod with INFO-level once-per-skill logging.

**Step 3 (gate open on staging)**: `Cena:Concepts:SupportingNudges:Enabled=true` on staging. E2E emulator: student attempts a question with two supporting concepts both ≥10 → two nudges on the masterysignal stream. Verify via Marten query.

**Step 4 (prod)**: flip after staging green for ≥48h.

**Reverse path**: Flag 2 off → nudge service no-ops; already-emitted events stay (append-only audit; no BKT consumer yet). Flag 1 off → counter rebinds to `Null` → gate closes even if Flag 2 is on. Belt-and-suspenders rollback.

**No DB migration**. `opts.Schema.For<>().Identity(x => x.Id)` auto-creates on startup. The Inline projection backfills on first start; implementer should verify backfill latency on a representative-size store before flipping Flag 1 — ~73 skills × N confirm events is bounded and completes in seconds.

**Cohort leakage is intended**. Curator A confirming 10 items with skill X immediately unlocks the gate for curator B's students. ADR-0062 §Decision drivers cites EdNet KT1 (188 skills × 13,169 questions) — we WANT cross-cohort calibration as the corpus grows. Per-tenant scoping is a future ADR (open question §10.1).

## §7 Failure modes + degradation

Every nudge path is fail-closed at the gate (silent suppression) and fail-isolated at the emitter (one bad skill doesn't stop the rest). The student-answer endpoint MUST NOT fail because nudge fan-out failed.

| Failure mode | Behavior | Why |
|---|---|---|
| Counter projection lags behind Confirm events | `IsAboveStabilityFloorAsync` returns stale value | Inline projection's catch-up window is sub-second; worst case one student attempt right after the 10th confirm doesn't fan out, the next does. |
| Counter throws (DB, deserialization) | Returns false | **Fail-closed** per `IConceptItemPublicationCounter` contract. |
| Counter returns "above floor" but the supporting `SkillCode` is non-canonical | Fan-out emits with the value as-is | Canonicalization ran upstream at confirm time; non-canonical values in `ConceptIds` are a Phase-1 bug, not Phase-2's to handle. |
| `IMasterySignalEmitter.EmitAsync` throws on skill #2 of 3 | Service logs at Error, continues to skill #3 | **Fail-isolated**. One bad skill must not suppress healthy nudges. |
| `MasterySignalEmitted_V1` Marten append fails | Logged at Error; partial fan-out completes | Primary-concept BKT update is on a separate session/transaction (§3 seam) — unaffected. |
| `QuestionDocument` not found | Empty list, Warning log | Rare deletion-between-answer-and-fan-out; not worth raising. |
| `QuestionDocument.ConceptIds` empty (legacy item) | Empty list silently | Legacy pre-Phase-1 items never had multi-concept tags. |
| Flag flips off mid-request | Snapshot-semantic `IOptions` → request completes at start-of-request value | Bounded: at most one stale-window emit, positive-only, no harm. |
| Service invoked outside the answer endpoint | Same checks; `isCorrect=false` or flag off → empty list | Interface is the contract; misuse is contained. |

The student-answer endpoint wraps the call to `EmitNudgesAsync` in a `try/catch` that logs and swallows. **The student NEVER sees a 500 because of nudge fan-out.** The nudge channel is decorative correctness — it cannot block the primary-correctness path.

## §8 Cost + telemetry

**Cost**: storage ≤1MB total (73 rows × 80B + ~5,000 questions × 3 skills × 60B membership cells). Per-attempt: N × one `LoadAsync` ≤15ms p99 (N typically 0-3). Per-confirm: one projection apply Inline, ≤10ms p99 added to a curator-action path. No external API cost.

**Telemetry (the channel must prove it's working AND must be killable on evidence of harm)**:

```
cena.concepts.nudge.attempts.total{outcome="emitted|gated|skipped_incorrect|disabled|error"}
cena.concepts.nudge.gated_below_floor.total{skill}      # which skills are still below floor
cena.concepts.nudge.skills_per_attempt                  (Histogram, 0..5)
cena.concepts.nudge.duration.ms                         (Histogram)
cena.concepts.publication.count{skill}                  (Gauge — sampled hourly)
```

**Proof the nudge channel is working**:
- `cena.concepts.nudge.attempts.total{outcome="emitted"}` rises after staging gate flip.
- BKT consumer (when Phase 3 lands) sees supporting-skill posteriors moving on questions where they didn't before.

**Proof the nudge channel is hurting** (kill criteria):
- If curator-override-after-publication telemetry (Phase 1.5 metric) shows curators NOW frequently *removing* a previously-confirmed supporting concept after they see Phase-2 nudges firing on it, the system is over-emitting. Threshold: >10% retroactive removals over 30 days → flip Flag 2 off and revisit.
- If overall skill posteriors drift unexpectedly (e.g. a student's `function-domain` posterior climbs faster than primary-concept evidence supports), measured against a synthetic baseline → flip Flag 2 off and add per-skill caps.
- If post-reflection retry success metrics (PRR-381) regress because the supporting-channel signal is overwhelming the post-reflection signal → flip Flag 2 off; consider lowering the Phase-2 delta below 0.025 in a follow-up brief.

## §9 Out of scope

1. **BKT update logic for supporting concepts.** ADR-0062 §3: BKT keys on primary only. This brief emits events; consumption is Phase 3.
2. **Per-tenant counter isolation** (TENANCY-P3 territory). Future ADR.
3. **Variable `StabilityFloor`**. Hardcoded to 10 per ADR-0062 §3.7.
4. **Counter cache layer.** Optional; not load-bearing.
5. **Nudges on `WrongAnswerWithReflection`.** Phase 2 is positive-only; reflection-retry-success is PRR-381's domain.
6. **Curator UI** showing "X items to unlock nudges" — separate front-end brief.
7. **Decay of nudge signals.** Append-only; HLR-style decay is a Phase-3 BKT-side concern.
8. **Per-question suppress flag.** Curators get granularity by editing the supporting set; no separate flag.

## §10 Open questions

1. **Counter cohort scope**: global vs `(tenantId, skillCode)` keyed? Stay global for v1: per-tenant keying would silence small institutes that can't independently reach the floor. Revisit at first multi-institute deploy.
2. **Slicer ergonomics**: `IEventSlicer<TDoc, TKey>` vs hand-rolled `MultiStreamProjection.Identities<T>(e => e.Concepts.Select(...))`? Both work; pick whichever the pinned Marten version exposes more cleanly.
3. **Backfill timing**: does flipping Flag 1 require a completed backfill, or is "first read pulls the projection forward" enough? Inline-on-append behavior on existing events at deploy time depends on Marten config — verify before staging flip.
4. **Fan-out order**: nudge before or after the BKT primary append? v1 = after (primary is the correctness signal; nudges are informational). Inconsistency window on process crash is bounded; no reconcile path is provided in v1.
5. **Dedup**: if `ConceptIds` contained `[primary, X, X, Y]` (shouldn't happen per the canonicalizer, but defense-in-depth), do we emit twice? v1: `Skip(1).Distinct(StringComparer.Ordinal)` — implementer should add it explicitly.
6. **Retroactive nudges**: when a leaf crosses the floor, do we backfill past attempts? v1: forward-only. Backfill is a future ADR if anyone asks.

---

```text
SELF-CHECK
  baseline:                NOT MEASURED — docs-only brief; no test counts to baseline against.
  refusals-considered:     refused per-tenant counter scoping in v1; refused decay logic; refused "celebrate" UX for emitted nudges (would be variable-ratio); refused to entangle with PostReflectionMasteryService.
  meta-patterns-noticed:   the codebase already has the (Counter, NullCounter, MartenCounter) shape and (InMemoryEmitter, MartenEmitter) shape — Phase 2 reuses both verbatim. No new abstractions introduced where existing ones fit.
  deletion-vs-addition:    nothing deleted on disk. The Null counter binding is *replaced* in DI (not removed from source) so test contexts can still resolve it.
  commit-message-lines:    no commit this turn (will land via the architecture-briefs branch commit).
  honesty-led-with:        the counter projection slicer choice is the load-bearing complexity — surfaced in §3 with options A/B/C explicit, plus open questions §10.2/§10.3.
  smoke-evidence:          label not claimed.
```
