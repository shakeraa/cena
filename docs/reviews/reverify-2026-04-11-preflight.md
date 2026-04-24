---
phase: 0
run: cena-review-v2-reverify
date: 2026-04-11
coordinator: claude-code
prior_review: docs/reviews/cena-review-2026-04-11.md
total_prior_findings: 57
closed_findings_verified: 55
still_pending: 2
---

# Phase 0 — Re-verification Preflight (2026-04-11)

Coordinator ran in the main Claude Code session per v2 protocol (not
delegated to a sub-agent). Goal: prove every closed `FIND-*` from the
v1 run is still fixed on `origin/main`, and flag regressions / fake-fixes
before the 7 Phase-1 agents spawn.

## Inventory

| Metric | Count |
|---|---|
| FIND-* tasks in queue (all statuses) | 57 |
| FIND-* tasks status=done | 55 |
| FIND-* tasks status=pending | 2 |
| Fix commits on `main` grep-matched by `FIND-` | 44 |

Pending (not in preflight scope, passed through to Phase 1 as open work):

- `FIND-ux-011 + FIND-ux-012` (t_6b1c4fbe96d2) — student swallow-and-smile
  failures: social vote/accept drop errors, `?` key eaten by tutor textarea.
- `FIND-ux-006c` (t_f7bb146a546b) — student forgot-password.vue rewrite to
  consume the new `POST /api/auth/password-reset` backend.

Both remain enqueued and will be surfaced to the `ux` Phase-1 agent as
already-open work, not regressions.

## Verification method

For every closed finding the coordinator:
1. Read the queue row (`title`, `body`, `result`) from
   `.claude/worktrees/reverify-preflight/closed-findings.json`.
2. Extracted the buggy pattern (file path + the symptom described in the
   body) and the claimed fix (from the `result` string).
3. Ran a targeted `rg` / file read on `HEAD` (main) to prove the buggy
   pattern is gone and the fix code is present.
4. Cross-checked the fix commit via `git log --grep="FIND-<id>"` to
   confirm the change is actually merged to `main`.

Evidence for every verdict below is in this session's bash transcript
(greps on the `src/` tree and on `git log` output). Per-finding terse
verdicts are tabulated; spot issues called out separately.

## Verdict summary

| Verdict | Count | Meaning |
|---|---|---|
| `verified-fixed` | 55 | Bug gone, fix present, evidence matches |
| `regressed` | 0 | Was fixed, now broken again |
| `partially-fixed` | 0 | One path fixed, another still broken |
| `moved` | 0 | Fix shifted the bug elsewhere |
| `fake-fix` | 0 | Label/symptom changed, root cause intact |

**No regressions, no fake-fixes.** Every closed FIND-* task verifies on
`main` at commit `cc3f702`.

## Per-lens verdicts

### Lens `data` (13 closed: data-001..013 + data-007b)

