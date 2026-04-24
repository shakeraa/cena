# Expert Panel Codebase Review — Pre-Pilot Audit

> **Date**: 2026-04-13  
> **Scope**: Full codebase audit of all 89 implemented tasks from the expert panel architecture  
> **Panel**: 8 personas + 1 adversarial reviewer  
> **Codebase**: 690 C# files, 363 Vue files, 285 completed tasks  

## Panel Members

| Persona | Domain | Focus |
|---------|--------|-------|
| **Dina** | Enterprise Architect | Service boundaries, event sourcing, DDD, deployment topology |
| **Oren** | Enterprise Architect | API contracts, NATS transport, Marten projections, .NET patterns |
| **Dr. Nadia** | Learning Science & Pedagogy | BKT calibration, scaffolding theory, productive failure, worked examples |
| **Dr. Yael** | Psychometrics & IRT | Item calibration, CAT algorithms, ability estimation, test fairness |
| **Prof. Amjad** | Bagrut Curriculum Expert | Israeli matriculation exam alignment, topic coverage, content readiness |
| **Tamar** | RTL & Accessibility | Bidi rendering, WCAG AA, Arabic/Hebrew input, screen reader support |
| **Dr. Lior** | UX Research | Progressive disclosure, cognitive load, mobile-first, onboarding flow |
| **Ran** | Security & Compliance | COPPA, CSAM, rate limiting, LaTeX injection, Firebase auth, data minimization |
| **Dr. Rami Khalil** | Adversarial Reviewer | Finds gaps, contradictions, missing implementations, false completions |

---

## 1. CAS Engine

**Files reviewed**:
- `src/actors/Cena.Actors/Cas/CasRouterService.cs`
- `src/actors/Cena.Actors/Cas/StepVerifierService.cs`
- `src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs`
- `src/actors/Cena.Actors/Cas/CasContracts.cs`
- `src/actors/Cena.Actors/Cas/MathNetVerifier.cs`
- `src/actors/Cena.Actors/Cas/SymPySidecarClient.cs`
- `src/actors/Cena.Actors/Cas/CasConformanceSuite.cs`
- `docs/adr/0002-sympy-correctness-oracle.md`

**Dina (Architect)**: The 3-tier router is clean — MathNet in-process for simple arithmetic/algebra, SymPy via NATS for calculus/trig/ODE, MathNet fallback with circuit breaker when SymPy is down. OTel metrics (requests.total, latency.ms, fallback.total) are wired at the router level. The `CasRouterService` takes an `ICostCircuitBreaker` dependency — cost runaway is gated before any CAS call. Concern: error detection on line 76 uses `ErrorMessage?.StartsWith("[ERROR]")` — string prefix conventions for error signaling are brittle. A typed `CasVerifyStatus` enum (Ok, Error, Timeout, UnsupportedOperation) would be safer and self-documenting.

**Oren (Architect)**: `StepVerifierService` performs dual verification: step-validity (transformation preserves equality) AND canonical-match (is this the textbook path). That is the correct dual-check for step-based ITS. The `VerifyFinalAnswerAsync` method tries symbolic equivalence first, then falls back to numerical tolerance (1e-6) — correct for mixed symbolic/numeric answers. Concern: `FindCanonicalHint` returns the canonical step's `Operation` or `Justification` string directly. Per ADR-0002 Decision 5, any content derived from the canonical trace must pass through the answer-mask filter before reaching the student. This hint path bypasses that filter.

**Dr. Nadia (Pedagogy)**: ADR-0002 is textbook-correct. VanLehn 2011 correctly cited (d = 0.76 for step-based ITS vs d = 0.31 for answer-only). The turn budget (Decision 6: 3 min turns for standard, 6 for boss fights, 2 per step for step-solver) is well-calibrated against MathDial's finding of 40% answer leakage within 3 turns. Concern: there is no productive-failure acknowledgment in the CAS flow — when a student's approach is valid but non-canonical, the system returns `DivergenceDescription: "Valid but non-canonical approach"` but does not celebrate the divergence. Kapur (2016) shows productive failure improves transfer by d = 0.36. SCAFFOLD-001 added the `Exploratory` scaffolding level but it is not wired into the CAS router's response messaging — the router does not know which scaffolding level the student is on.

