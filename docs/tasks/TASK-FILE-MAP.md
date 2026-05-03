# Task → File Map

> All 89 tasks from the expert panel coverage index, with the files each task created or modified.  
> **Generated**: 2026-04-13 | **Status**: 285 done, 1 in-progress, 2 failed

---

## Critical Priority

### GD-001 — ADR: CAS engine sole source of truth ✅
- `docs/adr/0002-sympy-correctness-oracle.md`

### GD-002 — ADR: Misconception state is session-scoped ✅
- `docs/adr/0003-misconception-session-scope.md`

### GD-004 — Ship-gate CI scanner + banned-terms + PR template ✅
- `.github/PULL_REQUEST_TEMPLATE.md`
- `docs/engineering/shipgate.md`
- `scripts/shipgate/allowlist.json`
- `scripts/shipgate/scan.mjs`

### CAS-001 — 3-tier CAS Engine (MathNet + SymPy sidecar) ✅
- `src/actors/Cena.Actors/Cas/CasContracts.cs`
- `src/actors/Cena.Actors/Cas/CasRouterService.cs`
- `src/actors/Cena.Actors/Cas/MathNetVerifier.cs`
- `src/actors/Cena.Actors/Cas/SymPySidecarClient.cs`

### CAS-002 — Step verifier API + NATS integration ✅
- `src/actors/Cena.Actors/Cas/StepVerifierService.cs`

### PHOTO-003 — Content moderation pipeline for minors ✅
- `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs`

### BKT-PLUS-001 — BKT+ extensions (forgetting, prerequisites, assistance) ✅
- `src/actors/Cena.Actors/Services/BktPlusCalculator.cs`
- `src/actors/Cena.Actors/Services/SkillPrerequisiteGraph.cs`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-036.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-806.json`
- `src/actors/Cena.Actors/Services/Prerequisites/SkillPrerequisites-807.json`
- `src/actors/Cena.Actors.Tests/Services/BktPlusCalculatorTests.cs`

### CAS-LLM-001 — CAS-verify all math in LLM output ✅
- `src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs`

---

## CAS Engine & Step Solver

### CAS-003 — 500-pair conformance suite (SymPy ↔ MathNet) ✅
- `src/actors/Cena.Actors/Cas/CasConformanceSuite.cs`

### CAS-BIND-001 — QuestionCasBinding ✅
- `src/shared/Cena.Infrastructure/Documents/QuestionCasBinding.cs`
- `src/actors/Cena.Actors/Events/CasBindingEvents.cs`

### STEP-001 — StepSolverCard.vue + StepInput.vue ✅
- `src/student/full-version/src/components/session/StepSolverCard.vue`
- `src/student/full-version/src/components/session/StepInput.vue`

### STEP-002 — MathInput.vue (MathLive wrapper) ✅
- `src/student/full-version/src/components/session/MathInput.vue`

### STEP-003 — StepSolverQuestion schema + events ✅
- `src/shared/Cena.Infrastructure/Documents/StepSolverQuestionDocument.cs`
- `src/actors/Cena.Actors/Events/StepSolverEvents.cs`

### STEP-004 — Step generation tooling in admin ✅
- `src/api/Cena.Admin.Api/Figures/StepGenerationService.cs`

### STEP-005 — Seed 10 step-solver questions ✅
- `src/shared/Cena.Infrastructure/Seed/StepSolverSeedData.cs`

### SCAFFOLD-001 — Exploratory scaffolding level (productive failure) ✅
- `src/shared/Cena.Infrastructure/Documents/StepSolverQuestionDocument.cs` (added `Exploratory=3` to enum)

---

## IRT & Mastery

### IRT-001 — Rasch/2PL item calibration pipeline ✅
- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs`