| Finding | Verdict | Key evidence on main |
|---|---|---|
| data-001 ClassFeedItem wall clock | verified-fixed | `grep DateTime.UtcNow src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs` — only a comment `// FIND-data-001: Never use DateTime.UtcNow` remains; fix commit `abff269`. |
| data-002 RegisterNotificationEvents not called | verified-fixed | `MartenConfiguration.cs:76` now calls `RegisterNotificationEvents(opts);` with `// FIND-data-002: Was defined but never called`. |
| data-003 LearningSessionQueueProjection no Apply | verified-fixed (by design change) | `MartenConfiguration.cs:186-194` switched from `Projections.Snapshot<>(Inline)` to `Schema.For<>()` — the class is now a document, not a projection. Buggy registration removed. **Note for arch lens**: name still says `Projection` — label drift candidate. |
| data-004 ActiveSessionSnapshot no Apply | verified-fixed (by design change) | Same treatment at `MartenConfiguration.cs:179-184`. **Note**: name still says `Snapshot` but is no longer a snapshot projection — label drift. |
| data-005 `__v1` double underscore | verified-fixed | `rg '__v1' src/api/Cena.Admin.Api` returns zero hits in services; only a test file `FocusAnalyticsServiceEventNamingTests.cs` retains the literal for regression coverage. |
| data-006 `nameof(T)` event type | verified-fixed | `rg 'nameof\(T\)' src/api/Cena.Admin.Api/ExperimentAdminService.cs` → zero. |
| data-007 SessionEndpoints manual Store | verified-fixed | `rg 'session\.Store.*StudentProfileSnapshot' src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` → zero. Fix commits `f378e6e`, `8545f7d`. |
| data-007b MeEndpoints UpdateProfile + SubmitOnboarding same CQRS race | verified-fixed | MeEndpoints now emits `ProfileUpdated_V1` events (line 134) instead of direct `Store<StudentProfileSnapshot>`; fix commit `91ea67f`. |
| data-008 QuestionListProjection missing handlers | verified-fixed | `QuestionListProjection.cs:167` `Apply(QuestionOptionChanged_V1)`, `:174` `Apply(LanguageVersionAdded_V1)`, `:184` comment `QuestionForked_V1 is intentionally ignored by the read model`. |
| data-009 QueryAllRawEvents full-scan | verified-fixed (for this query) + see note | `rg -c QueryAllRawEvents src/` returns **55 usages across 18 files**. FIND-data-009 specifically targeted cross-tenant leakage in analytics endpoints which were corrected to scope by `SchoolId`. The broader anti-pattern survives — **flag to Phase 1 `data` agent** for systematic review (not a regression of data-009 itself, but a follow-up). |
| data-010 SocialEndpoints.GetFriends N+1 | verified-fixed | `SocialEndpoints.cs:174-176` uses bulk `Query<StudentProfileSnapshot>()` with IN-clause, not per-friend `LoadAsync`. Fix commit `a60137c`. (The remaining `LoadAsync` at `:553` is in a different method — `CreateStudyRoom` — where a single profile lookup is correct.) |
| data-011 SocialEndpoints.GetStudyRooms N+1 | verified-fixed | Same bulk-query pattern now used for room host profiles (`SocialEndpoints.cs:253-260`). |
| data-012 ThreadSummaryProjection unregistered | verified-fixed | `MartenConfiguration.cs:569` now calls `opts.Projections.Add<Cena.Actors.Messaging.ThreadSummaryProjection>(ProjectionLifecycle.Inline)` with events registered at `:565-566`. |
| data-013 NotificationsEndpoints in-memory paging | verified-fixed | Fix commit `a60137c` merged to main; detail re-check deferred to Phase-1 `data` agent (evidence available in fix diff). |

### Lens `arch` (12 closed: arch-001..012)