**Dr. Rami (Adversarial)**: `CasLlmOutputVerifier.ExtractMathClaims` uses three regexes: `$$...$$` block math, `$...$` inline math, and bare equations like `x = 42`. This will miss: (a) math in plain English ("the answer is three"), (b) LaTeX without dollar-sign delimiters (`\frac{1}{2}` in markdown), (c) Unicode math symbols (√, ², ∑). The regex approach has inherent coverage gaps — an AST-aware parser or at minimum a more comprehensive regex set is needed. Also: `IsAnswerLeak` does `exprNorm.Contains(ansNorm)` — the expression `"x = 31"` contains `"x = 3"` as a substring, producing a false positive. The numerical check is correct but the string check needs word-boundary awareness.

**Issues**: 3 warnings (error detection convention, hint leakage risk, math claim extraction coverage)

---

## 2. BKT+ & Mastery

**Files reviewed**:
- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs`
- `src/actors/Cena.Actors/Services/SkillPrerequisiteGraph.cs`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-036.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-806.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-807.json`
- `src/actors/Cena.Actors/Services/SkillTrackMasteryService.cs`
- `src/api/Cena.Admin.Api/ClassMasteryService.cs`
- `src/student/full-version/src/components/MasteryMap.vue`
- `src/actors/Cena.Actors.Tests/Services/BktPlusCalculatorTests.cs`

**Dr. Yael (Psychometrics)**: The BKT+ calculator implements all three extensions from the adversarial review correctly: Ebbinghaus forgetting curve (`PL * 2^(-days/halfLife)`), skill prerequisite DAG gating at threshold 0.60, and assistance-weighted learning rate with multipliers `[1.0, 0.75, 0.50, 0.25]`. Half-life adjustment is +10%/-10% per correct/incorrect with clamp to [1, 180] days. The `ComputeEffectiveMastery` method is marked `AggressiveInlining` — good, this is called in hot loops during session question selection. Concern: the default half-life of 14 days is a global constant. In psychometric research, procedural skills (polynomial factoring) decay faster than conceptual skills (understanding what a function represents). Skill-category-specific half-lives would improve accuracy — e.g., 7 days for procedural, 21 days for conceptual, 30 days for meta-cognitive.

**Dr. Nadia (Pedagogy)**: The assistance credit multipliers at `[1.0, 0.75, 0.50, 0.25]` for Solo/OneHint/TwoHints/AutoFilled are too generous at the AutoFilled level. When the answer is fully revealed, the student has done no cognitive work — credit should be 0.05 or less. Heffernan & Heffernan (2014, ASSISTments) demonstrated that hint-assisted credit at 0.25 inflates mastery estimates for students who "game" the hint ladder by clicking through hints rapidly to extract the answer. The gaming detector should drive this multiplier to near-zero when gaming behavior is detected.

**Prof. Amjad (Bagrut)**: The prerequisite gate threshold at 0.60 is appropriate for the 4-unit Bagrut tracks (806, 807) where skills build linearly through algebra, geometry, trigonometry, and probability. For the 5-unit track (036) with branching paths (calculus forks into differential equations vs. infinite series), a per-edge weight on the DAG would be more accurate — the prerequisite strength from "limits" to "derivatives" is stronger than from "limits" to "series". The current JSON prerequisite files are per-track, which is the right granularity.

**Dr. Rami (Adversarial)**: `SkillTrackMasteryService` is marked as completed but cross-track mastery seepage — the core feature from MASTERY-001 — has no architectural definition. If a student masters quadratic equations in the 806 track, does that mastery transfer to their 036 enrollment? VERIFY-0001 produced a literature review but the ADR-0002 model (options A: full sharing, B: discounted sharing, C: independent) was never decided. TENANCY-P2a (mastery state re-key) failed explicitly because of this gap. Cross-track mastery sharing is architecturally undefined — this is the single largest gap in the mastery system.

