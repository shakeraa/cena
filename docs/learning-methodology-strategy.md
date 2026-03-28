# Learning Methodology Strategy: Per-Student, Per-Topic Adaptation

**Date:** 2026-03-28
**Status:** Proposal
**Author:** Architecture Review

---

## 1. Problem Statement

Cena's actor model already tracks which learning methodology (Socratic, Feynman, WorkedExample, etc.) is active per student per concept. The question: should we surface this as a first-class feature with per-topic tracking, admin visibility, and intelligent adaptation?

Key concerns:
- Per-topic methodology preference only becomes statistically meaningful after ~30+ attempts per method per topic per student
- Rapid methodology switching can disorient students
- Teachers need transparency into why the system chose what it chose

---

## 2. What Already Exists

The platform has substantial infrastructure in place:

| Component | Location | What It Tracks |
|-----------|----------|---------------|
| `StudentState.MethodologyMap` | `Students/StudentState.cs` | Active methodology per concept |
| `StudentState.MethodAttemptHistory` | `Students/StudentState.cs` | All tried methodologies per concept |
| `ConceptMasteryState.MethodHistory` | `Mastery/ConceptMasteryState.cs` | Attempt-level methodology tracking |
| `MethodologySwitched_V1` event | `Events/LearnerEvents.cs` | Full MCM trace on every switch |
| `MethodologySwitchService` | `Services/MethodologySwitchService.cs` | 5-step MCM error-type routing |
| `StagnationDetector` | `Stagnation/` | Accuracy plateau, response drift, error repetition |
| 9 methodology enum | `Methodology.cs` | Socratic, SpacedRepetition, Feynman, ProjectBased, BloomsProgression, WorkedExample, Analogy, RetrievalPractice, DrillAndPractice |

**Not yet built:**
- Topic-level aggregation (concepts group to topics, but no rollup projection)
- API exposure of methodology data in `ConceptMasteryDto`
- Real analytics queries (current `MethodologyAnalyticsService` returns mock data)
- Student learning profile / preference vector
- Methodology metadata on questions

---

## 3. Approaches Evaluated

### 3.1 Hierarchical Confidence-Gated Methodology (Recommended)

Assign methodology at subject level first, refine to topic level when enough data accumulates, then concept level.

**How it works:**

```
Math (subject)        -> WorkedExample    (confident after ~50 attempts across all topics)
  |-- Algebra (topic) -> Feynman          (overrides Math default after 30+ topic attempts)
  |   |-- Quadratics  -> inherits Feynman (only 8 attempts, not enough to override)
  |   |-- Factoring   -> Socratic         (42 attempts, earned its own assignment)
  |-- Geometry (topic) -> inherits WorkedExample (only 15 attempts, not confident yet)
```

**Resolution rule:** Use the most specific level that has crossed the confidence threshold (N >= 30). Everything else inherits from parent.

**Safeguards:**
- Cooldown: minimum 5 sessions or 1 week before switching at any level
- Require `ConsecutiveStagnantSessions >= 3` before triggering a switch
- Admin notification when cooldown defers a recommended switch
- Admin notification when a topic crosses the confidence threshold for the first time

**Strengths:**
- Transparent — teachers see exactly why and at what level the choice was made
- Noise-resistant — thin data inherits from the nearest confident parent
- Matches the existing actor model (StudentState already has concept-level maps)
- Dampening prevents thrashing

**Weaknesses:**
- Still needs ~30 attempts per method per topic to become topic-specific
- Doesn't leverage cross-student patterns

**Estimated effort:** ~2 weeks

---

### 3.2 Multi-Armed Bandit (Explore/Exploit)

Treat each methodology as a "bandit arm." Use Thompson Sampling or UCB to balance exploration (trying new methods) with exploitation (using the best known method).

**Strengths:**
- Self-correcting, no manual threshold tuning
- Handles cold-start by exploring more early on

**Weaknesses:**
- Students experience "exploration" attempts that feel random
- Hard to explain to teachers why it picked something — it's a probability, not a pedagogical reason
- Ignores the MCM domain knowledge (error type -> methodology mapping) that Cena already has
- The MCM graph encodes pedagogical research; a bandit learns from scratch and discards that

**Verdict:** Skip. The MCM error-type routing is more pedagogically grounded than a statistical bandit. Building a bandit would mean throwing away Cena's strongest differentiator.

---

### 3.3 Bayesian Knowledge Tracing with Method-Conditioned Transitions (BKT)

Extend mastery probability so transition parameters (P(learn), P(slip), P(guess)) are conditioned on the active methodology.

