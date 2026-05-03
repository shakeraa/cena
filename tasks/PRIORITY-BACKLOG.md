# Cena Platform -- Priority Backlog

**Generated:** 2026-03-30 | **Active tasks:** 97 | **Completed:** 145+

---

## Legend

- **P0** = Blocks production / core loop
- **P1** = Important for feature completeness
- **P2** = Enhancement / polish
- **P3** = Future / nice-to-have
- **Superseded** = Replaced by a REV task already implemented
- Deps in **bold** = the blocker is NOT yet done

---

## TIER 1: No Blockers -- Ready to Start Now

These have zero unmet dependencies. Can be parallelized.

| ID | Domain | Title | Priority |
|----|--------|-------|----------|
| REV-001 | Review | Firebase key rotation (manual GCP) | P0 |
| ERR-001 | Backend | Global error handling patterns | P0 |
| EMU-001 | Emulator | Population model (archetype-driven cohort) | P0 |
| INF-001 | Infra | AWS VPC + networking | P0 |
| INF-010 | Infra | Domain + DNS + SSL | P1 |
| INF-012 | Infra | gRPC mTLS for Proto.Actor | P0 |
| INF-018 | Infra | NATS backpressure handling | P1 |
| INF-019 | Infra | Redis circuit breaker enhancement | P1 |
| INF-022 | Infra | Proto.Actor portability layer | P3 |
| LLM-001 | LLM | ACL scaffold (Python FastAPI) | P0 |
| MOB-001 | Mobile | Flutter app scaffold | P0 |
| WEB-001 | Frontend | Student web app scaffold | P0 |
| ADM-017 | Admin | Tutoring session dashboard UI | P0 |
| ADM-021 | Admin | Methodology override UI | P1 |
| ADM-022 | Admin | Explanation version/language UI | P1 |
| ADM-024 | Admin | Existing page enhancements | P2 |
| SEC-002 | Security | IDOR prevention | P0 |

---

## TIER 2: One Blocker Away

These have 1 dependency that must be done first.

| ID | Domain | Title | Priority | Blocked By |
|----|--------|-------|----------|------------|
| INF-002 | Infra | RDS PostgreSQL (managed) | P0 | **INF-001** |
| INF-003 | Infra | NATS cluster (Synadia Cloud) | P0 | **INF-001** |
| INF-004 | Infra | Redis (ElastiCache) | P0 | **INF-001** |
| INF-008 | Infra | AWS Secrets Manager | P0 | **INF-001** |
| EMU-002 | Emulator | Arrival scheduler | P0 | **EMU-001** |
| LLM-002 | LLM | Multi-model router | P0 | **LLM-001** |
| LLM-003 | LLM | Input/output sanitizer | P0 | **LLM-001** |
| LLM-005 | LLM | Prompt templates | P0 | **LLM-001** |
| LLM-007 | LLM | PII stripping | P0 | SEC-003 |
| MOB-002 | Mobile | Domain models (Dart) | P0 | **MOB-001** |
| MOB-005 | Mobile | State management (Riverpod) | P0 | **MOB-001** |
| MOB-010 | Mobile | i18n (Hebrew/Arabic/English) | P0 | **MOB-001** |
| MOB-015 | Mobile | Math text rendering (KaTeX) | P0 | **MOB-001** |
| MOB-023 | Mobile | Deep linking | P0 | **MOB-001** |
| WEB-002 | Frontend | SignalR client | P0 | **WEB-001** |
| WEB-003 | Frontend | REST API client | P1 | **WEB-001** |
| CNT-002 | Content | Question generation pipeline | P0 | **LLM-001** |
| SES-001 | Backend | SignalR hub | P0 | MSG (done) |
| DATA-010 | Data | Optimistic concurrency tuning | P0 | DATA (done) |
| SEC-003 | Security | PII data classification | P0 | **LLM-001** |
| SEC-004 | Security | Injection prevention (full) | P0 | **LLM-001** |
| FOC-002 | Focus | Mobile sensor collection | P1 | **MOB-001** |
| ADM-025 | Admin | Chat wiring (messaging UI) | P1 | MSG (done) |