### IRT-002 — Item bank health dashboard + quality gate ✅
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs`

### IRT-003 — Constrained CAT algorithm + exposure control 🔄 (in-progress, claude-1)
- `src/actors/Cena.Actors/Assessment/ConstrainedCatAlgorithm.cs`

### MASTERY-001 — Per-skill-per-track mastery with cross-track seepage ✅
- `src/actors/Cena.Actors/Services/SkillTrackMasteryService.cs`

### MASTERY-002 — Anonymous class-level mastery stats (k≥10) ✅
- `src/api/Cena.Admin.Api/ClassMasteryService.cs`

### MASTERY-MAP-001 — Mastery map progress visualization ✅
- `src/student/full-version/src/components/MasteryMap.vue`

### STEP-IRT-001 — InstituteId + TrackId in step verification events ✅
- `src/actors/Cena.Actors/Events/StepSolverEvents.cs`

---

## Figures & Diagrams

### FIGURE-001 — ADR: Figure rendering stack ✅
- `docs/adr/0004-figure-rendering-stack.md`

### FIGURE-002 — FigureSpec schema on QuestionDocument ✅
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs`
- `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs`
- `src/actors/Cena.Actors/Events/QuestionEvents.cs`

### FIGURE-003 — `<QuestionFigure>` Vue component ✅
- `src/student/full-version/src/components/QuestionFigure.vue`

### FIGURE-004 — Wire QuestionFigure into QuestionCard ✅
- `src/student/full-version/src/components/session/QuestionCard.vue`

### FIGURE-005 — PhysicsDiagramService (programmatic SVG) ✅
- `src/api/Cena.Admin.Api/Figures/PhysicsDiagramService.cs`
- `src/api/Cena.Admin.Api/Figures/SvgBuilder.cs`

### FIGURE-006 — Admin figure editor ✅
- `src/admin/full-version/src/components/FigureEditor.vue`

### FIGURE-007 — Quality gate rules for figures ✅
- `src/api/Cena.Admin.Api/Figures/FigureQualityGate.cs`

### FIGURE-008 — AI figure spec generator with retry ✅
- `src/api/Cena.Admin.Api/Figures/AiFigureGenerator.cs`

### FIG-RTL-001 — Script property on diagram text (bidi) ✅
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs` (added Script property)

### FIG-MOBILE-001 — Mini figure thumbnail on mobile ✅
- `src/student/full-version/src/components/session/FigureThumbnail.vue`

### FIG-VIS-001 — visibleAtLevel on PhysicsDiagramSpec ✅
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs` (added VisibleAtLevel)

### FIG-QUAL-001 — Figure info-level vs difficulty consistency ✅
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs`

### FBD-001 — Free-body diagram Construct mode ✅
- `src/shared/Cena.Infrastructure/Documents/FigureSpecTypes.cs` (DiagramMode enum)
- `src/student/full-version/src/components/session/FreeBodyDiagramConstruct.vue`

---

## Photo Ingestion & Camera

### PHOTO-001 — Student photo capture + Gemini Vision ✅
- `src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs`

### PHOTO-002 — Bagrut PDF ingestion pipeline (admin) ✅
- `src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs`

### PWA-BE-003 — Photo upload endpoint hardening ✅
- `src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs`

---

## Tenancy — Phase 1

### TENANCY-P1 — Multi-institute schema scaffold (umbrella) ✅
- All P1a–P1f below

### TENANCY-P1a — InstituteDocument + CurriculumTrackDocument + EnrollmentDocument ✅
- `src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs`
- `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs`
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs`
- `src/shared/Cena.Infrastructure.Tests/Documents/DocumentSchemaTests.cs`

### TENANCY-P1b — ClassroomDocument extension ✅
- `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs`

### TENANCY-P1c — EnrollmentEvents.cs (8 event types) ✅
- `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs`

### TENANCY-P1d — Platform seed (3 Bagrut tracks + BAGRUT-GENERAL) ✅
- `src/shared/Cena.Infrastructure/Seed/PlatformSeedData.cs`
- `src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs`

