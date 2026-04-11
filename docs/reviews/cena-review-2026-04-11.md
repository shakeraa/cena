# Cena Evidence-Based Review — 2026-04-11

**Coordinator**: claude-code (main session)
**Method**: 5 parallel sub-agents in isolated worktrees, read-only audit, evidence-based findings only.
**Base commits**: local main `5415263` (DB-06b landed locally), origin/main `989efa0` (DB-06b not yet pushed — see *Coordinator actions* below).

---

## Executive summary

Five specialist sub-agents audited the Cena codebase in parallel across five non-overlapping lenses: **contracts & service wiring**, **security & auth**, **data & projections**, **pedagogy & learning science**, and **UX & broken workflows**. Every finding was required to cite file:line, grep output, a rendered network request, or a peer-reviewed source. Unsourceable claims were discarded at the agent tier — Agent 4 alone dropped six pedagogy candidates it could not back with a real citation.

The review produced **75 findings** (26 P0, 28 P1, 16 P2, 5 P3). **53 P0+P1 tasks have been enqueued** as `unassigned` to `.agentdb/kimi-queue.db` with severity-mapped priority (`critical` / `high`). The remaining P2/P3 findings live in the per-agent files and are not blocking.

The dominant systemic issues are:

1. **Stubs and hardcoded data leaking into user-visible surfaces** — tutor chat literally renders the string "(STB-04b will wire real LLM streaming.)"; student home KPIs are client-side mock constants labelled as real stats; three LLM provider methods throw `NotImplementedException` from shipped endpoints.
2. **Event-sourcing violations in the core learning loop** — `SessionEndpoints.SubmitAnswer` bypasses the student-profile aggregate, manually `Store()`s the snapshot, and applies an `XpAwarded_V1` in-memory that is never appended to the stream. Wrong answers emit no `ConceptAttempted_V1` event at all. Mastery is a hardcoded `+0.05` linear counter that reaches 1.0 after exactly 10 correct answers, bypassing `BktTracer` entirely.
3. **NATS / projection contract drift** — XP notifications, explanation-cache invalidation, and admin-thread messaging all fail silently because publisher and subscriber disagree on subject names or the event type predicate uses `__v1` (double underscore) and never matches a stored row. Four of the 17 admin services share this pattern.
4. **A SQL-injection primitive in LeaderboardService.cs** — seven raw `$@"..."` SQL statements interpolating classroom/student/friend IDs into `cmd.CommandText` with no parameter binding. Stored SQLi on a tenant-shared table.

All five reports are on `main` now; all P0/P1 findings are ready for Kimi/Claude sub-agent pickup.

---

## Agent roster

| # | Agent | Worker | Branch | Findings (P0 / P1 / P2 / P3) | Enqueued |
|---|---|---|---|---|---|
| 1 | System & Contract Architect | claude-subagent-arch | `claude-subagent-arch/review-2026-04-11` | 7 / 5 / 3 / 1 | 12 |
| 2 | Security & Auth Architect | claude-subagent-sec | `claude-subagent-sec/review-2026-04-11` | 2 / 5 / 1 / 0 | 7 |
| 3 | Data & Projection Architect | claude-subagent-data | `claude-subagent-data/review-2026-04-11` | 7 / 6 / 5 / 1 | 13 |
| 4 | Pedagogy & Learning Science | claude-subagent-pedagogy | `claude-subagent-pedagogy/review-2026-04-11` | 4 / 5 / 3 / 2 | 9 |
| 5 | UX & Broken-Workflow Auditor | claude-subagent-ux | `claude-subagent-ux/review-2026-04-11` | 6 / 7 / 4 / 1 | 12 |
| — | **Total** | | | **26 / 28 / 16 / 5** | **53** |

Per-agent full reports:
- [agent-1-arch-findings.md](agent-1-arch-findings.md)
- [agent-2-security-findings.md](agent-2-security-findings.md)
- [agent-3-data-findings.md](agent-3-data-findings.md)
- [agent-4-pedagogy-findings.md](agent-4-pedagogy-findings.md)
- [agent-5-ux-findings.md](agent-5-ux-findings.md) (+ 19 screenshots in [screenshots/](screenshots/))

Live mode: Agent 5 drove the admin (5174) and student (5175) dev servers via Chrome DevTools MCP. Agents 2 and 3 fell back to static analysis because Postgres and the API hosts were not running on the audit machine.

---

