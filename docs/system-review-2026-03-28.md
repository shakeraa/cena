# CENA PLATFORM -- FULL SYSTEM REVIEW REPORT

**Date**: 2026-03-28
**Reviewed by**: 10-agent specialist team (Claude Opus 4.6)
**Scope**: Full codebase at `/src/`
**Duration**: ~25 minutes parallel execution

---

## EXECUTIVE SUMMARY

Cena is an adaptive learning platform targeting Israeli Bagrut curriculum (Hebrew/Arabic/English) built on .NET 9 with Proto.Actor, Marten event sourcing, NATS messaging, Vue 3 admin dashboard, and Firebase Auth. The **domain intelligence layer is world-class** -- BKT, HLR, Elo scoring, Knowledge Space Theory, 9 pedagogical methodologies, and AI tutoring grounded in published research (D'Mello, Vygotsky, Bloom). However, **infrastructure and security are not production-ready**, with critical gaps in CI/CD, backup, NATS authentication, and FERPA compliance.

---

## REVIEW TEAM

| # | Agent | Role | Focus Area |
|---|-------|------|------------|
| 1 | Lead Architect | System Architect | Overall architecture, DDD, service boundaries |
| 2 | Solution Architect | Code Explorer | API design, NATS data flow, tenant scoping |
| 3 | Cyber Officer 1 | Security Auditor | Auth, OWASP Top 10, secrets, XSS, FERPA |
| 4 | Cyber Officer 2 | Security Architect | Docker, Terraform, NATS security, network |
| 5 | Pedagogy Professor 1 | Researcher | Question bank, assessment, learning progression |
| 6 | Pedagogy Professor 2 | Researcher | AI tutoring, LLM ACL, ethical AI, prompts |
| 7 | Backend Senior | Code Reviewer | .NET actors, EF Core, API patterns, tests |
| 8 | Frontend Senior | Code Reviewer | Vue 3, Vuexy, TypeScript, accessibility |
| 9 | DevOps Engineer | Code Reviewer | Docker, Terraform, CI/CD, observability |
| 10 | QA Lead | Tester | Test coverage, gaps, emulator, quality |

---

## CONSOLIDATED SCORECARD

| Domain | Score | Grade | Reviewer |
|--------|-------|-------|----------|
| **Architecture & DDD** | 4.0/5 | A- | Lead Architect |
| **Actor System** | 4.5/5 | A | Lead Architect |
| **API & Data Flow** | 3.5/5 | B+ | Solution Architect |
| **Application Security** | 51/100 | C- | Cyber Officer 1 |
| **Infrastructure Security** | 1.9/10 | F | Cyber Officer 2 |
| **Pedagogical Design** | 7.8/10 | B+ | Pedagogy Prof 1 |
| **AI-in-Education** | 4.0/5 | A- | Pedagogy Prof 2 |
| **Backend Code Quality** | Strong | B+ | Backend Senior |
| **Frontend Quality** | Good | B | Frontend Senior |
| **Test Coverage** | 938 tests | B- | QA Lead |
| **DevOps / Prod Readiness** | 1.5/5 | D | DevOps Engineer |

---

## TOP 20 FINDINGS BY PRIORITY

### CRITICAL (Fix Immediately)

| # | Finding | Source | Impact |
|---|---------|--------|--------|
| 1 | **Firebase service account key on disk** -- full GCP private key at `scripts/firebase/service-account-key.json`. If ever in git history, must rotate + purge. | DevOps | Credential compromise |
| 2 | **NATS has zero authentication** -- any network client can pub/sub student learning events. No TLS, no ACLs. | Cyber 2 | Data integrity, privacy |
| 3 | **No backup strategy** -- PostgreSQL event store (Marten) is the single source of truth. No pg_dump, no WAL archiving. Volume loss = unrecoverable. | Cyber 2 | Total data loss |
| 4 | **No CI/CD pipeline** -- zero GitHub Actions, no automated testing, scanning, or deployment gates. | DevOps, QA | Every merge is blind |
| 5 | **Production cluster uses TestProvider** -- Proto.Actor falls back to in-memory single-node even in prod. Multi-node is impossible. | Backend, Lead Arch | Deployment blocker |
| 6 | **Unauthenticated diagnostic endpoints** -- `/api/actors/stats` and `/api/actors/diag` expose student IDs, session data, and can spawn actors. No auth required. | Cyber 1 | Data leak, DoS vector |
| 7 | **JetStream subject mismatch** -- 9 configured streams subscribe to `cena.learner.events.>` etc., but publishers use `cena.events.*`. Streams capture nothing. | Solution Arch | Durability is broken |

### HIGH (Fix This Sprint)

| # | Finding | Source | Impact |
|---|---------|--------|--------|
| 8 | **Hardcoded DB credentials in 5 source files** -- `Password=cena_dev_password` + `Include Error Detail=true` compiled into binaries. | Cyber 1, Backend | Secret leak |
| 9 | **XSS via `v-html`** -- question stems rendered as raw HTML in 3+ Vue components. Stored XSS from malicious content authors. | Cyber 1 | Admin session hijack |
| 10 | **No HTTP security headers** -- no CSP, HSTS, X-Frame-Options on any API host. | Cyber 1 | XSS fully exploitable |
| 11 | **`async void` in actor** -- `PublishMethodologyAlert` in StudentActor.Methodology.cs. Unobserved exceptions can crash the process. | Backend | Process crash |
| 12 | **ConcurrentBag memory leak** -- `_recentErrors` in NatsBusRouter grows unbounded. | Backend | OOM over time |
| 13 | **EventStreamService returns mock data** -- admin dashboard shows fabricated event rates and DLQ data, not real system state. | Backend, Solution | Labels lie |
| 14 | **Firebase logout bug** -- `UserProfile.vue` clears cookies but never calls `signOut(firebaseAuth)`. Users auto-re-login on refresh. | Frontend | Auth bypass |
| 15 | **Divergent role abilities** -- `mapRoleToAbilities` duplicated in guards.ts and useFirebaseAuth.ts with different permissions. ADMIN loses Tutoring/Settings on page refresh. | Frontend | Broken RBAC |
| 16 | **No API rate limiting** -- no HTTP-level rate limiting on any endpoint, including AI generation (LLM cost amplification). | Cyber 1 | Cost attack, brute force |
| 17 | **Token revocation fails open** -- Redis outage lets revoked/banned users through. | Cyber 1, Backend | Security bypass |
| 18 | **FERPA compliance gaps** -- student PII processed without formal controls, audit trail, consent management, or data retention. | Cyber 2 | Regulatory exposure |

### MEDIUM (Fix Next Sprint)

| # | Finding | Source | Impact |
|---|---------|--------|--------|
| 19 | **Tenant isolation incomplete** -- only user management is school-scoped. SameOrg policy defined but never applied to any endpoint. | Solution Arch | Cross-tenant data leak |
| 20 | **Accessibility is D+** -- zero WCAG testing, no screen reader support for math content, no accommodations model. | Pedagogy 1, Frontend | Legal, inclusion |

---

## ARCHITECTURE HIGHLIGHTS (What's Working Well)

### Mastery Engine (Rated A+ by Pedagogy Professor 1)

The mastery engine is genuinely exceptional:

- **BKT** with hint-adjusted parameters (0 hints = 1.0x, 1 = 0.7x, 2 = 0.4x, 3+ = 0.1x learning rate)
- **HLR** (Half-Life Regression) for spaced repetition -- scientifically superior to SM-2
- **Elo scoring** for bidirectional item-student calibration
- **Knowledge Space Theory** diagnostic engine with Bayesian posterior over feasible states
- **9 pedagogical methodologies** with hierarchical resolution and confidence-gated switching (Wilson score intervals with cooldown)
- **Confusion detector** citing D'Mello & Graesser (2012, 2014) -- distinguishes productive vs. harmful confusion
- **Scaffolding service** implementing Vygotsky's ZPD with 4 fading levels (Full -> Partial -> HintsOnly -> None)
- **Learning frontier calculator** ranking concepts by information gain, review urgency, PSI, and interleaving bonus
- **Stagnation detector** with 5-signal composite (accuracy plateau, response time drift, session abandonment, error repetition, annotation sentiment)

### Event Sourcing & Actor Model (Rated 4.5/5 by Lead Architect)

- Stage-and-flush pattern with optimistic concurrency
- 38+ versioned events (_V1 suffix) across 7 categories
- Snapshot strategy at 100 events via Marten inline projection
- Transactional outbox with dead-letter tracking
- 500KB memory budget per actor with monitoring via `EstimateMemoryBytes()`
- Ghost timer prevention via `CancellationTokenSource`
- Circuit breaker pattern for LLM and Redis via Proto.Actor
- Proper supervision strategies (OneForOne with 3-restart/60s window)

### AI Integration (Rated 4.0/5 by Pedagogy Professor 2)

- **Multi-model routing**: Claude Sonnet 4.6 (primary), Haiku 4.5 (classifier), Gemini 2.5 Flash (OCR), OpenAI embeddings
- **Circuit breaker per model** via Proto.Actor (Closed/Open/HalfOpen)
- **4 methodology-specific tutoring prompts**: Socratic never gives answers, Worked Example shows steps, Feynman challenges articulation, Direct provides numbered steps
- **Methodology gate**: blocks tutoring for Drill&Practice and SpacedRepetition (correctly recognizes these don't benefit from conversation)
- **3-tier explanation system**: L1 (static) -> L2 (cached generic) -> L3 (personalized with full student context)
- **Quality gate** with 8 scoring dimensions, F1-validated against 100-item labeled dataset
- **PII never sent to LLM** -- documented in every prompt builder, hashed budget keys
- **Token budget**: 25,000 daily output tokens per student with L2 cache fallback
- **Research citations in code**: D'Mello & Graesser, Vygotsky ZPD, Bloom's 2-sigma, Corbett & Anderson BKT, Kapur productive failure

---

## DOMAIN-SPECIFIC REPORTS

### 1. Lead Architect -- Overall Architecture

**Maturity Ratings:**

| Area | Rating | Key Strength | Key Issue |
|------|--------|-------------|-----------|
| Overall Architecture | 4/5 | Strong DDD, event sourcing, clear bounded contexts | Duplicate service registration across hosts |
| Actor System | 4.5/5 | Production-grade lifecycle, rich domain, 60+ tests | Constructor overload (15 params), no prod cluster provider |
| API Layer | 3.5/5 | Minimal API, interface-first, quality gate pipeline | Flat file structure (50+ files in one dir), thin test coverage |
| Admin Dashboard | 3/5 | Firebase auth, 40+ pages, real-time potential | Massive template bloat, no domain composables |
| NATS Messaging | 4/5 | Clean subjects, envelope pattern, outbox | No request/reply impl, no dead-letter in routing |
| Shared Infrastructure | 3.5/5 | Centralized auth and seeding | No shared contracts, tight coupling |
| Student Emulator | 3.5/5 | 8 archetypes, comprehensive events | Single-file (408 lines), no round-trip validation |
| LLM ACL | 0/5 | N/A (directory empty) | No anti-corruption layer, code scattered in actors |

**Top 5 Architectural Priorities:**
1. Establish the LLM anti-corruption layer in `src/llm-acl/`
2. Eliminate duplicate service registration between hosts
3. Wire a production cluster provider
4. Prune admin dashboard of Vuexy template pages
5. Increase Admin API test coverage (currently 10:1 ratio vs actor tests)

---

### 2. Solution Architect -- API & Data Flow

**System Topology:**
```
Admin Dashboard (Vue 3, port 5174)
    |-- REST/HTTP -->
Admin API Host (.NET 9, port 5050)
    |-- Marten queries --> PostgreSQL (port 5433)
    |-- Redis cache --> Redis (port 6380)

Student Emulator (.NET Console)
    |-- NATS pub -->
NATS Server (port 4222, JetStream)
    |-- subscribe -->
Actor Host (.NET 9, port 5119, Proto.Actor)
    |-- Marten events --> PostgreSQL
    |-- Redis cache --> Redis
    |-- Neo4j (unused) --> Neo4j (port 7475)
```

**Endpoint Catalog:** 140+ REST endpoints across 20 endpoint groups covering user management, roles, dashboard, moderation, focus analytics, mastery tracking, system monitoring, ingestion, question bank, AI generation, pedagogy, cultural context, event stream, outreach, tutoring, explanations, experiments, embeddings, and token budget.

**Critical Findings:**
- **JetStream subject mismatch**: Streams subscribe to `cena.learner.events.>` but publishers use `cena.events.*` -- zero events captured
- **Dual NATS publishing**: NatsBusRouter (real-time) and NatsOutboxPublisher (transactional) both publish to overlapping subjects -- consumers get duplicates
- **Tenant isolation gap**: Only AdminUserService checks school_id. SameOrg policy exists but is applied to zero endpoints
- **No SignalR**: Referenced in comments but no implementation exists
- **Neo4j deployed but unused**: Knowledge graph served from in-memory seed data
- **HttpClient anti-pattern**: `new HttpClient()` inline in system monitoring endpoint

---

### 3. Cyber Officer 1 -- Application Security

**Security Scorecard:**

| Category | Score | Grade |
|----------|-------|-------|
| Authentication | 8/10 | B+ |
| Authorization | 7/10 | B |
| Input Validation | 3/10 | D |
| Secrets Management | 4/10 | D+ |
| CORS/Headers | 4/10 | D+ |
| Frontend Security | 5/10 | C |
| Dependency Health | 7/10 | B |
| Data Protection | 3/10 | D |
| Logging/Monitoring | 6/10 | C+ |
| Rate Limiting | 4/10 | D+ |
| **Overall** | **51/100** | **C-** |

**21 Findings (3 CRITICAL, 5 HIGH, 8 MEDIUM, 5 LOW):**

CRITICAL:
1. Hardcoded database credentials in 5 source files + committed appsettings
2. Unauthenticated diagnostic endpoints expose student IDs and can spawn actors
3. Token revocation fails open during Redis outage

HIGH:
4. No API rate limiting on any endpoint (including AI generation)
5. XSS via `v-html` in 3+ Vue components rendering question stems
6. No HTTP security headers (CSP, HSTS, X-Frame-Options)
7. Developer exception page + `Include Error Detail=true` can leak internals
8. Access token stored in JS-readable cookie (no httpOnly/secure flags)

---

### 4. Cyber Officer 2 -- Infrastructure Security

**Infrastructure Scorecard:**

| Domain | Score | Risk Rating |
|--------|-------|-------------|
| Docker Security | 3/10 | HIGH |
| Terraform IaC | 0/10 | CRITICAL |
| NATS Bus Security | 1/10 | CRITICAL |
| Network Architecture | 2/10 | HIGH |
| CI/CD Pipeline | 0/10 | CRITICAL |
| Logging & Monitoring | 4/10 | MEDIUM |
| Backup & Recovery | 0/10 | CRITICAL |
| Compliance (FERPA/GDPR) | 2/10 | CRITICAL |
| Application Security | 5/10 | MEDIUM |
| **Overall** | **1.9/10** | **CRITICAL** |

**Key Issues:**
- Docker: Containers run as root, no image digest pinning, `:latest` tags on DynamoDB/nats-box
- Terraform: Directory completely empty -- zero IaC
- NATS: No auth, no TLS, no ACLs. 9 JetStream streams carry sensitive learner/pedagogy/engagement data
- Network: All ports host-exposed, no segmentation, Redis unauthenticated
- CI/CD: No pipeline exists at all
- Backup: No pg_dump, no WAL archiving, no disaster recovery plan
- Compliance: Student PII (attempts, mastery, sessions, focus) processed without FERPA controls

---

### 5. Pedagogy Professor 1 -- Educational Design

**Pedagogical Scorecard:**

| Dimension | Rating | Score |
|-----------|--------|-------|
| Question Bank Design | A | 9.0/10 |
| Tutoring Session Model | A- | 8.5/10 |
| Assessment Framework | B+ | 7.5/10 |
| Learning Progression | A+ | 9.5/10 |
| Student Engagement | B | 7.0/10 |
| Feedback Mechanisms | A- | 8.5/10 |
| Accessibility | D+ | 3.5/10 |
| Bloom's Taxonomy Alignment | A- | 8.5/10 |
| **Overall** | **B+** | **7.8/10** |

**Strengths:**
- Event-sourced question bank with full lifecycle, AI generation, OCR ingestion, and quality gates
- Mastery-gated Bloom's progression (< 0.3 mastery = levels 1-2, >= 0.8 = levels 4-6)
- Scaffolding service with ZPD-based fading (Full/Partial/HintsOnly/None)
- HLR-based spaced repetition with prerequisite-aware review prioritization
- Tiered feedback (L1 static -> L2 cached -> L3 personalized with full student context)
- Confusion-aware delivery: suppresses L3 during productive confusion, upgrades scaffolding when stuck

**Critical Gaps:**
- Only MCQ supported -- no constructed-response, short-answer, or proof questions (blocks Bloom's 5-6)
- No WCAG compliance, no math accessibility (MathML/aria-math), no IEP accommodations
- SelfConfidence field exists but is never populated (missed metacognitive opportunity)
- No formal summative assessment mode (no time limits, no anti-cheating, no rubric scoring)
- No student-facing learning map or skill tree
- No peer/social learning features

---

### 6. Pedagogy Professor 2 -- AI-in-Education

**AI Maturity: 4.0/5 (Advanced)**

| Dimension | Score |
|-----------|-------|
| LLM Integration Architecture | 5/5 |
| Pedagogical AI Design | 5/5 |
| Content Generation Quality | 4/5 |
| Prompt Engineering | 4/5 |
| Ethical Safeguards | 3.5/5 |
| Assessment AI | 3/5 |
| Teacher AI Tools | 4/5 |
| Research Grounding | 5/5 |
| A/B Experimentation | 3/5 |

**Key Findings:**
- Multi-model architecture: Claude Sonnet (generation), Haiku (classification), Gemini (OCR), OpenAI (embeddings)
- 4 methodology-enforced tutoring modes with Socratic "NEVER give the answer" enforcement
- Mastery/Bloom's/fatigue/difficulty-calibrated prompts
- Error classification into 5 pedagogically meaningful categories
- Safety guard blocks answer leaking, prompt injection, inappropriate content
- PII protection: student IDs never sent to LLM, documented in every service

**Gaps:**
- No AI transparency disclosure to students (EU AI Act Article 52 concern)
- Cultural sensitivity scorer hardcoded to 80 (not implemented)
- A/B experiment metric persistence is TODO
- Off-topic detection uses brittle 15-phrase keyword list
- No chain-of-thought instruction in tutoring prompts

---

### 7. Backend Senior -- .NET Code Quality

**15 Findings (3 CRITICAL, 3 HIGH, 9 MEDIUM/LOW)**

CRITICAL:
- **C1**: Production cluster uses TestProvider (in-memory) -- deployment blocker
- **C2**: `async void PublishMethodologyAlert` -- unobserved exceptions can crash process
- **C3**: ConcurrentBag in NatsBusRouter grows unbounded -- OOM risk

HIGH:
- **I1**: Hardcoded PostgreSQL credentials in 5 files (violates CLAUDE.md)
- **I2**: Redis connection fallback logic is flawed (first connect not disposed, second may never connect)
- **I3**: EventStreamService returns static mock data on production endpoints

MEDIUM:
- Duplicate service registration between two hosts
- No input validation at API boundary
- NATS connection has no reconnection policy
- Diagnostic endpoint spawns non-idempotent actors without auth
- Fabricated physics mastery data (`Physics: MathF.Round(masteryPct * 0.7f, 1)`)
- ActivitySource disposed on stop, lost after restart (silent tracing loss)

**Strengths Observed:**
- Event sourcing with stage-and-flush, optimistic concurrency, deterministic replay
- Transactional outbox with dead-letter tracking
- Circuit breaker actors for LLM and Redis
- 2-second persistence timeouts to prevent mailbox starvation
- Actor memory budget monitoring (500KB per actor)
- OpenTelemetry traces and metrics on all critical paths

---

### 8. Frontend Senior -- Vue 3 Admin Dashboard

**Frontend Scorecard:**

| Area | Rating |
|------|--------|
| Component Architecture | A- |
| State Management | B |
| Routing | A |
| API Integration | A- |
| Vuexy Utilization | B- |
| TypeScript | B- |
| Layout Components | B |
| Form Handling | A- |
| Accessibility | D |
| Performance | C+ |

**Critical Issues:**
1. **Firebase logout bug** (Confidence: 95%) -- `UserProfile.vue` clears cookies but never calls `signOut(firebaseAuth)`. Users auto-re-login on refresh.
2. **Divergent role abilities** (Confidence: 90%) -- `mapRoleToAbilities` duplicated with different definitions. ADMIN loses Tutoring/Settings on page refresh.
3. **Footer shows Pixinvent/Vuexy branding** -- visible to all admin users.

**Important Issues:**
4. 61 occurrences of `: any` across 30 files
5. No debounce on search inputs (API call on every keystroke)
6. 60-70% of codebase is dead Vuexy template code (ecommerce, invoices, CRM, logistics, etc.)
7. `getCurrentInstance()!` used instead of `useAbility()` composable

---

### 9. DevOps Engineer -- Infrastructure & CI/CD

**Production Readiness: 1.5/5**

| Category | Score |
|----------|-------|
| Docker & Containers | 2.5/5 |
| IaC (Terraform) | 0/5 |
| Scripts & Automation | 3/5 |
| Service Configuration | 2/5 |
| Health Checks | 3/5 |
| Monitoring & Observability | 3/5 |
| CI/CD Pipeline | 0/5 |
| Scalability | 2/5 |
| Database Management | 2/5 |
| Security | 1.5/5 |

**Key Findings:**
- No Dockerfiles for .NET services (Actor Host, Admin API, Emulator)
- No Terraform -- zero IaC
- No CI/CD pipeline -- zero automation
- Firebase service account key on disk with full private key
- Prometheus scrapes wrong port (references 5000, service runs on 5050/5119)
- No OTLP collector -- traces export to black hole
- Admin API has zero observability (no OpenTelemetry, no metrics)
- `start.sh` hardcodes port 5000 but API listens on 5050

**Top 10 Actions:**
1. Rotate Firebase service account key immediately
2. Create Dockerfiles for all .NET services
3. Establish CI/CD pipeline
4. Implement secrets management
5. Create `appsettings.Production.json` with env-var injection
6. Wire DynamoDB cluster provider
7. Fix Prometheus scrape target
8. Add OTLP collector (Jaeger/Tempo)
9. Add proper health checks to Admin API
10. Establish database backup strategy

---

### 10. QA Lead -- Test Coverage

**Test Summary:**

| Metric | Value |
|--------|-------|
| Total tests | 938 (714 Actors + 224 Admin API) |
| Pass rate | 100% (0 failures) |
| Execution time | ~3 seconds |
| Backend test files | 66 |
| Frontend tests | Zero |
| E2E tests | Zero |
| CI pipeline | None |

**Test Type Matrix:**

| Type | Present? | Count | Quality |
|------|----------|-------|---------|
| Unit Tests | YES | ~900 | HIGH |
| Integration Tests | PARTIAL | ~14 | LOW (no real DB/Redis/NATS) |
| Contract Tests | YES | ~10 | GOOD |
| E2E Tests | NO | 0 | MISSING |
| Performance Tests | PARTIAL | ~3 | BKT zero-alloc only |
| Security Tests | PARTIAL | ~15 | Auth policies + moderation |
| Load Tests | MANUAL | Emulator only | No automation |
| Frontend Tests | NO | 0 | MISSING |
| API Endpoint Tests | NO | 0 | MISSING |

**Strongest Coverage:** Mastery engine (BKT, HLR, Elo, scaffolding, stagnation, diagnostics) -- 16 test files, ~200+ tests

**Critical Gaps (zero coverage):**
- StudentActor behavior (only state projection tested)
- TutorActor + TutorPromptBuilder + TutorSafetyGuard
- NatsBusRouter routing logic
- All 15+ Admin API endpoints (no WebApplicationFactory tests)
- Entire Vue frontend (zero component/E2E tests)
- Marten event store integration (all mocked)

**Quality Gate Testing (Standout):**
- 100 labeled test cases with F1 validation
- Computes precision, recall, and F1 per decision category
- Asserts weighted F1 >= 0.75
- ML-style evaluation harness, not just unit tests

---

## RECOMMENDED ACTION PLAN

### Phase 1: Security & Data Protection (Week 1-2)

1. Rotate Firebase service account key, purge from git history
2. Add NATS authentication + TLS
3. Add auth to `/api/actors/stats` and `/api/actors/diag`
4. Remove hardcoded credentials, require env-var injection
5. Implement PostgreSQL backup strategy (pg_dump or WAL archiving)
6. Add security headers middleware (CSP, HSTS, X-Frame-Options)
7. Sanitize v-html with DOMPurify

### Phase 2: Production Foundation (Week 3-4)

8. Create CI/CD pipeline (GitHub Actions: build, test, lint, scan)
9. Create Dockerfiles for .NET services (multi-stage builds)
10. Fix JetStream subject patterns to match publishers
11. Wire DynamoDB cluster provider (or K8s provider) for Proto.Actor
12. Fix Firebase logout bug + unify mapRoleToAbilities
13. Fix Prometheus scrape target + add OTLP collector
14. Replace mock data in EventStreamService + fabricated physics data

### Phase 3: Quality & Compliance (Week 5-8)

15. Add Admin API integration tests (WebApplicationFactory)
16. Add TutorActor + StudentActor behavior tests
17. Add frontend component tests (Vitest)
18. Remove dead Vuexy template pages (~100+ files)
19. Implement FERPA audit logging for student record access
20. Begin WCAG 2.1 AA accessibility remediation

### Phase 4: Enhancement (Week 9+)

21. Add constructed-response question types
22. Implement student-facing learning map/skill tree
23. Add AI transparency disclosure to students
24. Implement cultural sensitivity scorer in quality gate
25. Add rate limiting middleware
26. Establish Terraform IaC for production infrastructure
27. Implement the LLM anti-corruption layer in `src/llm-acl/`

---

## KEY FILES REFERENCED

### Architecture & Configuration
- `src/actors/Cena.Actors.Host/Program.cs` -- Actor Host bootstrap (most comprehensive single file)
- `src/api/Cena.Api.Host/Program.cs` -- Admin API host setup
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` -- Event store schema
- `docker-compose.yml` -- Infrastructure topology
- `config/docker-compose.observability.yml` -- Prometheus + Grafana
- `src/infra/docker/nats-setup.sh` -- JetStream stream setup

### Domain Core
- `src/actors/Cena.Actors/Students/StudentActor.cs` -- Aggregate root (5 partial files)
- `src/actors/Cena.Actors/Students/StudentState.cs` -- Event-sourced state
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` -- NATS-to-Actor bridge
- `src/actors/Cena.Actors/Bus/NatsSubjects.cs` -- Subject hierarchy
- `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs` -- Transactional outbox

### AI & Tutoring
- `src/actors/Cena.Actors/Tutoring/TutorActor.cs` -- Conversational tutoring
- `src/actors/Cena.Actors/Tutoring/TutorPromptBuilder.cs` -- Methodology-aware prompts
- `src/actors/Cena.Actors/Tutoring/TutorSafetyGuard.cs` -- Output safety
- `src/actors/Cena.Actors/Gateway/LlmClientRouter.cs` -- Multi-model routing
- `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` -- Circuit breaker
- `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` -- L3 explanations
- `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` -- Error categorization
- `src/api/Cena.Admin.Api/AiGenerationService.cs` -- AI question generation
- `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` -- 8-dimension quality gate

### Mastery & Pedagogy
- `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` -- ZPD scaffolding
- `src/actors/Cena.Actors/Mastery/DiagnosticEngine.cs` -- Knowledge Space Theory
- `src/actors/Cena.Actors/Mastery/BktParameters.cs` -- Bayesian Knowledge Tracing
- `src/actors/Cena.Actors/Methodology/MethodologyResolver.cs` -- 9-methodology resolution
- `src/actors/Cena.Actors/Services/ConfusionDetector.cs` -- D'Mello-grounded detection

### Security & Auth
- `src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs` -- RBAC policies
- `src/shared/Cena.Infrastructure/Auth/FirebaseAuthExtensions.cs` -- Firebase JWT
- `src/shared/Cena.Infrastructure/Auth/TokenRevocationMiddleware.cs` -- Redis revocation
- `src/shared/Cena.Infrastructure/Auth/CenaClaimsTransformer.cs` -- Claims extraction

### Frontend
- `src/admin/full-version/src/composables/useApi.ts` -- Reactive API client
- `src/admin/full-version/src/composables/useFirebaseAuth.ts` -- Firebase auth
- `src/admin/full-version/src/layouts/components/UserProfile.vue` -- Logout bug
- `src/admin/full-version/src/plugins/1.router/guards.ts` -- Route guards
