# Cena Production Readiness — Expert Panel Audit

> **Date**: 2026-04-13
> **Method**: 8 parallel codebase audits, one per persona, followed by cross-persona refinement
> **Scope**: Full `/src` tree + `/docs` + `/tasks` — finding gaps, not celebrating wins
> **Adversarial reviewer**: Dr. Rami Khalil stress-tested every finding

## Panel Summary

| Persona | Domain | Current Readiness | Pilot Target | Production Target |
|---------|--------|------------------:|-------------:|------------------:|
| Dina | Backend Architecture | 50% | 70% | 95% |
| Oren | API & Integration | 65% | 75% | 95% |
| Dr. Nadia | Learning Science | 60% | 75% | 92% |
| Dr. Yael | Psychometrics & IRT | 40% | 55% | 90% |
| Prof. Amjad | Curriculum & Content | 30% | 55% | 90% |
| Tamar | RTL & Accessibility | 45% | 65% | 95% |
| Dr. Lior | UX Research | 55% | 70% | 90% |
| Ran | Security & Compliance | 55% | 75% | 97% |
| **Weighted Average** | | **50%** | **68%** | **93%** |

## Tier 0 — Ship-Blockers (3 items, blocks ALL deployment)

These three findings were unanimously flagged by the panel. No pilot, no demo, no deployment until resolved.

### SB-1: CSAM Detection is a Stub
**Raised**: Ran | **Agreed**: All 8 personas

`ContentModerationPipeline.CheckCsamHashAsync()` returns `false` unconditionally. Image upload endpoints exist. A platform serving minors cannot operate with placeholder child safety.

**Rami's challenge**: *"The comment in code says 'MUST be implemented before any image upload goes live.' Image upload endpoints ARE live. Who approved this contradiction?"*

**Panel resolution**: Disable image upload endpoints behind a feature flag until PhotoDNA/Cloud Vision integration is complete. Content moderation must fail-closed (block when AI service unavailable). Cost circuit breaker comment says "failing closed" but code fails open — fix the bug (1 line).

→ Task: [RDY-001](../../tasks/readiness/RDY-001-csam-moderation-failclosed.md)

### SB-2: RTL Hardcoded to False
**Raised**: Tamar | **Agreed**: Amjad, Lior, Ran

`isAppRTL = ref(false)` at `@layouts/stores/config.ts:94`. The LanguageSwitcher sets `document.dir` correctly, but the Vuetify layout store overrides it. Arabic/Hebrew students see LTR layout.

**Rami's challenge**: *"The binding was intentionally commented out. Why? Was it a Vuetify bug workaround? The fix might be one line or might expose a cascade of layout breaks."*

**Panel resolution**: Uncomment the binding, run full visual regression on Arabic locale. If Vuetify layout breaks, fix individual components rather than disabling RTL globally. Tamar flagged 10 ARIA/a11y items that become testable only once RTL is enabled.

→ Task: [RDY-002](../../tasks/readiness/RDY-002-rtl-enable-regression.md)

### SB-3: Empty Prerequisites + No Arabic Content
**Raised**: Amjad | **Agreed**: Nadia, Yael, Lior

All `Prerequisites[]` arrays on 1,000 questions are empty. BKT+ prerequisite gating (0.60 threshold) never fires. Students can attempt advanced topics without foundational knowledge. Additionally, zero Arabic translations exist despite Arabic being the primary user language.

**Nadia's refinement**: *"This isn't just a content gap — it invalidates the entire adaptive learning claim. Scaffolding, difficulty progression, and CAT content balancing all assume prerequisite ordering exists."*

**Yael's addition**: *"And without prerequisites, the CAT algorithm's topological ordering is impossible. It's doing random selection with a difficulty filter."*

**Panel resolution**: Split into two tasks — (a) populate prerequisite graph from curriculum knowledge, (b) begin Arabic translations with top-200 questions prioritized by topic coverage breadth.

→ Tasks: [RDY-003](../../tasks/readiness/RDY-003-prerequisite-graph.md), [RDY-004](../../tasks/readiness/RDY-004-arabic-translations.md)

