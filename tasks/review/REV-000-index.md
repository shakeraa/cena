# System Review Tasks -- Index

**Generated from:** Full System Review 2026-03-28 (10-agent specialist team)
**Report:** `docs/system-review-2026-03-28.md`

---

## Task Summary

| ID | Title | Priority | Effort | Status |
|----|-------|----------|--------|--------|
| REV-001 | Rotate Firebase Service Account Key & Purge Git History | P0 EMERGENCY | 2 hours | TODO |
| REV-002 | Add NATS Authentication, TLS, and Subject ACLs | P0 BLOCKER | 3 days | TODO |
| REV-003 | PostgreSQL Backup Strategy for Marten Event Store | P0 CRITICAL | 1 day | TODO |
| REV-004 | Secure Diagnostic Endpoints & Add Security Headers | P0 CRITICAL | 4 hours | TODO |
| REV-005 | Wire Production Cluster Provider for Proto.Actor | P0 BLOCKER | 2 days | TODO |
| REV-006 | Sanitize v-html Content & Fix Frontend Auth Bugs | P1 HIGH | 1 day | TODO |
| REV-007 | Fix JetStream Subject Mismatch & Dual Publishing | P1 HIGH | 1 day | TODO |
| REV-008 | Fix Backend Critical Code Issues (async void, memory leak, mock data) | P1 HIGH | 1 day | TODO |
| REV-009 | Establish CI/CD Pipeline (GitHub Actions) | P1 HIGH | 2 days | TODO |
| REV-010 | Remove Hardcoded Credentials & Establish Config Pattern | P1 HIGH | 4 hours | TODO |
| REV-011 | API Rate Limiting, Input Validation & File Upload Hardening | P1 HIGH | 2 days | TODO |
| REV-012 | Fix Token Revocation Fail-Open & Add Redis Authentication | P1 HIGH | 4 hours | TODO |
| REV-013 | FERPA Compliance Framework (Audit Logging, Data Retention) | P1 HIGH | 5 days | TODO |
| REV-014 | Complete Tenant Isolation (Apply SameOrg Policy) | P2 MEDIUM | 3 days | TODO |
| REV-015 | WCAG 2.1 AA Accessibility Baseline for Admin Dashboard | P2 MEDIUM | 5 days | TODO |
| REV-016 | Admin API Restructure (Feature Folders, Error Handling, Dedup Registration) | P2 MEDIUM | 2 days | TODO |
| REV-017 | Frontend Cleanup (Dead Vuexy Code, TypeScript any, Debounce) | P2 MEDIUM | 2 days | TODO |
| REV-018 | Fix Observability Stack (Prometheus, OTLP, Admin API Metrics) | P2 MEDIUM | 1 day | TODO |
| REV-019 | Close Critical Test Coverage Gaps (Actor, API, Frontend, NATS) | P2 MEDIUM | 5 days | TODO |
| REV-020 | Establish LLM Anti-Corruption Layer in src/llm-acl/ | P2 MEDIUM | 3 days | TODO |

**Total estimated effort: ~40 days**

---

## Finding Coverage Matrix

Every finding from all 10 review agents mapped to a task:

### Top 20 Findings (from consolidated report)

| # | Finding | Task |
|---|---------|------|
| 1 | Firebase service account key on disk | REV-001 |
| 2 | NATS zero authentication | REV-002 |
| 3 | No backup strategy | REV-003 |
| 4 | No CI/CD pipeline | REV-009 |
| 5 | Production cluster uses TestProvider | REV-005 |
| 6 | Unauthenticated diagnostic endpoints | REV-004 |
| 7 | JetStream subject mismatch | REV-007 |
| 8 | Hardcoded DB credentials | REV-010 |
| 9 | XSS via v-html | REV-006 |
| 10 | No HTTP security headers | REV-004 |
| 11 | async void in actor | REV-008 |
| 12 | ConcurrentBag memory leak | REV-008 |
| 13 | EventStreamService mock data | REV-008 |
| 14 | Firebase logout bug | REV-006 |
| 15 | Divergent role abilities | REV-006 |
| 16 | No API rate limiting | REV-011 |
| 17 | Token revocation fails open | REV-012 |
| 18 | FERPA compliance gaps | REV-013 |
| 19 | Tenant isolation incomplete | REV-014 |
| 20 | Accessibility D+ | REV-015 |

