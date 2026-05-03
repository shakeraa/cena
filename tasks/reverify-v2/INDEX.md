# Reverify v2 Task Index (2026-04-11)

Generated from the Cena Review v2 re-verification run.
Report: `docs/reviews/cena-review-2026-04-11-reverify.md`

## Priority order for remediation

1. **Privacy compliance** (7 P0) — legal blocker; no controls passing
2. **Tenant isolation** (sec P0s) — data leakage + privilege escalation
3. **MSW production gate** (arch-017 P0) — kills the "dev path diverges" cluster
4. **CI wiring** (qa-001 P0) — one YAML change closes biggest CI gap
5. Everything else by severity then lens

## Quality gate (applies to every task)

Every fix MUST:
- Be production-grade (no stubs, no canned data, no placeholders)
- Include a CI-wired regression test that fails on the buggy commit
- Add a structured log line on the error path
- Pass `dotnet build` + `dotnet test` + `npm test` where applicable

---

## privacy (14 tasks: 7 P0, 7 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-PRIVACY-006 | GDPR data export incomplete — only profile snapshot, missing tutor history + sessions + events | `t_2ad633abfa65` | [find-privacy-006.md](privacy/find-privacy-006.md) |
| **P1** | FIND-PRIVACY-009 | No DPIA exists for high-risk minor profiling + AI tutoring | `t_ee705ad16927` | [find-privacy-009.md](privacy/find-privacy-009.md) |
| **P1** | FIND-PRIVACY-010 | Default student preferences are NOT high-privacy (8/9 toggles default ON) | `t_f8ad584a9fb3` | [find-privacy-010.md](privacy/find-privacy-010.md) |
| **P1** | FIND-PRIVACY-012 | FERPA audit middleware only covers 6 hardcoded paths — most admin reads not audited | `t_1484495513fe` | [find-privacy-012.md](privacy/find-privacy-012.md) |
| **P1** | FIND-PRIVACY-015 | Raw client IPs persisted with no truncation, no disclosure | `t_ec5f4fe6d4a1` | [find-privacy-015.md](privacy/find-privacy-015.md) |
| **P1** | FIND-PRIVACY-016 | Sentry stub pre-wires student email to SaaS — gate before STU-W-OBS-SENTRY unblocks | `t_1245132fe3e4` | [find-privacy-016.md](privacy/find-privacy-016.md) |
| **P1** | FIND-PRIVACY-018 | Social UGC has no in-app reporting, blocking, or moderation | `t_fc830c05b406` | [find-privacy-018.md](privacy/find-privacy-018.md) |
| **P0** | FIND-PRIVACY-001 | No age gate or parental consent on student registration | `t_4c77eb4b3436` | [find-privacy-001.md](privacy/find-privacy-001.md) |
| **P0** | FIND-PRIVACY-002 | No Privacy Policy / Terms of Service / Children's Notice anywhere | `t_44b059e06deb` | [find-privacy-002.md](privacy/find-privacy-002.md) |
| **P0** | FIND-PRIVACY-003 | GDPR rights endpoints all gated AdminOnly — no student/parent self-service | `t_2d8e5ce4037f` | [find-privacy-003.md](privacy/find-privacy-003.md) |
| **P0** | FIND-PRIVACY-004 | Retention policy declared but never enforced — 'retained indefinitely' | `t_a852a034cd2a` | [find-privacy-004.md](privacy/find-privacy-004.md) |
| **P0** FAKE-FIX | FIND-PRIVACY-005 | Right-to-erasure is a fake fix — only deletes consent log; ProcessErasureAsync has zero callers | `t_601136dd9b19` | [find-privacy-005.md](privacy/find-privacy-005.md) |
| **P0** FAKE-FIX | FIND-PRIVACY-007 | Consent system is cosmetic — HasConsentAsync defined, never called | `t_d2be304a10fe` | [find-privacy-007.md](privacy/find-privacy-007.md) |
| **P0** | FIND-PRIVACY-008 | Tutor sends child free-text to Anthropic with no DPA, no scrub, no safeguarding | `t_8bba4bf4ad8f` | [find-privacy-008.md](privacy/find-privacy-008.md) |