---

## Tier 1 — Critical (Legal & Architecture, blocks launch)

### C-1: Content Moderation + Cost Budgets Fail Open
**Raised**: Ran, Dina

`ClassifyContentAsync()` returns 0.98 (safe) unconditionally — fails open when AI service is unavailable. `RedisCostCircuitBreaker` line 74 comment says "failing closed (allowing requests)" but actually fails open. Redis outage = unlimited LLM spend.

→ Included in [RDY-001](../../tasks/readiness/RDY-001-csam-moderation-failclosed.md)

### C-2: DPA with Anthropic Missing
**Raised**: Ran

GDPR Art. 28 requires a Data Processing Agreement with any third-party processor. Anthropic processes student tutor messages. No DPA exists.

→ Task: [RDY-005](../../tasks/readiness/RDY-005-legal-compliance-docs.md)

### C-3: Privacy Notice / Code Retention Mismatch
**Raised**: Ran

Children's privacy notice promises "30 days" for data deletion. Code retains tutor messages for 90 days. Legal inconsistency.

→ Included in [RDY-005](../../tasks/readiness/RDY-005-legal-compliance-docs.md)

### C-4: ML Training Exclusion Not Implemented
**Raised**: Ran

ADR-0003 mandates `[ml-excluded]` tag on misconception events. Tag doesn't exist in code. No test enforces exclusion.

→ Task: [RDY-006](../../tasks/readiness/RDY-006-ml-exclusion-tag.md)

### C-5: IRT Honest Labeling
**Raised**: Yael

Code labels itself "Rasch/2PL" but discrimination is always 1.0. N=10 minimum is dangerously low.

**Nadia's refinement**: *"If IRT parameters are unreliable, BKT mastery model builds on sand. This cascades to wrong scaffolding levels."*

→ Existing task [PP-011](../pre-pilot/TASK-PP-011-irt-honest-labeling.md) covers this. Promoted to Tier 1 priority.

### C-6: No DIF Analysis (Hebrew vs. Arabic)
**Raised**: Yael | **Agreed**: Amjad, Nadia, Ran

Zero DIF implementation despite bilingual population. Health service has `trackId` parameter that does nothing.

**Amjad's refinement**: *"Arabic-speaking students face terminology barriers. Items easy in Hebrew may be hard in Arabic due to language, not math. Without DIF, we can't distinguish."*

→ Task: [RDY-007](../../tasks/readiness/RDY-007-dif-analysis-pipeline.md)

### C-7: God Aggregate (65+ Events in StudentActor)
**Raised**: Dina | **Agreed**: Oren, Nadia

StudentActor handles 65+ event types across learner, sessions, pedagogy, engagement, outreach, focus, challenges — all in one stream. Projection complexity, contention at scale, impossible to reason about state transitions.

**Oren's refinement**: *"This directly causes projection idempotence risk. Splitting would let each projection subscribe to a narrow stream."*

**Rami's pushback**: *"This is a multi-sprint refactor. For pilot, contain it — don't refactor. For production, it's mandatory."*

**Panel resolution**: Document the decomposition plan (StudentProfile, LearningSession, StudentMetrics) as an ADR. Defer execution to post-pilot, but lock the target architecture now.

→ Task: [RDY-008](../../tasks/readiness/RDY-008-aggregate-decomposition-adr.md)

---

## Tier 2 — High (Blocks Quality, not Launch)

### H-1: No OpenAPI/Swagger
**Raised**: Oren

Neither API host has auto-generated API docs. Frontend developers and integrators have no discoverable contract.

→ Task: [RDY-009](../../tasks/readiness/RDY-009-openapi-swagger.md)

### H-2: REST API Versioning Not Implemented
**Raised**: Oren

Spec says `/api/v1/`, reality is `/api/`. Mobile app updates will break when API changes.

→ Task: [RDY-010](../../tasks/readiness/RDY-010-api-versioning.md)

### H-3: Health Probes Don't Check Dependencies
**Raised**: Dina | **Agreed**: Oren, Ran