### Lead Architect Findings

| Finding | Task |
|---------|------|
| Duplicate service registration across hosts | REV-016 |
| 15-param constructor on StudentActor | Deferred (refactor, lower priority) |
| Empty src/llm-acl/ (no anti-corruption layer) | REV-020 |
| Admin API flat file structure (50+ files) | REV-016 |
| No shared contracts between actor/API | REV-016 (partial -- shared registration) |
| Admin Dashboard template bloat | REV-017 |
| No domain composables | REV-017 (partial -- cleanup enables this) |
| Emulator single-file architecture | Deferred (functional, lower priority) |
| No request/reply in NATS | Deferred (architecture enhancement) |

### Solution Architect Findings

| Finding | Task |
|---------|------|
| JetStream subject mismatch | REV-007 |
| Dual NATS publishing (overlapping events) | REV-007 |
| Tenant isolation gap (SameOrg unused) | REV-014 |
| No SignalR despite documentation claims | Deferred (feature, not a bug) |
| Neo4j deployed but unused | REV-018 (documentation note; removal is optional) |
| HttpClient anti-pattern (new HttpClient inline) | REV-016 (global error handler covers HTTP client factory) |
| NATS no reconnection policy | REV-002 (included in NatsOpts config) |

### Cyber Officer 1 Findings (21 total)

| Finding | Severity | Task |
|---------|----------|------|
| Hardcoded DB credentials | CRITICAL | REV-010 |
| Unauthenticated diagnostic endpoints | CRITICAL | REV-004 |
| Token revocation fails open | HIGH | REV-012 |
| No API rate limiting | HIGH | REV-011 |
| XSS via v-html | HIGH | REV-006 |
| No security headers | HIGH | REV-004 |
| Developer exception page leaks | HIGH | REV-016 (global error handler) |
| Access token in JS-readable cookie | MEDIUM | REV-006 (partial -- logout fix reduces exposure) |
| File upload no limits/validation | MEDIUM | REV-011 |
| No input validation framework | MEDIUM | REV-011 |
| CORS allows all headers/methods | MEDIUM | REV-012 |
| NATS bus no authentication | MEDIUM | REV-002 |
| AllowedHosts: "*" | MEDIUM | REV-013 |
| AllowAnonymous on admin health | MEDIUM | REV-004 |
| Seeding endpoints in production | MEDIUM | REV-011 |
| No audit logging for admin ops | LOW | REV-013 |
| Firebase key path in config | LOW | REV-001 |
| No FERPA controls | LOW | REV-013 |
| TestProvider in non-dev | LOW | REV-005 |
| No encryption at rest | LOW | Deferred (production infra -- INF-002/003) |
| Antiforgery disabled on upload | LOW | Documented as acceptable (JWT auth) |

### Cyber Officer 2 Findings

| Finding | Task |
|---------|------|
| Docker containers run as root | Deferred (INF-006 ECS handles this) |
| No image digest pinning | Deferred (INF-006/007) |
| NATS no auth/TLS | REV-002 |
| NATS monitoring port exposed | REV-002 |
| Redis no authentication | REV-012 |
| All ports host-exposed | Deferred (production networking -- INF-001) |
| No Docker network segmentation | Deferred (INF-001) |
| No CI/CD | REV-009 |
| No Terraform | Deferred (INF-001 through INF-010) |
| No backup strategy | REV-003 |
| No disaster recovery | REV-003 (partial -- dev backup; prod DR is INF-002) |
| FERPA PII without controls | REV-013 |
| GDPR gaps | REV-013 (partial) |
| Data retention partially configured | REV-013 |
| Token revocation fail-open | REV-012 |
| Hardcoded credentials in source | REV-010 |
| Include Error Detail=true | REV-010 |
| Firebase key path in config | REV-001 |
| No security headers | REV-004 |
| Grafana default password | REV-018 |

