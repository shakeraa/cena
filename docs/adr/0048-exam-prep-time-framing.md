# ADR-0048 — Exam-prep time framing: awareness yes, pressure no

- **Status**: Accepted
- **Date proposed**: 2026-04-20
- **Deciders**: Shaker (project owner), claude-code (coordinator), claude-subagent-prr006
- **Supersedes**: none
- **Related**:
  - [ADR-0003 (misconception session-scope)](0003-misconception-session-scope.md) — session vs. profile boundary for session-derived signals
  - [ADR-0040 (accommodation scope + Bagrut parity)](0040-accommodation-scope-and-bagrut-parity.md) — honest-accommodations tone already adopted on disability-accommodations surface
  - [ADR-0043 (Bagrut reference-only enforcement)](0043-bagrut-reference-only-enforcement.md) — surrounding exam-content doctrine
  - CLAUDE.md non-negotiable #3 — "Dark-pattern engagement mechanics are banned"
  - [docs/engineering/shipgate.md](../engineering/shipgate.md) — ship-gate CI policy
  - [docs/design/exam-prep-positive-framing.md](../design/exam-prep-positive-framing.md) — authoritative positive-framing reference copy
- **Task**: prr-006 (pre-release review Epic D — Ship-gate scanner v2, banned-vocabulary expansion)
- **Lens consensus**: persona-ethics, persona-educator, persona-cogsci, persona-ministry

---

## Context

The 2026-04-20 ten-persona pre-release review flagged two naming patterns that had been informally discussed in design docs but had never reached production code:

1. **"Crisis Mode"** — a proposed high-intensity study surface that would spin up when a student's trajectory fell below a threshold close to the exam date.
2. **"Bagrut Countdown"** — a widget showing `N days until Bagrut` with urgency styling (red badge, "days remaining", "last chance" copy).

Both patterns are banned by CLAUDE.md non-negotiable #3 and by `docs/engineering/shipgate.md` — they are textbook loss-aversion + artificial-urgency mechanics targeted at minors, which maps directly to the FTC v. Epic / FTC v. Edmodo / ICO v. Reddit / Israel PPL Amendment 13 enforcement surface.

A 2026-04-20 grep of the entire codebase confirmed neither pattern had shipped: every hit was inside persona-review YAMLs proposing the patterns, never in `src/`. The question left open by the review was the **boundary condition**: a student facing a scheduled Bagrut exam does need to know roughly where they stand in time. An earnest reader of the ship-gate policy might conclude that any reference to the exam date — including calm, factual "your Bagrut is on 2026-05-14" — is suspect.

The R-28 retirement review held the day before had answered an adjacent question: on tone, the project chose **harsh-honest numbers with confidence intervals** over soft euphemism (see `feedback_honest_not_complimentary.md`). R-28 did **not** sanction loss-aversion mechanics — a countdown that is mechanically coercive is still banned regardless of how honest the number is.

This ADR codifies the boundary so the next designer proposing an exam-prep surface does not have to re-argue it from first principles.

---

## Decision

**Time-awareness is permitted on student-facing surfaces. Time-pressure mechanics are not.** The two differ on six concrete axes:

| Axis | Time-awareness (ALLOWED) | Time-pressure mechanic (BANNED) |
|------|--------------------------|---------------------------------|
| **Information density** | Static, factual date or schedule point | Live-ticking counter |
| **Framing reference** | Internal, progress-based ("2 of 8 units mastered") | External, deadline-based ("62 days remaining") |
| **Visual styling** | Neutral typography, brand-default colour | Red/amber alerts, pulsing animation, bell/alarm iconography |
| **Copy voice** | Honest + specific + supportive (R-28 posture) | Urgent, coercive, loss-framed ("last chance", "hurry", "only N left") |
| **Emotional valence** | Informational — neutral affect | Anxiety-inducing by design |
| **Session effect** | Independent of user behaviour during the session | Intensifies as deadline approaches; decorrelated from mastery |

**Operationally**, the test a reviewer applies to any proposed exam-prep surface is:

> *Would a student reading this surface feel an urge to act NOW that is decoupled from their mastery state?*

If yes → time-pressure mechanic → banned.
If no → time-awareness → allowed.

### Permitted examples (time-awareness)

- "Your Bagrut is on 2026-05-14. Your current accuracy on Unit 3 is 72% (95% CI 68–76%) over 180 attempts." — honest, specific, date as a fact, not a threat.
- "2 of 8 Unit 3 sub-skills mastered. 3 more sessions will close the gap at your current rate (95% CI 2–5 sessions)." — progress-based, CI-bounded.
- "Your weekly study cadence is 4 of 5 planned sessions." — Apple-Fitness-rings-style positive-frame cadence signal, explicitly allowed by `docs/engineering/shipgate.md`.

### Banned examples (time-pressure mechanic)