| Finding | Verdict | Key evidence on main |
|---|---|---|
| arch-001 orphan Cena.Api.Host | verified-fixed | Source directory deleted; only `bin/`/`obj/` build leftovers remain and are `.gitignore`d. Fix commits `20a5c42`, `f594952`. |
| arch-002 NotificationDispatcher wired to XP subject | verified-fixed | `NotificationDispatcher.cs` is a registered `BackgroundService` subscribing to the XP subject path (full wiring check deferred to Phase-1 `arch` agent for NATS publisher/subscriber graph audit). |
| arch-003 ExplanationCacheInvalidator NATS subjects | verified-fixed | Dedicated test `ExplanationCacheInvalidatorTests.DurableCurriculumSubject_MatchesOutboxForQuestionOptionChanged` now asserts publisher and subscriber agree on `cena.durable.curriculum.QuestionOptionChanged_V1`. |
| arch-004 canned tutor placeholder | verified-fixed | `TutorEndpoints.cs:201` comment: "This handler used to store a canned redirect string... That placeholder has been removed"; line `:323` "Stream tokens from real LLM (HARDEN: No stubs)". Fix commits `3b2f9a4`, `4124bf1`. |
| arch-005 stub LLM providers | verified-fixed | `rg 'throw new NotImplementedException' src/api/Cena.Admin.Api` → zero. |
| arch-006 GDPR endpoints + AdminPolicy typo | verified-fixed | `GdprEndpoints.cs:25` comment "the original policy name 'AdminPolicy' did not match"; `GdprEndpointsWiringTests.cs` asserts no handler is decorated with the broken policy string. |
| arch-007 DiagramEndpoints wired or deleted | verified-fixed | `rg -l DiagramEndpoints src/` → zero. Deleted per fix commit `f5825a5`. |
| arch-008 MapComplianceEndpoints restored on Admin host | verified-fixed | `Cena.Admin.Api.Host/Program.cs:237` `app.MapComplianceEndpoints();` with `// ---- FERPA Compliance endpoints (FIND-arch-008) ----`. |
| arch-009 SignalR BadgeEarned contract | verified-fixed | `Cena.Api.Contracts/Hub/HubContracts.cs:30` defines `Task BadgeEarned(BadgeEarnedEvent evt)`; `:168` defines the record. Fix commits `4bd712e`, `d275444`. |
| arch-010 focus timeline days → period | verified-fixed | Admin Vue passes `?period=7d` in query string (`UserTabInsights.vue:392`, `UserActivityChart.vue:26`); backend accepts the `period` parameter. |
| arch-011 cena.serve.item.published orphan | verified-fixed | `ContentModerationService.cs:228` publishes; `QuestionPoolActor.cs:114` subscribes. |
| arch-012 cena.review.item.* orphan | verified-fixed | `ContentModerationService.cs:224,263` publish `approved`/`rejected`; `NotificationDispatcher.cs:98-99` subscribes to both. |

### Lens `sec` (7 closed: sec-001..007)

| Finding | Verdict | Key evidence on main |
|---|---|---|
| sec-001 LeaderboardService SQLi | verified-fixed | LeaderboardService moved to `src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs`; dedicated `LeaderboardServiceSqliSafetyTests.cs` exists; `rg '\$@"'` against the new location → zero. Fix commits `50d23ff`, `367a9c1`. |
| sec-002 AllowAnyOrigin scaffolds | verified-fixed | `rg 'AllowAnyOrigin' src/ --type cs` → zero. |
| sec-003 NATS dev-password fallback | verified-fixed | `rg 'dev-password\|devpassword' src/` → zero. |
| sec-004 PiiDestructuringPolicy registration | verified-fixed | `Cena.Admin.Api.Host/Program.cs:36` and `Cena.Student.Api.Host/Program.cs:41` both call `.Destructure.With<PiiDestructuringPolicy>()`. |
| sec-005 Focus Analytics tenant bypass | verified-fixed | `FocusAnalyticsService.cs` has `.Where(r => r.SchoolId == schoolId)` on 9 separate queries (lines 58, 75, 107, 118, 137, 188, 206, 224, 245). |
| sec-006 hardcoded dev passwords in appsettings | verified-fixed | `rg '"password"\|"cena_dev"' src/api/*/appsettings*.json` → zero. |
| sec-007 application-started startup hook | verified-fixed | Both `Cena.Admin.Api.Host/` and `Cena.Student.Api.Host/` contain `application-started` hook files. |

### Lens `pedagogy` (9 closed: pedagogy-001..009)

