---
run: cena-review-v2-reverify
date: 2026-04-11
version: 2
coordinator: claude-code
prior_review: docs/reviews/cena-review-2026-04-11.md
preflight_report: docs/reviews/reverify-2026-04-11-preflight.md
agents: [arch, sec, data, pedagogy, ux, privacy, qa]
base_sha_at_spawn: cc3f702
---

# Cena Review v2 — Re-verification Merge Report (2026-04-11)

## Executive summary (≤300 words)

Seven lens agents (arch, sec, data, pedagogy, ux, privacy, qa) re-audited
`origin/main` @ `cc3f702` against the 55 closed v1 `FIND-*` findings and
against the v2 expanded mandates (event-schema evolution, observability,
cost, i18n, WCAG 2.2 AA, child privacy, test coverage).

**Headline**: Phase 0 preflight cleared all 55 closed findings as
`verified-fixed` based on grep + git-log on the files the v1 tasks named.
**That verdict is partially wrong.** Phase 1 agents, running the fixes
at runtime (or tracing sibling patterns), found:

- **4 partial regressions** (sec × 2, data × 1, ux × 1) — the v1 fix was
  applied correctly to the file named, but the same bug class survived
  in sibling files the v1 sweep did not touch.
- **7 fake-fixes** (arch × 1, data × 1, pedagogy × 2, privacy × 2,
  qa × 1) — the label says fixed, the root cause is intact. Two of these
  live in the minor-facing path (pedagogy-001/-003 defeated in the dev
  MSW handler; FIND-ux-014 hide-Hebrew bypassable).
- **73 new findings enqueued** (33 P0, 39 P1, 24 P2, 4 P3 across the 7
  lenses — P2/P3 are in reports but not enqueued per the v2 protocol).
- **Compliance matrix**: 0 passing controls across COPPA, GDPR-K, ICO
  Children's Code, FERPA, Israel PPL. Cena collects PII from minors with
  zero age verification, no parental consent flow, no Privacy Policy, no
  DPA with Anthropic, no DPIA, and a fake right-to-erasure service.
- **CI gap**: the 18-assertion `Cena.Infrastructure.Tests` suite (the
  regression guard for `FIND-sec-001` SQL injection) is in the repo but
  never executes in CI — a real test in dead CI.

The remediation backlog is now dominated by **privacy compliance** (7 P0)
and **tenant isolation** (sec found 4 P0 cross-tenant admin writes incl.
privilege escalation to SUPER_ADMIN). Both are legal-exposure bets —
privacy gets a 10× multiplier in the impact matrix below.

---

## Part 1 — Regressions & fake-fixes (the headline)

Per v2 protocol: "Regression & fake-fix section first — these are the
report's headline. Everything else is secondary."

### 1.1 — Coordinator preflight correction

The preflight at [docs/reviews/reverify-2026-04-11-preflight.md](reverify-2026-04-11-preflight.md)
reported zero regressions and zero fake-fixes. That verdict was based on
grep-level verification of the specific files and patterns the v1
findings named, not on runtime behavior or sibling-file drift. Phase 1
agents corrected it:

- **Preflight method hole**: "Fix applied to `FocusAnalyticsService.cs`"
  was scored `verified-fixed`, but the same `Where(x => x.SchoolId == schoolId)`
  anti-pattern survived in `AdminUserService`, `MasteryTrackingService`,
  `MessagingAdminService`, `GdprEndpoints`. The fix was correct but
  scoped too narrowly. Preflight did not check siblings.
- **Preflight method hole**: "StudentLifetimeStatsProjection registered"
  was scored `verified-fixed`, but the projection is dead (zero readers)
  AND reintroduces `DateTimeOffset.UtcNow` in `Apply` (the exact
  anti-pattern `FIND-data-001` retired). Preflight checked registration,
  not reachability.
- **Preflight method hole**: "FIND-pedagogy-001/-003 fixed in
  `SessionEndpoints.cs`" was scored `verified-fixed`, but the
  developer-facing MSW dev path still ships canned binary feedback
  plus a hardcoded `±0.05` mastery delta. Preflight did not exercise
  the dev path.