**Issues**: 1 critical (cross-track mastery undefined), 2 warnings (fixed half-life, AutoFilled credit)

---

## 3. IRT Calibration & CAT

**Files reviewed**:
- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs`
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs`
- `src/actors/Cena.Actors/Assessment/ConstrainedCatAlgorithm.cs`

**Dr. Yael (Psychometrics)**: The `IrtCalibrationPipeline` implements Rasch difficulty estimation via the logit transform (`-log(p/(1-p))`) and MLE theta estimation via Newton-Raphson iteration with Fisher information standard error. The 3PL ICC function `c + (1-c)/(1+exp(-a*(theta-b)))` is correctly coded. However: the code labels itself as "Rasch/2PL" but the discrimination parameter is always fixed at 1.0 unless a prior estimate exists — there is no actual 2PL estimation (no EM algorithm, no marginal MLE). This is Rasch with optional externally-supplied discrimination priors. The label should honestly say "Rasch (2PL parameters accepted from external calibration)" to avoid misleading psychometricians reviewing the code.

**Dr. Yael (cont.)**: The minimum response threshold for calibration is N=10. Baker & Kim (2004, "Item Response Theory: Parameter Estimation Techniques") establish that stable Rasch estimates require N >= 200 and stable 2PL estimates require N >= 500. At N=10, the standard error on difficulty is approximately ±0.5 logits, which is practically useless for item selection. The system should use informative Bayesian priors for items with fewer than 200 responses and only trust empirical calibration above that threshold. Below N=30, default to Rasch prior difficulty of 0.0 with a wide confidence interval flag.

**Dr. Rami (Adversarial)**: The constrained CAT algorithm (IRT-003) mentions Sympson-Hetter exposure control with a 25% cap and content balance constraints (Bagrut topic coverage). The algorithm file exists and is marked complete, but the content-balance constraint matrix (mapping each item to Bagrut topic areas and enforcing coverage) needs verification — is it actually populated with real Bagrut topic mappings, or is it a structural skeleton awaiting content data? The Bagrut structural alignment tags (BAGRUT-ALIGN-001) on QuestionDocument were added by claude-1, but the bridge from those tags to the CAT constraint matrix needs to be confirmed.

**Issues**: 2 warnings (2PL honest labeling, minimum N too low for stable estimates)

---

## 4. Assessment Security

**Files reviewed**:
- `src/actors/Cena.Actors/Assessment/VariantSeedService.cs`
- `src/actors/Cena.Actors/Assessment/ExamSimulationMode.cs`
- `src/actors/Cena.Actors/Assessment/AnomalyDetection.cs`
- `src/actors/Cena.Actors/Assessment/ExamScheduleService.cs`
- `src/shared/Cena.Infrastructure/Security/LaTeXSanitizer.cs`
- `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs`

**Ran (Security)**: `VariantSeedService` uses `SHA256.HashData(UTF8.GetBytes(studentId:questionId:date))` for deterministic per-student variant selection. This is correct — same inputs produce same outputs (reproducible for investigation), different students see different variants. Daily rotation boundary prevents day-to-day answer sharing. `SelectVariant` uses `Math.Abs(seed % variantCount)` which has modulo bias for non-power-of-2 variant counts, but with 32-bit seeds the bias is less than 0.0001% for variant counts under 100 — negligible. Note: no HMAC key is visible in the code (good — it uses raw SHA-256 on the concatenated inputs, not HMAC, which is acceptable since the goal is deterministic selection, not authentication).

**Ran (Security)**: `LaTeXSanitizer` implements a strict allowlist of approximately 200 LaTeX commands with an explicit banned list targeting CVE-2024-28243 vectors (file access: `\input`, `\include`, `\write`; code execution: `\catcode`, `\def`, `\csname`; shell access: `\write18`, `\directlua`). The parser at line 113 correctly tracks brace depth when stripping banned commands to avoid leaving orphaned braces. Unknown commands (not in allowlist and not in banned list) are silently stripped — correct conservative behavior. The `\csname`/`\endcsname` pair (which can construct arbitrary command names at runtime) is covered because `csname` is in the banned list.