Student/Admin API `/health/ready` returns OK without checking PostgreSQL, Redis, or NATS.

→ Task: [RDY-011](../../tasks/readiness/RDY-011-health-probes.md)

### H-4: HTTP Clients Lack Circuit Breakers
**Raised**: Dina

`EmbeddingService`, `GeminiOcrClient`, `MathpixClient` — registered as HTTP clients but no Polly policies, no retries, no fallback.

→ Task: [RDY-012](../../tasks/readiness/RDY-012-http-circuit-breakers.md)

### H-5: Worked Examples Not Rendered
**Raised**: Nadia, Lior

Backend scaffolding sets `ShowWorkedExample=true`. QuestionCard.vue references `props.question.workedExample` but never renders it. Core scaffolding technique fully designed but invisible to students.

**Tamar's addition**: *"When rendered, each step needs aria-label. Fading must use opacity, not display:none, to stay in accessibility tree."*

→ Task: [RDY-013](../../tasks/readiness/RDY-013-worked-examples-ui.md)

### H-6: Misconception Detection Not Wired
**Raised**: Nadia

Catalog of 15 buggy rules exists (Koedinger et al.). Remediation templates exist. But wrong answers don't trigger detection. The pipeline is: catalog → (gap) → remediation.

→ Task: [RDY-014](../../tasks/readiness/RDY-014-misconception-detection-pipeline.md)

### H-7: ARIA Live Regions Missing
**Raised**: Tamar

No `aria-live` on dynamic content: hint reveals, answer feedback, notifications, session progress. Screen reader users miss all state changes.

→ Task: [RDY-015](../../tasks/readiness/RDY-015-aria-live-regions.md)

### H-8: Touch Targets Below WCAG Minimum
**Raised**: Tamar

Vuetify buttons default to 38px. WCAG 2.2 AA requires 44x44px minimum.

→ Included in [RDY-015](../../tasks/readiness/RDY-015-aria-live-regions.md) (a11y sweep)

### H-9: Celebration & Flow State UX Missing
**Raised**: Lior

Zero celebration animations. Flow state tokens exist (warming/approaching/inFlow/disrupted/fatigued) with FlowAmbientBackground component, but no API drives the transitions.

→ Task: [RDY-016](../../tasks/readiness/RDY-016-celebration-flow-state.md)

### H-10: No NATS Dead-Letter Queue Stream
**Raised**: Dina

Failed events after 10 retries are logged but not persisted. No replay capability for lost events.

→ Task: [RDY-017](../../tasks/readiness/RDY-017-nats-dlq-tls.md)

### H-11: Sympson-Hetter Exposure Control is a Stub
**Raised**: Yael

Basic exposure tracking exists but full algorithm missing — no cumulative tracking, no probability thresholding, no per-item targets.

→ Task: [RDY-018](../../tasks/readiness/RDY-018-sympson-hetter-exposure.md)

---

## Tier 3 — Medium (Polish, Calibration, DX)

### M-1: Content Desert (No Real Bagrut Items)
**Raised**: Amjad

1,000 generated questions. Zero real Bagrut exam items. 640 pages of Ministry exams unscraped.

→ Task: [RDY-019](../../tasks/readiness/RDY-019-bagrut-corpus-ingestion.md)

### M-2: Topic Taxonomy Not Formalized
**Raised**: Amjad

Concepts are implicit string IDs. No formal hierarchy matching Ministry syllabus.

→ Included in [RDY-019](../../tasks/readiness/RDY-019-bagrut-corpus-ingestion.md)

### M-3: SignalR Event Push-Back Bridge
**Raised**: Oren

Hub sends commands to NATS but unclear how events route back to connected clients.

→ Task: [RDY-020](../../tasks/readiness/RDY-020-signalr-event-bridge.md)

### M-4: Projection Idempotence Tests
**Raised**: Oren

Mutable `Apply()` methods could double-count. No tests verify duplicate-event safety.

→ Task: [RDY-021](../../tasks/readiness/RDY-021-projection-idempotence.md)

### M-5: Session Timer & Fatigue UI
**Raised**: Lior, Nadia