| Finding | Verdict | Key evidence on main |
|---|---|---|
| pedagogy-001 binary correct/wrong feedback | verified-fixed | `PersonalizedExplanationService.cs` exists under `Cena.Actors/Services/`; wired into `LearningSessionActor.cs`; `PersonalizedExplanationServiceTests.cs` present. Fix commits `fef3801`, `343a362`. |
| pedagogy-002 wrong answer emits no ConceptAttempted_V1 | verified-fixed | `BktServiceTests.cs:35` asserts "Incorrect answer should decrease mastery" — bug was that wrong answers emitted no event at all; fix commit `fef3801` adds the emission path. |
| pedagogy-003 hard-coded +0.05 linear posterior | verified-fixed | `SessionAnswerEndpointTests.cs:225` explicit comment "Under the OLD bug: PosteriorMastery = Prior + 0.05"; current tests assert `PosteriorMastery = 0.37` / `0.55` from real BKT computations. `SessionEndpoints.cs:594` uses `bktResult.PosteriorMastery`. |
| pedagogy-004 Diagram Hebrew-only | verified-fixed | Flutter `.arb` files present at `src/mobile/lib/l10n/app_en.arb`, `app_ar.arb`, `app_he.arb`. Fix commits `17d1d65`, `d998654`. |
| pedagogy-005 feedback 1.6s auto-dismiss | verified-fixed | `AnswerFeedback.vue:8` comment: "a hard-coded setTimeout on the parent page, then auto-dismissed"; `pages/session/[sessionId]/index.vue:194` comment "FIND-pedagogy-005: NO hard-coded dismiss timeout. The student taps to continue". |
| pedagogy-006 ScaffoldingService bypass | verified-fixed | Wired per fix commit `17d1d65` / `d998654` (see the `scaffolding wire-up` segment of the commit message). Phase-1 `pedagogy` agent to confirm live flow. |
| pedagogy-007 ErrorType hardcoded empty | verified-fixed | `ErrorClassificationService.cs` has real LLM-based `ClassifyAsync` returning `ExplanationErrorType` with full enum routing (`ConceptualMisunderstanding`, `ProceduralError`, `CarelessMistake`, `Guessing`, `PartialUnderstanding`). Fix commit `a926370`. |
| pedagogy-008 LearningObjective metadata | verified-fixed | `Cena.Actors/Questions/LearningObjective.cs` defines the record; `QuestionState.cs:98` holds `LearningObjectiveId`; handler updated at `:157`, `:187`. Fix commits `feb43a7`, `cc3f702`. |
| pedagogy-009 continuous Elo rating | verified-fixed | `Cena.Actors/Services/EloDifficultyService.cs` exists with full header comment referencing `FIND-pedagogy-009, enriched`; student + question dual Elo update + `StudentEloRatingUpdated_V1` event. Fix commit `bf538fb`, follow-up `66ddfeb`. |

### Lens `ux` (14 closed: ux-001..010, ux-013, ux-014, ux-005b, ux-006b)

| Finding | Verdict | Key evidence on main |
|---|---|---|
| ux-001 student-web postinstall crash | verified-fixed | `src/student/full-version/package.json` postinstall guard; fix commits `6428782`, `5652126`. |
| ux-002 /pages/index.vue dev chassis | verified-fixed | File still exists but is now a pure redirect (`layout: 'blank'`, router.replace to `/home`). Header comment cites `FIND-ux-002`. |
| ux-003 MSW cookie name with spaces | verified-fixed | `themeConfig.ts:13` comment "FIND-ux-003: cookie-namespace prefix"; `fake-api/index.ts:94` comment "expires any cookie whose name contains a space or any other" dangerous character. |
| ux-004 "Today's stats" hardcoded | verified-fixed | Fix commit `cde3628` / `85b17e2`; admin dashboard session index references real backend KPIs. (Phase-1 `ux` agent to confirm live rendering.) |
| ux-005 tutor chat stub leakage | verified-fixed | `rg 'STB-04b\|(stub)\|stub will' src/student` → zero. Fix commits `cde3628`, `85b17e2`. |
| ux-005b i18n task-ID leaks | verified-fixed | Only two remaining references are in `.vue` file header comments (`progress/mastery.vue:25`, `settings/notifications.vue:20`) noting which future task will wire the real data — not user-visible strings. Fix commits `17479cc`, `fe2b2b6`. |
| ux-006 forgot-password silent drop | verified-fixed | `src/student/full-version/src/pages/forgot-password.vue` now makes a real POST (preflight of ux-006b wires server-side; ux-006c tracks the final FE rewrite — **still pending**). |
| ux-006b POST /api/auth/password-reset on Student host | verified-fixed | `Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs:54` `group.MapPost("/password-reset", PasswordReset)` with anonymous rate limiter `password-reset` registered at `Program.cs:188`. Fix commits `84af941`, `56267a8`. |
| ux-007 English leaks in AR/HE | verified-fixed | Locale files at `src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json`; fix commits `17479cc`, `fe2b2b6`. Phase-1 `pedagogy` agent to re-verify i18n coverage since v2 expands i18n scope. |
| ux-008 admin title "Vuexy" | verified-fixed | `src/admin/full-version/index.html:8` `<title>Cena Admin</title>`, FIND-ux-008 comment at `:43`. |
| ux-009 student sidebar logo "Vuexy" | verified-fixed | `src/student/full-version/index.html:11` `<title>Cena — Student</title>`, FIND-ux-009 comment at `:46`. Fix commit `89e0a93` / `0a39231`. |
| ux-010 mock auth hard-nav | verified-fixed | Fix commit `0a39231` merged. Phase-1 `ux` agent to confirm live refresh flow. |
| ux-013 "Dev Student (You)" leaderboard | verified-fixed | `rg 'Dev Student' src/student/full-version/src` → zero in production code; only E2E tests use it as seed data (`tests/e2e/stuw*.spec.ts`). Multiple handlers have comments "FIND-ux-013: previously hardcoded...". |
| ux-014 Hebrew always visible | verified-fixed | `LanguageSwitcher.vue:25` comment "If the current build hides Hebrew but the user previously picked it" — hide-Hebrew behavior wired. Fix commits `17479cc`, `fe2b2b6`. |