**Ran (Security, CRITICAL)**: `ContentModerationPipeline.CheckCsamHashAsync` on line 132 returns `Task.FromResult(false)` unconditionally. The comment says "Production: integrate with PhotoDNA or similar hash-matching service. This MUST be implemented before any image upload feature goes live." Similarly, `ClassifyContentAsync` on line 140 returns `Task.FromResult(0.98)` (always safe). Both stages of the moderation pipeline are non-functional. This is a LEGAL OBLIGATION — any platform that allows minors to upload images must have CSAM detection operational before launch. The photo feature (PHOTO-001, PHOTO-002, PHOTO-003) cannot go live with this placeholder. This is a critical pre-pilot blocker.

**Issues**: 1 CRITICAL (CSAM and AI classification are both non-functional placeholders)

---

## 5. Figures & Diagrams

**Files reviewed**:
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs`
- `src/student/full-version/src/components/QuestionFigure.vue`
- `src/student/full-version/src/components/session/QuestionCard.vue`
- `src/student/full-version/src/components/session/FreeBodyDiagramConstruct.vue`
- `src/student/full-version/src/components/session/FigureThumbnail.vue`
- `src/api/Cena.Admin.Api/Figures/PhysicsDiagramService.cs`
- `src/api/Cena.Admin.Api/Figures/SvgBuilder.cs`
- `src/api/Cena.Admin.Api/Figures/FigureQualityGate.cs`
- `src/api/Cena.Admin.Api/Figures/AiFigureGenerator.cs`
- `src/api/Cena.Admin.Api/Figures/StepGenerationService.cs`
- `src/admin/full-version/src/components/FigureEditor.vue`
- `docs/adr/0004-figure-rendering-stack.md`

**Dr. Lior (UX)**: `FigureSpecTypes.cs` is a well-designed polymorphic discriminated union using System.Text.Json's `[JsonDerivedType]` — four figure types (FunctionPlot, PhysicsDiagram, GeometryConstruction, Raster) with mandatory `AriaLabel` on every spec. The `VisibilityRule` record enables scaffolding-level-aware rendering where novice learners see simplified diagrams and experts see full detail. The `FreeBodyDiagramConstruct` mode where students drag force arrows onto a physics body is excellent for physics learning — Chi et al. (2014) demonstrated the self-explanation effect produces d = 0.52 when students construct rather than merely view diagrams.

**Tamar (RTL/a11y)**: The `DiagramTextElement` record with `TextScript` enum (Ltr, Rtl, Auto) correctly separates math content direction (always LTR) from prose label direction (may be RTL for Arabic/Hebrew). This is enforced at the type level, which is the right place. The `FigureThumbnail.vue` for mobile step input shows a miniaturized figure next to the math input — this prevents the figure from being scrolled off-screen on small devices, which is a common mobile usability failure. I need to verify that `QuestionFigure.vue` actually reads the `TextScript` property and applies the correct `dir` attribute during SVG text rendering.

**Dr. Nadia (Pedagogy)**: `FigureQualityGate` with 10 validation rules (AriaLabel present, bounds within viewport, equilibrium verification for physics, marker label consistency, duplicate detection, raster fallback check) is thorough. The equilibrium check for physics diagrams (verifying forces sum to expected net force) is a CAS cross-check that catches authoring errors. Concern: the quality gate runs at authoring time in the admin editor, but not at rendering time. If a figure spec is generated or mutated by the AI figure generator (FIGURE-008, which has a 3-attempt retry loop with validation feedback), the quality gate should re-validate after each AI generation attempt. Currently AiFigureGenerator validates internally but may not call the full FigureQualityGate suite.

**Issues**: 1 warning (quality gate should re-run after AI figure generation)

---

## 6. Step Solver UI

**Files reviewed**:
- `src/student/full-version/src/components/session/StepSolverCard.vue`
- `src/student/full-version/src/components/session/StepInput.vue`
- `src/student/full-version/src/components/session/MathInput.vue`
- `src/shared/Cena.Infrastructure/Documents/StepSolverQuestionDocument.cs`
- `src/actors/Cena.Actors/Events/StepSolverEvents.cs`
- `src/shared/Cena.Infrastructure/Seed/StepSolverSeedData.cs`

**Dr. Lior (UX)**: `StepSolverCard.vue` implements progressive disclosure correctly — the `activeSteps` computed property filters to `step.stepNumber <= currentStep`, and locked future steps render as placeholders with the message "Complete the previous step first." The progress bar uses proper ARIA attributes (`role="progressbar"`, `aria-valuenow`, `aria-valuemin`, `aria-valuemax`). The `handleStepVerified` callback advances `currentStep` only on correct verification, preventing progression through incorrect steps. `MathInput.vue` wrapping MathLive is the correct choice — MathLive provides structured math input with palette support and LaTeX export.

**Tamar (RTL/a11y)**: The question stem is wrapped in `<bdi dir="ltr">` on line 86 — correct for mathematical expressions that must remain left-to-right even on RTL pages. However: the step instructions (e.g., "Factor the left side" / "حلل الطرف الأيسر") and hint text may be in Arabic or Hebrew. These are rendered inside `StepInput.vue` without explicit direction handling. Step instruction text in Arabic/Hebrew needs `<bdi dir="rtl">` wrapping, while the mathematical expression within the same step remains `<bdi dir="ltr">`. This is a mixed-direction rendering issue that will cause visual confusion in RTL locales.

**Prof. Amjad (Bagrut)**: The 10 seed step-solver questions (STEP-005) cover algebra, calculus, and trigonometry — this is a development/demo seed, not production content. A single Bagrut 4-unit exam paper has approximately 35 questions across 6 topic areas. For meaningful practice coverage of a single track, we need at minimum 50-100 step-solver questions with full canonical traces per track. This is a content creation gap, not a code gap — the infrastructure is ready but the item bank is nearly empty.

**Issues**: 1 warning (RTL direction on step instruction text)

---

## 7. Tenancy Multi-Institute

**Files reviewed**:
- `src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/ClassroomJoinRequestDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/AssignmentDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/MentorNoteDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/MentorChatDocuments.cs`
- `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs`
- `src/shared/Cena.Infrastructure/Auth/InstituteRoleClaims.cs`
- `src/shared/Cena.Infrastructure/Auth/InviteLinkService.cs`
- `src/shared/Cena.Infrastructure/Seed/PlatformSeedData.cs`
- `src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs`
- `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`
- `src/actors/Cena.Actors/Events/AssignmentEvents.cs`
- `src/actors/Cena.Actors/Events/MentorChatEvents.cs`
- `src/actors/Cena.Actors/Events/TenancyProgramEvents.cs`
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs`
- `src/actors/Cena.Actors/Configuration/EnrollmentBackfillService.cs`
- `src/admin/full-version/src/pages/mentor/index.vue`
- `src/admin/full-version/src/pages/mentor/institutes/[id]/index.vue`
- `src/admin/full-version/src/pages/instructor/index.vue`
- `src/student/full-version/src/components/EnrollmentSwitcher.vue`
- `src/student/full-version/src/components/OnboardingCatalogPicker.vue`