## sec (8 tasks: 4 P0, 4 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-SEC-012 | Actor host /api/actors/stats is anonymous and leaks studentIds | `t_417c37caf3fb` | [find-sec-012.md](sec/find-sec-012.md) |
| **P1** | FIND-SEC-013 | /api/content/questions/{id}/explanation leaks LLM prompt + serves draft questions | `t_18c6b8a10695` | [find-sec-013.md](sec/find-sec-013.md) |
| **P1** | FIND-SEC-014 | No custom metrics or alert rules on the security-critical fix paths | `t_c449d12b59af` | [find-sec-014.md](sec/find-sec-014.md) |
| **P1** | FIND-SEC-015 | No global or per-tenant cap on AI tutor cost (per-user rate limit only) | `t_ba8a2aa46b59` | [find-sec-015.md](sec/find-sec-015.md) |
| **P0** | FIND-SEC-008 | Cross-tenant write surface in AdminUserService (any ADMIN can edit/delete any user) | `t_074e96ac9059` | [find-sec-008.md](sec/find-sec-008.md) |
| **P0** REGRESSION | FIND-SEC-009 | Actor host NATS dev-password fallback (partial regression of FIND-sec-003) | `t_7d460d39c14f` | [find-sec-009.md](sec/find-sec-009.md) |
| **P0** | FIND-SEC-010 | Privilege escalation via POST /api/admin/users/{id}/role (any ADMIN can mint SUPER_ADMIN) | `t_d4524ac529cd` | [find-sec-010.md](sec/find-sec-010.md) |
| **P0** REGRESSION | FIND-SEC-011 | Cross-tenant reads + destructive writes in Mastery/Messaging/GDPR (partial regression of FIND-sec-005) | `t_1705a03eaaba` | [find-sec-011.md](sec/find-sec-011.md) |