---

## TIER 3: Two+ Blockers

| ID | Domain | Title | Priority | Blocked By |
|----|--------|-------|----------|------------|
| INF-005 | Infra | S3 + CloudFront CDN | P1 | **INF-001**, INF-010 |
| INF-006 | Infra | ECS Fargate deployment | P0 | **INF-001**, **INF-002**, **INF-004** |
| INF-007 | Infra | Full CI/CD (Docker + deploy) | P1 | **INF-001**, **INF-006**, **INF-008** |
| INF-009 | Infra | Production monitoring | P1 | **INF-006** |
| INF-011 | Infra | NATS full account auth (prod) | P0 | SEC (done) |
| INF-016 | Infra | Load testing | P0 | ACT (done), **INF-001** |
| INF-017 | Infra | Payments integration | P0 | SEC (done) |
| INF-020 | Infra | Distributed tracing (prod) | P2 | **INF-009** |
| INF-021 | Infra | PG read replica | P2 | **INF-002** |
| EMU-003 | Emulator | Session behavior simulation | P0 | **EMU-002** |
| EMU-004 | Emulator | Orchestrator | P0 | **EMU-001**, **EMU-002**, **EMU-003** |
| EMU-005 | Emulator | Admin dashboard integration | P1 | **EMU-004** |
| LLM-004 | LLM | Token budget enforcement | P1 | **LLM-001** |
| LLM-006 | LLM | Quality gate (LLM-based) | P1 | **LLM-005** |
| LLM-008 | LLM | Cost tracking | P1 | **LLM-001** |
| LLM-010 | LLM | Socratic tutoring E2E | P1 | **LLM-005** |
| LLM-009 | LLM | Diagram generation | P2 | **LLM-005**, **INF-005** |
| MOB-003 | Mobile | WebSocket service | P1 | **MOB-002** |
| MOB-004 | Mobile | Offline sync (Drift) | P1 | **MOB-002**, **MOB-003** |
| MOB-006 | Mobile | Knowledge graph visualization | P1 | **MOB-005**, **MOB-002** |
| MOB-007 | Mobile | Session screen | P0 | **MOB-005**, **MOB-001** |
| MOB-008 | Mobile | Gamification (XP, streaks) | P1 | **MOB-005** |
| MOB-011 | Mobile | Accessibility | P1 | **MOB-007** |
| MOB-013 | Mobile | Onboarding flow | P1 | **MOB-007** |
| MOB-014 | Mobile | Push notifications | P2 | **MOB-001** |
| MOB-009 | Mobile | Math diagrams | P2 | **INF-005**, **LLM-009** |
| MOB-012 | Mobile | Analytics | P2 | **MOB-005** |
| MOB-016 | Mobile | App size optimization | P2 | All other MOB |
| MOB-001-session-chat | Mobile | Session chat UI | P0 | **SES-001** |
| WEB-004 | Frontend | State management | P0 | **WEB-001**, **WEB-002** |
| WEB-005 | Frontend | Offline support | P1 | **WEB-001**, **WEB-002**, **WEB-004** |
| WEB-006 | Frontend | Teacher dashboard | P1 | **WEB-001**, **WEB-003**, **WEB-004** |
| WEB-007 | Frontend | Parent dashboard | P2 | **WEB-001**, **WEB-003**, **WEB-004** |
| WEB-008 | Frontend | Knowledge graph (web) | P2 | **WEB-001**, **WEB-003**, **WEB-004** |
| WEB-009 | Frontend | Session UI | P0 | **WEB-001**, **WEB-002**, **WEB-004** |
| CNT-003 | Content | Review admin tool | P1 | **CNT-002** |
| CNT-004 | Content | QA pass pipeline | P1 | **CNT-002** |
| CNT-005 | Content | Publishing pipeline | P1 | **CNT-004**, **INF-005** |
| CNT-007 | Content | Diagram scheduler | P0 | DATA (done), **INF-001** |
| CNT-008 | Content | Ingestion pipeline | P1 | DATA (done), **INF-005** |
| CNT-009 | Content | Content moderation | P1 | **CNT-008** |
| CNT-010 | Content | Content serving (actor) | P0 | **CNT-009** |
| CNT-006 | Content | Physics curriculum | P2 | CNT (done) |
| SES-002 | Backend | Session lifecycle API | P1 | **SES-001** |
| ADM-026 | Admin | Real-time session monitor | P1 | ADM-017, **SES-001** |
| ACT-010 | Actors | Session SignalR bridge | P0 | None (but couples with SES-001) |
| DATA-006 | Data | Neo4j AuraDB (managed) | P1 | **INF-002** |
| DATA-009 | Data | Event upcasters | P2 | DATA (done) |
| DATA-011 | Data | GDPR crypto-shredding | P1 | DATA (done), **INF-004** |
| SEC-005 | Security | GDPR full compliance | P1 | **INF-002**, **INF-004**, **INF-005** |
| SEC-006 | Security | Client tamper detection | P0 | SEC (done), **MOB-001** |
| SEC-007 | Security | Rate limiting (Redis sliding window) | P0 | **INF-004** |
| SEC-008 | Security | Full audit framework | P1 | All other SEC |
| FOC-004 | Focus | Mind wandering detector v2 | P2 | FOC (done) |
| FOC-007 | Focus | Chronotype detector | P2 | **FOC-002** |
| FOC-008 | Focus | Solution diversity tracker | P1 | ACT (done) |
| FOC-009 | Focus | Sensor privacy layer | P0 | **FOC-002** |
| FOC-010 | Focus | Focus A/B testing | P2 | FOC (done) |
| FOC-011 | Focus | Gamification novelty rotation | P2 | **MOB-008** |
| FOC-012 | Focus | Cultural resilience stratification | P3 | FOC (done) |
| MST-015 | Mastery | BKT parameter trainer | P2 | DATA (done) |
| MST-016 | Mastery | HLR parameter trainer | P2 | MST (done) |
| MST-018 | Mastery | MIRT estimator | P3 | **MST-015** |