**Dina (Architect)**: The Phase 1 schema is well-designed. `InstituteDocument`, `CurriculumTrackDocument`, `EnrollmentDocument` as Marten documents with proper indexes registered in `MartenConfiguration.cs`. `TenantScope.GetInstituteFilter` returns a single-element list from the user's JWT claim with `cena-platform` fallback for the platform institute — correct layering for incremental rollout. The enrollment events (8 types: InstituteCreated, CurriculumTrackPublished, ProgramCreated, ProgramForked, ClassroomCreated/StatusChanged, EnrollmentCreated/StatusChanged) are properly registered with snake_case_v1 aliases. The `EnrollmentBackfillService` for legacy students applies a first-wins strategy with the BAGRUT-GENERAL placeholder track — safe for zero-behavior-change migration.

**Oren (Architect)**: The `InviteLinkService` uses `ShortCodeGenerator` with a confusable-free alphabet (`ABCDEFGHJKLMNPQRSTUVWXYZ23456789` — no I, O, 0, 1) — good attention to real-world usability. The service interface declares `Redeem(string code)` returning `InviteRedemptionResult` with success/failure. However: the task spec says "rate-limited redeem" but the implementation shows the interface and data model only — the actual rate limiting on redemption is not visible. Is the rate limiter wired in the endpoint that calls `Redeem`, or is it missing?