**Corrected verdicts** for the affected prior findings:

| Prior finding | Preflight verdict | Corrected verdict | Evidence |
|---|---|---|---|
| FIND-sec-003 (NATS dev-password) | verified-fixed | **partial regression** | `FIND-sec-009` — Actor host fallback bypasses `CenaNatsOptions` helper |
| FIND-sec-005 (Focus Analytics tenant bypass) | verified-fixed | **partial regression** | `FIND-sec-011` — same `.Where(SchoolId)` missing in 3 sibling services |
| FIND-data-006 (PascalCase event predicate) | verified-fixed | **partial regression** | `FIND-data-022` — `AnalysisJobActor.cs:244` still uses `"ConceptAttempted_V1"` |
| FIND-data-009 (QueryAllRawEvents) | verified-fixed | **fake-fix** | `FIND-data-023` — `StudentLifetimeStatsProjection` dead; 13 callsites still scan |
| FIND-arch-004 (canned tutor placeholder) | verified-fixed | **fake-fix** | `FIND-arch-025` — `ClaudeTutorLlmService` fake-streams a unary response |
| FIND-ux-011 (social swallow-and-smile) | partially pending | **partial regression** | `FIND-ux-024` — `social/index.vue` still drops errors; only `friends.vue`/`peers.vue` patched |
| FIND-ux-014 (hide-Hebrew outside Israel) | verified-fixed | **fake-fix** | `FIND-pedagogy-010` — cookie + onboarding picker bypass the build flag |
| FIND-pedagogy-001 (binary feedback) | verified-fixed | **fake-fix (dev path)** | `FIND-pedagogy-011` — MSW handler ships canned binary feedback |
| FIND-pedagogy-003 (linear +0.05 posterior) | verified-fixed | **fake-fix (dev path)** | `FIND-pedagogy-011` — MSW handler + `LearningSessionQueueProjection` dead-linear counter |
| FIND-pedagogy-006 (ScaffoldingService bypass) | verified-fixed | **partial-fix** | `FIND-pedagogy-012 / -016` — helper exists but `/hint` route not registered; REST queue never seeded |

### 1.2 — Regressions (4)

| ID | Lens | Sev | File | Reproduces | Task |
|---|---|---|---|---|---|
| FIND-sec-009 | sec | P1 | `src/actors/Cena.Actors.Host/Program.cs` (NATS fallback) | FIND-sec-003 | `t_7d460d39c14f` |
| FIND-sec-011 | sec | **P0** | `AdminUserService.cs`, `MasteryTrackingService.cs`, `MessagingAdminService.cs`, `GdprEndpoints.cs` | FIND-sec-005 | `t_1705a03eaaba` |
| FIND-data-022 | data | **P0** | `src/actors/Cena.Actors/Services/AnalysisJobActor.cs:244` | FIND-data-006 | `t_7a7cb4849130` |
| FIND-ux-024 | ux | **P0** | `src/student/full-version/src/pages/social/index.vue` | FIND-ux-011 | `t_47762c224fe9` |

### 1.3 — Fake-fixes (7)

