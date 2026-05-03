# EPIC-PRR-N — Mock-exam runner: "perfect" gap roadmap (PRR-316..PRR-333)

**Status**: post-launch-polish (default tier; per-task promotion criteria below)
**Source**: claude-code "what is needed to make it perfect?" audit 2026-04-29
**Context**: the runner is **production-grade Phase 5** on origin/main as of `5f163309` (Phase 1A → Phase 5 + claude-1's PRR-289/290/291/299 + claude-10's PRR-267 all merged). 24 unit + 5 e2e specs green. This roadmap is the path from "production-grade" to "world-class".

---

## Six categories

### 1. Content depth (the biggest gap)
- **PRR-316** (high) — Bilingual native question content (Hebrew + Arabic; beyond EN translation review). PRR-284 covers translation review of EN copy; this task is **producing native HE/AR question content**, which is qualitatively different.
- **PRR-317** (high) — CAS proof-checker tier (geometry proofs, step-credit grading, graph-sketch verification). Today CAS handles equality only; real Bagrut math needs proof-checking.
- **PRR-330** (normal) — IRT difficulty calibration (replace hand-assigned Bloom levels with cohort attempt data).

### 2. Pedagogy depth
- **PRR-318** (high) — Spaced repetition: cross-run resurfacing of missed questions (1d/3d/7d/14d/30d intervals).
- **PRR-319** (normal) — Adaptive coaching notifications, ADR-0048-compliant (no streaks, no loss-aversion).

### 3. Workflow integration
- **PRR-328** (normal) — ExamTarget.QuestionPaperCodes binding to runner entry (default from onboarding).
- **PRR-332** (normal) — Onboarding "first run" tutorial overlay.
- **PRR-333** (normal) — Parent visibility surface (opt-in; ADR-0050 13+ default-hidden).

### 4. UX modalities
- **PRR-329** (normal) — Tablet + stylus input path for handwritten math.
- **PRR-331** (high) — Mobile-first responsive design + offline-capable PWA.

### 5. Operational + compliance
- **PRR-320** (high, **legal**) — Counsel-signed legal posture for ADR-0059 §15 (memo + signature on PRR-249). User accepted out-of-band; counsel-signed memo is still missing.
- **PRR-321** (high, **ops**) — Quarterly takedown drill execution. Operationalize the runbook shipped at `edc6b0c9` (PRR-254).
- **PRR-322** (normal, **ops**) — Cost telemetry per mock-exam run with $-projection on admin dashboard.
- **PRR-323** (high, **infra**) — Load testing at 1000-concurrent exam-day scale (k6 / locust harness).

### 6. Architectural rigor
- **PRR-324** (normal) — DDD aggregate refactor of ExamSimulationState (proper command handlers per ADR-0001).
- **PRR-325** (normal) — Event-sourcing rebuild verification test (replay events → state byte-for-byte).
- **PRR-326** (low) — CQRS read/write split for mock-exam runner.
- **PRR-327** (low) — Idempotency keys on Pause / Resume / AnswerSubmitted endpoints.

---

## Promotion criteria

All 18 are **post-launch-polish** by default. Promote to **launch-blocking** only if one of:

1. **Regulator action** — Ministry takedown notice triggers PRR-320 + PRR-321 to launch-blocking (need counsel posture + drill executed before defending).
2. **Privacy / compliance audit** — surfaces a gap that PRR-322 or PRR-313 sibling needs to close.
3. **User-facing failure mode** observed in production — e.g., mobile traffic > 60% promotes PRR-331; hostile content posture promotes PRR-317.
4. **Counsel demand** — pre-launch counsel review demands PRR-320 first.

---

## What's already done (so this roadmap doesn't repeat)

| Done | Where |
|---|---|
| 100+ paper structures (77 of them, 5 hand + 72 synthetic) | PRR-290 @ `5f163309` |
| MathLive integration | PRR-275 still pending (claude-10 lane) |
| Photo-of-paper input | PRR-285 still pending |
| Real-browser logged-in DOM drive | PRR-272 still pending |
| A11y beyond extra-time | PRR-274 still pending |
| Variant gen wired into runner | PRR-286 still pending (depends on PRR-267 ✅) |
| Save-and-resume | PRR-287 ✅ at `2318c34b` |
| Teacher review surface | PRR-288 still pending |
| BKT integration | PRR-289 ✅ at `5f163309` |
| Cohort fairness | PRR-291 ✅ at `5f163309` |
| Per-Q time tracking | PRR-299 ✅ at `5f163309` |
| Analytics dashboard | PRR-300 still pending |
| i18n native review | PRR-284 still pending (Prof. Amjad human gate) |
| LOC refactor | PRR-304 still pending |
| RTBF E2E | PRR-313 still pending (GDPR P0) |
| Reference&lt;T&gt; wrapper + R2 retention | PRR-267 + PRR-266 ✅ at `5f163309` |
| Ministry takedown runbook | PRR-254 ✅ at `edc6b0c9` |
| Per-runner rate-limit | PRR-302 ✅ at `81beeeee` |
| Formula sheet + calculator policy | PRR-292 + PRR-293 ✅ at `81beeeee` |
| Longitudinal trend card | PRR-294 ✅ at `81beeeee` |
| PDF export | PRR-303 ✅ at `2852da8b` |
| Pause/resume idempotency | PRR-287 ✅ at `2318c34b` |

---

## Realistic timeline to "perfect"

~3 months elapsed if 2-3 engineers + 1 content lead + counsel work in parallel:

| Track | Tasks | Owner candidate | Effort |
|---|---|---|---|
| Content lane | PRR-316, PRR-317, PRR-330 | content team + claude-3 | 6-8 weeks |
| Pedagogy lane | PRR-318, PRR-319 | claude-1 | 3 weeks |
| Workflow lane | PRR-288, PRR-328, PRR-332, PRR-333 | claude-10 | 4 weeks |
| UX modalities | PRR-275, PRR-285, PRR-329, PRR-331 | unassigned | 4-6 weeks |
| Ops + compliance | PRR-320, PRR-321, PRR-322, PRR-323 | shaker + sre | 2 weeks |
| Architectural | PRR-324, PRR-325, PRR-326, PRR-327 | claude-1 | 2 weeks |
| Filed-but-already-pending | PRR-271/272/274/284/300/304/313 | various | parallel |

Total parallelizable to ~12 weeks elapsed. Sequential would be 6+ months.

---

## Reporting

Each PRR-XXX is independently completable. Pick from the queue:

```bash
node .agentdb/kimi-queue.js list --status pending --tags source=exam-prep-perfect-gap-2026-04-29
```

Coordinator (claude-code) reviews + merges per the standard merge-wave protocol.