No visible timer. No "How are you feeling?" prompt. Backend CognitiveLoadService exists, no frontend.

→ Task: [RDY-022](../../tasks/readiness/RDY-022-session-timer-fatigue.md)

### M-6: Diagnostic Quiz in Onboarding
**Raised**: Lior, Nadia

Onboarding collects role/language but doesn't calibrate starting mastery.

→ Task: [RDY-023](../../tasks/readiness/RDY-023-diagnostic-onboarding.md)

### M-7: BKT Parameter Calibration
**Raised**: Nadia

All BKT parameters are defaults. No per-subject calibration. Requires pilot data.

→ Task: [RDY-024](../../tasks/readiness/RDY-024-bkt-calibration.md)

### M-8: Skip Link + Focus Management
**Raised**: Tamar

No skip link, no focus restoration after modal close, heading hierarchy unvalidated.

→ Included in [RDY-015](../../tasks/readiness/RDY-015-aria-live-regions.md)

### M-9: Deployment Manifests
**Raised**: Dina

No Kubernetes specs, Docker image layering, replica config.

→ Task: [RDY-025](../../tasks/readiness/RDY-025-deployment-manifests.md)

---

## Cross-Persona Agreements (Refined)

### Agreement 1: "Designed but not wired" is the systemic pattern
All 8 personas independently found the same anti-pattern: backend algorithms complete, frontend rendering missing; schemas defined, data empty; circuit breaker patterns exist, not applied to all clients. **The platform's ceiling is high but its floor is low.**

### Agreement 2: Content is the bottleneck, not code
Yael, Amjad, and Nadia agree: IRT calibration, prerequisite graphs, Arabic translations, and topic taxonomy are all blocked on CONTENT, not CODE. The infrastructure is ready. The data isn't.

### Agreement 3: Accessibility is a legal requirement, not optional
Tamar and Ran agree: Israeli law requires accessible interfaces for educational platforms. RTL disabled + no skip link + 38px targets + missing ARIA = legal exposure before launch.

### Agreement 4: Security ship-blockers are fixable fast
Ran and Dina agree: CSAM stub → real integration is 1 week. Fail-open → fail-closed is 1 day. Cost circuit breaker is 1 line. The security gaps are implementation gaps, not design gaps.

### Agreement 5: God aggregate is a ticking bomb but not a pilot blocker
Dina, Oren, and Rami agree: StudentActor's 65+ events is unsustainable at production scale but acceptable for a 1-2 school pilot. Document the decomposition target now, execute post-pilot.

---

## Task Index

| ID | Title | Tier | Effort | Personas |
|----|-------|------|--------|----------|
| RDY-001 | CSAM + Moderation Fail-Closed | 0 | 1 week | Ran, Dina |
| RDY-002 | Enable RTL + Visual Regression | 0 | 2-3 days | Tamar, Amjad, Lior |
| RDY-003 | Populate Prerequisite Graph | 0 | 1-2 weeks | Amjad, Nadia, Yael |
| RDY-004 | Arabic Translations (Top 200) | 1 | 2-3 weeks | Amjad |
| RDY-005 | Legal Compliance Docs (DPA, Privacy, COPPA) | 1 | 4-8 weeks | Ran (legal) |
| RDY-006 | ML Exclusion Tag (ADR-0003) | 1 | 2 days | Ran |
| RDY-007 | DIF Analysis Pipeline | 1 | 2-3 weeks | Yael, Amjad |
| RDY-008 | Aggregate Decomposition ADR | 1 | 3 days (ADR) | Dina, Oren |
| RDY-009 | OpenAPI/Swagger | 2 | 2-3 days | Oren |
| RDY-010 | REST API Versioning | 2 | 3-5 days | Oren |
| RDY-011 | Health Probes Check Dependencies | 2 | 2 days | Dina |
| RDY-012 | HTTP Client Circuit Breakers | 2 | 1 week | Dina |
| RDY-013 | Worked Examples UI | 2 | 3-5 days | Nadia, Lior, Tamar |
| RDY-014 | Misconception Detection Pipeline | 2 | 1-2 weeks | Nadia |
| RDY-015 | A11y Sweep (ARIA, Skip Link, Touch, Focus) | 2 | 1 week | Tamar |
| RDY-016 | Celebration + Flow State UX | 2 | 1 week | Lior |
| RDY-017 | NATS DLQ Stream + TLS | 2 | 1 week | Dina |
| RDY-018 | Sympson-Hetter Full Implementation | 2 | 1-2 weeks | Yael |
| RDY-019 | Bagrut Corpus Ingestion + Taxonomy | 3 | 3-4 weeks | Amjad |
| RDY-020 | SignalR Event Push-Back Bridge | 3 | 3-5 days | Oren |
| RDY-021 | Projection Idempotence Tests | 3 | 2-3 days | Oren, Dina |
| RDY-022 | Session Timer + Fatigue UI | 3 | 3 days | Lior, Nadia |
| RDY-023 | Diagnostic Quiz in Onboarding | 3 | 1 week | Nadia, Lior |
| RDY-024 | BKT Parameter Calibration | 3 | 2-3 weeks | Nadia |
| RDY-025 | Deployment Manifests (K8s/Docker) | 3 | 1-2 weeks | Dina |