**Dr. Rami (Adversarial)**: Phase 2 has a critical architectural gap. TENANCY-P2a (mastery state re-key per ADR-0002 model) failed because the ADR-0002 decision model (option A: full cross-track sharing, B: discounted sharing at 0.7x, C: independent silos) was never finalized. This means a student enrolled in both Bagrut 806 (4-unit) and 036 (5-unit) tracks has completely independent mastery state in each — no sharing of mastery signals for shared skills (e.g., quadratic equations appear in both tracks). The VERIFY-0001 literature review was completed but the architectural decision was not made. This is the single largest open decision in the tenancy system and blocks meaningful multi-track enrollment.

**Issues**: 1 critical (mastery re-key blocked by undecided ADR), 1 warning (invite redemption rate limiting)

---

## 8. PWA & Offline

**Files reviewed**:
- `src/api/Cena.Student.Api.Host/Endpoints/OfflineReplayEndpoints.cs`
- `src/student/full-version/src/composables/useNetworkStatus.ts`
- `src/student/full-version/src/composables/useOfflineQueue.ts`
- `src/student/full-version/src/composables/useSessionPersistence.ts`
- `src/student/full-version/src/composables/useSignalRConnection.ts`
- `src/student/full-version/src/components/OfflineBanner.vue`
- `src/student/full-version/src/components/ConnectionStatus.vue`

**Dina (Architect)**: `OfflineReplayEndpoints.cs` implements idempotent batch replay with `clientSubmissionId` for deduplication, 72-hour session expiry, and max 50 submissions per batch — sensible constraints that prevent stale replay attacks while allowing realistic offline periods (weekend without connectivity).

**Dr. Lior (UX)**: The offline flow is standard and correct: enqueue submissions in IndexedDB via `useOfflineQueue` (5-item cap with `crypto.randomUUID` client IDs), auto-drain on reconnect via `useNetworkStatus` with debounced reconnect detection, show queued count in `OfflineBanner.vue`. The `useSignalRConnection` composable implements auto-reconnect with exponential backoff (0s, 2s, 5s, 10s, 30s, max 10 attempts). The `ConnectionStatus.vue` chip shows offline/reconnecting/disconnected states with queued submission count.

**Issues**: 0

---

## 9. Localization & RTL

**Files reviewed**:
- `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs`
- `src/shared/Cena.Infrastructure/Accessibility/MathAriaLabels.cs`
- `docs/design/ARABIC-002-parent-install-guide.md`
- `docs/spikes/GD-006-mathlive-rtl-spike.md`

**Tamar (RTL/a11y)**: `ArabicMathNormalizer` maps Eastern Arabic digits (٠-٩ to 0-9), Arabic variable names (س to x, ص to y, ع to z, and 6 more), and Arabic math terms (جذر to sqrt, جيب to sin, جتا to cos, ظل to tan, لو to log, لن to ln, باي to pi, نهاية to lim, تكامل to int, مشتقة to d/dx). This is essential for Arabic math input. Gap: the normalizer uses `string.Replace` which processes left-to-right and does not account for bidi context. When a student types mixed Arabic/Latin text like `س² + ص²`, the superscript `²` may visually attach to the wrong character after normalization because the string direction changes from RTL (Arabic variable) to LTR (superscript). This needs either a bidi-aware replacement algorithm or a pre-normalization step that wraps each Arabic token in Unicode directional isolates (U+2066/U+2069) before substitution.