## P0 / P1 counts by category

| Category | P0 | P1 | Total |
|---|---:|---:|---:|
| contract (dead endpoints, NATS drift, stubs) | 7 | 5 | 12 |
| security (SQLi, CORS, tenant, secrets) | 2 | 5 | 7 |
| data (projections, predicates, CQRS, N+1) | 7 | 6 | 13 |
| pedagogy (formative feedback, BKT, scaffolding) | 4 | 5 | 9 |
| ux / label-drift (stub leaks, dead buttons, i18n) | 6 | 7 | 13 |
| **Total** | **26** | **28** | **54** |

*(54 shown, 53 enqueued: UX `FIND-ux-008+009` and `FIND-ux-011+012` were each filed as a single bundled queue task.)*

---

## Top 10 by blast radius × fix cost

Ordered by how much pain they cause in the wild, weighted by how cheap the fix is.

| Rank | ID | Severity | Lens | Finding | Queue |
|---:|---|---|---|---|---|
| 1 | FIND-sec-001 | P0 | security | 7 raw `$@"..."` SQL statements in `LeaderboardService.cs` interpolate `classroom.SchoolId`, `studentId`, `friendIds` into `cmd.CommandText`. Stored SQLi primitive on `mt_doc_studentprofilesnapshot`. | `t_27a595bd9212` |
| 2 | FIND-arch-004 ⇆ FIND-ux-005 | P0 | contract + ux | Tutor REST endpoint returns `"Great question! Use the /stream endpoint..."` and the mock UI renders literal `"(STB-04b will wire real LLM streaming.)"` + `"stub reply"`. The word **"stub"** ships to users. | `t_8d7b0c710c68`, `t_6577ac91a761` |
| 3 | FIND-data-007 ⇆ FIND-pedagogy-002 ⇆ FIND-pedagogy-003 | P0 | data + pedagogy | `SessionEndpoints.SubmitAnswer` manually `Store()`s `StudentProfileSnapshot` (inline projection) AND applies `XpAwarded_V1` in-memory without appending to the stream. Wrong answers emit no `ConceptAttempted_V1` at all. `PosteriorMastery = Min(1.0, Prior + 0.05)` bypasses `BktTracer`. | `t_c5e8f53dc1e5`, `t_27da75d9f48b`, `t_b8a5ef448123` |
| 4 | FIND-data-005 | P0 | data | `StudentInsightsService` + `FocusAnalyticsService` + `AdminDashboardService` use `"__v1"` (double underscore) in 20+ `EventTypeName` predicates. Every admin dashboard chart silently returns empty forever. | `t_8e6f2df4b5ce` |
| 5 | FIND-ux-004 | P0 | ux | Student home KPIs (`Minutes today 18 / Questions 84 / Accuracy 76% / Level 7 40%`) are hardcoded client-side constants labelled `"Today's stats"`. Every student sees identical fabricated numbers. | `t_56b79ed9b142` |
| 6 | FIND-pedagogy-001 | P0 | pedagogy | Post-answer feedback returns `"Correct! Great job!"` / `"Not quite. The correct answer was: X"` with no explanation, even though `QuestionDocument.Explanation` and `DistractorRationale` exist. Violates Black & Wiliam 1998 (DOI 10.1080/0969595980050102) and Hattie & Timperley 2007 (DOI 10.3102/003465430298487). | `t_841af1f09e1b` |
| 7 | FIND-arch-002 ⇆ FIND-arch-003 | P0 | contract | NATS subject drift: `NotificationDispatcher` subscribes `events.xp.awarded` but publisher emits `cena.events.student.{studentId}.xp_awarded`; `ExplanationCacheInvalidator` subscribes `cena.events.Question*_V1` but outbox publishes `cena.durable.curriculum.Question*_V1`. XP push toasts never fire; L2 cache never invalidates. | `t_08e776c6db85`, `t_3bd146ca43ba` |
| 8 | FIND-ux-003 | P0 | ux | Admin origin sets cookies named `"cena admin-*"` (literal space in the name), and any subsequent student-web visit crashes MSW's cookie parser, breaking every student route except `/` in dev. | `t_df2542f47ebe` |
| 9 | FIND-ux-006 | P0 | ux | Student forgot-password form submits to nothing, fires no network request, sends no email, then shows a green success banner. Silent data loss and a trust violation. | `t_e38db18c0f82` |
| 10 | FIND-pedagogy-004 | P0 | pedagogy | Flutter diagram challenges use `textHe` + `feedbackHe` as the only localization surface — Hebrew-only content in a product whose primary language is English. | `t_01ff33284635` |