### TENANCY-P1e — Student stream upcaster ✅
- `src/actors/Cena.Actors/Configuration/EnrollmentBackfillService.cs`
- `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs`

### TENANCY-P1f — TenantScope.GetInstituteFilter ✅
- `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs`

---

## Tenancy — Phase 2

### VERIFY-0001 — Transfer-of-learning literature review ✅
- `docs/research/VERIFY-0001-transfer-of-learning.md`

### TENANCY-P2 — Cross-enrollment umbrella ✅
- All P2a–P2f below

### TENANCY-P2a — Mastery state re-key ❌ FAILED
- Blocked on ADR-0002 model decision

### TENANCY-P2b — PersonalMentorship classroom mode ✅
- `src/shared/Cena.Infrastructure/Documents/ClassroomJoinRequestDocument.cs`

### TENANCY-P2c — AssignmentDocument + events ✅
- `src/shared/Cena.Infrastructure/Documents/AssignmentDocument.cs`
- `src/actors/Cena.Actors/Events/AssignmentEvents.cs`

### TENANCY-P2d — MentorNoteDocument ✅
- `src/shared/Cena.Infrastructure/Documents/MentorNoteDocument.cs`

### TENANCY-P2e — Student onboarding V2 ✅
- `src/student/full-version/src/components/OnboardingCatalogPicker.vue`

### TENANCY-P2f — Enrollment switcher UI ✅
- `src/student/full-version/src/components/EnrollmentSwitcher.vue`

---

## Tenancy — Phase 3

### TENANCY-P3 — Mentor admin umbrella ✅

### TENANCY-P3a — Firebase custom claims per institute ✅
- `src/shared/Cena.Infrastructure/Auth/InstituteRoleClaims.cs`

### TENANCY-P3b — Mentor dashboard Vue pages ✅
- `src/admin/full-version/src/pages/mentor/index.vue`
- `src/admin/full-version/src/pages/mentor/institutes/[id]/index.vue`

### TENANCY-P3c — Instructor-scoped view ✅
- `src/admin/full-version/src/pages/instructor/index.vue`

### TENANCY-P3d — Chat capability (mentor-student SignalR) ✅
- `src/shared/Cena.Infrastructure/Documents/MentorChatDocuments.cs`
- `src/actors/Cena.Actors/Events/MentorChatEvents.cs`

### TENANCY-P3e — Platform program fork/reference workflow ✅
- `src/actors/Cena.Actors/Events/TenancyProgramEvents.cs`

### TENANCY-P3f — Invite link machinery (JWT + short code + QR) ✅
- `src/shared/Cena.Infrastructure/Auth/InviteLinkService.cs`

---

## Assessment Security

### SEC-ASSESS-001 — Per-student variant seeding ✅
- `src/actors/Cena.Actors/Assessment/VariantSeedService.cs`
- `src/actors/Cena.Actors/Events/AssessmentEvents.cs`

### SEC-ASSESS-002 — Exam simulation mode ✅
- `src/actors/Cena.Actors/Assessment/ExamSimulationMode.cs`
- `src/actors/Cena.Actors/Events/ExamSimulationEvents.cs`

### SEC-ASSESS-003 — Behavioral anomaly detection ✅
- `src/actors/Cena.Actors/Assessment/AnomalyDetection.cs`
- `src/actors/Cena.Actors/Events/AnomalyEvents.cs`

### SEC-ASSESS-004 — Exam-time upload detection + similarity checker ✅
- `src/actors/Cena.Actors/Assessment/ExamScheduleService.cs`
- `src/actors/Cena.Actors/Events/ExamScheduleEvents.cs`

### LATEX-001 — LaTeX sanitization (200-command allowlist) ✅
- `src/shared/Cena.Infrastructure/Security/LaTeXSanitizer.cs`