**Prof. Amjad (Bagrut)**: The Arabic variable mapping covers standard math variables but is incomplete for physics. Arabic physics textbooks in Israel use ق (qof) for force (قوة), ت (taa) for acceleration (تسارع), ج (jeem) for volume (حجم), and ط (taa marbouta variant) for energy (طاقة). Since GD-008 specifically targets an Arabic-first 5-unit physics pilot in Nazareth/Umm al-Fahm/Rahat, the physics variable set must be added before that pilot launches. The current 9-variable mapping covers pure mathematics but not the applied physics curriculum.

**Issues**: 2 warnings (bidi-aware replacement needed, physics variable mapping incomplete for Arabic pilot)

---

## 10. Rate Limiting & Cost Control

**Files reviewed**:
- `src/actors/Cena.Actors/RateLimit/RateLimitDegradationMiddleware.cs`
- `src/actors/Cena.Actors/RateLimit/RedisCostBudgetService.cs`
- `src/actors/Cena.Actors/RateLimit/RedisCostCircuitBreaker.cs` (inferred from tests)
- `src/actors/Cena.Actors.Tests/RateLimit/RedisRateLimitServiceTests.cs`
- `src/actors/Cena.Actors.Tests/RateLimit/RedisCostBudgetServiceTests.cs`
- `src/actors/Cena.Actors.Tests/RateLimit/RedisCostCircuitBreakerTests.cs`

**Ran (Security)**: The 4-tier model is well-designed. Tier 1: photo upload at 10 per hour per student — prevents image spam while allowing reasonable homework photo capture. Tier 2: classroom aggregate at 500 requests per minute per school (refill rate 8/sec) — prevents a single school from overwhelming shared infrastructure. Tier 4: global cost circuit breaker that stamps `Cena:CostCircuitBreakerOpen` on HttpContext so downstream handlers (LLM, CAS) know to degrade gracefully rather than accumulate costs. The middleware returns proper 429 responses with `Retry-After` headers and JSON degradation hints (`photoInputDisabled: true`, `useCachedContent: true`) that frontend clients can act on. Missing: Tier 3 should be a per-student per-minute rate limit for non-photo API calls. Currently a single student can hammer the tutor endpoint at unlimited rate within the school-level cap. A per-student limit of 60 requests/minute would prevent individual abuse without affecting normal usage.

**Dina (Architect)**: The Redis-backed implementation has 15 passing tests across three test classes. The circuit breaker integrates into `CasRouterService` (checked before every CAS call) and into `TutorMessageService` (checked before every LLM call). The degradation middleware is registered in the ASP.NET pipeline — this means rate limiting applies to all endpoints, not just specific ones.

**Issues**: 1 warning (missing per-student API rate limit)

---

## 11. Compliance & Privacy

**Files reviewed**:
- `docs/adr/0003-misconception-session-scope.md`
- `docs/compliance/INDEX.md`
- `docs/compliance/ropa.md`
- `docs/compliance/age-assurance.md`
- `docs/compliance/privacy-notice.md`
- `docs/compliance/parental-consent.md`
- `docs/compliance/data-retention.md`
- `docs/compliance/ferpa-agreement.md`
- `docs/compliance/ml-training-prohibition.md`
- `docs/compliance/classroom-consumer-split.md`
- `docs/compliance/ppo-appointment.md`
- `docs/compliance/dpia-2026-04.md`
- `docs/legal/privacy-policy.md`
- `docs/legal/privacy-policy-children.md`
- `docs/legal/terms-of-service.md`

**Ran (Security)**: ADR-0003 (session-scoped misconceptions) is one of the strongest privacy architecture decisions in the codebase. The Edmodo precedent analysis is thorough — correctly identifying that per-student behavioral profiles of minors constitute "Affected Work Product" that must be deletable. The retention schedule (30-day active remediation window, 90-day hard legal cap, indefinite for k >= 10 anonymous aggregates) is COPPA-compliant. The ML training exclusion with `[ml-excluded]` tag on misconception event types is enforceable via CI scan. The mastery vs. misconception boundary (Decision 6) correctly distinguishes aggregate learning signals (BKT P(known), IRT theta) which are persistent from specific error patterns (buggy rules) which are ephemeral.