### Backend Senior Findings (15 total)

| Finding | Severity | Task |
|---------|----------|------|
| Production cluster uses TestProvider | CRITICAL | REV-005 |
| async void PublishMethodologyAlert | CRITICAL | REV-008 |
| ConcurrentBag memory leak | CRITICAL | REV-008 |
| Hardcoded DB credentials | HIGH | REV-010 |
| Redis connection fallback flaw | HIGH | REV-008 |
| EventStreamService mock data | HIGH | REV-008 |
| Duplicate service registration | MEDIUM | REV-016 |
| No input validation | MEDIUM | REV-011 |
| NATS no reconnection policy | MEDIUM | REV-002 |
| Diagnostic endpoint spawns actors | MEDIUM | REV-004 |
| Fabricated physics data | MEDIUM | REV-008 |
| ActivitySource disposed on stop | MEDIUM | Deferred (tracing concern, low impact) |
| Token revocation fail-open | LOW-MED | REV-012 |
| Ambiguous class name collision | LOW-MED | Deferred (naming, low risk) |
| Emulator non-thread-safe dicts | LOW-MED | Deferred (single-threaded access) |
| Missing Admin API test coverage | LOW-MED | REV-019 |
| Custom Log class in emulator | LOW | Deferred (functional) |

### Frontend Senior Findings

| Finding | Task |
|---------|------|
| Firebase logout bug | REV-006 |
| Divergent mapRoleToAbilities | REV-006 |
| Footer shows Pixinvent branding | REV-006 |
| 61 occurrences of `: any` | REV-017 |
| No debounce on search inputs | REV-017 |
| Dead Vuexy demo code (60-70%) | REV-017 |
| getCurrentInstance() usage | REV-017 |
| Accessibility D rating | REV-015 |
| chunkSizeWarningLimit: 5000 | REV-017 |

### Pedagogy Professor 1 Findings

| Finding | Task |
|---------|------|
| Only MCQ (no constructed-response) | Deferred (feature -- new question types) |
| No student-facing learning map | Deferred (feature -- MOB/WEB task) |
| SelfConfidence field never populated | Deferred (feature enhancement) |
| No summative assessment mode | Deferred (feature) |
| No peer/social features | Deferred (feature) |
| No metacognitive prompting | Deferred (feature) |
| Accessibility D+ | REV-015 |
| No delayed feedback option | Deferred (research-backed enhancement) |

### Pedagogy Professor 2 Findings

| Finding | Task |
|---------|------|
| No AI transparency disclosure | Deferred (EU AI Act -- needs UX design) |
| Cultural sensitivity scorer hardcoded | Deferred (feature -- quality gate enhancement) |
| A/B experiment metric persistence TODO | Deferred (feature) |
| Off-topic detection brittle keywords | Deferred (feature enhancement) |
| No chain-of-thought in tutoring prompts | Deferred (prompt engineering) |
| Kimi provider not implemented | Deferred (feature -- REV-020 enables this) |

### DevOps Engineer Findings

| Finding | Task |
|---------|------|
| No Dockerfiles for .NET services | Deferred (INF-006/007) |
| Prometheus scrapes wrong port | REV-018 |
| No OTLP collector | REV-018 |
| Admin API zero observability | REV-018 |
| start.sh hardcodes wrong port | REV-018 |
| Grafana default password | REV-018 |
| No log aggregation | Deferred (INF-009) |
| No horizontal scaling strategy | Deferred (INF-006) |
| No formal migration framework | Deferred (DATA-009) |
| Observability compose path issues | REV-018 |

### QA Lead Findings

