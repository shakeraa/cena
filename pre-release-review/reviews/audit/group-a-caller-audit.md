# Group A substrates — live caller audit (prr-151)

> Read-only audit. Every verdict backed by `path:line` citations.
> Method: grep for type + method references across `src/`, filter out
> `**/*Tests*/`, trace surviving call sites back to a live entry point
> (HTTP endpoint registered in a Program.cs, SignalR hub, background
> worker, actor processing real messages, startup seeder). Flutter
> (`src/mobile/`) is treated as retired per user memory (2026-04-13)
> and noted but does not count as a live caller.

---

## R-01: AdaptiveScheduler

**Verdict**: ORPHANED

**Production callers found** (exclude tests):
- *(none)*

**Test-only callers**:
- `src/actors/Cena.Actors.Tests/Mastery/CompressionDiagnosticTests.cs:177,190,201,210,223,233,248` — `AdaptiveScheduler.PrioritizeTopics(...)`
- `src/actors/Cena.Actors.Tests/Mastery/TopicPrerequisiteGraphTests.cs:123,147` — `AdaptiveScheduler.PrioritizeTopics(...)`

**Internal-only references**:
- `src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs:113,123` — declaration of `static class AdaptiveScheduler` and `PrioritizeTopics` method
- `src/actors/Cena.Actors/Mastery/TopicPrerequisiteGraph.cs:6` — doc-comment only, no executable call

Note: `TutorActor.cs` (`src/actors/Cena.Actors/Tutoring/TutorActor.cs`) was listed in `retired.md` R-01 as a caller. It is not — `TutorActor` handles conversational tutoring (LLM chat), it never touches `AdaptiveScheduler`. The `retired.md` entry is wrong to cite it as a caller site.

**Path from user-facing entry point**:
None. `PrioritizeTopics(...)` is invoked only from `CompressionDiagnosticTests` and `TopicPrerequisiteGraphTests`. No HTTP endpoint, no SignalR hub, no actor message handler, no startup seeder, no Admin API, no Student API reaches the method. The Flutter mirror (`src/mobile/lib/core/services/adaptive_interleaving.dart`, cited in retired.md) is a retired platform.