**How it works:** Instead of one P(learn) per concept, maintain P(learn | Socratic), P(learn | WorkedExample), etc. The method with the highest P(learn) wins.

**Strengths:**
- Statistically rigorous
- Gives a real probability: "Feynman is better than Socratic for this student on this topic"
- Standard approach in intelligent tutoring systems (ITS) research

**Weaknesses:**
- Needs ~15-20 attempts per method per concept to fit reliably — even hungrier for data
- Cold-start is brutal (every student x concept x method combination starts unknown)
- Implementation complexity: must integrate with existing mastery probability engine

**Verdict:** Strong future addition. Add as the mathematical engine behind the hierarchy once data volume justifies it (~month 2+). Not a standalone approach due to cold-start.

**Estimated effort:** 3-4 weeks (on top of the hierarchy)

---

### 3.4 Collaborative Filtering ("Students Like You")

Use methodology success patterns from similar students to predict what will work for a new student.

**How it works:** Cluster students by error profile, mastery velocity, and methodology response patterns. New students inherit their cluster's best methodology.

**Strengths:**
- Solves cold-start entirely — day-1 students get a recommendation based on hundreds of similar students
- Improves with scale

**Weaknesses:**
- Chicken-and-egg: needs a meaningful user base first (500+ active students minimum)
- Can reinforce biases — if most students in a cluster got WorkedExamples, the system never discovers Socratic might be better
- Privacy implications of cross-student profiling
- Doesn't replace per-student adaptation, only supplements cold-start

**Verdict:** Add at scale (1000+ students). Good complement to the hierarchy for cold-start scenarios, not a replacement for individual tracking.

**Estimated effort:** 4-6 weeks

---

### 3.5 Teacher-Driven with System Suggestions

The system never auto-switches. It detects stagnation, computes which methodology would help, and sends the teacher a recommendation for approval.

**How it works:** Existing stagnation detector + MCM produces a recommendation. Instead of applying it, it becomes a notification: "Student X stuck on Algebra. Recommend switching from Socratic to WorkedExample (procedural error pattern). Approve?"

**Strengths:**
- Teachers stay in control
- Zero risk of bad automated switches
- Builds teacher trust
- Simplest to implement — signals already exist

**Weaknesses:**
- Doesn't scale. 30 students x 5 active topics = 150 potential recommendations. Alert fatigue kills adoption.
- Adds latency — student waits for teacher to approve before methodology changes

**Verdict:** Use as the initial UX layer. Start with teacher-approval mode, gather data on override patterns, then automate the cases where teachers always accept the recommendation.

**Estimated effort:** ~1 week

---

### 3.6 Rule-Based Curriculum Sequencing

No ML. Define a fixed methodology progression per topic based on pedagogical theory.

**How it works:** Algebra always starts with WorkedExamples, progresses to Socratic at Bloom's level 3+, switches to RetrievalPractice at mastery > 0.7. Hard-coded by curriculum designers.

**Strengths:**
- Transparent, predictable, zero data requirements
- Easy to explain to teachers and parents

**Weaknesses:**
- Not personalized — every student gets the same path
- Ignores that some students thrive with Socratic from day one

**Verdict:** Already partially exists in Bloom's progression logic. Use as the root fallback when no student-specific data exists.

**Estimated effort:** ~1 week

---

## 4. Recommendation: Layered Compound Design

No single approach is sufficient. The recommendation is a **layered system** where each layer activates when it has enough data to be reliable, and lower layers always serve as fallback.

```
Layer 1 (always present):   Rule-based curriculum default (Blooms progression)
                            |
Layer 2 (day 1):            MCM error-type routing (existing MethodologySwitchService)
                            |
Layer 3 (week 1-2):         Hierarchical confidence-gated (subject -> topic -> concept)
                            |
Layer 4 (month 2+):         BKT method-conditioned transitions (refine probabilities)
                            |
Layer 5 (1000+ students):   Collaborative filtering (cold-start for new students)

Teacher overlay:            Can override any layer at any time
Admin alerts:               Fire at layer transitions, stagnation, confidence milestones
```

### Why This Design

1. **Layer 1+2 are already built.** Bloom's progression and MCM routing exist today. Zero new work to activate them as the foundation.

2. **Layer 3 (the hierarchy) is the high-value build.** It gives teachers real insight ("Algebra now has enough data — WorkedExample wins for this student") without noise. The confidence gate and cooldown prevent the system from acting on bad data. ~2 weeks of work.

