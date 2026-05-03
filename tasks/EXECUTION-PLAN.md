# Cena Platform -- Execution Plan

**Updated:** 2026-03-30 | **Status:** Post-system-review, 145+ tasks done, 97 active

---

## Current State

### What's Built (Production-Quality)
- **Actor System**: Proto.Actor cluster with event-sourced StudentActor, 38+ events, Marten snapshots, 714 tests
- **Mastery Engine**: BKT + HLR + Elo + Knowledge Space Theory diagnostics, 9 methodologies, scaffolding, stagnation detection
- **AI Tutoring**: Multi-model LLM (Claude/Haiku/Gemini), 4 methodology-enforced modes, safety guard, tiered explanations
- **Admin Dashboard**: Vue 3 + Vuexy + Vuetify, 30+ Cena pages, Firebase Auth, CASL RBAC
- **Admin API**: .NET 9 Minimal API, 140+ endpoints, rate limiting, input validation, FERPA audit logging
- **NATS Messaging**: Authenticated bus with subject ACLs, JetStream durability, transactional outbox
- **CI/CD**: GitHub Actions (backend build+test, frontend lint+build, Dependabot)
- **Security**: Security headers, XSS sanitization, fail-closed token revocation, Redis auth, tenant isolation
- **Observability**: OpenTelemetry + Prometheus + Grafana + Jaeger on both hosts
- **LLM ACL**: .NET scaffold with ILlmGateway interface, global rate limiter (to be expanded)

### What's NOT Built
- **Mobile app** (Flutter) -- zero code
- **Student web app** -- zero code (admin dashboard exists, student-facing does not)
- **AWS infrastructure** -- local Docker only, no Terraform/ECS/RDS
- **SignalR real-time** -- no hub, no WebSocket connections
- **Content pipeline** -- question bank exists but no bulk generation/review/publish workflow
- **Payments** -- no billing integration

---

## Architecture Decisions (Locked)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Mobile framework | **Flutter** (Dart, Riverpod, Drift) | Cross-platform, strong RTL support |
| Student web | **Vue 3** (same stack as admin) | Code sharing, team expertise |
| API pattern | **REST + SignalR** (no GraphQL) | Simpler, sufficient for real-time needs |
| LLM integration | **.NET ACL layer** (not Python FastAPI) | Single runtime, REV-020 scaffold done |
| Auth | **Firebase Auth** | Already integrated, JWT + JWKS rotation |
| Event store | **Marten on PostgreSQL** | Already running, 38+ events, snapshots |
| Actor framework | **Proto.Actor** | Already running, cluster-ready via REV-005 |
| Message bus | **NATS** (authenticated, JetStream) | Already running, REV-002 auth done |

---

## Execution Sprints

### Sprint 1: Real-Time Foundation (Week 1-2)

**Goal:** SignalR hub running, mobile scaffold created, student sessions work end-to-end.

**Track A -- Backend (3 devs)**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | REV-001 | 2h | Firebase key rotated in GCP Console |
| 2 | SES-001 | 3d | SignalR hub with session events, auth, NATS bridge |
| 3 | ACT-010 | 2d | Actor-to-SignalR bridge for real-time student events |
| 4 | SES-002 | 2d | Session lifecycle REST API (start/end/status) |
| 5 | ERR-001 | 1d | Error handling patterns across all services |

**Track B -- Mobile (2 devs)**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | MOB-001 | 3d | Flutter project scaffold (navigation, DI, theming) |
| 2 | MOB-002 | 2d | Dart domain models matching .NET contracts |
| 3 | MOB-005 | 2d | Riverpod state management |
| 4 | MOB-010 | 2d | i18n (Hebrew/Arabic/English, RTL) |
| 5 | MOB-015 | 2d | Math text rendering (KaTeX/flutter_math) |

**Track C -- Admin (1 dev)**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | ADM-017 | 2d | Tutoring session dashboard (list, detail, analytics) |
| 2 | ADM-021 | 1d | Methodology override UI |
| 3 | ADM-022 | 1d | Explanation version/language management |
| 4 | SEC-002 | 2d | IDOR prevention across all endpoints |

**Sprint 1 Exit Criteria:**
- [ ] SignalR hub accepts connections with Firebase JWT auth
- [ ] Student can start a session via SignalR -> NATS -> Actor Host -> response
- [ ] Flutter app compiles and renders a home screen with RTL Hebrew
- [ ] Admin can view tutoring sessions and override methodologies

---

### Sprint 2: Core Student Loop (Week 3-4)

**Goal:** A student can answer questions, see mastery updates, get AI tutoring on mobile.