| Finding | Task |
|---------|------|
| StudentActor behavior untested | REV-019 |
| TutorActor + SafetyGuard untested | REV-019 |
| NatsBusRouter untested | REV-019 |
| Admin API zero HTTP integration tests | REV-019 |
| Zero frontend tests | REV-019 |
| Zero E2E tests | REV-019 (partial -- setup only) |
| No Testcontainers | Deferred (infrastructure) |
| No CI pipeline | REV-009 |
| FatigueComputationTests uses reflection | Deferred (test quality) |
| Emulator has no assertions | Deferred (emulator enhancement) |

---

## Dependency Graph

```
Phase 1: Security (Week 1-2) -- all parallelizable
  REV-001 (Firebase key)          -- do FIRST
  REV-002 (NATS auth)
  REV-003 (Postgres backup)
  REV-004 (Secure endpoints + headers)
  REV-010 (Hardcoded credentials)
  REV-012 (Token revocation + Redis auth)

Phase 2: Production Foundation (Week 3-4)
  REV-005 (Cluster provider)
  REV-006 (Frontend auth bugs + XSS)
  REV-007 (JetStream subjects)     -- after REV-002
  REV-008 (Backend critical fixes)
  REV-009 (CI/CD pipeline)
  REV-011 (Rate limiting + validation)

Phase 3: Compliance & Quality (Week 5-8)
  REV-013 (FERPA compliance)       -- after REV-003
  REV-014 (Tenant isolation)
  REV-015 (Accessibility)
  REV-016 (Admin API restructure)
  REV-017 (Frontend cleanup)
  REV-018 (Observability fixes)
  REV-019 (Test coverage)          -- after REV-009
  REV-020 (LLM ACL layer)
```

---

## Cross-References to Existing Tasks

| Review Task | Related Existing Task | Relationship |
|-------------|----------------------|--------------|
| REV-002 | INF-011 (NATS Account-Based Auth) | REV-002 = minimum viable; INF-011 = full production |
| REV-005 | ACT-001 (Cluster Bootstrap) | ACT-001 done with TestProvider; REV-005 wires real provider |
| REV-009 | INF-007 (CI/CD Pipelines) | REV-009 = build+test; INF-007 = Docker+deploy |
| REV-010 | INF-008 (Secrets) | REV-010 = remove hardcoded; INF-008 = add secrets manager |
| REV-006 | SEC-004 (Injection) | REV-006 = XSS specifically; SEC-004 = all injection types |
| REV-011 | SEC-007 (Rate Limiting) | REV-011 = HTTP rate limiting; SEC-007 = Redis sliding window (stale) |
| REV-013 | SEC-005 (GDPR), SEC-008 (Audit), DATA-011 | REV-013 = FERPA-focused compliance |
| REV-015 | MOB-011 (Accessibility) | REV-015 = admin dashboard; MOB-011 = mobile app |
| REV-020 | LLM-001 (ACL Scaffold) | REV-020 = extract from actors; LLM-001 = original scaffold plan |

---

## Deferred Items (Not Tasked)

These findings are acknowledged but deferred -- they are either features, production infrastructure (handled by INF-* tasks), or low-risk refinements:

| Finding | Reason Deferred |
|---------|----------------|
| 15-param constructor on StudentActor | Refactor, low urgency |
| Neo4j deployed but unused | Resource waste, not a bug |
| No SignalR | Feature, not a deficiency |
| Docker root containers, no digest pinning | Handled by INF-006 |
| No Terraform | Handled by INF-001 through INF-010 |
| No Dockerfiles for .NET services | Handled by INF-006/007 |
| No network segmentation | Handled by INF-001 |
| No encryption at rest | Handled by INF-002/003 |
| Only MCQ question types | Feature (new question types) |
| Student-facing learning map | Feature (MOB/WEB) |
| Peer/social features | Feature |
| AI transparency disclosure | Needs UX design for EU AI Act |
| A/B experiment persistence TODO | Feature completion |
| Testcontainers | Infrastructure enhancement |
| Emulator single-file, no assertions | Functional, low priority |