---

---

## Cross-Persona Review Round 2 (Refined)

> Five dedicated cross-review agents re-examined all 25 tasks through each persona's lens. Results below.

### Nadia (Pedagogy) — Cross-Review Findings

1. **Scaffolding code bug**: `ScaffoldingService.cs` sets `ShowWorkedExample=false` at Partial level (~line 55), but Partial is exactly where faded worked examples (Renkl & Atkinson) should appear. Should be `true`. Fixed in RDY-013 scope.
2. **RDY-023 conceptual misalignment**: Diagnostic quiz should estimate IRT theta first, then derive BKT P_Initial via theta-to-mastery mapping. Original spec skipped IRT entirely. Fixed.
3. **RDY-024 split**: Phase A (data infrastructure) is pre-pilot; Phase B (calibration) requires pilot data. Now documented as 2-phase task.
4. **7 undocumented dependencies** identified between tasks → new task RDY-031.

### Amjad (Curriculum) — Cross-Review Findings

1. **RDY-004 promoted to Tier 0**: Arabic is 80% of target users. Without Arabic content, the platform is a Hebrew demo, not a product.
2. **Effort upgraded**: 2-3 weeks → 4-6 weeks. Per-question translation takes 22-32 minutes including terminology consistency, gender agreement, and bidi rendering checks.
3. **3 missing tasks created**: RDY-026 (Arabic input normalization — students type س for x), RDY-027 (glossary curation — prerequisite to translations), RDY-028 (Bagrut calibration baseline — anchor IRT to real exam data).
4. **RDY-019 legal risk**: Scraping Ministry Bagrut exams may have copyright implications. Task should document legal basis.

### Ran (Security) — Cross-Review Findings

1. **RDY-001**: Feature flag alone is insufficient security boundary. Recommend conditional compilation to strip upload endpoints from binary.
2. **RDY-005 expanded**: Original scope missed 7 documents — SDPA for schools, teacher consent, breach notification template, subprocessor disclosure, AI transparency notice, privacy notice correction (claims "12 months" but code retains 90 days).
3. **RDY-006 hardened**: Attribute + CI test is necessary but not sufficient. Added runtime egress enforcement to prevent misconception data leaking via analytics, metrics, or DLQ replay.
4. **12 completely missing security tasks** → bundled as RDY-029 (SBOM, security headers, CSP, secrets rotation, audit logging, vendor assessment, encryption at rest, data rights portal, rate limiting, incident runbook, compliance monitoring, pen test plan).

### Tamar (A11y & RTL) — Cross-Review Findings

