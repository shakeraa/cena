# Cena Academic References

Every pedagogical and security decision in the Cena codebase that invokes an outside source must cite it here. This file is the single authoritative index — keep it in sync with inline citations in code, tests, and commit messages.

**Rule**: every `FIND-pedagogy-*`, `FIND-sec-*`, or adaptive-learning design decision must either (a) cite a verifiable source from this file or (b) be explicitly marked as proprietary Cena-internal with reasoning. No hand-wave claims.

---

## Formative feedback & assessment

### Black, P. & Wiliam, D. (1998)
"Assessment and Classroom Learning."
*Assessment in Education: Principles, Policy & Practice*, 5(1), 7–74.
DOI: [10.1080/0969595980050102](https://doi.org/10.1080/0969595980050102)

- **What it says**: formative feedback must include information that helps the learner close the gap between current and target performance — a binary correct/incorrect is not formative.
- **Used in**: `FIND-pedagogy-001` review finding; cited in the pedagogy-bundle commit.

### Hattie, J. & Timperley, H. (2007)
"The Power of Feedback."
*Review of Educational Research*, 77(1), 81–112.
DOI: [10.3102/003465430298487](https://doi.org/10.3102/003465430298487)

- **What it says**: effective feedback answers three questions — *Where am I going? How am I going? Where to next?* — at the task, process, and self-regulation levels.
- **Used in**: `FIND-pedagogy-001` — justifies plumbing `Explanation` + `DistractorRationale` through `SessionAnswerResponseDto`. Cited in commit [`d998654`](docs/reviews/cena-review-2026-04-11.md).

### Shute, V. J. (2008)
"Focus on Formative Feedback."
*Review of Educational Research*, 78(1), 153–189.
DOI: [10.3102/0034654307313795](https://doi.org/10.3102/0034654307313795)

- **What it says**: learner-controlled pacing (tap-to-continue) is more effective than time-gated auto-dismissal — learners need time to process the rationale.
- **Used in**: `FIND-pedagogy-005`. Cited in `src/student/full-version/src/components/session/AnswerFeedback.vue`.

### Kulhavy, R. W. & Stock, W. A. (1989)
"Feedback in Written Instruction: The Place of Response Certitude."
*Educational Psychology Review*, 1(4), 279–308.
DOI: [10.1007/BF01320096](https://doi.org/10.1007/BF01320096)

- **What it says**: "mindful processing on errors" — learners engage more deeply with wrong-answer feedback when they must explicitly dismiss it. Correct answers can auto-advance; wrong answers must not.
- **Used in**: `FIND-pedagogy-005`. Cited in `AnswerFeedback.vue` + `src/student/full-version/src/pages/session/[sessionId]/index.vue`.

---

## Cognitive Load Theory & worked examples

### Sweller, J., van Merriënboer, J. J. G. & Paas, F. G. W. C. (1998)
"Cognitive Architecture and Instructional Design."
*Educational Psychology Review*, 10(3), 251–296.
DOI: [10.1023/A:1022193728205](https://doi.org/10.1023/A:1022193728205)

- **What it says**: the **worked example effect** — novices learn more from studying complete worked examples than from solving problems. Working memory has ~4 chunks of capacity; novel problem-solving saturates it before schema acquisition can happen.
- **Used in**: `FIND-pedagogy-006` scaffolding service + `QuestionCard.vue` worked-example UI. Cited in `src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:110`, `src/student/full-version/src/components/session/QuestionCard.vue:21`, `src/actors/Cena.Actors.Tests/Session/SessionHintEndpointTests.cs:17`.

### Renkl, A. & Atkinson, R. K. (2003)
"Structuring the Transition From Example Study to Problem Solving in Cognitive Skill Acquisition: A Cognitive Load Perspective."
*Educational Psychologist*, 38(1), 15–22.
DOI: [10.1207/S15326985EP3801_3](https://doi.org/10.1207/S15326985EP3801_3)

- **What it says**: **fading** — progressively remove scaffolding (worked steps) as mastery increases. The fade should be paced to the individual learner, not a fixed schedule.
- **Used in**: `FIND-pedagogy-006` progressive hint reveal. Cited in `QuestionCard.vue:22`, `SessionHintEndpointTests.cs:20`.

### Kalyuga, S., Ayres, P., Chandler, P. & Sweller, J. (2003)
"The Expertise Reversal Effect."
*Educational Psychologist*, 38(1), 23–31.
DOI: [10.1207/S15326985EP3801_4](https://doi.org/10.1207/S15326985EP3801_4)

- **What it says**: instructional techniques that help novices can **harm** experts — worked examples become redundant once schemas exist. Scaffolding must be adaptive, not universal.
- **Used in**: `FIND-pedagogy-006` — justifies fading hints as Elo rating grows. Cited in `QuestionCard.vue:23`, `SessionHintEndpointTests.cs:23`.

---

## Bayesian Knowledge Tracing & mastery

### Corbett, A. T. & Anderson, J. R. (1995)
"Knowledge Tracing: Modeling the Acquisition of Procedural Knowledge."
*User Modeling and User-Adapted Interaction*, 4, 253–278.
DOI: [10.1007/BF01099821](https://doi.org/10.1007/BF01099821)

- **What it says**: the original **Bayesian Knowledge Tracing** (BKT) model. Four parameters — prior, learn, slip, guess — predict per-concept mastery from a sequence of observations. Wrong answers must decrease posterior mastery; you cannot ignore failure signals.
- **Used in**: `FIND-pedagogy-002` (wrong answers must emit `ConceptAttempted_V1`) and `FIND-pedagogy-003` (real BKT replaces hardcoded `+0.05`). Implemented by `BktService` and `BktTracer` in `src/actors/Cena.Actors/Mastery/`.

---

## Adaptive difficulty & Elo

### Wilson, R. C., Shenhav, A., Straccia, M. & Cohen, J. D. (2019)
"The Eighty Five Percent Rule for optimal learning."
*Nature Communications*, 10, 4646.
DOI: [10.1038/s41467-019-12552-4](https://doi.org/10.1038/s41467-019-12552-4)

- **What it says**: for binary-outcome learning with a noisy signal, optimal learning rate is achieved when the student's expected success probability is ≈**0.847** (commonly rounded to 0.85). Easier questions don't challenge; harder ones don't generate learnable signal.
- **Used in**: `FIND-pedagogy-009` Elo difficulty + `ItemSelector.TargetCorrectness = MasteryConstants.ProgressionThresholdF = 0.85f`. Cited in `src/actors/Cena.Actors/Services/EloDifficultyService.cs:12`, `src/actors/Cena.Actors/Events/LearnerEvents.cs:262`, `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs:37`, `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:26`.

### Elo, A. E. (1978)
*The Rating of Chessplayers, Past and Present.*
Arco Publishing.
ISBN: **0-668-04721-6**

- **What it says**: the classical **dual-update rule** for pairwise outcomes. Player and opponent ratings move in opposite directions by a K-factor times the probability delta `(actual - expected)`. Expected correctness: `1 / (1 + 10^((B - A) / 400))`.
- **Used in**: `EloScoring.ExpectedCorrectness` and `EloScoring.UpdateRatings` in `src/actors/Cena.Actors/Mastery/EloScoring.cs`. `EloDifficultyService` in `src/actors/Cena.Actors/Services/EloDifficultyService.cs` applies it per-answer.

### Pelanek, R. (2016)
"Applications of the Elo rating system in adaptive educational systems."
*User Modeling and User-Adapted Interaction*, 26(1), 89–123.
DOI: [10.1007/s11257-015-9156-4](https://doi.org/10.1007/s11257-015-9156-4)

- **What it says**: Elo is a tractable special case of 1PL Item Response Theory for educational contexts. Item K-factors should decay more slowly than student K-factors because items see many more attempts — a poorly-updated popular item contaminates every future learner.
- **Used in**: `EloDifficultyService.QuestionKFactor` decay schedule (32 → 16 → 8). Cited in `src/actors/Cena.Actors/Services/EloDifficultyService.cs:21`.

---

## Spaced repetition

### Settles, B. & Meeder, B. (2016)
"A Trainable Spaced Repetition Model for Language Learning."
*Proceedings of the 54th Annual Meeting of the Association for Computational Linguistics (ACL 2016)*, 1848–1858.
[Duolingo research blog entry](https://research.duolingo.com/papers/settles.acl16.pdf)

- **What it says**: **Half-Life Regression (HLR)** — model the probability a learner can recall an item `t` days after last practice as `2^(-t/h)` where `h` (half-life) is learned from features. Outperforms classic Leitner boxes and SM-2 on large-scale Duolingo data.
- **Used in**: `src/actors/Cena.Actors/Mastery/HlrCalculator.cs` and `src/actors/Cena.Actors/Services/HlrService.cs` — Cena's spaced-repetition scheduler.

---

## Learning objectives & curriculum design

### Wiggins, G. & McTighe, J. (2005)
*Understanding by Design* (2nd ed.).
ASCD.
ISBN: **978-1416600350**

- **What it says**: **backward design** — start from the desired understandings (learning objectives), then derive the acceptable evidence (assessments), then plan the learning experiences. Every assessment item must trace to an explicit learning goal.
- **Used in**: `FIND-pedagogy-008` LearningObjective metadata on every `QuestionDocument`. Cited in `src/actors/Cena.Actors/Questions/LearningObjective.cs:7`, `src/shared/Cena.Infrastructure/Documents/LearningObjectiveDocument.cs:7`, `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:123`, `src/api/Cena.Admin.Api/LearningObjectiveService.cs:7`, `src/admin/full-version/src/pages/apps/questions/edit/[id].vue:28`, `src/api/Cena.Admin.Api.Tests/LearningObjectiveTests.cs:18`.

### Anderson, L. W. & Krathwohl, D. R. (Eds.) (2001)
*A Taxonomy for Learning, Teaching, and Assessing: A Revision of Bloom's Taxonomy of Educational Objectives.*
Pearson / Longman.
ISBN: **978-0321084057**

- **What it says**: the **revised Bloom's taxonomy** is 2-dimensional:
  - **Cognitive Process dimension**: Remember → Understand → Apply → Analyze → Evaluate → Create
  - **Knowledge dimension**: Factual, Conceptual, Procedural, Metacognitive
  Every learning objective should be classified on both axes.
- **Used in**: `FIND-pedagogy-008` — `LearningObjectiveDocument.CognitiveProcess` + `KnowledgeType` enums. Cited in `LearningObjective.cs:10`, `LearningObjectiveDocument.cs:11`, `LearningObjectiveService.cs:10`, `LearningObjectiveTests.cs:20`.

### Biggs, J. (2003)
*Aligning Teaching for Constructing Learning.*
Higher Education Academy (AIMS).

- **What it says**: **constructive alignment** — teaching methods, learning activities, and assessment tasks must all target the same learning objectives. Alignment is what separates assessment FOR learning from assessment OF learning.
- **Used in**: `FIND-pedagogy-008` — rationale for why `LearningObjectiveId` is nullable in v1 but runtime-warned (tight alignment is the target, not a day-one requirement). Cited in `LearningObjective.cs:12`, `LearningObjectiveDocument.cs:15`, `LearningObjectiveService.cs:12`, `LearningObjectiveTests.cs:21`.

---

## Multilingual learning / L2 literacy

### August, D. & Shanahan, T. (Eds.) (2006)
*Developing Literacy in Second-Language Learners: Report of the National Literacy Panel on Language-Minority Children and Youth.*
Lawrence Erlbaum Associates.
ISBN: **978-0805860788**

- **What it says**: second-language learners benefit from L1 scaffolding early, but **primary-language instruction should transition to L2 once foundational literacy is established**. English-primary with Arabic/Hebrew as secondary support aligns with the evidence base.
- **Used in**: `FIND-pedagogy-004` — rationale for requiring English on every diagram challenge even when Hebrew localization is the authored target. Cited in `src/mobile/lib/features/diagrams/` Dart files + project `feedback_language_strategy` memory.

---

## Attention, focus & motivation

The following references live in `src/actors/Cena.Actors/Services/FocusDegradationService.cs` (focus degradation model). Citations are reproduced here for the index.

### Warm, J. S. (1984)
"An Introduction to Vigilance." In Warm (Ed.), *Sustained Attention in Human Performance*. Wiley.

- Foundational vigilance-decrement theory — attention degrades predictably over sustained-attention tasks.

### Parasuraman, R. (1986)
"Vigilance, monitoring, and search." In Boff, Kaufman & Thomas (Eds.), *Handbook of Human Perception and Performance, Vol II*. Wiley.

- Empirical characterisation of the attention-decay curve.

### Kapur, M. (2008)
"Productive Failure in Mathematical Problem Solving."
*Cognition and Instruction*, 26(3), 379–424.
DOI: [10.1080/07370000802212669](https://doi.org/10.1080/07370000802212669)

- **What it says**: **productive failure** — students who struggle with problems before instruction often out-learn students given direct instruction up front. Distinguishing productive struggle from destructive fatigue is a design requirement for adaptive systems.
- **Used in**: `FocusDegradationService` — productive-struggle vs. fatigue detection.

### Csikszentmihalyi, M. (1990)
*Flow: The Psychology of Optimal Experience.*
Harper & Row.
ISBN: **978-0061339202**

- **What it says**: the **9 flow conditions** — especially challenge/skill balance. Flow happens when challenge slightly exceeds current skill. This is the motivational twin of Wilson's 85% rule.
- **Used in**: `FocusDegradationService` — challenge-skill balance signal.

### Duckworth, A. L. et al. (2007)
"Grit: Perseverance and Passion for Long-Term Goals."
*Journal of Personality and Social Psychology*, 92(6), 1087–1101.
DOI: [10.1037/0022-3514.92.6.1087](https://doi.org/10.1037/0022-3514.92.6.1087)

- Note: *Grit has been critiqued — Crede et al. (2017) meta-analysis found weak incremental validity over conscientiousness.* Cena's resilience score is a composite, not grit alone.
- **Used in**: `FocusDegradationService` — one input among several to the resilience composite.

### Esterman, M. et al. (2013)
"In the Zone or Zoning Out? Tracking Behavioral and Neural Fluctuations During Sustained Attention."
*Cerebral Cortex*, 23(11), 2712–2723.

- **What it says**: RT-variance is a reliable proxy for attentional state (lower variance = more in-zone).
- **Used in**: `FocusDegradationService` — attention signal derived from response-time variance.

---

## Security standards (non-academic but load-bearing)

### OWASP Foundation — Authentication Cheat Sheet
*"Forgot password / Password reset"* section.
[cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)

- **What it says**: password-reset endpoints must return **uniform 204 responses** for both "email known" and "email unknown" outcomes to prevent account-enumeration attacks. Log the miss internally only.
- **Used in**: `FIND-ux-006b` — `POST /api/auth/password-reset` on `Cena.Student.Api.Host` in [`AuthEndpoints.cs`](src/api/Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs). Also cited in `Cena.Infrastructure/Firebase/FirebaseAdminService.cs` and `StudentAuthEndpointsTests.cs`.

### OWASP API Security Top 10

- **Used in**: general reference — API endpoints validate input at boundaries (API2:2019 Broken User Authentication, API1:2019 Broken Object Level Authorization). Referenced in `system-review-2026-03-28.md` Security Auditor role.

---

## Discarded / unsourced claims (kept for visibility)

The pedagogy audit on 2026-04-11 (`docs/reviews/agent-4-pedagogy-findings.md`) discarded the following claims because they could not be supported by a verifiable citation at the time:

1. "Spaced repetition is not implemented" — discarded: `HlrCalculator` + Settles & Meeder 2016 citation exists.
2. "Gamification undermines intrinsic motivation" — discarded: flat `+10 XP` with no streak/speed multiplier is not strong enough evidence to invoke Deci 1971 without user-research data.
3. "High cognitive load on mobile viewports" — kept only as P2; home grid auto-collapses on mobile.
4. "Difficulty progression violates Bjork's desirable-difficulties" — discarded as duplicate of the Wilson 85% rule finding.
5. "Flattened Bloom's level loses Anderson & Krathwohl knowledge dimension" — rolled into `FIND-pedagogy-008` (now fixed via `CognitiveProcess × KnowledgeType`).

Adding any of these back to the active codebase requires a real citation from this file.

---

## How to add a new reference

1. Add the full entry in the matching section above (author, year, title, venue, DOI or ISBN).
2. Cite it inline in the code or test where it is load-bearing: `// Cite: Author (Year) — what it justifies`.
3. Reference this file from the call-site: `// See docs/references.md for the full entry.`
4. If the reference is proprietary or unpublished, mark it explicitly as `Cena-internal — no external citation` and explain why.

Unsourced pedagogy claims are a P1 finding per the 2026-04-11 review standard.