- "Crisis Mode active — Bagrut in 42 days!" — combines red-tier label with countdown.
- "Only 7 days left — hurry up!" — scarcity + urgency.
- "Don't lose your 14-day streak!" — loss-aversion streak mechanic (separately banned by GD-004).
- "Your trajectory says you'll fail — act now!" — coerces action via outcome prediction (separately banned by prr-019 / outcome-prediction ban).

### Per-family informational opt-in (deferred to EPIC-PRR-C)

A small fraction of families may want the exam date rendered as informational copy on the parent surface (`docs/feature-specs/parent-aggregate.md` scope). That opt-in ships **only** under these guardrails and **never** reaches the student surface:

1. Default OFF. Parent explicitly opts in via the parent-settings UI.
2. Rendered only on the parent surface, never mirrored to the student home/session/progress surfaces.
3. Styled with neutral typography; never red/amber, never with urgency iconography.
4. Copy is date-as-fact only: "Bagrut exam on YYYY-MM-DD." No `N days until`, no `days remaining`, no scarcity language.
5. Surface can be hidden from the parent surface per-family at any time.

The opt-in UI itself is deferred to EPIC-PRR-C Parent Aggregate. This ADR locks the **policy** so that epic's implementer does not need a fresh swarm review.

---

## Enforcement layers

Four independent layers prevent a regression:

1. **CI ship-gate scanner** (`scripts/shipgate/rulepack-scan.mjs` + `scripts/shipgate/banned-mechanics.yml`) — 30 rules across en/he/ar flagging every known time-pressure term (countdown, days-remaining, hurry, last-chance, only-N-left, Crisis Mode variants, etc.). Fails the PR on any match in non-whitelisted files.
2. **Whitelist discipline** (`scripts/shipgate/banned-mechanics-whitelist.yml`) — policy docs, test assertions, scanner rule files, and this ADR are the only surfaces allowed to reference the banned terms. The whitelist is reviewed per-PR.
3. **Positive-framing reference copy** ([docs/design/exam-prep-positive-framing.md](../design/exam-prep-positive-framing.md)) — authoritative examples pairing each anti-pattern with a target phrasing plus rationale. Designers reference it before proposing any new exam-prep surface; a reviewer can cite it when rejecting urgency copy.
4. **CLAUDE.md non-negotiable #3 cross-link** — the project-rules file now points directly to this ADR so future authors hit the boundary condition on their first read of the repo.

---

## Consequences

- **Positive**: designers proposing an exam-prep surface have a six-axis test and a rich set of worked examples. The default-fail-closed posture of the scanner means an accidental urgency-copy regression shows up in CI, not in production. The per-family informational opt-in gives parents a legitimate route to see the exam date without ever coercing the student.
- **Negative**: the pattern catalog is English-plus-machine-translated Hebrew-and-Arabic. The Hebrew/Arabic rules have been sharpened to avoid known false-positive classes (adverbial `בסרעה`, adverbial `מהר יותר`, the physics-question "velocity" meaning of `بسرعة`) but remain flagged for native-speaker sign-off. Prof. Amjad (Arabic) and the Hebrew translation team are the designated reviewers; the `reason:` field on each non-English rule names the ambiguity so review is scoped.
- **Operational**: the scanner is advisory-capable via `--advisory`, but the default CI mode is enforcing. A legitimate-use whitelist entry is the escape hatch, not `--advisory`.

---

## Alternatives considered

### Alt 1 — Ban every reference to the Bagrut date

Simplest to enforce but breaks legitimate accommodations paperwork, Ministry-regulated parent communications, and the scheduling integration with school calendars. Rejected: too broad.

### Alt 2 — Allow countdown if it is "honest"

R-28 established honesty as a posture for performance numbers. It explicitly did **not** sanction coercive mechanics — the loss-aversion framing is the problem, not the number's accuracy. A perfectly honest countdown is still a countdown. Rejected.

### Alt 3 — Soft-launch on parent surface with telemetry

"Ship it, measure harm" is the classic engagement-metric trap. The enforcement landscape (FTC Edmodo, ICO Reddit, Israel PPL) makes "measure harm on minors first" a legal non-starter. Rejected.

### Alt 4 — Informational opt-in (CHOSEN)

Provides a legitimate route for families who want the date without building any coercive mechanic into the default surface. Student path never sees it; parent surface sees it only with explicit consent and no urgency styling.

---

## Review / rotation

- **Reviewers must re-confirm the ADR** if a future pre-release review surfaces a new "is this a mechanic or awareness?" boundary question.
- **Scanner rule pack** (`banned-mechanics.yml`) is the living artefact; add rules here as new banned variants are caught. Additions do not require re-opening this ADR.
- **Positive-framing reference copy** is the living design artefact; add worked examples as new surfaces are designed.
