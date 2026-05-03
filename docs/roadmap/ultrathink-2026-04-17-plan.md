# Ultrathink 2026-04-17 — next-ship plan

**Context**: RDY-059 corpus expander and RDY-034 flow-state slice 1 shipped
clean. Queue debt cleared. Roadmap synced to reality (commit `05792b6`).
This doc captures the task list that fell out of the 2026-04-17 ultrathink
and the three-way parallel sub-agent execution plan.

**Canonical state**: `docs/roadmap/production-content-pipeline-roadmap.md`
(always kept in sync with `origin/main`).

---

## Remaining production-pipeline gaps

Only **four real code gaps** remain in the production content pipeline:

1. **Phase 3.2 — RDY-019b recreation loop** (in flight, sub-agent A below)
2. **Phase 3.4 — admin `pipeline.vue` full review screen** (partial; CuratorMetadataPanel shipped)
3. **Phase 4.1 — OCR regression harness** (in flight, sub-agent B below)
4. **Phase 4.4 — E2E load test**

Plus two RDY-034 flow-state follow-ons:

- **Slice 2** — fold `FlowStateAssessment` into `GET /api/sessions/{id}` (in flight, sub-agent C below)
- **Slice 3** — wire `LearningSessionActor` to emit transitions + SignalR push

## Secondary gaps (not in flight)

- RDY-017a DLQ follow-ups (replay script live validation, health check
  integration test, admin DLQ depth widget)
- RDY-025b / RDY-025c — actor cluster K8s provider + deploy validation
- RDY-029 — security hardening sweep
- RDY-030b — a11y baseline fixes
- RDY-032 — pilot data export
- RDY-019d — Bagrut content expert follow-ups
- `IngestionPipelineCloudDir` S3 provider placeholder (deferred cloud-ops)

## Decision gates (require user input — can't unblock from here)

- **VERIFY-0001 / ADR-0002** — enrollment-scoped mastery key (Model A / B / C).
  Blocks TENANCY-P2a mastery re-key and Phase 2 tenancy rollout.
- **RDY-024b** BKT calibration Phase B — needs pilot-completion data
  (200+ attempts per concept).
- **RDY-002** RTL + visual regression — codex-coder 3-day stale claim;
  pinged, will release if silent by EOD 2026-04-17.

---

## Three-way parallel sub-agent plan

All three sub-agents operate on `origin/main` HEAD in isolated `.claude/worktrees/*`
directories, push feature branches, and report results back for the
coordinator to review + merge. No direct pushes to `main`.

### A — RDY-019b recreation loop (Phase 3.2)

- **Worker**: `claude-subagent-rdy019b-recreation`
- **Worktree**: `.claude/worktrees/rdy019b-recreation/`
- **Branch**: `claude-subagent-rdy019b-recreation/recreation-loop`
- **Scope**:
  - New `src/shared/Cena.Infrastructure/Content/ReferenceCalibratedGenerationService.cs`
    - Reads `corpus/bagrut/reference/analysis.json` (written by
      `scripts/bagrut-reference-analyzer.py`)
    - For every topic × difficulty × Bloom × format cluster, builds
      `AiGenerateRequest` bundles
    - Calls existing `IAiGenerationService.BatchGenerateAsync` (CAS-gated
      via `IQualityGateService` — no new write paths)
    - Tags each candidate with `Provenance=recreation` +
      `ReferenceCalibration={year,topic,difficulty}`
  - DI wiring in `Cena.Admin.Api` + `CenaAdminServiceRegistration`
  - New admin endpoint `POST /api/admin/content/recreate-from-reference`
    (SuperAdminOnly + `ai` rate limit, dry-run default)
  - Tests against a seeded fixture `analysis.json`
- **NO STUBS**. Reuse `BatchGenerateAsync` — it already routes through
  CAS gate + QualityGate + `CasGatedQuestionPersister`.
- **DoD**: full `Cena.Actors.sln` build 0 errors; `Cena.Admin.Api.Tests`
  passes; ≥5 CAS-verified recreations generated against seeded fixture;
  feature branch pushed.

### B — Phase 4.1 OCR regression harness

- **Worker**: `claude-subagent-ocr-regression`
- **Worktree**: `.claude/worktrees/ocr-regression/`
- **Branch**: `claude-subagent-ocr-regression/phase-4-1-harness`
- **Scope**:
  - New `src/shared/Cena.Infrastructure.Tests/Ocr/OcrFixtureRegressionTests.cs`
    runs `IOcrCascadeService` against a frozen subset of
    `scripts/ocr-spike/dev-fixtures-v2/` fixtures, compares WER + math
    extraction accuracy to a committed baseline
  - New `ops/baselines/ocr-regression-baseline.json` — committed baseline
  - New `.github/workflows/ocr-regression.yml` — PR-triggered CI gate
  - Fail threshold: WER > baseline + 5 pp, math accuracy < baseline − 5 pp
- **NO STUBS**. Uses real cascade (mocking only the cloud fallbacks if
  the runner lacks API keys — those are test-infrastructure only).
- **DoD**: passes on current `main`; baseline committed; workflow wired;
  feature branch pushed.

### C — RDY-034 Slice 2 (session response integration)

- **Worker**: `claude-subagent-flow-slice2`
- **Worktree**: `.claude/worktrees/flow-slice2/`
- **Branch**: `claude-subagent-flow-slice2/session-integration`
- **Scope**:
  - Extend `GET /api/sessions/{sessionId}` in `SessionEndpoints.cs` to
    include a `flowState` field containing the `FlowStateAssessmentResponse`
  - Fetch signals from the existing session projection:
    - Fatigue via `ICognitiveLoadService.ComputeFatigue` using projection
      accuracy/RT signals
    - Accuracy trend from the rolling-5 window already tracked
    - Consecutive correct from session state
    - Session duration from session start timestamp
  - Update `Cena.Api.Contracts/Sessions/SessionDtos.cs` (add
    `FlowStateAssessmentResponse? FlowState` to the detail DTO)
  - Add integration test in `Cena.Admin.Api.Tests` or equivalent student-API
    test suite covering: no-session (404), empty session (state=warming),
    populated session (non-warming assessment)
- **NO STUBS**. `IFlowStateService` already registered in DI (commit `907ca6c`).
- **DoD**: full sln builds; existing session endpoint tests still pass;
  new tests cover the flow-state field; feature branch pushed.

---

## Coordination protocol

Each sub-agent:

1. Reads `.agentdb/AGENT_CODER_INSTRUCTIONS.md` before any work.
2. Creates its worktree from a fresh `origin/main`.
3. Does all work inside the worktree.
4. Runs the full `dotnet build src/actors/Cena.Actors.sln` before declaring done.
5. Runs relevant test filters.
6. Commits + pushes the feature branch.
7. Returns a structured report: branch name, commit hash, file list,
   build+test summary, any blockers.

Coordinator (claude-code) reviews each branch, merges to `main` via the
temp merge-worktree pattern (per `feedback_always_merge_to_main` memory),
removes the sub-agent worktree, and updates the roadmap scoreboard.

## What this closes

After all three land:

- Phase 3.2 ✅ (Ministry reference → AI-authored CAS-gated recreations)
- Phase 4.1 ✅ (OCR regression gate active in CI)
- RDY-034 slice 2 ✅ (flow state exposed on the main session read)

Remaining real gaps after this wave: Phase 3.4 `pipeline.vue` review
screen, Phase 4.4 E2E load test, RDY-034 slice 3 (actor-side transition
emission). Then the production content pipeline is feature-complete.