**Track A -- Backend**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | EMU-001 | 2d | Enhanced emulator population model |
| 2 | EMU-002 | 1d | Arrival scheduler (realistic session timing) |
| 3 | EMU-003 | 2d | Session behavior simulation (attempts, confusion, hints) |
| 4 | EMU-004 | 1d | Emulator orchestrator (run cohorts, validate round-trip) |

**Track B -- Mobile**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | MOB-007 | 3d | Session screen (question display, answer input, hints) |
| 2 | MOB-003 | 2d | WebSocket/SignalR service |
| 3 | MOB-001-session-chat | 2d | Chat UI for AI tutor interaction |
| 4 | MOB-023 | 1d | Deep linking (Firebase dynamic links) |

**Track C -- Content**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | CNT-002 | 3d | Question generation pipeline (AI + manual + OCR) |
| 2 | CNT-003 | 2d | Review admin tool |
| 3 | CNT-004 | 2d | QA pass (quality gate integration) |

**Track D -- Admin**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | ADM-025 | 2d | Chat wiring (messaging UI) |
| 2 | ADM-026 | 2d | Real-time session monitor |
| 3 | ADM-024 | 2d | Existing page enhancements |

**Sprint 2 Exit Criteria:**
- [ ] Emulator runs 300 students through full sessions with round-trip validation
- [ ] Mobile app displays questions, accepts answers, shows mastery updates
- [ ] AI tutor responds via SignalR in < 3 seconds
- [ ] Question pipeline: generate -> review -> approve -> publish flow works E2E

---

### Sprint 3: Infrastructure & Security (Week 5-6)

**Goal:** AWS production environment, full security hardening, deployment pipeline.

**Track A -- Infrastructure**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | INF-001 | 3d | AWS VPC + networking (subnets, NAT, ALB) |
| 2 | INF-002 | 2d | RDS PostgreSQL (encrypted, multi-AZ) |
| 3 | INF-004 | 1d | ElastiCache Redis (auth, TLS) |
| 4 | INF-003 | 1d | NATS cluster (Synadia Cloud or self-hosted) |
| 5 | INF-008 | 1d | AWS Secrets Manager integration |
| 6 | INF-006 | 3d | ECS Fargate (Dockerfiles + task definitions) |
| 7 | INF-007 | 2d | Full CI/CD (Docker build + push + ECS deploy) |

**Track B -- Security**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | SEC-003 | 2d | PII data classification + field-level controls |
| 2 | SEC-004 | 2d | Injection prevention (parameterized queries audit) |
| 3 | SEC-006 | 2d | Client tamper detection (mobile) |
| 4 | INF-011 | 3d | NATS full account-based auth (production) |
| 5 | DATA-011 | 3d | GDPR crypto-shredding |

**Track C -- Mobile (continued)**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | MOB-004 | 3d | Offline sync (Drift SQLite + queue) |
| 2 | MOB-006 | 2d | Knowledge graph visualization |
| 3 | MOB-008 | 2d | Gamification (XP, streaks, badges) |
| 4 | MOB-013 | 2d | Onboarding flow |

**Sprint 3 Exit Criteria:**
- [ ] Cena deployed to AWS staging environment
- [ ] All secrets in AWS Secrets Manager (zero hardcoded)
- [ ] Mobile works offline and syncs on reconnect
- [ ] GDPR: student data deletable via crypto-shredding

---

### Sprint 4: Student Web & Polish (Week 7-8)

**Goal:** Student web app working, all platforms polished, load-tested.

**Track A -- Student Web**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | WEB-001 | 2d | Vue 3 scaffold (shared with admin stack) |
| 2 | WEB-002 | 2d | SignalR client |
| 3 | WEB-004 | 2d | Pinia state management |
| 4 | WEB-009 | 3d | Session UI (question display, answer, mastery) |
| 5 | WEB-003 | 1d | REST API client (useApi pattern from admin) |

**Track B -- Polish & Quality**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | MOB-011 | 2d | Mobile accessibility |
| 2 | MOB-014 | 2d | Push notifications (Firebase Cloud Messaging) |
| 3 | INF-016 | 3d | Load testing (k6/Locust, target: 1000 concurrent) |
| 4 | INF-009 | 2d | Production monitoring (CloudWatch, alerts) |
| 5 | INF-010 | 1d | Domain + DNS + SSL |

**Track C -- Content & Mastery**
| Order | Task | Effort | Output |
|-------|------|--------|--------|
| 1 | CNT-005 | 2d | Content publishing pipeline |
| 2 | CNT-006 | 3d | Physics curriculum (second subject) |
| 3 | MST-015 | 3d | BKT parameter trainer (batch calibration) |
| 4 | MST-016 | 3d | HLR parameter trainer |