3. **Layer 4 (BKT) adds mathematical rigor later.** Once students have 2+ months of data, BKT can refine the hierarchy's probability estimates with real transition parameters. But it's not needed at launch.

4. **Layer 5 (collaborative filtering) only matters at scale.** Don't build it until you have the user base to power it.

5. **Teacher-driven suggestions are the UX, not a separate approach.** Every automated switch should surface as a suggestion first. Track teacher accept/reject rates. Auto-approve methodology switches that teachers accept >90% of the time.

### Build Sequence

| Phase | What | Effort | Prerequisite |
|-------|------|--------|-------------|
| **Phase 1** | Expose existing methodology data in API + admin UI | 1 week | None |
| **Phase 2** | Hierarchical confidence-gated methodology (Layer 3) | 2 weeks | Phase 1 |
| **Phase 3** | Teacher suggestion UX with approve/reject flow | 1 week | Phase 2 |
| **Phase 4** | Real analytics queries replacing mock data | 1 week | Phase 1 |
| **Phase 5** | BKT method-conditioned transitions (Layer 4) | 3-4 weeks | Phase 2 + 2 months of student data |
| **Phase 6** | Collaborative filtering (Layer 5) | 4-6 weeks | 1000+ active students |

**Critical path: Phases 1-4 (~5 weeks) deliver a usable, insight-generating system.**

---

## 5. Data Model Changes

### New on StudentState

```
SubjectMethodologyMap: Dictionary<string, MethodologyAssignment>
TopicMethodologyMap: Dictionary<string, MethodologyAssignment>
// Existing ConceptMethodologyMap stays as-is

MethodologyAssignment:
  - Methodology: enum
  - Confidence: float (0-1)
  - AttemptCount: int
  - SuccessRate: float
  - LastSwitchTimestamp: DateTime
  - Source: enum (Inherited, DataDriven, TeacherOverride, MCMRouted)
```

### New Events

```
MethodologyConfidenceReached_V1(StudentId, Level, LevelId, Methodology, Confidence, AttemptCount)
MethodologySwitchDeferred_V1(StudentId, ConceptId, Reason, CooldownRemaining)
TeacherMethodologyOverride_V1(StudentId, Level, LevelId, FromMethod, ToMethod, TeacherId)
```

### Resolution Function

```
ResolveMethodology(conceptId):
  1. Check concept-level override (N >= 30) -> use it
  2. Check topic-level assignment (N >= 30) -> use it
  3. Check subject-level assignment (N >= 50) -> use it
  4. Fall back to MCM error-type routing
  5. Fall back to Blooms progression default
```

---

## 6. Key Insight Metrics

What teachers and admins will actually see:

| Metric | Source | Value |
|--------|--------|-------|
| Best methodology per student per topic | Hierarchy Layer 3 | "WorkedExample beats Socratic 73% vs 45% for Algebra" |
| Stagnation-resistant concepts | Existing stagnation detector | "This student has exhausted 3 methods on Quadratics" |
| Methodology effectiveness by error type | MCM analytics | "Procedural errors resolve 2x faster with WorkedExample" |
| Time-to-mastery by methodology | BKT (Layer 4) | "Feynman achieves mastery in 12 sessions vs 20 for Drill" |
| Cluster methodology patterns | Collaborative filtering (Layer 5) | "Students with this profile succeed 80% with Analogy" |
| Teacher override rate | Suggestion tracking | "Teachers accept MCM recommendations 87% of the time for Algebra" |

---

## 7. Comparables

This layered approach aligns with production ITS systems:

- **Carnegie Learning MATHia:** Uses BKT with method-conditioned transitions, curriculum-sequenced defaults, teacher override layer
- **Khan Academy:** Rule-based mastery progression with collaborative filtering for recommendations at scale
- **ALEKS:** Knowledge-space approach with assessment-driven methodology, no per-student method adaptation (simpler but less personalized)

Cena's MCM error-type routing is a differentiator none of these have — it uses pedagogical reasoning about *why* a student is failing, not just *that* they're failing.

---

## 8. Risks

| Risk | Mitigation |
|------|-----------|
| Thin data leads to bad methodology assignments | Confidence gate (N >= 30) + inheritance from parent level |
| Rapid switching disorients students | Cooldown (5 sessions or 1 week) + consecutive stagnation requirement (3+) |
| Teachers distrust automated decisions | Teacher-approval UX, transparent confidence badges, override capability |
| Methodology effectiveness varies by teacher's instructional style | Track per-teacher override patterns, surface as admin insight |
| Over-engineering before product-market fit | Phase 1-4 only (~5 weeks), defer Phases 5-6 until scale justifies them |