1. **RDY-002 expanded**: Added 5 Vuetify-specific RTL acceptance criteria (VNavigationDrawer, directional icons, form inputs, breadcrumbs, CSS logical properties).
2. **RDY-013 enhanced**: Added screen reader step progression (`aria-live`, `aria-current`, `aria-disabled`).
3. **RDY-015 split**: Sprint 1 (3 days) = ship-blocking compliance. Sprint 2 (1 week) = quality polish.
4. **RDY-016 hardened**: Added vestibular disorder considerations (animation duration cap, parallax ban, static celebration fallback).
5. **RDY-022 enhanced**: Timer needs `role="timer"` with `aria-live="off"`.
6. **RDY-023 had zero a11y criteria** — added keyboard navigation, ARIA progress bar, timer behavior.
7. **New task RDY-030**: A11y test automation (axe-core + custom rules) to prevent regressions after sweep.

### Cross-Review Consensus

| Decision | Agreed By | Dissent |
|----------|-----------|--------|
| RDY-004 is Tier 0 | Amjad, Nadia, Tamar, Lior | None |
| RDY-027 (glossary) prerequisite to RDY-004 | Amjad, Nadia | None |
| RDY-015 split into 2 sprints | Tamar, Ran | None |
| RDY-023 should init IRT theta | Nadia, Yael | None |
| 12 security tasks bundled as 1 | Ran, Dina | None |
| A11y automation needed post-sweep | Tamar, Lior | None |

---

## Updated Task Index

| ID | Title | Tier | Effort | Personas |
|----|-------|------|--------|----------|
| RDY-001 | CSAM + Moderation Fail-Closed | 0 | 1 week | Ran, Dina |
| RDY-002 | Enable RTL + Visual Regression | 0 | 2-3 days | Tamar, Amjad, Lior |
| RDY-003 | Populate Prerequisite Graph | 0 | 1-2 weeks | Amjad, Nadia, Yael |
| RDY-004 | Arabic Translations (Top 200) | 0 | 4-6 weeks | Amjad |
| RDY-005 | Legal Compliance Docs (expanded) | 1 | 4-8 weeks | Ran (legal) |
| RDY-006 | ML Exclusion Tag + Runtime Enforcement | 1 | 3 days | Ran |
| RDY-007 | DIF Analysis Pipeline | 1 | 2-3 weeks | Yael, Amjad |
| RDY-008 | Aggregate Decomposition ADR | 1 | 3 days (ADR) | Dina, Oren |
| RDY-026 | Arabic Variable Input Normalization | 1 | 3-5 days | Amjad |
| RDY-027 | Math/Physics Glossary Validation | 1 | 2-3 weeks | Amjad |
| RDY-009 | OpenAPI/Swagger | 2 | 2-3 days | Oren |
| RDY-010 | REST API Versioning | 2 | 3-5 days | Oren |
| RDY-011 | Health Probes Check Dependencies | 2 | 2 days | Dina |
| RDY-012 | HTTP Client Circuit Breakers | 2 | 1 week | Dina |
| RDY-013 | Worked Examples UI | 2 | 3-5 days | Nadia, Lior, Tamar |
| RDY-014 | Misconception Detection Pipeline | 2 | 1-2 weeks | Nadia |
| RDY-015 | A11y Sweep (2 sprints) | 2 | 3d + 1w | Tamar |
| RDY-016 | Celebration + Flow State UX | 2 | 1 week | Lior |
| RDY-017 | NATS DLQ Stream + TLS | 2 | 1 week | Dina |
| RDY-018 | Sympson-Hetter Full Implementation | 2 | 1-2 weeks | Yael |
| RDY-028 | Bagrut Calibration Baseline | 2 | 2-3 weeks | Amjad, Yael |
| RDY-029 | Security Hardening Bundle (12 sub-tasks) | 2 | 3-4 weeks | Ran, Dina |
| RDY-030 | A11y Test Automation | 2 | 3-5 days | Tamar |
| RDY-019 | Bagrut Corpus Ingestion + Taxonomy | 3 | 3-4 weeks | Amjad |
| RDY-020 | SignalR Event Push-Back Bridge | 3 | 3-5 days | Oren |
| RDY-021 | Projection Idempotence Tests | 3 | 2-3 days | Oren, Dina |
| RDY-022 | Session Timer + Fatigue UI | 3 | 3 days | Lior, Nadia |
| RDY-023 | Diagnostic Quiz (IRT theta init) | 3 | 1 week | Nadia, Lior |
| RDY-024 | BKT Parameter Calibration (2 phases) | 3 | 2-3 weeks | Nadia |
| RDY-025 | Deployment Manifests (K8s/Docker) | 3 | 1-2 weeks | Dina |
| RDY-031 | Task Dependency Graph | 3 | 1 day | Nadia |