| ID | Lens | Sev | Fake-fix of | What the fix lied about | Task |
|---|---|---|---|---|---|
| FIND-arch-025 | arch | **P0** | FIND-arch-004 | `ClaudeTutorLlmService` claims `HARDEN: No stubs` / streaming, but fake-streams a unary response | see arch report |
| FIND-data-023 | data | **P0** | FIND-data-009 | `StudentLifetimeStatsProjection` registered Inline but zero readers; reintroduces `DateTimeOffset.UtcNow` in `Apply` (reverts FIND-data-001); broken streak math | `t_c5be29342c1d` |
| FIND-pedagogy-010 | pedagogy | **P0** | FIND-ux-014 | `VITE_ENABLE_HEBREW=false` hides menu only; runtime cookie + onboarding picker bypass it | `t_f0cfa809cd67` |
| FIND-pedagogy-011 | pedagogy | **P0** | FIND-pedagogy-001 + -003 | MSW dev handler returns canned binary feedback + hardcoded `±0.05` mastery delta (`CANNED` literal) | `t_fb95d37042e7` |
| FIND-privacy-005 | privacy | **P0** | "GDPR Art 17 right to erasure" | `RightToErasureService.ProcessErasureAsync` is dead code (zero callers); only deletes consent + audit rows; never touches event stream, snapshot, or tutor history; log line "Records anonymized" is a lie | `t_601136dd9b19` |
| FIND-privacy-007 | privacy | **P0** | "GDPR Art 7 consent + GDPR Art 6 lawful basis" | `GdprConsentManager.HasConsentAsync` is defined but called by no data processor; `settings/privacy.vue` toggles are localStorage-only; consent UI is cosmetic | `t_d2be304a10fe` |
| FIND-qa-001 | qa | **P0** | "FIND-sec-001 has a 18-assertion SQLi regression suite" | The suite is in the repo but never executes in `.github/workflows/backend.yml`; dead CI. Structural regression guard does not fire on the merge gate. | `t_e1485b61506e` |

### 1.4 — Partial re-opens (pedagogy)

| ID | Re-opens | Sev | Task |
|---|---|---|---|
| FIND-pedagogy-012 | FIND-pedagogy-006 (Scaffolding DTO never populated) | P0 | `t_db9eaa46f096` |
| FIND-pedagogy-013 | FIND-pedagogy-001 (Explanation monolingual, ignores student locale) | P0 | `t_d36e5f09a241` |
| FIND-pedagogy-016 | FIND-pedagogy-006 root cause (REST queue never seeded; `/current-question` returns "Session completed" on first call) | P1 | `t_36ad75f9a484` |
| FIND-pedagogy-017 | FIND-pedagogy-001 (AnswerFeedback.vue:87 renders English server string alongside translated heading) | P1 | `t_b8d4df8b2911` |
| FIND-pedagogy-018 | FIND-pedagogy-003 (dead linear counter still in `LearningSessionQueueProjection`) | P2 | — |

**Trust cost**: 4 regressions + 7 fake-fixes + 5 partial re-opens = **16
items where the v1 review reported "done" but the bug is still live.**
The "quality over speed" rule applies: every item here MUST be closed by
a real fix with a CI-wired regression test before the next v3 review.

---

## Part 2 — Counts by lens and by framework

### 2.1 — Severity by lens

| Lens | P0 | P1 | P2 | P3 | Total new | Enqueued (P0+P1) | Regr. | Fake |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| arch | 4 | 5 | 4 | 1 | 14 | 9 | 0 | 1 |
| sec | 4 | 4 | 2 | 0 | 10 | 8 | 2 | 0 |
| data | 4 | 5 | 4 | 1 | 14 | 9 | 1 | 1 |
| pedagogy | 4 | 4 | 2 | 0 | 10 | 8 | 0 | 2 |
| ux | 6 | 9 | 4 | 1 | 20 | 15 | 1 | 0 |
| privacy | 7 | 7 | 4 | 1 | 19 | 14 | 0 | 2 |
| qa | 4 | 5 | 4 | 1 | 14 | 10 | 0 | 1 |
| **total** | **33** | **39** | **24** | **4** | **100** | **73** | **4** | **7** |

### 2.2 — Compliance status by framework (privacy lens)

| Framework | Controls checked | PASS | PARTIAL | FAIL | FAKE |
|---|---:|---:|---:|---:|---:|
| COPPA (16 CFR Part 312) | 5 | 0 | 0 | 5 | 0 |
| GDPR-K + GDPR core articles | 7 | 0 | 1 | 4 | 2 |
| UK ICO Children's Code (15 standards) | 15 | 0 | 3 | 8 | 1 (+3 N/A) |
| FERPA | 3 | 0 | 0 | 3 | 0 |
| Israel Privacy Protection Law 5741-1981 | 2 | 0 | 0 | 2 | 0 |
| **total** | **32** | **0** | **4** | **22** | **3** (+3 N/A) |

Zero passing controls. Every framework in scope has at least one P0 gap.

### 2.3 — WCAG 2.2 AA (ux lens)