**Sprint 4 Exit Criteria:**
- [ ] Student web app functional with session flow
- [ ] Load test: 1000 concurrent students, < 200ms p95 latency
- [ ] Push notifications working on iOS + Android
- [ ] Physics curriculum with 100+ questions

---

### Sprint 5: Launch Prep (Week 9-10)

| Task | Effort | Output |
|------|--------|--------|
| SEC-005 | 3d | Full GDPR compliance audit |
| SEC-008 | 2d | Complete audit framework |
| INF-017 | 3d | Payments integration |
| WEB-006 | 3d | Teacher dashboard (web) |
| WEB-007 | 2d | Parent dashboard (web) |
| FOC-002 | 3d | Mobile sensor collection |
| FOC-009 | 2d | Sensor privacy layer |
| EMU-005 | 2d | Emulator admin dashboard integration |
| MOB-016 | 1d | App size optimization |

**Launch Exit Criteria:**
- [ ] All P0 tasks complete
- [ ] Security audit passed
- [ ] FERPA + GDPR compliant
- [ ] Load test passed at target concurrency
- [ ] App Store + Play Store submissions ready

---

## Deferred (Post-Launch)

| Task | Domain | Why Deferred |
|------|--------|-------------|
| MST-018 | Mastery | MIRT estimator -- needs months of data |
| FOC-007 | Focus | Chronotype detector -- needs sensor data |
| FOC-010 | Focus | Focus A/B testing -- post-launch experiments |
| FOC-011 | Focus | Gamification novelty -- post-launch iteration |
| FOC-012 | Focus | Cultural resilience -- research-grade feature |
| INF-020 | Infra | Distributed tracing (production) -- post-deploy |
| INF-021 | Infra | PG read replica -- scale-up need |
| INF-022 | Infra | Proto.Actor portability -- future-proofing |
| DATA-009 | Data | Event upcasters -- needed at v2 schema change |
| MOB-009 | Mobile | Math diagrams -- nice-to-have |
| MOB-012 | Mobile | Analytics -- post-launch instrumentation |
| WEB-005 | Frontend | Offline (web) -- PWA enhancement |
| WEB-008 | Frontend | Knowledge graph (web) -- nice-to-have |
| LLM-009 | LLM | Diagram generation -- content enrichment |

---

## Resource Model

| Role | Count | Sprint 1-2 | Sprint 3-4 | Sprint 5 |
|------|-------|------------|------------|----------|
| Backend (.NET) | 3 | SignalR, sessions, emulator | Infra, security, ECS | Payments, audit |
| Mobile (Flutter) | 2 | Scaffold, domain, state | Offline, gamification, a11y | Polish, app store |
| Frontend (Vue) | 1 | Admin pages | Student web app | Teacher/parent dashboards |
| DevOps | 1 | CI/CD support | AWS infra, secrets, ECS | Load testing, monitoring |
| Security | 1 | IDOR, auth review | PII, injection, GDPR | Full audit |

**Total: 8 engineers, 10 weeks to launch-ready.**

---

## Critical Path

The longest dependency chain determines the minimum timeline:

```
INF-001 (VPC, 3d)
  → INF-002 (RDS, 2d)
    → INF-006 (ECS, 3d)
      → INF-007 (CI/CD, 2d)
        → INF-016 (Load test, 3d)
          → Launch
= 13 days (3 weeks) for infra alone

MOB-001 (scaffold, 3d)
  → MOB-002 (models, 2d)
    → MOB-007 (session screen, 3d)
      → MOB-004 (offline, 3d)
        → MOB-013 (onboarding, 2d)
          → Launch
= 13 days (3 weeks) for mobile alone

SES-001 (SignalR, 3d)
  → ACT-010 (bridge, 2d)
    → MOB-001-session-chat (2d)
      → Full student loop
= 7 days (2 weeks) for real-time
```

**Minimum time to production: 10 weeks** (infra and mobile run in parallel, backend delivers first).

---

## How to Use This Plan

1. **Start each sprint** by reading the track assignments
2. **Each task** has a detailed .md file in `tasks/{domain}/` with acceptance criteria
3. **Use agents** -- spawn one agent per task, review when done, move to `done/`
4. **No stubs** -- see `tasks/00-master-plan.md` for the full no-stubs rule
5. **Commit per task** -- one commit per completed task, push to main
6. **Cross-reference** `tasks/PRIORITY-BACKLOG.md` for dependency details