## arch (9 tasks: 4 P0, 5 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-ARCH-021 | 5 orphan NATS publishers (ingest, student.escalation, admin.methodology) | `t_3c0bbeea2124` | [find-arch-021.md](arch/find-arch-021.md) |
| **P1** | FIND-ARCH-022 | NatsOutboxPublisher publishes core NATS not JetStream + 5 orphan durable categories | `t_7f3d9fcf1b56` | [find-arch-022.md](arch/find-arch-022.md) |
| **P1** | FIND-ARCH-023 | SessionEndpoints GetSessionDetail/Replay full-scan event store | `t_766c5582f2a2` | [find-arch-023.md](arch/find-arch-023.md) |
| **P1** | FIND-ARCH-024 | FeatureFlagActor in-memory only — no persistence, no replica sync, no audit | `t_a2aef6aa1112` | [find-arch-024.md](arch/find-arch-024.md) |
| **P1** FAKE-FIX | FIND-ARCH-025 | ClaudeTutorLlmService fake-streams a unary Anthropic response (label drift) | `t_b62dff440b61` | [find-arch-025.md](arch/find-arch-025.md) |
| **P0** | FIND-ARCH-017 | MSW intercepts /api/* in production student-web builds | `t_37119818c91a` | [find-arch-017.md](arch/find-arch-017.md) |
| **P0** | FIND-ARCH-018 | NotificationChannelService stub channels (web push, email, SMS) | `t_25df87c51509` | [find-arch-018.md](arch/find-arch-018.md) |
| **P0** | FIND-ARCH-019 | DLQ retry/bulk-retry endpoints return success without retrying | `t_60bf2c15d4cc` | [find-arch-019.md](arch/find-arch-019.md) |
| **P0** | FIND-ARCH-020 | IMAP/Cloud-dir test + Embedding reindex stubs return fake success | `t_017eed8be44b` | [find-arch-020.md](arch/find-arch-020.md) |

## data (9 tasks: 4 P0, 5 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-DATA-024 | audit log ignores filter param + full mt_events seq-count per page + hardcoded IP | `t_731b808f3ad1` | [find-data-024.md](data/find-data-024.md) |
| **P1** | FIND-DATA-025 | StudentInsightsService - 13 global event-store scans, no tenant scope, sample-truncation bug | `t_d460e84481fa` | [find-data-025.md](data/find-data-025.md) |
| **P1** | FIND-DATA-026 | ExperimentAdminService - 9 unscoped full-scans, 5x per funnel request, no persisted assignments | `t_8eec1c3c7039` | [find-data-026.md](data/find-data-026.md) |
| **P1** | FIND-DATA-027 | SessionEndpoints + StudentAnalyticsEndpoints still do full mt_events scans per request | `t_7cc75a600edf` | [find-data-027.md](data/find-data-027.md) |
| **P1** | FIND-DATA-028 | GamificationEndpoints.GetBadges fabricates EarnedAt from Random (fake data on learner surface) | `t_503e56126008` | [find-data-028.md](data/find-data-028.md) |
| **P0** | FIND-DATA-020 | rate-limit policies are global, not per-user (AI cost bomb) | `t_3fffc5d5baad` | [find-data-020.md](data/find-data-020.md) |
| **P0** | FIND-DATA-021 | token cost meter fabricated from 200-char preview; real tokens ignored | `t_b224f213658a` | [find-data-021.md](data/find-data-021.md) |
| **P0** REGRESSION | FIND-DATA-022 | AnalysisJobActor dead query - EventTypeName PascalCase regression | `t_7a7cb4849130` | [find-data-022.md](data/find-data-022.md) |
| **P0** FAKE-FIX | FIND-DATA-023 | StudentLifetimeStatsProjection reintroduces wall-clock Apply + is dead code (fake-fix of FIND-data-009) | `t_c5be29342c1d` | [find-data-023.md](data/find-data-023.md) |

## pedagogy (8 tasks: 4 P0, 4 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-PEDAGOGY-014 | Zero plural forms in student web i18n — Arabic/Hebrew show ungrammatical singular | `t_9e338d79571a` | [find-pedagogy-014.md](pedagogy/find-pedagogy-014.md) |
| **P1** | FIND-PEDAGOGY-015 | Date and number formatters hardcoded en-US — do not switch with UI language | `t_1bec032a8290` | [find-pedagogy-015.md](pedagogy/find-pedagogy-015.md) |
| **P1** | FIND-PEDAGOGY-016 | REST session not adaptive — queue never seeded; returns 'Session completed' on first GET | `t_36ad75f9a484` | [find-pedagogy-016.md](pedagogy/find-pedagogy-016.md) |
| **P1** | FIND-PEDAGOGY-017 | AnswerFeedback.vue renders English server feedback string alongside translated heading | `t_b8d4df8b2911` | [find-pedagogy-017.md](pedagogy/find-pedagogy-017.md) |
| **P0** FAKE-FIX | FIND-PEDAGOGY-010 | Hide-Hebrew gate bypassable via cookie + onboarding picker hardcodes HE | `t_f0cfa809cd67` | [find-pedagogy-010.md](pedagogy/find-pedagogy-010.md) |
| **P0** FAKE-FIX | FIND-PEDAGOGY-011 | MSW dev mock returns binary feedback + linear delta — defeats every pedagogy fix | `t_fb95d37042e7` | [find-pedagogy-011.md](pedagogy/find-pedagogy-011.md) |
| **P0** | FIND-PEDAGOGY-012 | REST current-question never populates ScaffoldingLevel; /hint route not registered | `t_db9eaa46f096` | [find-pedagogy-012.md](pedagogy/find-pedagogy-012.md) |
| **P0** | FIND-PEDAGOGY-013 | Authored question Explanation is monolingual — student locale ignored on the wire | `t_d36e5f09a241` | [find-pedagogy-013.md](pedagogy/find-pedagogy-013.md) |

## ux (17 tasks: 6 P0, 10 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** | FIND-UX-011 | FIND-ux-011 + FIND-ux-012: Student swallow-and-smile failures — social vote/accept silently drop errors, keyboard shortcut eats '?' in tutor textarea | `t_6b1c4fbe96d2` | [find-ux-011.md](ux/find-ux-011.md) |
| **P1** | FIND-UX-025 | All 4 student header icon buttons missing aria-label — sidebar toggle, language, theme, notifications | `t_da7affaf863c` | [find-ux-025.md](ux/find-ux-025.md) |
| **P1** | FIND-UX-026 | Admin login heading hierarchy is H1 -> H4 (skips H2 + H3) | `t_c92f6a542e75` | [find-ux-026.md](ux/find-ux-026.md) |
| **P1** | FIND-UX-027 | Admin password eye icon is a 16x16 hit target — wrap in IconBtn (subsumes FIND-ux-018) | `t_4b64574b8e08` | [find-ux-027.md](ux/find-ux-027.md) |
| **P1** | FIND-UX-028 | Notifications bell labeled תג (Hebrew Badge default) — escalate FIND-ux-017 to P1 | `t_e74d3e4c9ee0` | [find-ux-028.md](ux/find-ux-028.md) |
| **P1** | FIND-UX-029 | PWA manifest icon /images/logo.png is missing — Vite returns HTML, browser rejects | `t_75bfec46c698` | [find-ux-029.md](ux/find-ux-029.md) |
| **P1** | FIND-UX-030 | Session-setup subject chips have no role/aria-pressed — multi-select unusable for SR users | `t_ac2f39927d61` | [find-ux-030.md](ux/find-ux-030.md) |
| **P1** | FIND-UX-031 | OAuth provider buttons fake the sign-in — bypass real Google/Apple/Microsoft/phone | `t_169f384cdae3` | [find-ux-031.md](ux/find-ux-031.md) |
| **P1** | FIND-UX-032 | Settings/Notifications persists to localStorage only — escalate FIND-ux-016 to P1 (no Phase-1 stubs) | `t_6bcdeac9aa0e` | [find-ux-032.md](ux/find-ux-032.md) |
| **P1** | FIND-UX-033 | Admin login labels are title-case + not i18n'd — sentence-case + extract | `t_43a02f11c2b4` | [find-ux-033.md](ux/find-ux-033.md) |
| **P2** | FIND-UX-006 | FIND-ux-006c: Student forgot-password.vue rewrite to consume real POST /api/auth/password-reset | `t_f7bb146a546b` | [find-ux-006.md](ux/find-ux-006.md) |
| **P0** | FIND-UX-019 | Vuexy primary #7367F0 fails WCAG 2.2 AA contrast — fix via usage pattern (4.26:1 light, 2.91:1 dark) | `t_89e7d3c33286` | [find-ux-019.md](ux/find-ux-019.md) |
| **P0** | FIND-UX-020 | Student desktop sidebar is empty — CASL gate filters every nav item because userAbilityRules cookie never set | `t_bcba38da3393` | [find-ux-020.md](ux/find-ux-020.md) |
| **P0** | FIND-UX-021 | MSW worker race + raw error message leak — user sees [GET] /api/me 404 Not Found on every cold reload | `t_2a3d67a71b2f` | [find-ux-021.md](ux/find-ux-021.md) |
| **P0** | FIND-UX-022 | Student user-profile menu activator is a div not a button — sign-out unreachable for keyboard/SR users | `t_9236e6014b58` | [find-ux-022.md](ux/find-ux-022.md) |
| **P0** | FIND-UX-023 | Student login is a stub — wire real Firebase Auth (cena-platform), delete __mockSignIn | `t_9b19a52d72df` | [find-ux-023.md](ux/find-ux-023.md) |
| **P0** REGRESSION | FIND-UX-024 | Class-feed reaction button silently fails AND increments to fixed value 1 (regression of FIND-ux-011) | `t_47762c224fe9` | [find-ux-024.md](ux/find-ux-024.md) |

## qa (10 tasks: 4 P0, 6 P1)

| Sev | ID | Headline | Queue | File |
|---|---|---|---|---|
| **P1** REGRESSION | FIND-QA-002 | MeEndpoints CQRS race regression test missing | `t_bdf505599872` | [find-qa-002.md](qa/find-qa-002.md) |
| **P1** REGRESSION | FIND-QA-005 | ClassFeedItemProjection determinism not asserted (FIND-data-001) | `t_25b60d65bbb2` | [find-qa-005.md](qa/find-qa-005.md) |
| **P1** REGRESSION | FIND-QA-006 | ux-008/009/010/013 regression tests missing — brand/auth/leaderboard | `t_d2688b9bc6fb` | [find-qa-006.md](qa/find-qa-006.md) |
| **P1** | FIND-QA-007 | Wall-clock flakiness in Cena.Actors.Tests — introduce TimeProvider | `t_07b82353c70d` | [find-qa-007.md](qa/find-qa-007.md) |
| **P1** | FIND-QA-009 | Cena.Actors.sln missing 5 of 13 projects (incl. Infrastructure.Tests) | `t_2205e5d2b53f` | [find-qa-009.md](qa/find-qa-009.md) |
| **P1** | FIND-QA-011 | No shared Firebase Auth test double; JWT verify path untested | `t_d356ef113d23` | [find-qa-011.md](qa/find-qa-011.md) |
| **P0** REGRESSION | FIND-QA-001 | Cena.Infrastructure.Tests (FIND-sec-001 SQLi suite) not wired into backend.yml | `t_e1485b61506e` | [find-qa-001.md](qa/find-qa-001.md) |
| **P0** REGRESSION | FIND-QA-003 | FocusAnalytics tenant filter has zero regression test (FIND-sec-005) | `t_4243ac31521f` | [find-qa-003.md](qa/find-qa-003.md) |
| **P0** REGRESSION | FIND-QA-004 | QueryAllRawEvents anti-pattern needs analyzer + tests (FIND-data-009) | `t_644582f3aea7` | [find-qa-004.md](qa/find-qa-004.md) |
| **P0** | FIND-QA-008 | Admin frontend has 1 test file, no E2E — establish baseline suite | `t_8387e7d8fecf` | [find-qa-008.md](qa/find-qa-008.md) |

---

**Totals**: 75 tasks, 33 P0, 41 P1