---

## Superseded Tasks

These were replaced by REV tasks already implemented:

| ID | Replaced By | Status |
|----|-------------|--------|
| SEC-007 (Rate limiting) | REV-011 | REV-011 done (HTTP rate limiting). SEC-007 targets Redis sliding window for prod. |
| INF-011 (NATS auth) | REV-002 | REV-002 done (dev auth). INF-011 is full account-based prod auth. |
| INF-007 (CI/CD) | REV-009 | REV-009 done (build+test). INF-007 is full Docker+deploy pipeline. |
| LLM-001 (ACL scaffold) | REV-020 | REV-020 done (.NET scaffold). LLM-001 was Python FastAPI -- architecture changed. |

---

## Recommended Execution Order

### Phase 1: Core Infrastructure (Week 1-2)
**Parallel tracks:**
1. **INF-001** (VPC) -> INF-002 (RDS) -> INF-004 (Redis) -> INF-006 (ECS)
2. **MOB-001** (Flutter scaffold) -> MOB-002 -> MOB-005 -> MOB-007
3. **EMU-001** -> EMU-002 -> EMU-003 -> EMU-004
4. **REV-001** (Firebase key rotation -- manual, do now)

### Phase 2: Connectivity (Week 3-4)
1. **SES-001** (SignalR hub) -> SES-002 -> ACT-010
2. **ADM-017** (Tutoring dashboard) -> ADM-026
3. **WEB-001** (Web scaffold) -> WEB-002 -> WEB-004

### Phase 3: Content & LLM (Week 5-6)
1. **CNT-002** (Questions) -> CNT-003 -> CNT-004
2. **SEC-002** (IDOR) + **SEC-003** (PII) + **SEC-004** (Injection)
3. **INF-008** (Secrets) -> INF-007 (Full CI/CD)

### Phase 4: Polish & Scale (Week 7-8)
1. Mobile: MOB-010 (i18n) + MOB-015 (math) + MOB-011 (a11y)
2. Focus: FOC-002 (sensors) -> FOC-009 (privacy)
3. Mastery: MST-015 (BKT trainer) + MST-016 (HLR trainer)
4. INF-016 (Load testing) -> INF-009 (Monitoring)