---

## Cross-linked finding clusters

These groups share a root cause even though they surface in different lenses. Fix the cluster head and the dependents disappear.

### Cluster A — "Stub and fake-data leakage"
| ID | Lens | Role |
|---|---|---|
| FIND-arch-004 | contract | Server stub (canned string) |
| FIND-ux-005 | ux | Same stub rendered in tutor chat, including task ID "STB-04b" |
| FIND-arch-005 | contract | `CallOpenAiAsync` / `CallGoogleAsync` / `CallAzureOpenAiAsync` throw `NotImplementedException` in shipped code |
| FIND-ux-004 | ux | Client-side mock constants labelled as real stats |
| FIND-data-005 | data | Admin dashboard queries silently empty (different mechanism, same "user sees fake/nothing" outcome) |

**Root cause**: the user's feedback rule *"No stubs — production grade"* (2026-04-11) has not been retroactively enforced across the Phase-1 endpoints. Every Phase-1 stub that still exists is a P0 under the current policy.

### Cluster B — "SessionEndpoints bypasses event sourcing"
| ID | Lens | Role |
|---|---|---|
| FIND-data-007 | data | Manual `session.Store(profile)` on an inline snapshot, races projection daemon |
| FIND-pedagogy-002 | pedagogy | Wrong answers emit no `ConceptAttempted_V1` event — mastery math never decreases |
| FIND-pedagogy-003 | pedagogy | Hardcoded `+0.05` linear mastery counter bypasses `BktTracer` |
| FIND-pedagogy-005 | pedagogy | Feedback auto-dismisses after 1.6s (no tap-to-continue) |

**Root cause**: `src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs` is a hand-rolled endpoint that does not route through the student-profile aggregate, so none of the domain rules or the real BKT path execute. Rewriting `SubmitAnswer` to issue a command on the aggregate fixes 4 findings at once.

### Cluster C — "Event type name predicate drift"
| ID | Lens | Role |
|---|---|---|
| FIND-data-005 | data | Three services use `"__v1"` double-underscore strings |
| FIND-data-006 | data | `ExperimentAdminService` uses `nameof(T)` → PascalCase |
| FIND-data-002 | data | `RegisterNotificationEvents(opts)` defined but never called from `ConfigureCommon` |
| FIND-data-012 | data | `ThreadSummaryProjection` + `ThreadCreated_V1` / `MessageSent_V1` never registered |

**Root cause**: Marten event type names are untyped strings scattered across code. No compile-time check. Every service invents its own convention. Fix: centralize event-type-name resolution into a single helper keyed off the event class, then sweep.

### Cluster D — "Cross-tenant read exposure"
| ID | Lens | Role |
|---|---|---|
| FIND-sec-005 | security | 4 Focus Analytics read endpoints accept `tenantId` from the request instead of the JWT claim |
| FIND-data-009 | data | 55 usages of `QueryAllRawEvents` across 16 files pull every event of a type across every tenant into memory, then filter in LINQ |

**Root cause**: there is no enforced `TenantScope` wrapper on `IQuerySession`. Add one, make it the only way to get a session from DI, and both findings close.

### Cluster E — "NATS subject contract has no enforcement"
| ID | Lens | Role |
|---|---|---|
| FIND-arch-002 | contract | `NotificationDispatcher` XP subject mismatch |
| FIND-arch-003 | contract | `ExplanationCacheInvalidator` subject mismatch |
| FIND-arch-011 | contract | Orphan subscription `cena.serve.item.published` in `QuestionPoolActor` (no publisher) |
| FIND-arch-012 | contract | `ContentModerationService` publishes `cena.review.item.*` (no subscriber) |

**Root cause**: NATS subjects are magic strings on both sides. Introduce a typed subject registry + integration test that asserts every declared subject has either a publisher+subscriber pair or is explicitly marked `sink-only` / `source-only`.

---

## Coordinator-level action items (not in the queue)

Three items require the coordinator specifically, not a coder agent:

