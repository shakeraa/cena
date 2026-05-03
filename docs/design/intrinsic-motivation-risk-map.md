# Intrinsic-motivation risk map

**Status**: Authoritative design-review tool
**Owner**: Product design + ethics lens
**Task**: prr-169 (EPIC-PRR-D Ship-gate scanner v2 — P2 tail)
**Related**:
- [ADR-0048 exam-prep time framing](../adr/0048-exam-prep-time-framing.md)
- [ADR-0049 citation-integrity / effect-size communication](../adr/0049-citation-integrity-effect-size-communication.md)
- [Engineering shipgate policy](../engineering/shipgate.md)
- [CLAUDE.md non-negotiable #3](../../CLAUDE.md) — dark-pattern engagement mechanics banned
- [Positive-framing-extended rule pack](../../scripts/shipgate/positive-framing-extended.yml)

## Purpose

The Cena platform's positive-framing rule packs block specific banned terms
(streaks, countdowns, "lost points", "easier problem", "you made a mistake",
etc.) once a designer has already written the copy. This design doc operates
one step earlier in the process: it gives a designer proposing a new
student-facing surface a **15-minute pre-design checklist** that flags
intrinsic-motivation risks BEFORE the copy is written, so the scanner becomes
a safety net rather than the first line of defence.

The risk map draws on three evidence bases, cited honestly (ADR-0049):

- **Self-Determination Theory** — autonomy, competence, relatedness (Deci &
  Ryan; meta-analyses d≈0.3–0.5 for autonomy-supportive interventions on
  sustained engagement).
- **Growth-mindset literature** — process praise vs. outcome praise; honest
  meta-analytic means are small-to-moderate (d≈0.08–0.19 per Sisk et al.
  2018), not the inflated "d=1.0 transformative" claims that circulate in
  EdTech marketing.
- **Cognitive-evaluation theory** — extrinsic rewards crowd out intrinsic
  motivation when they are contingent, expected, and salient (Deci, Koestner
  & Ryan 1999 meta-analysis — the classic result, holds up).

## How to use this doc

When a designer, PM, or engineer proposes a new student-facing surface, they
run through the risk map BEFORE writing user-visible copy and BEFORE
specifying telemetry events. The output is a short written answer to each of
the six dimensions, attached to the design PR. A reviewer can cite a
specific dimension when rejecting a proposal ("this fails #3 — the
rewarded action is extrinsic to the skill being built") without having to
re-derive the argument from first principles.

If a proposal fails any dimension, the design needs reworking, not a copy
tweak. A scanner-level copy fix on a structurally-extrinsic mechanic is
cosmetic.

## The six dimensions

### 1 — Autonomy: does the student have a genuine opt-out?

**Low-risk**: The surface is visible-but-dismissable. The student can close
it, skip it, or disable it from a settings page, and the platform does not
penalise the skip. Example: the "familiar pattern" warm-up after a fatigue
signal — the student can wave it off and see the next problem.

**High-risk**: The surface is modal, non-dismissable, or tied to session
continuation. The student is forced to interact with it before proceeding.
Example (banned): a mid-session "how stressed are you?" tap that blocks the
next problem until the student answers.

Ask: *what happens if the student ignores this surface for a full session?*
If the answer is "the session cannot progress", autonomy is compromised.

### 2 — Competence: is feedback honest about the student's actual state?

**Low-risk**: Feedback cites the specific skill or step ("step 3 scored 2/4
because the negative-exponent rule was applied before distribution"). CI
bounds where numbers are reported (ADR-0049). Neutral tone on errors.

**High-risk**: Feedback is inflated ("amazing!" on a correct trivial step),
vague ("great job!"), or loss-framed ("you lost 4 points" — banned by
positive-framing-extended/prr-166). Praise that does not tie to observable
skill is the classic "empty praise" failure mode.

Ask: *could this feedback message be written without knowing what the
student actually did?* If yes, it is empty praise; rewrite to cite the
specific step.

### 3 — Relatedness: does the mechanic replace or support relationships?

**Low-risk**: The surface supports student-teacher, student-parent, or
student-peer-within-classroom relationships. Example: the parent-digest's
session-summary that a parent and student can discuss together.

**High-risk**: The surface simulates a relationship the student does not
have. "The assistant understands your anxiety" (banned by therapeutic-claims
pack) is the classic failure — it replaces a human-support relationship with
a chatbot performance of empathy, which under-delivers on the moment the
student actually needs help.

Ask: *is this surface helping the student connect with a real human, or
performing connection itself?*

### 4 — Contingency of reward: is the reward tied to the skill itself?

**Low-risk**: The "reward" is intrinsic to the skill — visible mastery of
the next item, a harder-but-appropriately-scaffolded follow-up, unlocking a
genuinely more interesting problem. The student notices the pleasure of
getting better at math.

**High-risk**: Decorative rewards (badges, confetti, reward emoji — banned
by reward-inflation-emoji pack) that are contingent on the task but
semantically unrelated. The Deci-Koestner-Ryan result: these reduce
intrinsic motivation for the target skill, reliably, across meta-analyses.

Ask: *does the reward refer to the skill, or is it generic?* If the reward
would work for brushing teeth, doing dishes, or answering a math question,
it is extrinsic.

### 5 — Predictability of reward: is this variable-ratio?

**Low-risk**: Reward schedule is fixed and predictable. Correct-answer
feedback is tied deterministically to the answer; there is no "surprise" or
"lucky streak". The shipgate banned-mechanics rule `no-loot-boxes` already
enforces this for the extreme case.

**High-risk**: Any "surprise" reward, "streak bonus", "random encouragement",
or animation that pulses unpredictably. Variable-ratio reinforcement is the
mechanic that makes slot machines addictive; it is banned by
CLAUDE.md non-negotiable #3 and enforced by the shipgate.

Ask: *could an engineer add an RNG call to this mechanic to make it
"fun"?* If yes, it is at variable-ratio risk.

### 6 — Time horizon: is this a session-local signal or a lifetime score?

**Low-risk**: Signal is session-scoped or short-window. Misconception data
is session-scoped (ADR-0003). The "go gentler today" opt-in (prr-179) is
session-local. Progress is framed as "where you are now, in this unit", not
"what you are".

**High-risk**: Scores-as-identity — "you are a B-level student" (banned by
progress-framing pack), persistent rank, all-time leaderboard position,
"lifetime streak". Any signal the student can experience as a label on
themselves rather than a description of a specific moment.

Ask: *can the student reasonably interpret this as a statement about who
they are, versus a statement about what they did in the last hour?*

## Review checklist

A design review uses this compressed version:

- [ ] **Autonomy**: student can dismiss / opt out / ignore without penalty
- [ ] **Competence**: feedback cites specific skill + CI where numeric
- [ ] **Relatedness**: supports real human relationships, does not simulate
- [ ] **Contingency**: reward refers to the skill, not generic decoration
- [ ] **Predictability**: reward schedule is fixed, no variable-ratio / RNG
- [ ] **Time horizon**: signal is session-local / short-window, not
      lifetime-identity

Any unchecked box requires a written rationale in the PR description citing
the dimension number. The rule-pack scanner is the downstream safety net —
a proposal that fails the risk map but passes the scanner is still a design
problem.

## Worked examples

### Example 1 — "Here is an easier problem" (fails #2, #6)

Fails dimension #2 (labels the problem as easier, which is empty feedback
about the student's state — the honest claim is "this is a familiar
pattern"). Fails dimension #6 mildly (an "easier" label telegraphs an
implicit identity claim: "you need the easier one"). Rewritten with
"familiar pattern" framing per ADR-0048 §R-28-posture.

### Example 2 — "Finish this session to keep your 14-day streak!" (fails all six)

Every dimension fails. Banned by CLAUDE.md non-negotiable #3 and by the
banned-mechanics rule pack. This is the canonical counter-example: any
surface that reads like this must be reworked, not rephrased.

### Example 3 — "Go gentler today" self-opt-in (passes all six)

**Autonomy**: student sets it for themselves, session-local, dismissable.
**Competence**: does not claim to know anything about the student —
honest signal from student to pacing engine. **Relatedness**: no simulated
empathy. **Contingency**: no reward attached; it is a pacing control, not
a gamification mechanic. **Predictability**: effect on pacing is
deterministic and described in the accommodation profile. **Time
horizon**: session-scoped by default. Passes. (Wired as
`AccommodationDimension.GoGentlerToday` per prr-179.)

## Related artefacts

### prr-169 — this document

This document is the reviewer-facing artefact the pre-release review asked
for. It is stored under `docs/design/` so the design team lands on it
alongside other design-review tools.

### prr-182 — shortlist export schema

The persona-review top-10 shortlist exports that live under
`pre-release-review/reviews/` must carry a `critique_note` field on every
line so the carry-forward critique annotation survives the export. The rule
is documented here rather than enforced as a scanner rule because there is
no runtime seam yet — when the shortlist-export code lands, the author must
include the field. Schema addition:

```json
{
  "id": "<persona-id>",
  "title": "<short>",
  "rationale": "<reasoning>",
  "critique_note": "<carry-forward critique captured at the moment of review>"
}
```

Reviewer: enforce the field's presence on every new shortlist export PR. An
export missing `critique_note` on any entry must be rejected.

## Rotation

- Re-run this risk map on every new student-facing surface, before copy is
  written.
- Extend the six dimensions if a pre-release review surfaces a seventh
  intrinsic-motivation risk class that cannot be expressed as a sub-case of
  the existing six.
- Update the worked-examples section as new cases land. Do not delete old
  examples — they are the living record of rejected patterns.