---

## Rami's Closing

> *"Fix CSAM, enable RTL, populate prerequisites. That's your pilot. Everything else is quality — real quality, not fake quality — but those three are the floor. Without them you have a demo, not a product."*

## Post-Cross-Review Addendum

> *Amjad's amendment: "Add Arabic to the floor. Without Arabic content, you have a Hebrew demo for an Arabic audience. That's not a pilot — that's a misunderstanding of who your users are."*

---

## Rami's Adversarial Review (Final)

> Dr. Rami Khalil verified all 25 tasks against the actual codebase. His operating principle: *"If it can't be verified, it doesn't exist. Show me the test, the log line, the signed document — not the TODO comment."*

### Verification Results

**Code claims verified**: All 3 Tier 0 ship-blockers confirmed in code:
- `CheckCsamHashAsync()` returns `false` unconditionally (line 137, ContentModerationPipeline.cs) ✓
- `isAppRTL = ref(false)` hardcoded (line 94, config.ts) ✓
- All `Prerequisites[]` arrays empty in seed data ✓
- `ClassifyContentAsync()` returns 0.98 unconditionally (line 147) ✓
- `RedisCostCircuitBreaker` fails open despite "failing closed" comment ✓

### Effort Sandbagging

**20 of 25 tasks underestimate effort.** Rami's revised estimates add ~60% to overall timeline:

| Tier | Panel | Rami | Delta |
|------|-------|------|-------|
| 0 | ~7w | ~11w | +57% |
| 1 | ~10w | ~18w | +80% |
| 2 | ~10w | ~14w | +40% |
| 3 | ~8w | ~12w | +50% |

Root causes: PhotoDNA registration lag, translator sourcing, legal counsel process, Vuetify bug risk, algorithm complexity (Sympson-Hetter, error pattern matching), K8s scope.

### Duplicate Tasks

- **RDY-001 ↔ PP-001**: Nearly identical scope. PP-001 has more detail (NCMEC CyberTipline).
- **RDY-005 ↔ PP-002**: RDY-005 is comprehensive version. Retire PP-002.

### Missing Tasks (3 added)

| ID | Title | Tier | Rationale |
|----|-------|------|-----------|
| RDY-032 | Pilot Data Export Pipeline | 0 | Prerequisite for DIF, BKT calibration, Bagrut baseline |
| RDY-033 | Error Pattern Matching Infrastructure | 1 | Prerequisite for RDY-014 misconception detection |
| RDY-034 | Flow State Backend API | 2 | Prerequisite for RDY-016 flow state UX |

### Cross-Task Contradictions

1. **RTL vs math LTR**: RDY-002 enables RTL globally but doesn't specify how to keep math LTR. Must add `<bdi dir="ltr">` to all math components.
2. **Health checks vs HPA**: RDY-011 returns 503 when PostgreSQL is down, but RDY-025 HPA could scale to zero when all pods fail health checks. Need rolling update strategy.
3. **Feature flag bypass**: RDY-001 uses feature flag for image uploads, but feature flags aren't security boundaries. Consider conditional compilation.

### Rami's Honest Timeline

> *"Earliest realistic pilot: 10-12 weeks from now, not 6-8. Spend THIS WEEK de-risking: contact PhotoDNA, Anthropic Legal, and confirm Amjad's calendar. If any of these slip, re-plan immediately."*

### Final Task Count: 34 tasks (was 25)

| Stage | Tasks | New |
|-------|-------|-----|
| Tier 0 | 5 | +RDY-032 |
| Tier 1 | 7 | +RDY-033 |
| Tier 2 | 14 | +RDY-034 |
| Tier 3 | 8 | — |