**Recommendation**:
- Already captured by **prr-149 (live caller)** and **prr-148 (student-input UI)**. Confirm those two are still open; if one is closed prematurely, reopen.
- Do NOT delete the substrate — the research-backed algorithm has been implemented + tested and is needed for the Phase 1B learning-path endpoint. The gap is purely the missing caller (an endpoint or a scheduler actor that invokes `PrioritizeTopics` on a real learner's snapshot).

---

## R-03: ScaffoldingService

**Verdict**: WIRED

**Production callers found** (exclude tests):
- `src/api/Cena.Student.Api.Host/Program.cs:234` — DI registration `AddSingleton<IScaffoldingService, ScaffoldingServiceWrapper>()`
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:537` — `IScaffoldingService` resolved by GET `/{sessionId}/current-question` handler
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1074,1077` — `ScaffoldingService.DetermineLevel(...)`, `GetScaffoldingMetadata(...)` on the answer-flow path
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs:523,525,634` — `ScaffoldingService.DetermineLevel(...)`, `GetScaffoldingMetadata(...)` in the actor-path branches (hint + next-question)
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs:953` — `Cena.Actors.Mastery.ScaffoldingService.DetermineLevel(effectiveMastery, psi)` inside the inline-BKT branch
- `src/actors/Cena.Actors/Students/StudentActor.Queries.cs:297` — `ScaffoldingService.DetermineLevel(mastery, 1.0f)` on a student query

**Test-only callers** (listed for completeness):
- `src/actors/Cena.Actors.Tests/Session/ScaffoldingIntegrationTests.cs` (multiple)
- `src/actors/Cena.Actors.Tests/Session/SessionHintEndpointTests.cs:195,206,207`
- `src/actors/Cena.Actors.Tests/Mastery/ScaffoldingServiceTests.cs` (multiple)

**Path from user-facing entry point**:
`HTTP GET /api/session/{sessionId}/current-question` (Student API, SessionEndpoints.cs:537) → handler resolves `IScaffoldingService` and calls `DetermineLevel + GetScaffoldingMetadata` to shape the response payload. Also reached from `POST /api/session/{sessionId}/answer` (SessionEndpoints.cs:1074) and from `LearningSessionActor` which processes actor-side session commands.

**Recommendation**:
- Keep. ScaffoldingService is the clearest fully-wired substrate in Group A.
- No follow-up task needed; prr-041 (BKT + fading policy ADR, the R-03 delta) remains in scope for the fading-policy refinement, but wiring is not the gap.

---

## R-04: HlrCalculator

**Verdict**: PARTIALLY-WIRED (one method wired into a user flow; three methods orphaned)

**Production callers found** (exclude tests):
- `src/actors/Cena.Actors/Students/StudentActor.Mastery.cs:50` — `HlrCalculator.ComputeHalfLife(hlrFeatures, hlrWeights)` called from `EnrichMasteryAfterAttempt`, which runs after every answered question for a student
- `src/actors/Cena.Actors/Simulation/MasterySimulator.cs:235` — `HlrCalculator.ComputeHalfLife(...)` used by simulator, which is run at startup via `SimulationEventSeeder.SeedSimulationEventsAsync` (registered at `src/actors/Cena.Actors.Host/Program.cs:794` and referenced by an Admin endpoint at `src/api/Cena.Admin.Api/AdminApiEndpoints.cs:616`)
- `src/actors/Cena.Actors/Mastery/MasteryPipeline.cs:45` — `HlrCalculator.UpdateState(...)` inside `MasteryPipeline.ProcessAttempt`, but `ProcessAttempt` has zero production callers (only `Cena.Actors.Tests/Mastery/EffectiveMasteryTests.cs:129,156`)

**Test-only callers**:
- `src/actors/Cena.Actors.Tests/Mastery/HlrCalculatorTests.cs:17,25,32,39,45,53,57,71,81,89,98,107` — exercises `ComputeRecall`, `ScheduleNextReview`, `UpdateState`, `ComputeHalfLife`

**Per-method verdict**:
| Method | Live caller? |
|---|---|
| `ComputeHalfLife` | WIRED (`StudentActor.Mastery.cs:50` + simulator) |
| `UpdateState` | ORPHANED (only reached via `MasteryPipeline.ProcessAttempt`, which is itself test-only) |
| `ComputeRecall` | ORPHANED (tests only) |
| `ScheduleNextReview` | ORPHANED (tests only) |

**Path from user-facing entry point**:
`HTTP POST /api/session/{sessionId}/answer` → `LearningSessionActor` handles the attempt → staged events applied to `StudentActor` → `StudentActor.EnrichMasteryAfterAttempt` fires `HlrCalculator.ComputeHalfLife` and stashes the half-life into the `HlrTimers` dictionary used by the decay scan. This is a real, per-answer production path.

**Recommendation**:
- Keep the one wired method.
- Open a follow-up to either (a) wire `MasteryPipeline.ProcessAttempt` into `StudentActor.EnrichMasteryAfterAttempt` (so `UpdateState`, `ComputeRecall`, `ScheduleNextReview` become live) or (b) delete the three orphaned methods to stop paying maintenance cost for dead API surface. Option (a) is the ADR-0036-compliant path; option (b) is the pre-launch simplification.

---

## R-08: IrtCalibrationPipeline / BktService / EloScoring

**Verdict**: PARTIALLY-WIRED
- `BktService` — WIRED
- `EloScoring` — WIRED
- `IrtCalibrationPipeline` — ORPHANED

### BktService — WIRED

**Production callers**:
- `src/actors/Cena.Actors.Host/Program.cs:202` — `AddSingleton<IBktService, BktService>()` DI registration in Actor Host
- `src/api/Cena.Student.Api.Host/Program.cs:246` — `AddSingleton<IBktService, BktService>()` DI registration in Student API
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:683` — resolved by `POST /{sessionId}/answer` handler to run the Corbett-Anderson update
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs:28,29,111,112` — `_bkt` field + ctor injection; called in the session's answer processing path
- `src/actors/Cena.Actors/Students/StudentActor.cs:54,126` and `StudentActor.Commands.cs:75-90` — inline-BKT path when session actor isn't yet available
- `src/actors/Cena.Actors/Services/HintAdjustedBktService.cs:16,19` — wraps `IBktService` for the hint-adjusted SAI-02 flow
- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs:140,154` — `BktPlusCalculator` composes `IBktService`

### EloScoring — WIRED

**Production callers**:
- `src/actors/Cena.Actors/Services/EloDifficultyService.cs:146` — `EloScoring.StudentKFactor(profile.EloAttemptCount)` on the answer-update path (EloDifficultyService is registered in Program.cs and resolved by `SessionEndpoints.cs:683` as `IEloDifficultyService`)
- `src/actors/Cena.Actors/Mastery/ItemSelector.cs:88` — `EloScoring.ExpectedCorrectness(studentTheta, item.DifficultyElo)` inside the item-selection heuristic (ItemSelector is used by MasterySimulator which seeds demo data; check whether ItemSelector is also called from live endpoints)
- `src/actors/Cena.Actors/Simulation/MasterySimulator.cs:261,262,264` — simulation path (startup seed)

### IrtCalibrationPipeline — ORPHANED

**Production callers** (exclude tests): *(none — it is a registered-by-name dependency of two orphaned consumers)*

**Surface:**
- `src/api/Cena.Student.Api.Host/Endpoints/DiagnosticEndpoints.cs:40` — parameter `IIrtCalibrationPipeline irt` on the `EstimateAbility` handler of `/api/diagnostic/estimate`. **But** the extension method `MapDiagnosticEndpoints` (defined at `DiagnosticEndpoints.cs:24`) is **never called** anywhere in `src/` — no `app.MapDiagnosticEndpoints()` in either `Cena.Student.Api.Host/Program.cs` or `Cena.Actors.Host/Program.cs`. The route therefore does not exist at runtime.
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs:272` — `CalibrateWithAnchors(..., IIrtCalibrationPipeline pipeline)` on `ItemBankHealthService`. **But** `ItemBankHealthService` is never DI-registered and never instantiated (no `new ItemBankHealthService(` or `services.Add*<ItemBankHealthService>`). It is an unreferenced class.
- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs:106,132,142` — interface + impl declarations
- `src/actors/Cena.Actors/Services/BagrutAnchorProvider.cs:9,47` — doc-comment references only

No DI registration for `IIrtCalibrationPipeline` was found in any Program.cs / ServiceRegistration file. A request to `/api/diagnostic/estimate` would 404 because the route is not mapped; even if it were mapped, DI resolution would throw because the interface has no registered implementation.

**Path from user-facing entry point**:
None. Closing the gap requires (a) calling `app.MapDiagnosticEndpoints()` from `Cena.Student.Api.Host/Program.cs`, (b) registering `IIrtCalibrationPipeline` → `IrtCalibrationPipeline` in DI, (c) implementing the admin item-bank health endpoint that actually instantiates `ItemBankHealthService`.

**Recommendation**:
- `BktService`, `EloScoring`: keep as WIRED.
- `IrtCalibrationPipeline`: open a follow-up to wire the `/api/diagnostic/estimate` route end-to-end (the Ministry-exam diagnostic IRT estimate is in the critical path for prr-007 "IRT-theta architecturally isolated from student-visible DTOs"). Until wired, the diagnostic substrate is shipping dead code.

---

## R-09: MisconceptionDetectionService

**Verdict**: WIRED

**Production callers found** (exclude tests):
- `src/actors/Cena.Actors.Host/Program.cs:244` — `AddSingleton<IMisconceptionDetectionService, MisconceptionDetectionService>()` DI registration (Actor Host)
- `src/api/Cena.Student.Api.Host/Program.cs:295` — `AddSingleton<IMisconceptionDetectionService, MisconceptionDetectionService>()` DI registration (Student API)
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:683` — `[FromServices] IMisconceptionDetectionService misconceptionDetector` parameter on the `POST /{sessionId}/answer` handler
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1423,1433` — helper that delegates wrong-answer classification to the detector

**Test-only callers**:
- `src/actors/Cena.Actors.Tests/Session/SessionMisconceptionDetectionTests.cs:18-19`
- `src/actors/Cena.Actors.Tests/Session/AnswerEndpointDiResolutionTests.cs:71,85,88` — DI-resolution sanity check
- `src/actors/Cena.Actors.Tests/Services/MisconceptionDetectionServiceTests.cs:10,16,17`

**Path from user-facing entry point**:
`HTTP POST /api/session/{sessionId}/answer` (Student API, SessionEndpoints.cs:683) → handler resolves `IMisconceptionDetectionService` alongside `IBktService`, `IErrorClassificationService`, etc. On a wrong answer, the detector classifies the misconception tag, which travels into the event stream and the outgoing DTO. DI-resolution is covered by an integration test.

**Recommendation**:
- Keep. No wiring gap. Verify ADR-0003 (session-scoped, 30-day retention) is enforced at the storage layer — that's a separate compliance task, not a caller-audit finding.

---

## R-13: ParentDigest / ParentalControls

**Verdict**: PARTIALLY-WIRED (R-13 is two substrates glued together)
- ParentalControls — WIRED (partially — event-sourcing only; enforcement records orphaned)
- ParentDigest — ORPHANED

### ParentalControls — PARTIALLY-WIRED (event write/read only)

**Production callers**:
- `src/api/Cena.Admin.Api/Features/ParentConsole/TimeBudgetEndpoint.cs:84,164` — GET/PUT `/api/v1/parent/minors/{studentAnonId}/timebudget`; persists + reads `ParentalControlsConfiguredV1` events
- `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs:277` — `MapTimeBudgetEndpoint(app)` registered in Admin API

**Orphaned parts of the bounded context** (test-only):
- `TimeBudget.IsOverBudget`, `UsageRatio`, `IsConfigured` (record methods at `ParentalControls/TimeBudget.cs:33,40,47`) — only tested, never consulted at runtime
- `TimeOfDayRestriction.IsOutsideWindow` (TimeBudget.cs:66) — test-only
- `ParentalControlSettings` record + `None(...)`, `IsTopicAllowed(...)`, `IsAnyControlConfigured` (TimeBudget.cs:82-105) — test-only; `AllowedTopic`, `SoftCapDecision`, `SoftCapBanner` — test-only
- Session pipeline never consults any of these for soft-cap banner rendering

**Path from user-facing entry point**:
`HTTP GET/PUT /api/v1/parent/minors/{studentAnonId}/timebudget` (Admin API, TimeBudgetEndpoint.cs:57-72) → emits/reads `ParentalControlsConfiguredV1` events via Marten. **No session-time enforcement path**: the student session never looks up the latest `ParentalControlsConfiguredV1`, never folds it into a `ParentalControlSettings` aggregate, never surfaces a `SoftCapDecision` banner. The parent can configure; the student will not see the result.

### ParentDigest — ORPHANED

**Production callers** (exclude tests): *(none)*

**Test-only callers**:
- `src/actors/Cena.Actors.Tests/ParentDigest/ParentDigestRendererTests.cs` (multiple `ParentDigestRenderer.Render(env)`)
- `src/actors/Cena.Actors.Tests/ParentDigest/ParentDigestAggregatorTests.cs` (multiple `ParentDigestAggregator.BuildEnvelope(...)`)
- `src/actors/Cena.Actors.Tests/ParentDigest/ParentDigestShipgateTests.cs` — shipgate-banned-copy verification
- `src/actors/Cena.Actors.Tests/ParentDigest/TwilioWhatsAppSenderTests.cs` — mapping + unconfigured-state tests
- `src/actors/Cena.Actors.Tests/ParentDigest/WhatsAppChannelTests.cs` — Null sender + enum tests

**No production wiring found**:
- No DI registration for `IWhatsAppSender`, `TwilioWhatsAppSender`, or `IWhatsAppRecipientLookup` in any Program.cs
- No hosted service / background worker that builds digests or flushes them to Twilio
- No endpoint that triggers a digest build
- No actor that calls `ParentDigestAggregator.BuildEnvelope` or `ParentDigestRenderer.Render`

**Path from user-facing entry point**:
None. To reach these substrates, a caller must: (a) register `IWhatsAppSender` (Twilio or Null), (b) add a scheduled hosted service (weekly cron) that iterates consenting parents, (c) call `ParentDigestAggregator.BuildEnvelope` + `ParentDigestRenderer.Render` + `IWhatsAppSender.SendAsync`. None of these exist.

**Recommendation**:
- **ParentalControls**: open a follow-up to wire `ParentalControlsConfiguredV1` into the student session pipeline (read latest event → fold into `ParentalControlSettings` → emit `SoftCapDecision` banner on the session DTO). Until that path lands, the parent console is a write-only dead-letter endpoint.
- **ParentDigest**: open a follow-up to either (a) ship the WhatsApp scheduled digest (RDY-067 + RDY-069 were tagged as F5a/F5b in Phase 1B notes but never wired) or (b) mark the bounded context as "Phase 2, do not ship" and lift it out of the `Cena.Actors` project to an unreferenced folder so it doesn't dilute grep noise.

---

## R-15: CulturalContextService

**Verdict**: WIRED

**Production callers found** (exclude tests):
- `src/actors/Cena.Actors/Services/FocusDegradationService.cs:287,289,304,306,709` — consumes the `CulturalContext` enum (defined in `Services/CulturalContextService.cs:65`) as input to `FocusDegradationService` weight branching
- `src/actors/Cena.Actors.Host/Program.cs:220` — `AddSingleton<IFocusDegradationService, FocusDegradationService>()` registers the consumer
- Whichever caller hands `FocusDegradationService` a `ResilienceInput` with `CulturalContext` is the final upstream. The classifier (`CulturalContextService.Detect`) itself is not DI-registered and its `Detect(...)` is only called from `CulturalResilienceTests.cs:33,40,47,54,61,68,75`.

**Per-symbol breakdown**:
| Symbol | Live? |
|---|---|
| `enum CulturalContext` | WIRED (used by `FocusDegradationService`) |
| `CulturalContextInput` record | test-only |
| `ICulturalContextService` | test-only (no DI registration found) |
| `CulturalContextService.Detect(...)` | test-only |

**Test-only callers**:
- `src/actors/Cena.Actors.Tests/Services/CulturalResilienceTests.cs:14,22-75,99-167` — exercises both the classifier and the enum via `FocusDegradationService`

**Not to be confused with**: `src/api/Cena.Admin.Api/CulturalContextService.cs` — a **different type** with the same name, in the Admin API namespace (`Cena.Admin.Api.CulturalContextService` backed by Marten documents). That one IS fully wired (DI at `Registration/CenaAdminServiceRegistration.cs:138`, seeder at :139, endpoints at `AdminApiEndpoints.cs:1333,1344,1355,1370,1381`). It is not the substrate retired.md R-15 refers to.

**Path from user-facing entry point**:
Answer ingestion → `LearningSessionActor` → `FocusDegradationService.ComputeEngagement(ResilienceInput with CulturalContext)` → the enum value drives weight branching. Today the `CulturalContext` value that flows into `ResilienceInput` defaults to `CulturalContext.Unknown` (`FocusDegradationService.cs:709`); nothing reads a student language profile and calls `CulturalContextService.Detect` at runtime to populate that field.

**Recommendation**:
- Keep the enum (WIRED).
- Open a follow-up: wire `ICulturalContextService.Detect(...)` into the student-session context resolver so `ResilienceInput.CulturalContext` receives a real classification instead of the `Unknown` default. Without this, `FocusDegradationService`'s cultural-sensitive branches are dead weight for every student.

---

## R-22: Accommodations bounded context

**Verdict**: PARTIALLY-WIRED

**Production callers found** (exclude tests):
- `src/api/Cena.Admin.Api/Features/ParentConsole/AccommodationsEndpoints.cs:94,166,183,208` — GET/PUT `/api/v1/parent/minors/{studentAnonId}/accommodations`; reads `AccommodationProfileAssignedV1`, validates with `Phase1ADimensions.IsShipped`, parses `AccommodationDimension` enum, emits `AccommodationProfileAssignedV1`
- `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs:273` — `MapAccommodationsEndpoints(app)` registered in Admin API

**Orphaned parts of the bounded context** (test-only):
- `AccommodationProfile` record (`Accommodations/AccommodationProfile.cs:119`) — `AccommodationProfile.Default(...)` only called from `AccommodationProfileTests.cs:23`; `new AccommodationProfile(...)` only from the same test file (lines 32, 59)
- `AccommodationProfile.IsEnabled(AccommodationDimension)` (AccommodationProfile.cs:131) — test-only
- `MinistryAccommodationMapping.Lookup(...)`, `Translate(...)`, `Rows` (MinistryAccommodationMapping.cs:58) — test-only (`AccommodationProfileTests.cs:100,106,117,120,127,133,146,161`)

**Per-symbol breakdown**:
| Symbol | Live? |
|---|---|
| `enum AccommodationDimension` | WIRED |
| `enum AccommodationAssigner` | WIRED |
| `Phase1ADimensions.IsShipped` | WIRED (endpoint validation) |
| `AccommodationProfileAssignedV1` event | WIRED (persisted + replayed) |
| `AccommodationProfile` record + `IsEnabled(...)` | ORPHANED (no session-time rendering consults it) |
| `MinistryAccommodationMapping.Lookup/Translate/Rows` | ORPHANED (no onboarding / import flow translates Ministry Hatama codes) |

**Path from user-facing entry point**:
`HTTP GET/PUT /api/v1/parent/minors/{studentAnonId}/accommodations` (Admin API, AccommodationsEndpoints.cs:57-72) → emits/reads `AccommodationProfileAssignedV1` events. **No session-time enforcement path**: the student session never folds the latest event into an `AccommodationProfile`, never calls `IsEnabled` to gate the TTS button / extended-time countdown / distraction-reduced layout / no-comparative-stats decision. Parents can configure; students will not receive the accommodation.

Similarly, `MinistryAccommodationMapping.Translate("5")` (maps a Ministry Hatama code to the Cena runtime dimension set) is never called on the PUT handler — the endpoint accepts a `MinistryHatamaCode` string from the request, stores it on the event verbatim, but never invokes `Translate` to turn it into dimensions. A parent supplying Ministry code 5 (high-contrast) and no explicit `EnabledDimensions` will get nothing.

**Recommendation**:
- Keep the event + enum + validator.
- Open a follow-up: wire `AccommodationProfileAssignedV1` into the student session pipeline (fold event → `AccommodationProfile` → `IsEnabled(...)` gates in the rendering DTO). This is the critical gap — a disability-accommodation feature that stores consent but doesn't render the accommodation is a ministry-reportable compliance issue.
- Open a second follow-up: invoke `MinistryAccommodationMapping.Translate(MinistryHatamaCode)` inside `AccommodationsEndpoints.HandleSetAsync` so Ministry codes auto-populate the dimension set, rather than the current behaviour of storing the code as orphan metadata.

---

## Summary

| R-ID | Substrate | Verdict | Recommended follow-up |
|---|---|---|---|
| R-01 | AdaptiveScheduler | ORPHANED | Already captured by prr-149 (live caller) + prr-148 (student-input UI); keep code, wire caller |
| R-03 | ScaffoldingService | WIRED | None — only prr-041 (fading ADR) remains from R-03 delta list |
| R-04 | HlrCalculator | PARTIALLY-WIRED | New task: either wire MasteryPipeline.ProcessAttempt into StudentActor or delete UpdateState/ComputeRecall/ScheduleNextReview |
| R-08 | BktService | WIRED | None |
| R-08 | EloScoring | WIRED | None |
| R-08 | IrtCalibrationPipeline | ORPHANED | New task: map `/api/diagnostic/estimate`, register `IIrtCalibrationPipeline` DI, instantiate `ItemBankHealthService` — critical for prr-007 |
| R-09 | MisconceptionDetectionService | WIRED | None (verify ADR-0003 retention separately) |
| R-13 | ParentalControls (event shell) | PARTIALLY-WIRED | New task: wire ParentalControlsConfiguredV1 into session pipeline so SoftCapDecision banner reaches the student |
| R-13 | ParentDigest / WhatsApp | ORPHANED | New task: ship the RDY-067/RDY-069 scheduled worker OR descope to Phase 2 and move out of Cena.Actors |
| R-15 | CulturalContextService (enum) | WIRED | None |
| R-15 | CulturalContextService.Detect | ORPHANED | New task: wire Detect(...) into session-context resolver so ResilienceInput.CulturalContext ≠ Unknown |
| R-22 | Accommodations event shell | WIRED | None |
| R-22 | AccommodationProfile.IsEnabled | ORPHANED | New task: wire AccommodationProfileAssignedV1 fold → IsEnabled gates in session rendering DTO (compliance-critical) |
| R-22 | MinistryAccommodationMapping | ORPHANED | New task: call Translate(MinistryHatamaCode) inside AccommodationsEndpoints.HandleSetAsync |

**Verdict counts**:
- WIRED: 6 (ScaffoldingService, BktService, EloScoring, MisconceptionDetectionService, CulturalContext enum used by FocusDegradationService, Accommodations event shell)
- ORPHANED: 5 (AdaptiveScheduler, IrtCalibrationPipeline, ParentDigest/WhatsApp, CulturalContextService.Detect, MinistryAccommodationMapping / AccommodationProfile.IsEnabled)
- PARTIALLY-WIRED: 3 (HlrCalculator, ParentalControls, Accommodations bounded context overall)

---

## Observations

- **R-01 is not an anomaly — it's the canary**. AdaptiveScheduler was flagged as the scheduler-wiring gap; the same pattern repeats four more times (IrtCalibrationPipeline, ParentDigest, CulturalContextService.Detect, MinistryAccommodationMapping). In each case a well-tested domain class has no caller path from any HTTP endpoint / actor / worker. SYNTHESIS.md's claim that Group A is "already built" is literally true at the `ls src/actors/Cena.Actors/*` level but misleading at the runtime-graph level.
- **Compliance-critical orphan**: R-22 Accommodations. The endpoint accepts a parent-consent dimension set, persists it, returns 200, but the student session never consults the profile. For a Ministry-of-Education product that advertises Hatama-code support, this is a ship-blocker class defect, not a polish task — parents will have legal paper (consent signature stored) that their disabled child received an accommodation the platform never rendered.
- **The `/api/diagnostic/estimate` dead route**. `MapDiagnosticEndpoints` is defined but never registered on any app pipeline; `IIrtCalibrationPipeline` has no DI binding; `ItemBankHealthService` is never constructed. Three substrates held up by TASK-PRR-007 are all wired to a dead endpoint. Any integration test that purports to call this route must be mocking the HTTP surface or running against a stub.
- **Naming collision to watch**: two different types called `CulturalContextService` live in different namespaces (`Cena.Actors.Services` — the R-15 substrate, orphaned classifier; `Cena.Admin.Api` — the fully-wired dashboard service). A grep without namespace discipline yields misleading hit counts. Any future rollup of "what does the culture substrate do" has to keep the two straight.
- **ParentDigest's 5 files, 0 callers** is the largest fully-dead substrate. The feature is in a bounded context folder with namespaces, shipgate tests, template localisation, Twilio vendor adapter — every engineering signal says "ready" except the wiring. Either finish it this sprint or move it out of `Cena.Actors/` so it stops looking production-adjacent.
- **Static-helper bias is the common theme**. Every ORPHANED substrate in this audit is implemented as a static class (AdaptiveScheduler, HlrCalculator methods, ParentDigestAggregator/Renderer, MinistryAccommodationMapping) or a DI-able service that was never DI-registered. Static helpers compile, test, and invite "done" status without forcing the wiring question the DI container would surface at startup.