| Page | Lighthouse a11y (target ≥95) | axe violations |
|---|---:|---:|
| Admin login | 84 | 4 |
| Admin dashboard | 84 | 4 |
| Student login | 90 | 5 |
| Student register | 90 | 5 |
| Student home | 90 | 5 |
| Student forgot-password | 91 | 5 |
| Mobile viewport (student) | — | 5 |

Every page fails the target. Top recurring violations: `color-contrast`
on `text-primary` (`#7367F0` on white/dark — **fix via usage pattern only,
palette locked**), `aria-prohibited-attr` on the `<div>` profile avatar,
`landmark-one-main` on auth pages, `target-size` on 16×16 eye icon.

### 2.4 — Test coverage (qa lens)

| Surface | Tests | In CI | Playwright | Note |
|---|---:|---|---|---|
| `Cena.Actors.Tests` | 972 | yes | — | 1-in-19 flake observed (likely wall-clock) |
| `Cena.Admin.Api.Tests` | 376 | yes | — | explicit CI restore workaround |
| `Cena.Infrastructure.Tests` | 18 | **NO** | — | **dead CI** (FIND-qa-001) — SQLi regression guard |
| Admin web vitest | 1 file / 25 assertions | yes | — | template tag balance only |
| Admin web Playwright | 0 | no | **no config** | no E2E on admin at all |
| Student web vitest | 43 files | yes | — | unit |
| Student web Playwright | 16 files | yes | yes | E2E |
| Mobile (Flutter) | 1 file | no | — | presence only |

Total .NET tests on disk: **1,366**. Total .NET tests in CI: **1,348**.
Gap = 18 SQLi assertions = the regression suite for `FIND-sec-001`.

---

## Part 3 — Top 10 by impact (privacy 10× legal multiplier)

Ranked by blast radius × fix cost × legal exposure. Privacy lens findings
apply a 10× multiplier on legal exposure per the v2 protocol.

| # | ID | Lens | Sev | Headline | Task |
|---:|---|---|---|---|---|
| 1 | FIND-privacy-001 | privacy | P0 | Cena collects PII from minors with zero age verification, no parental consent flow, no DOB field, no privacy policy at collection. COPPA §312.4+.5, GDPR Art 8, ICO Std 7+11, Israel PPL §11 all failing. | `t_4c77eb4b3436` |
| 2 | FIND-privacy-008 | privacy | P0 | Tutor chat sends child free-text to `api.anthropic.com` with no DPA, no PII scrubbing, no safeguarding escalation path, no consent gate. A child disclosing self-harm hits Claude with no ingress. GDPR Art 28. | `t_8bba4bf4ad8f` |
| 3 | FIND-privacy-005 | privacy | P0 | **Fake-fix**: `RightToErasureService` is dead code; even when called only deletes consent + audit rows. GDPR Art 17. ICO Std 15. | `t_601136dd9b19` |
| 4 | FIND-sec-010 | sec | P0 | Privilege escalation: any school admin can POST `/api/admin/users/{id}/role` to promote themselves to SUPER_ADMIN (endpoint is `AdminOnly`, service has no SUPER_ADMIN gate). | `t_d4524ac529cd` |
| 5 | FIND-sec-008 | sec | P0 | Cross-tenant write across entire admin user-management surface: any school admin can read PII / suspend / delete / force-reset / revoke sessions for any user in any other school. | `t_074e96ac9059` |
| 6 | FIND-arch-017 | arch | P0 | Student-web MSW fake-api worker is loaded unconditionally in production builds (13 handler categories). Header comment claims "Production bypasses MSW entirely" — it doesn't. | see arch report |
| 7 | FIND-data-023 | data | P0 | **Fake-fix** of FIND-data-009: `StudentLifetimeStatsProjection` is dead code; reintroduces `DateTimeOffset.UtcNow` in `Apply` (reverts FIND-data-001); broken streak math. | `t_c5be29342c1d` |
| 8 | FIND-pedagogy-011 | pedagogy | P0 | **Fake-fix** of FIND-pedagogy-001 + -003 in the dev MSW surface (every dev and contributor sees canned binary feedback + linear mastery `CANNED` literal). | `t_fb95d37042e7` |
| 9 | FIND-data-020 | data | P0 | Rate-limit policies are global (unpartitioned `AddFixedWindowLimiter`); one runaway script can drain the platform LLM budget in seconds. Worst-case ~$19.4k/month from a single attacker. | `t_3fffc5d5baad` |
| 10 | FIND-arch-018 | arch | P0 | `NotificationChannelService` Web Push / Email / SMS dispatch are all stubs that log "Would send..." and return `true`. The system reports 100% delivery while no notification is sent. | see arch report |