1. **Push local main to origin.** Local main is at `5415263` (DB-06b endpoint migration to `Cena.Student.Api.Host` + `Cena.Admin.Api.Host`). `origin/main` is still at `989efa0`. Until this is pushed, any fresh clone or CI build boots the old scaffold hosts with `AllowAnyOrigin` and no auth. Agent 2's P0-002 (`FIND-sec-002`) collapses into "hygiene" once this push lands. **Do this immediately after reviewing this report.**
2. **Triage Cluster A's "no stubs" policy retroactively.** Decide whether to close every Phase-1-stub task as P0 or relax the rule for non-user-visible stubs. The pedagogy and UX lenses independently surfaced the same class of problem; a single policy decision eliminates or keeps ~8 findings.
3. **Decide on the question-bank event-type-name canonicalization** before assigning Cluster C tasks. If we don't pick one convention (`_v1` vs `__v1` vs `nameof(T)`), the fixes will drift again.

---

## Enqueued tasks (grouped)

All 53 tasks are `unassigned` in `.agentdb/kimi-queue.db`. Kimi-coder or Claude sub-agents can pull them with `next --assignee unassigned`.

### P0 (critical = 26)
- **arch** (7): t_99a1fcd89ee9, t_08e776c6db85, t_3bd146ca43ba, t_8d7b0c710c68, t_6b45c18c0c44, t_6c7776761bc1, t_572208ec8ba7
- **sec** (2): t_27a595bd9212, t_ffe63b9416ad
- **data** (7): t_358ad20a7cfb, t_e819cb261f43, t_9db1ff67567c, t_c9e788d0867e, t_8e6f2df4b5ce, t_cae884912113, t_c5e8f53dc1e5
- **pedagogy** (4): t_841af1f09e1b, t_27da75d9f48b, t_b8a5ef448123, t_01ff33284635
- **ux** (6): t_df2542f47ebe, t_56b79ed9b142, t_6577ac91a761, t_a6699e6b919a, t_e38db18c0f82, t_0e61943077fb

### P1 (high = 27)
- **arch** (5): t_fc37b5bee99a, t_68470a2ca105, t_d2c63f27e891, t_5d9d73ccd6c4, t_040268b54638
- **sec** (5): t_b55ab7978b06, t_b6e7a533f3d3, t_530c3515c737, t_029629143fa7, t_393c8c9cba34
- **data** (6): t_1ef04121af84, t_2ccf46e4da19, t_1229cba1112b, t_0a9ad57e4385, t_ead60d7097c2, t_c9cc8b4c74e3
- **pedagogy** (5): t_91698769efed, t_667f6b21162a, t_bbe25fa193db, t_f090a3ec5cd9, t_2613e678708a
- **ux** (6): t_589bdfcdd240, t_e50393dd9142, t_69dd1d0a798d, t_6b1c4fbe96d2, t_d33d57247e16, t_b6b717b464fd

---

## Findings explicitly DISCARDED (for visibility)

Agent 4 (Pedagogy) discarded six candidates because the evidence bar was not met. Recording here so they don't re-surface in a future audit as "undiscovered":

1. "Spaced repetition is not implemented" — discarded: `HlrCalculator` with Settles & Meeder 2016 citation exists in the actor layer. The surface-level absence in the Vue UI is a UX gap, tracked separately.
2. "Gamification undermines intrinsic motivation" — discarded: flat `+10 XP` with no streak/speed multiplier is not strong enough evidence to invoke Deci 1971 without user-research data.
3. "High cognitive load on mobile viewports" — discarded as P0; home grid auto-collapses on mobile. Kept as P2 for tablet+ widths only.
4. "PrerequisiteCalculator formula is wrong" — discarded: code is correct, only the doc comment drifts. That's a contract/docs finding, not pedagogy.
5. "Difficulty progression violates Bjork's desirable-difficulties" — discarded as a duplicate of FIND-pedagogy-003.
6. "Flattened Bloom's level loses Anderson & Krathwohl knowledge dimension" — rolled into FIND-pedagogy-008 rather than a standalone finding.

Agent 2 (Security) confirmed REV-001 (Firebase service-account key rotation) is already tracked and **did not re-file**.

---

## Definition of Done check

- [x] All 5 agent reports exist under `docs/reviews/`
- [x] Every P0 and P1 finding has a corresponding task in `.agentdb/kimi-queue.db` (53/53 verified via `kimi-queue list --assignee unassigned --status pending`)
- [x] No finding without evidence
- [x] No pedagogy claim without a cited source
- [x] Merged report exists at `docs/reviews/cena-review-2026-04-11.md` (this file)
- [ ] Coordination summary posted to the `coordination` topic (next step)