### RATE-001 — 4-tier rate limiting + cost circuit breaker ✅
- `src/actors/Cena.Actors/RateLimit/RedisCostBudgetService.cs`
- `src/actors/Cena.Actors/RateLimit/RedisCostCircuitBreaker.cs` (inferred)
- `src/actors/Cena.Actors/RateLimit/RateLimitDegradationMiddleware.cs`
- `src/actors/Cena.Actors.Tests/RateLimit/RedisCostBudgetServiceTests.cs`
- `src/actors/Cena.Actors.Tests/RateLimit/RedisCostCircuitBreakerTests.cs`
- `src/actors/Cena.Actors.Tests/RateLimit/RedisRateLimitServiceTests.cs`

---

## PWA & Backend Infrastructure

### PWA-BE-002 — Web Push notification backend ✅
- `src/actors/Cena.Actors/Notifications/PushNotificationRateLimiter.cs`
- `src/actors/Cena.Actors/Notifications/WebPushDispatchService.cs` (in kimi branch)
- `src/actors/Cena.Actors.Tests/Notifications/WebPushDispatchServiceTests.cs`
- `src/actors/Cena.Actors.Tests/Notifications/PushNotificationRateLimiterTests.cs`
- `src/actors/Cena.Actors.Tests/Notifications/NotificationsEndpointsTests.cs`

### PWA-BE-004 — Offline submission replay ✅
- `src/api/Cena.Student.Api.Host/Endpoints/OfflineReplayEndpoints.cs`

### EVENT-SCALE-001 — Event store scaling (snapshots, partitioning) ✅
- `src/shared/Cena.Infrastructure/EventStore/EventStoreScalingConfig.cs`

### OBS-001 — Three-layer observability ✅
- `src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs`

---

## Session UX & Pedagogy

### SESSION-UX-001 — Session start with topic choice ✅
- `docs/design/SESSION-UX-001-session-start.md`

### SESSION-UX-002 — Progressive disclosure + natural boundaries ✅
- `docs/design/SESSION-UX-002-progressive-disclosure.md`

### MISC-001 — Misconception catalog (15 entries) ✅
- `src/actors/Cena.Actors/Services/MisconceptionCatalog.cs`

### REMEDIATION-001 — Remediation micro-task templates ✅
- `src/actors/Cena.Actors/Services/RemediationTemplates.cs`

### READINESS-001 — Bagrut readiness report with CIs ✅
- `docs/design/READINESS-001-bagrut-readiness-report.md`

### DATA-READY-001 — ContentReadiness on CurriculumTrack ✅
- `src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs`
- `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`

---

## Accessibility & Localization

### A11Y-SRE-001 — SRE aria-labels for math in Arabic/Hebrew ✅
- `src/shared/Cena.Infrastructure/Accessibility/MathAriaLabels.cs`

### ARABIC-001 — Arabic math input normalizer ✅
- `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs`

### ARABIC-002 — Arabic parent install guide PDF ✅
- `docs/design/ARABIC-002-parent-install-guide.md`

### BAGRUT-ALIGN-001 — Bagrut structural alignment tags ✅
- `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs`
- `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`

---

## Game Design & Strategy

### GD-003 — Daily community puzzle (no streak) ✅
- `docs/design/daily-community-puzzle.md`

### GD-005 — Compliance artifacts umbrella (10 docs) ✅
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

### GD-006 — Spike: MathLive RTL parity ✅
- `docs/spikes/GD-006-mathlive-rtl-spike.md`

### GD-007 — PhET-style student interview protocol ✅
- `docs/protocols/GD-007-phet-interview-protocol.md`

### GD-008 — Arabic-first 5-unit physics wedge ✅
- `docs/market/GD-008-arabic-physics-wedge.md`

### GD-009 — Competitor study week ✅
- `docs/protocols/GD-009-competitor-hands-on-week.md`

### GD-010 — Memory update (ship-gate + SymPy oracle + misconception scope) ✅
- `CLAUDE.md`

---

## Bug Fixes

### BUG-test-001 — QuestionSelectorTests flake ✅
- `src/actors/Cena.Actors/Serving/QuestionBank.cs`