Runner-up (would be #11): **FIND-privacy-007** — `GdprConsentManager.HasConsentAsync` never called by any data processor (consent is cosmetic).

---

## Part 4 — Cross-lens causal chains

The 73 findings cluster into 5 causal chains. Fixing the root of each
chain collapses multiple children.

### Chain A — "The MSW dev path diverges from the real backend"
- Root: `FIND-arch-017` (MSW worker loaded unconditionally in prod)
- Children: `FIND-pedagogy-011` (canned binary feedback + linear mastery),
  `FIND-ux-021` (MSW 404 leaks raw error string untranslated),
  `FIND-ux-024` (social mock hardcodes `newCount: 1`)
- Root-cause fix: gate the MSW worker on `import.meta.env.DEV` AND build-time
  env, delete `mockServiceWorker.js` from the public dir, remove the
  `.gitignore` comment that allows it in. Contributor-facing feedback and
  mastery then surface real backend state.

### Chain B — "Tenant isolation assumes one file, not a pattern"
- Root: missing reusable `IRequireSchoolScope<T>` abstraction
- Children: `FIND-sec-008` (admin user mgmt), `FIND-sec-010` (role promotion),
  `FIND-sec-011` (mastery/messaging/GDPR cross-tenant writes)
- Root-cause fix: introduce a `TenantScopedQueryFactory` that every Admin
  service consumes; guard with an architectural unit test that fails the
  build if any service queries `IQuerySession` without filtering by
  `SchoolId`.

### Chain C — "No test + no alert means regression is invisible"
- Root: `FIND-qa-001` (SQLi suite in dead CI) + `FIND-sec-014` (zero
  metrics/alerts on the security-critical paths v1 fixed)
- Children: every one of the 4 regressions + 7 fake-fixes in Part 1 —
  none of them would have fired a failing CI job or a production alert
- Root-cause fix: wire `Cena.Infrastructure.Tests` into `backend.yml`;
  add 1 Prometheus counter per closed `FIND-sec-*` error path; add one
  alert per counter.

### Chain D — "Compliance is labelled on, gated off"
- Root: `FIND-privacy-005` + `FIND-privacy-007` (fake erasure + fake consent)
- Children: `FIND-privacy-001/-002/-003/-004/-009/-010`
- Root-cause fix: consent becomes a first-class preflight on every data
  processor; erasure becomes an aggregate operation on the Marten event
  stream, not a SQL delete on two tables.

### Chain E — "AdaptiveQuestionPool is wired to the actor but not to REST"
- Root: `FIND-pedagogy-016` (REST queue never seeded; `/current-question`
  returns "Session completed" on first call)
- Children: `FIND-pedagogy-012` (Scaffolding DTO empty), `FIND-arch-018`
  (notification stubs), every student-visible "I clicked Start and nothing
  happened" finding
- Root-cause fix: register `AdaptiveQuestionPool` in
  `Cena.Student.Api.Host/Program.cs`, seed the queue on `POST /sessions`,
  populate scaffolding DTO on every `GET /current-question`.

---

## Part 5 — Enqueued task IDs (73 items)

All IDs are in `.agentdb/kimi-queue.db` with `status=pending`, `assignee=unassigned`.

**arch** (9): `t_37119818c91a`, `t_25df87c51509`, `t_60bf2c15d4cc`,
`t_017eed8be44b`, `t_3c0bbeea2124`, `t_7f3d9fcf1b56`, `t_766c5582f2a2`,
`t_a2aef6aa1112`, `t_b62dff440b61`

**sec** (8): `t_074e96ac9059`, `t_7d460d39c14f`, `t_d4524ac529cd`,
`t_1705a03eaaba`, `t_417c37caf3fb`, `t_18c6b8a10695`, `t_c449d12b59af`,
`t_ba8a2aa46b59`

**data** (9): `t_3fffc5d5baad`, `t_b224f213658a`, `t_7a7cb4849130`,
`t_c5be29342c1d`, `t_731b808f3ad1`, `t_d460e84481fa`, `t_8eec1c3c7039`,
`t_7cc75a600edf`, `t_503e56126008`

**pedagogy** (8): `t_f0cfa809cd67`, `t_fb95d37042e7`, `t_db9eaa46f096`,
`t_d36e5f09a241`, `t_9e338d79571a`, `t_1bec032a8290`, `t_36ad75f9a484`,
`t_b8d4df8b2911`

**ux** (15): `t_89e7d3c33286`, `t_bcba38da3393`, `t_2a3d67a71b2f`,
`t_9236e6014b58`, `t_9b19a52d72df`, `t_47762c224fe9`, `t_da7affaf863c`,
`t_c92f6a542e75`, `t_4b64574b8e08`, `t_e74d3e4c9ee0`, `t_75bfec46c698`,
`t_ac2f39927d61`, `t_169f384cdae3`, `t_6bcdeac9aa0e`, `t_43a02f11c2b4`

**privacy** (14): `t_4c77eb4b3436`, `t_44b059e06deb`, `t_2d8e5ce4037f`,
`t_a852a034cd2a`, `t_601136dd9b19`, `t_d2be304a10fe`, `t_8bba4bf4ad8f`,
`t_2ad633abfa65`, `t_ee705ad16927`, `t_f8ad584a9fb3`, `t_1484495513fe`,
`t_ec5f4fe6d4a1`, `t_1245132fe3e4`, `t_fc830c05b406`

**qa** (10): `t_e1485b61506e`, `t_bdf505599872`, `t_4243ac31521f`,
`t_644582f3aea7`, `t_25b60d65bbb2`, `t_d2688b9bc6fb`, `t_07b82353c70d`,
`t_8387e7d8fecf`, `t_2205e5d2b53f`, `t_d356ef113d23`

---

## Part 6 — Observability coverage matrix

(Excerpt from sec lens, `FIND-sec-014`.)

| Prior finding | Has structured log on error path? | Has Prometheus counter? | Has alert rule? |
|---|:-:|:-:|:-:|
| FIND-sec-001 (SQLi) | no | no | no |
| FIND-sec-002 (CORS) | no | no | no |
| FIND-sec-005 (tenant bypass) | partial (SchoolId attached) | no | no |
| FIND-data-001 (determinism) | no | no | no |
| FIND-data-009 (full-scan) | no | no | no |
| FIND-pedagogy-003 (BKT) | no | no | no |
| FIND-arch-004 (real tutor LLM) | yes (ILogger) | **yes** (`AiGenerationService`) | no |
| All other 48 | no | no | no |

Only 1 of 55 closed findings has a metric on its error path. A silent
re-regression of any of the other 54 in production would not be
detectable inside any alerting window.

---

## Part 7 — Pending pre-existing items passed through (2)

Not discovered by this run; verified still open; pass through to `ux`
coder-agent follow-up:

- `FIND-ux-011 + FIND-ux-012` (t_6b1c4fbe96d2) — now partially
  regressed by `FIND-ux-024` in `social/index.vue`.
- `FIND-ux-006c` (t_f7bb146a546b) — student `forgot-password.vue` rewrite
  to consume the new backend endpoint.

---

## Part 8 — REV-001 passthrough

`REV-001` (Firebase service-account key rotation) is **verified still
pending**. No new task enqueued per v2 protocol; referenced here for
traceability. The rotation is a manual GCP console operation and is a
hard gate before the first production deployment.

---

## Part 9 — Scope gaps (admitted)

The agents could not do everything live. What was deferred:

- **Postgres + NATS + Redis down on workstation** — sec, data, qa had no
  live DB/queue to probe. All of those lenses' findings are backed by
  grep + file:line + dotnet test on `cc3f702`. EXPLAIN ANALYZE, live
  projection rebuild timings, and NATS integration tests were filed as
  `NOT_RUN` with reason.
- **Admin credentials not available** — ux could not drive the admin
  dashboard behind real Firebase auth. Admin Lighthouse scores reflect
  the `/login` bounce. Admin dashboard live-audit deferred.
- **Marten integration tests requiring a live DB** — qa classified 35 of
  the 55 prior findings as `NOT_RUN` rather than fabricating a verdict.
- **Chrome DevTools MCP profile contention** — pedagogy used Playwright
  instead of Chrome DevTools for Hebrew/Arabic geo-spoofed runs; ux
  used Playwright throughout.

None of these gaps softens any P0/P1 finding — each finding above has
static evidence sufficient to support the verdict.

---

## Part 10 — Recommendations for the next wave

1. **Before claiming any new P0/P1 as "done"**, require:
   - (a) a CI-wired regression test that fails on the parent commit,
   - (b) a structured log line on the error path, and
   - (c) a Prometheus counter wrapping (b).
2. **Merge `Cena.Infrastructure.Tests` into `backend.yml` today.**
   This one workflow change closes the most dangerous CI gap.
3. **Freeze new features** on the admin user-management surface until
   the tenant-scoping abstraction from Chain B lands. The current
   surface is a P0 data-leakage and a privilege-escalation path.
4. **Privacy has no quick fix** — no age gate, no parental consent, no
   privacy policy, no DPA, no DPIA. Treat `FIND-privacy-001..010` as a
   compliance program, not a bug backlog. Legal review required before
   any public launch.
5. **The dev MSW path** has to stop shipping "canned" constants that
   contradict the real backend. Either it stubs realistic data matching
   the server contract, or it's gated to pass through to the live API.

---

## Part 11 — Artifacts

- Phase 0 preflight: [docs/reviews/reverify-2026-04-11-preflight.md](reverify-2026-04-11-preflight.md)
- Per-lens reports:
  - [docs/reviews/agent-arch-reverify-2026-04-11.md](agent-arch-reverify-2026-04-11.md)
  - [docs/reviews/agent-sec-reverify-2026-04-11.md](agent-sec-reverify-2026-04-11.md)
  - [docs/reviews/agent-data-reverify-2026-04-11.md](agent-data-reverify-2026-04-11.md)
  - [docs/reviews/agent-pedagogy-reverify-2026-04-11.md](agent-pedagogy-reverify-2026-04-11.md)
  - [docs/reviews/agent-ux-reverify-2026-04-11.md](agent-ux-reverify-2026-04-11.md)
  - [docs/reviews/agent-privacy-reverify-2026-04-11.md](agent-privacy-reverify-2026-04-11.md)
  - [docs/reviews/agent-qa-reverify-2026-04-11.md](agent-qa-reverify-2026-04-11.md)
- UX screenshots: `docs/reviews/screenshots/reverify-2026-04-11/ux/`
- Privacy screenshots: `docs/reviews/screenshots-privacy/`
- Prior merged v1 report: [docs/reviews/cena-review-2026-04-11.md](cena-review-2026-04-11.md)

## Definition of done (this run)

- [x] Phase 0 preflight written
- [x] 7 lens agent reports written
- [x] Regression + fake-fix table first (Part 1)
- [x] 73 tasks enqueued (33 P0 + 39 P1 + 1 note: privacy had 7 P0 and 7 P1 per return but the YAML header in the report said 6 P1; 14 total privacy enqueued matches the 14 task IDs)
- [x] Compliance matrix per framework
- [x] Test coverage matrix
- [x] Top 10 ranked with privacy 10× multiplier
- [x] Cross-agent causal chains linked
- [ ] Coordination topic message posted (next)
- [ ] All 7 review worktrees removed (after merge to main)
- [ ] 7 agent branches merged to main (after this file is written)

Coordinator: `claude-code`