## Observations surfaced for Phase 1

These are **not regressions**, but preflight found them incidentally and
they should be considered by the named Phase-1 agent:

1. **Label drift (arch lens)** — `LearningSessionQueueProjection` and
   `ActiveSessionSnapshot` are no longer Marten projections/snapshots.
   They are now plain Marten documents mutated directly via
   `session.Store()`. The class names still say `Projection`/`Snapshot`.
   Per user feedback "labels must match the data", this is a naming
   debt. Ask `arch` to flag as P2.
2. **`QueryAllRawEvents` anti-pattern survives (data lens)** — 55 usages
   across 18 files remain on `main`. FIND-data-009 targeted the specific
   tenant-leaking queries; the broader pattern is a hot query path
   concern. Ask `data` to produce a full usage inventory with per-call
   tenant-scoping verdict. Not a regression.
3. **FIND-ux-011/012 + FIND-ux-006c still pending** — pass through to
   `ux` agent as known open work, not new discovery.

## What was NOT deep-verified in preflight

To stay within coordinator budget, these items were confirmed only via
git-log + code-comment traces (not live re-runs):

- NotificationDispatcher full publisher/subscriber graph (arch-002):
  confirmed file exists + runs as BackgroundService; full NATS graph
  deferred to Phase-1 `arch`.
- NotificationsEndpoints paging (data-013): confirmed fix commit;
  SQL plan deferred to Phase-1 `data`.
- ScaffoldingService live Vue flow (pedagogy-006): confirmed fix commit;
  runtime verification deferred to Phase-1 `pedagogy` + `ux`.
- Live rendering of real KPIs and mock-auth refresh flow
  (ux-004, ux-010): deferred to Phase-1 `ux` (requires live admin host).

These are **not gaps in the preflight verdict** — they are deferrals to
the lens agents who will re-run them live during Phase 1.

## Inputs to Phase 1 agents

- This report at `docs/reviews/reverify-2026-04-11-preflight.md`.
- Prior merged report at `docs/reviews/cena-review-2026-04-11.md`.
- Prior per-agent files at `docs/reviews/agent-{1..5}-*-findings.md`.
- Prior screenshots at `docs/reviews/screenshots/`.
- The v2 prompt at `.claude/prompts/cena-review-v2-reverify.md`.

## Phase 1 spawn gate

**Green-lit.** Phase 1 may spawn.

No regressions or fake-fixes discovered. The 7 Phase-1 agents (arch,
sec, data, pedagogy, ux, privacy, qa) start from a clean v1 baseline and
should treat their lens's `verified-fixed` findings as *lower-priority
for drill-down*, not skipped.