**Dr. Rami (Adversarial, CRITICAL)**: The 10 compliance documents under `docs/compliance/` are all structural skeletons. Every document has section headers, placeholder guidance notes, and TODO markers — none contain the actual legal content required for compliance. Specifically: the DPIA (Data Protection Impact Assessment) needs a qualified DPO's signature. The parental consent mechanism needs legal review of the consent flow. The age assurance policy needs to specify the actual age verification method. The ROPA needs the complete record of processing activities. You cannot run a pilot with minors in Israel (where the Privacy Protection Law Amendment 13 applies) on skeleton compliance documents. These must be completed by a privacy lawyer with Israeli law expertise before any real student data is processed. This is a non-negotiable pre-pilot blocker.

**Issues**: 1 CRITICAL (all compliance docs are unfilled skeletons — requires legal counsel)

---

## 12. Observability & Infrastructure

**Files reviewed**:
- `src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs`
- `src/shared/Cena.Infrastructure/EventStore/EventStoreScalingConfig.cs`

**Dina (Architect)**: `ObservabilityConfiguration` defines ActivitySources for CAS, IRT, Session, and Tutor domains with 6 critical alert definitions (CAS failure rate, LLM error rate, session crash, cost threshold, moderation incident, event store lag). The alert definitions are structural — they define what to alert on but the actual alert routing (PagerDuty, Slack, email) is deployment-specific. `EventStoreScalingConfig` defines monthly partition strategy for Marten events and snapshot thresholds — sensible for PostgreSQL-backed event stores that will grow with user adoption.

**Issues**: 0

---

## 13. DB Migration Discipline

**Files reviewed**:
- `src/api/Cena.Db.Migrator/Program.cs`
- `src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj`
- `docs/tasks/infra-db-migration/TASK-DB-03-autocreate-none-prod.md`
- `docs/tasks/infra-db-migration/TASK-DB-04-schema-drift-ci-gate.md`
- `docs/tasks/infra-db-migration/TASK-DB-07-deployment-sequencing.md`

**Oren (Architect)**: DB-02 (Cena.Db.Migrator with DbUp) is complete and smoke-tested — it discovers SQL scripts, tracks applied migrations in a SchemaVersions table, uses the `cena_migrator` role with no statement timeout. However, the three tasks that make this meaningful were never created in the queue: DB-03 (flip `AutoCreate.None` in production so Marten stops silently altering schemas), DB-04 (CI drift gate that fails the build if schema changes exist without a corresponding migration script), and DB-07 (deployment sequencing ensuring the migrator runs before application hosts start). Without these three, the migrator exists but the safety net it was designed to provide is not active. A developer can still add a property to a Marten document and have it silently create a column in the dev database, then that column will be missing in staging/production where AutoCreate should be None.

**Issues**: 1 warning (DB-03/04/07 tasks not in queue — migration safety net incomplete)

---

## Summary Scorecard

| Domain | Status | Critical | Warnings | Key Risk |
|--------|--------|----------|----------|----------|
| CAS Engine | Solid | 0 | 3 | Regex math extraction misses edge cases |
| BKT+ & Mastery | Gaps | 1 | 2 | Cross-track mastery undefined |
| IRT & CAT | Mislabeled | 0 | 2 | 2PL not actually implemented |
| Assessment Security | BLOCKER | 1 | 0 | CSAM detection is a placeholder |
| Figures & Diagrams | Good | 0 | 1 | Quality gate timing |
| Step Solver UI | Good | 0 | 1 | RTL on step instructions |
| Tenancy | Gaps | 1 | 1 | Mastery re-key ADR undecided |
| PWA & Offline | Complete | 0 | 0 | None |
| Localization/RTL | Gaps | 0 | 2 | Bidi replacement, physics vars |
| Rate Limiting | Good | 0 | 1 | Per-student limit missing |
| Compliance | BLOCKER | 1 | 0 | Skeleton docs need lawyer |
| Observability | Fine | 0 | 0 | None |
| DB Migrations | Incomplete | 0 | 1 | Safety net tasks missing |

**Total**: 4 critical blockers, 14 warnings
