# Learning Science, Memory & Spaced Repetition for Educational Mobile Apps

> **Status:** Research complete
> **Date:** 2026-03-31
> **Scope:** Comprehensive learning science foundation for Cena's Flutter mobile app
> **Applies to:** SRSActor, KnowledgeMapActor, AdaptiveActor, LearningSessionActor, StudentActor
> **Codebase context:** BKT + HLR already implemented in `Cena.Actors.Mastery/`, FSRS fields on `ConceptMasteryState`, gamification in Flutter `src/mobile/lib/features/gamification/`

---

## Table of Contents

1. [Spaced Repetition Systems (SRS)](#1-spaced-repetition-systems-srs)
2. [Active Recall](#2-active-recall)
3. [Interleaving & Varied Practice](#3-interleaving--varied-practice)
4. [Testing Effect (Retrieval Practice)](#4-testing-effect-retrieval-practice)
5. [Elaborative Interrogation & Self-Explanation](#5-elaborative-interrogation--self-explanation)
6. [Dual Coding & Multimedia Learning (Mayer)](#6-dual-coding--multimedia-learning-mayer)
7. [Metacognition & Self-Regulated Learning](#7-metacognition--self-regulated-learning)
8. [Bloom's Taxonomy in App Design](#8-blooms-taxonomy-in-app-design)
9. [Zone of Proximal Development (Vygotsky)](#9-zone-of-proximal-development-vygotsky)
10. [Transfer of Learning](#10-transfer-of-learning)
11. [Motivation & Learning Science Intersection](#11-motivation--learning-science-intersection)
12. [Sleep & Learning](#12-sleep--learning)
13. [Actor Mapping](#13-actor-mapping)
14. [Comparative Analysis of Existing Apps](#14-comparative-analysis-of-existing-apps)

---

## 1. Spaced Repetition Systems (SRS)

### 1.1 The Ebbinghaus Forgetting Curve

**Core Finding:** Memory decays exponentially without reinforcement. Ebbinghaus (1885) demonstrated that after learning, retention drops rapidly -- approximately 56% is forgotten within one hour, 66% within one day, and 75% within six days.

**Mathematical Model (exponential):**
```
R(t) = e^(-t/S)
```
Where:
- `R(t)` = retention probability at time t
- `S` = memory stability (higher = slower decay)
- Each successful retrieval increases S, lengthening the curve

**Power-Law Alternative (FSRS model):**
```
R(t, S) = (1 + t / (9 * S))^(-1)
```
Power-law decay better fits empirical data than exponential (Wixted & Ebbesen, 1991). FSRS uses this.

**How to Combat It:**
1. **Spaced practice:** Review at increasing intervals timed to the forgetting curve
2. **Active retrieval:** Force recall rather than re-reading (testing effect)
3. **Interleaving:** Mix topics to force discrimination and strengthen distinct memory traces
4. **Sleep consolidation:** Review before sleep for memory transfer from hippocampus to neocortex
5. **Elaboration:** Connect new information to existing knowledge networks

**Cena Implementation:**
- Already tracked via `HlrCalculator.ComputeRecall()` in `src/actors/Cena.Actors/Mastery/HlrCalculator.cs`
- `p(recall) = 2^(-delta / h)` computed per (student, concept) pair
- Decay scanner runs on `StudentActor.ReceiveTimeout` every 6 hours
- `MasteryDecayed` event fires when `p(recall) < 0.70`, triggers Outreach Context notifications

**Mobile UI Design -- Forgetting Curve Visualization:**
```
Screen: "Memory Health" widget on Home
+----------------------------------------------+
|  [Concept Name]          [Recall: 73%]        |
|  =========-----------     Decaying...         |
|  Last reviewed: 3 days ago                    |
|  Optimal review: TODAY                        |
|  [Review Now]                                 |
+----------------------------------------------+
```
Show a color-coded curve: green (>85%), yellow (70-85%), orange (<70%), red (<50%). The curve shape itself is the Ebbinghaus curve, personalized to this student's actual half-life for this concept.

**Citations:**
- Ebbinghaus, H. (1885). *Memory: A Contribution to Experimental Psychology*. Dover. DOI: 10.1037/10011-000
- Wixted, J.T. & Ebbesen, E.B. (1991). On the form of forgetting. *Psychological Science*, 2(6), 409-415. DOI: 10.3758/BF03202914
- Murre, J.M. & Dros, J. (2015). Replication and analysis of Ebbinghaus' forgetting curve. *PLOS ONE*, 10(7), e0120644. DOI: 10.1371/journal.pone.0120644

---

### 1.2 SM-2 Algorithm (SuperMemo)

**What It Is:** Created by Piotr Wozniak (1987), SM-2 is the algorithm that popularized spaced repetition software. It remains the default algorithm in Anki (as of 2024).

**How It Works:**

After each review, the student self-rates quality on a 0-5 scale:
- 5 = perfect response, no hesitation
- 4 = correct with slight hesitation
- 3 = correct but with significant difficulty
- 2 = incorrect, but upon seeing answer, remembered
- 1 = incorrect, vague recollection
- 0 = complete blackout

**Core Update Rules:**
```
If quality >= 3 (pass):
  If repetition == 0: interval = 1 day
  If repetition == 1: interval = 6 days
  If repetition >= 2: interval = previous_interval * EF

If quality < 3 (fail):
  repetition = 0
  interval = 1 day
  (restart from beginning)

Easiness Factor (EF) update:
  EF_new = EF + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02))
  EF = max(EF_new, 1.3)  // floor at 1.3
```

**Strengths:**
- Simple to implement (30 lines of code)
- Proven effective over 30+ years of real-world use
- Self-rating gives students agency in the process
- The EF per-card personalization captures item-level difficulty

**Weaknesses:**
- Self-rating is unreliable -- the Dunning-Kruger effect means students rate their recall accuracy poorly (see Section 2)
- Binary pass/fail on quality < 3 causes harsh resets
- No data-driven optimization -- intervals are fixed formulas, not learned from population data
- The EF floor of 1.3 means "leeches" (permanently hard cards) never stabilize
- No explicit memory model -- EF is a heuristic, not a probability of recall
- Outdated compared to FSRS (see below)

**Why Cena Does Not Use SM-2:**
- Self-rating is inappropriate for a population of 16-18 year old exam-prep students -- they are notoriously bad at judging what they know (Section 7)
- Cena uses BKT + HLR, which are data-driven and do not require self-assessment
- FSRS is the recommended upgrade path, not SM-2

**Citations:**
- Wozniak, P.A. (1990). *Optimization of Repetition Spacing in the Practice of Learning*. University of Technology in Poznan.
- Wozniak, P.A. & Gorzelanczyk, E.J. (1994). Optimization of repetition spacing in the practice of learning. *Acta Neurobiologiae Experimentalis*, 54, 59-62.

---

### 1.3 FSRS Algorithm (Free Spaced Repetition Scheduler)

**What It Is:** A modern, open-source spaced repetition algorithm developed by Jarrett Ye (2022). Uses a three-component memory model grounded in the three-component model of memory (stability, difficulty, retrievability). Empirically validated on 10K+ Anki users, significantly outperforms SM-2.

**Mathematical Model:**

**Retrievability (recall probability):**
```
R(t, S) = (1 + t / (9 * S))^(-1)
```
Where `S` is stability (days until recall drops to 90%) and `t` is days since last review.

**Stability update (correct response):**
```
S_new = S * (1 + e^(w_8) * (11 - D) * S^(-w_9) * (e^(w_10 * (1 - R)) - 1))
```

**Stability update (lapse / incorrect):**
```
S_new = w_11 * D^(-w_12) * ((S + 1)^w_13 - 1) * e^(w_14 * (1 - R))
```

**Difficulty update:**
```
D_new = D - w_6 * (grade - 3)
```

**15 Learnable Parameters (w_0 through w_14):**
- Optimized per user via gradient descent on their review history
- Can also use global defaults from 10K-user training set
- Converges with approximately 20 reviews per card

**FSRS vs SM-2 Performance (Ye et al., 2022):**

| Metric | SM-2 | FSRS-4 | Improvement |
|--------|------|--------|-------------|
| Log-loss | 0.3684 | 0.3276 | 11.1% |
| RMSE(bins) | 0.0814 | 0.0447 | 45.1% |
| AUC | N/A | 0.773 | N/A |

**Cena Integration Path:**

`ConceptMasteryState` already has FSRS-compatible fields:
```csharp
// src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs
public float Stability { get; init; }    // FSRS S parameter
public float Difficulty { get; init; }    // FSRS D parameter (0-10)
```

Phase 3 migration path (specified in `docs/mastery-engine-architecture.md`):
1. Run both HLR and FSRS in parallel ("shadow mode") for 2 months
2. Compare predicted recall vs actual recall for both models
3. A/B test retention rates
4. If FSRS wins, replace HLR computation in `HlrCalculator.cs`

**SRSActor Design for FSRS:**
```
SRSActor (child of StudentActor):
  State:
    - Map<ConceptId, FsrsState> where FsrsState = { S, D, R, lastReview, reps }
    - FsrsWeights: float[15] (per-student or global defaults)

  Messages:
    - ReviewCompleted(conceptId, grade: 1-4)
      -> Compute S_new, D_new
      -> Schedule next review at t where R(t, S_new) = 0.90
      -> Emit SpacedRepetitionScheduled event

    - GetReviewQueue(maxItems)
      -> Sort all concepts by R ascending (most forgotten first)
      -> Filter where R < 0.90
      -> Return top N

    - TrainWeights(reviewHistory)
      -> Gradient descent on w_0..w_14
      -> Minimize log-loss between predicted R and actual recall
      -> Store updated weights
```

**Mobile UI -- Review Session Screen:**
```
+----------------------------------------------+
|  Daily Review     3 of 12 due                |
|                                               |
|  [Concept: Chain Rule]                        |
|                                               |
|  d/dx [f(g(x))] = ?                          |
|                                               |
|  Think... then tap to reveal                  |
|                                               |
|  [Show Answer]                                |
|                                               |
|  ---- After reveal: ----                      |
|                                               |
|  f'(g(x)) * g'(x)                            |
|                                               |
|  Rate your recall:                            |
|  [Again] [Hard] [Good] [Easy]                 |
|   1 day   3 d    7 d    14 d                  |
|                                               |
|  Progress: [====>-------] 3/12                |
+----------------------------------------------+
```

**Design Note:** For Cena, the self-rating is replaced with automatic grading. The student answers a question; correctness + response time + hint usage determines the grade programmatically:
- Grade 1 (Again): incorrect, needed all hints
- Grade 2 (Hard): incorrect first attempt, correct with hints
- Grade 3 (Good): correct, normal response time
- Grade 4 (Easy): correct, fast response time, no hints

**Citations:**
- Ye, J. (2022). A Stochastic Shortest Path Algorithm for Optimizing Spaced Repetition Scheduling. arXiv:2402.01032.
- FSRS GitHub: https://github.com/open-spaced-repetition/fsrs4anki
- FSRS4Anki Benchmark: https://github.com/open-spaced-repetition/fsrs-benchmark

---

### 1.4 Anki's Scheduling Algorithm

**Current State (2024):** Anki offers both SM-2 (legacy default) and FSRS (opt-in, becoming default). Most serious Anki users have switched to FSRS.

**Anki's SM-2 Modifications:**
- Added "learning steps" before cards enter the SM-2 cycle (e.g., 1 min, 10 min warmup)
- Added "relearning steps" for lapsed cards
- Modified interval fuzz (random +/-5% to prevent clustering)
- Added maximum interval cap (default 36500 days)
- Added "bury siblings" to prevent related cards appearing same day

**Anki's FSRS Integration (2023+):**
- Users can optimize FSRS parameters from their own review history
- Parameters trained client-side (no data leaves the device)
- "Desired retention" setting: user picks target R (default 0.9), FSRS computes intervals
- Experimental: FSRS-5 with same-day reviews modeled as short-term memory

**What Cena Should Borrow from Anki:**
1. The concept of "desired retention" as a configurable parameter (teacher/student choice)
2. Learning steps before entering the SRS cycle (warm-up questions)
3. Bury siblings -- don't review related concepts in the same session (already partially implemented via interleaving logic in `ItemSelector.cs`)
4. FSRS parameter optimization from individual review history

**What Cena Should NOT Borrow:**
1. Self-rating (replaced by automatic grading)
2. Flashcard-only format (Cena uses multi-format questions)
3. Manual deck management (Cena's concept graph auto-selects)
4. No adaptive difficulty within a card (Cena adjusts Bloom's level)

---

### 1.5 Implementing SRS in a Mobile Learning App

**Notification Scheduling Architecture:**

```
StudentActor (background, server-side):
  Every 6 hours: MasteryDecayScanner
    -> For each concept where R < threshold:
       -> Compute review_priority = (0.85 - R) * (1 + log2(descendant_count))
       -> Emit SpacedRepetitionDue event to NATS

OutreachActor (server-side):
  Subscribes to SpacedRepetitionDue
    -> Check student notification preferences
    -> Check do-not-disturb window
    -> Compute optimal notification time (see Section 12)
    -> Queue push notification via FCM
    -> Message: "3 concepts need review -- your Chain Rule memory is at 68%"

Flutter Mobile (client-side):
  On app open:
    -> Fetch /api/v1/mastery/{studentId}/review-schedule
    -> Show "Daily Review" badge on home screen with count
    -> Sort by priority descending
    -> Pre-load first 3 review questions
```

**Review Session Flow:**
```
1. Student opens Daily Review
2. Show count: "12 concepts need review"
3. Present first concept (highest priority = most decayed)
4. Student answers question (not flashcard -- full question)
5. Automatic grading (BKT update + HLR/FSRS update)
6. Show feedback + updated recall probability
7. Animate concept's memory curve extending (half-life grew)
8. Next concept
9. After session: show summary
   - "You reviewed 12 concepts"
   - "Average recall improved from 62% to 94%"
   - "Next review scheduled in 2 days"
   - XP earned: +60
```

**UI Design Patterns for Review:**
1. **Review badge on home screen** -- red dot with count of due items
2. **Recall probability shown per concept** -- percentage with color coding
3. **Memory curve visualization** -- small sparkline showing decay trajectory
4. **Estimated review time** -- "~8 min to review all due concepts"
5. **Streak integration** -- "Review counts toward your daily streak"
6. **Post-review celebration** -- confetti animation when all due items reviewed

---

## 2. Active Recall

### 2.1 Why Active Recall Beats Passive Review

**The Research:**
Karpicke & Blunt (2011, *Science*) demonstrated that retrieval practice produces 50% more long-term retention than elaborative studying with concept maps. Students who practiced retrieval recalled 67% of material after one week vs. 45% for those who studied repeatedly.

**The Mechanism:**
- **Retrieval strengthens memory traces** -- the act of pulling information from memory strengthens the neural pathways for that information (bifurcation model)
- **Desirable difficulty** -- the effort of retrieval causes deeper encoding than passive re-reading
- **Elaborative retrieval** -- during recall, students naturally activate related knowledge, strengthening interconnections
- **Metacognitive calibration** -- retrieval gives students accurate feedback on what they actually know vs. think they know

**Key Finding (Roediger & Karpicke, 2006):**
Students who took a practice test retained 61% after 1 week vs. 40% who re-studied. Even students who performed poorly on the practice test retained more than those who re-studied successfully.

**Cena Implementation:**
All Cena learning interactions are retrieval-based by design:
- Questions require active recall, not passive recognition (even MCQ distractors are designed to force discrimination)
- The Feynman Protocol (TASK-IQ-08) requires explanation before seeing options
- The Socratic Protocol (TASK-IQ-07) uses guided questions that force retrieval

**Citations:**
- Karpicke, J.D. & Blunt, J.R. (2011). Retrieval practice produces more learning than elaborative studying with concept mapping. *Science*, 331(6018), 772-775. DOI: 10.1126/science.1199327
- Roediger, H.L. & Karpicke, J.D. (2006). Test-enhanced learning. *Psychological Science*, 17(3), 249-255. DOI: 10.1111/j.1467-9280.2006.01693.x
- Rowland, C.A. (2014). The effect of testing versus restudy on retention: A meta-analytic review. *Psychological Bulletin*, 140(6), 1432-1463. DOI: 10.1037/a0037559

---

### 2.2 Flashcard Design Patterns for Mobile

**Atomic Flashcards (One Concept per Card):**
- Each card tests exactly one knowledge component
- Minimum information principle: what is the smallest piece that forces retrieval?
- Bad: "Explain the chain rule, its proof, and three applications"
- Good: "d/dx [f(g(x))] = ?" -> "f'(g(x)) * g'(x)"

**Cloze Deletion Cards:**
- Fill in the blank within context
- "The derivative of sin(x) is {{c1::cos(x)}}"
- Forces recall while providing retrieval cues
- Good for formulas, definitions, theorem statements

**Image Occlusion:**
- Hide part of a diagram and ask what belongs there
- Excellent for geometry, circuit diagrams, molecular structures
- Mobile implementation: tap to reveal hidden region

**Bi-Directional Cards:**
- Front: "What is the integral of 1/x?" -> Back: "ln|x| + C"
- Front: "ln|x| + C is the integral of ___" -> Back: "1/x"
- Tests both recognition and production

**Cena-Specific Card Formats:**
```
Format: Concept-Recall Card
+----------------------------------------------+
|  Subject: Calculus > Chain Rule               |
|                                               |
|  If y = sin(x^2), find dy/dx                  |
|                                               |
|  [Text input field]                           |
|                                               |
|  [Check Answer]  [Need Hint]                  |
+----------------------------------------------+

Format: Formula-Cloze Card
+----------------------------------------------+
|  Complete the formula:                        |
|                                               |
|  d/dx [f(g(x))] = f'(___) * ___              |
|                                               |
|  [g(x)]  [g'(x)]  [f(x)]  [x]               |
+----------------------------------------------+

Format: Diagram-Recall Card
+----------------------------------------------+
|  [Graph of f(x) shown]                        |
|                                               |
|  At x = 2, is f'(x) positive, negative,      |
|  or zero?                                     |
|                                               |
|  [Positive] [Negative] [Zero]                 |
+----------------------------------------------+
```

---

### 2.3 Free Recall vs. Cued Recall vs. Recognition

**Hierarchy of Retrieval Difficulty:**

| Type | Difficulty | Example | Learning Value |
|------|-----------|---------|----------------|
| Free recall | Highest | "List all trig identities you know" | Highest -- deepest encoding |
| Cued recall | Medium | "sin^2(x) + cos^2(x) = ?" | High -- retrieval with context |
| Recognition (MCQ) | Lowest | "sin^2(x) + cos^2(x) = ? (a) 1 (b) 2 (c) 0 (d) -1" | Moderate -- can be gamed by elimination |

**Cena Design Decision:** Use a mix, gated by mastery level:
- **Novice (mastery < 0.30):** Recognition (MCQ) -- low barrier, build confidence
- **Developing (0.30-0.60):** Cued recall (fill-in-blank, numeric entry)
- **Proficient (0.60-0.85):** Free recall (expression entry, worked solutions)
- **Mastered (>0.85):** Free recall + explanation (Feynman protocol)

This is already specified in `TASK-IQ-01` in `docs/interactive-questions-tasks.md`.

---

### 2.4 Self-Assessment Accuracy Problem

**Students Are Bad at Judging What They Know:**

Dunning-Kruger Effect (Kruger & Dunning, 1999):
- Bottom-quartile students overestimated their percentile rank by 46 points
- Top-quartile students underestimated by 14 points
- Calibration improves with expertise but remains imperfect

**Illusions of Competence (Bjork, 2011):**
- **Familiarity illusion:** Re-reading feels productive but does not improve retrieval ability
- **Fluency illusion:** Easy-to-read material feels better learned
- **Foresight bias:** Students predict future recall based on current accessibility

**Why This Matters for SRS:**
SM-2 relies on self-rated quality (0-5 scale). Research shows:
- Students systematically overrate easy items and underrate hard items (Kornell & Bjork, 2007)
- Self-rating accuracy improves with practice but never reaches reliability needed for scheduling
- Automatic grading (Cena's approach) is superior for scheduling purposes

**Cena's Solution:**
Replace self-assessment with behavioral signals:
1. **Correctness** -- binary, from automated evaluation
2. **Response time** -- fast+correct = high confidence; slow+correct = effortful
3. **Hint usage** -- escalating hints indicate lower confidence
4. **Answer changes** -- changing answer before submit = uncertainty
5. **Quality quadrant** -- `MasteryQualityClassifier` in `src/actors/Cena.Actors/Mastery/MasteryQualityClassifier.cs`

Confidence ratings ARE still valuable for metacognition (see Section 7), but they should NOT drive scheduling.

**Citations:**
- Kruger, J. & Dunning, D. (1999). Unskilled and unaware of it. *Journal of Personality and Social Psychology*, 77(6), 1121-1134. DOI: 10.1037/0022-3514.77.6.1121
- Bjork, R.A., Dunlosky, J. & Kornell, N. (2013). Self-regulated learning: Beliefs, techniques, and illusions. *Annual Review of Psychology*, 64, 417-444. DOI: 10.1146/annurev-psych-113011-143823
- Kornell, N. & Bjork, R.A. (2007). The promise and perils of self-regulated study. *Psychonomic Bulletin & Review*, 14(2), 219-224. DOI: 10.3758/BF03194055

---

## 3. Interleaving & Varied Practice

### 3.1 Interleaving vs. Blocking

**The Research:**
Rohrer & Taylor (2007) found that interleaved practice (mixing problem types) produced 43% higher scores on delayed tests compared to blocked practice (practicing one type at a time), despite students rating blocked practice as more effective.

**Why Interleaving Works:**
1. **Discrimination:** Students must identify which strategy applies, not just execute a known strategy
2. **Retrieval practice:** Switching topics forces retrieval of the previous topic's approach
3. **Spacing effect:** Interleaving naturally creates spacing between same-topic practice
4. **Contextual interference:** The difficulty of switching improves long-term retention

**Meta-Analysis (Brunmair & Richter, 2019):**
- Effect size: Cohen's d = 0.42 favoring interleaving over blocking
- Effect is stronger for: mathematics, visual discrimination tasks, motor skills
- Effect is weaker for: highly similar categories (too confusing), complete novices (need blocking first)

**The Novice Exception:**
For students with zero prior knowledge of a topic, blocking IS better initially. The sequence should be:
1. **Block** new concept for 3-5 problems (build basic schema)
2. **Interleave** once accuracy on individual concept exceeds ~60%
3. **Fully interleave** once all relevant concepts exceed ~70% accuracy

**Cena Implementation:**
Already partially implemented in `ItemSelector.cs`:
```csharp
// MST-010: Interleaving with probability 0.5
// Different concept from last item with probability 0.5
```

The interleaving probability should be adaptive:
```
interleave_probability(concept_c) =
  if mastery(c) < 0.30: 0.0  (block -- student is still learning basics)
  if mastery(c) < 0.60: 0.3  (light interleaving)
  if mastery(c) < 0.80: 0.5  (moderate interleaving)
  if mastery(c) >= 0.80: 0.7 (heavy interleaving for deep learning)
```

**Mobile UI -- Interleaving Notification:**
When topic switches during a session, do NOT show "Now switching to Geometry!" -- research shows invisible switches produce better learning. However, after the session summary, show: "You practiced 4 different topics in this session -- research shows this helps you learn 43% more."

**Citations:**
- Rohrer, D. & Taylor, K. (2007). The shuffling of mathematics problems improves learning. *Instructional Science*, 35, 481-498. DOI: 10.1007/s11251-007-9015-8
- Brunmair, M. & Richter, T. (2019). Similarity matters: A meta-analysis of interleaved learning. *Psychological Bulletin*, 145(11), 1029-1052. DOI: 10.1037/bul0000209
- Kornell, N. & Bjork, R.A. (2008). Learning concepts and categories: Is spacing the "enemy of induction"? *Psychological Science*, 19(6), 585-592. DOI: 10.1111/j.1467-9280.2008.02127.x

---

### 3.2 Desirable Difficulties

**Bjork's Framework (1994):**
Conditions that make learning harder during acquisition but improve long-term retention and transfer:

1. **Spacing** -- distributed practice > massed practice
2. **Interleaving** -- mixed practice > blocked practice
3. **Testing** -- retrieval practice > re-study
4. **Varying conditions** -- changing context between study sessions
5. **Reducing feedback** -- delayed feedback > immediate feedback (for some tasks)
6. **Generation** -- generating answers > reading answers

**The Paradox:**
Desirable difficulties make students feel like they are learning LESS (lower performance during practice) while they are actually learning MORE (higher retention on delayed tests). This creates a UX challenge: students may rate the app poorly because it "feels harder."

**Cena's Response:**
1. Show effort-based praise: "That was a tough mix of topics -- your brain is building stronger connections"
2. Show delayed-test evidence: "Last week's interleaved practice: you scored 85% on the surprise quiz vs. 62% for similar concepts you studied in blocks"
3. Frame difficulty as progress: "You're in the Challenge Zone -- this is where the deepest learning happens"
4. Never show raw accuracy during interleaved practice (it will be lower and demotivating) -- show streak count and XP instead

**Citations:**
- Bjork, R.A. (1994). Memory and metamemory considerations in the training of human beings. In J. Metcalfe & A. Shimamura (Eds.), *Metacognition: Knowing About Knowing*, pp. 185-205. MIT Press.
- Bjork, E.L. & Bjork, R.A. (2011). Making things hard on yourself, but in a good way. In M.A. Gernsbacher et al. (Eds.), *Psychology and the Real World*, pp. 56-64. Worth Publishers.

---

## 4. Testing Effect (Retrieval Practice)

### 4.1 Frequent Low-Stakes Testing

**Core Finding (Roediger & Karpicke, 2006):** Taking a test produces better long-term retention than an equal amount of study time. This is the "testing effect" or "retrieval practice effect."

**Key Studies:**

| Study | Finding | Effect Size |
|-------|---------|-------------|
| Roediger & Karpicke (2006) | Test > re-study after 1 week | 61% vs 40% |
| McDaniel et al. (2007) | Classroom quizzes improved exam scores by 10% | d = 0.60 |
| Adesope et al. (2017, meta-analysis) | Testing effect across 272 studies | g = 0.50 |

**Design Principles for Testing-as-Learning:**
1. **Low stakes** -- no grade penalty for wrong answers during practice
2. **Frequent** -- short quizzes every session, not long tests every month
3. **Feedback-rich** -- show correct answer and explanation after each question
4. **Difficulty-appropriate** -- target ~85% success rate (see Section 9)
5. **Cumulative** -- include questions from earlier topics (spaced retrieval)

**Quiz Design Template for Learning:**
```
Session Quiz (5-8 questions, ~5 minutes):
  - 2 questions from today's topic (current learning)
  - 2 questions from last session's topic (1-day spacing)
  - 2 questions from last week's topics (7-day spacing)
  - 1-2 review questions (R < 0.85, from SRS queue)

After each question:
  - Immediate correctness feedback
  - Brief explanation (L1 layer)
  - If wrong + confused: deeper explanation (L2/L3 layers)
  - BKT update + HLR half-life update

After quiz:
  - Summary: "5/7 correct, 2 concepts need more practice"
  - XP earned: based on correct answers + difficulty
  - Concepts that need review added to SRS queue
```

---

### 4.2 Pre-Testing (Testing Before Teaching)

**Counter-Intuitive Finding:** Richland et al. (2009) demonstrated that testing students on material BEFORE they have learned it improves subsequent learning, even when students get the pre-test questions wrong.

**Why Pre-Testing Works:**
1. **Priming:** Failed retrieval attempts create "retrieval routes" that are strengthened when the answer is later encountered
2. **Curiosity:** Getting a question wrong creates a knowledge gap that motivates learning
3. **Attention direction:** Pre-test questions highlight what to focus on during subsequent study
4. **Schema activation:** Even incorrect attempts activate relevant prior knowledge

**Implementation for Cena:**

Pre-Test Flow:
```
1. Before teaching new concept C:
   - Present 2-3 questions about C (student will likely get them wrong)
   - Do NOT count pre-test responses in BKT mastery (flag as pre_test=true)
   - DO record error types for MCM methodology routing

2. After pre-test:
   - "Don't worry about your answers -- research shows trying first helps you learn better"
   - Present teaching content (methodology-appropriate)

3. After teaching:
   - Post-test with similar questions
   - NOW count responses in BKT
   - Show improvement: "You got 0/3 before learning, now 2/3 -- great progress!"
```

**KST Diagnostic is Already a Pre-Test:**
Cena's onboarding diagnostic (`DiagnosticEngine.cs` in `src/actors/Cena.Actors/Mastery/`) is effectively a pre-test. It exposes students to concepts they may not know, creating priming effects for subsequent learning. This is a feature, not a bug.

---

### 4.3 Immediate vs. Delayed Feedback

**The Nuanced Finding:**

| Feedback Timing | Best For | Mechanism |
|----------------|----------|-----------|
| Immediate | Error correction, procedural skills | Prevents error consolidation |
| Delayed (minutes) | Conceptual understanding | Forces additional retrieval attempt |
| Delayed (1+ day) | Long-term retention of facts | Spacing effect on feedback itself |

**Butler et al. (2007):** Delayed feedback produced 15% higher retention than immediate feedback on a 1-week delayed test. However, immediate feedback was better for preventing persistent errors.

**Cena's Hybrid Approach:**
1. **Immediate:** Show correctness (checkmark/X) immediately after submission
2. **Brief delay (2-3 seconds):** Show explanation -- the delay forces a moment of reflection
3. **Delayed consolidation:** In the next session, revisit incorrectly answered questions (SRS scheduling handles this)
4. **Error-type dependent:** For procedural errors, show immediate correction. For conceptual errors, ask a follow-up question first (Socratic protocol).

**Citations:**
- Butler, A.C., Karpicke, J.D. & Roediger, H.L. (2007). The effect of type and timing of feedback on learning from multiple-choice tests. *Journal of Experimental Psychology: Applied*, 13(4), 273-281. DOI: 10.1037/1076-898X.13.4.273
- Richland, L.E., Kornell, N. & Kao, L.S. (2009). The pretesting effect: Do unsuccessful retrieval attempts enhance learning? *Journal of Experimental Psychology: Applied*, 15(3), 243-257. DOI: 10.1037/a0016496

---

## 5. Elaborative Interrogation & Self-Explanation

### 5.1 "Why Does This Make Sense?" Prompts

**The Technique:** After encountering a fact or concept, students are prompted with "Why?" or "How does this relate to what you already know?"

**Research (Dunlosky et al., 2013 -- mega-review in *Psychological Science in the Public Interest*):**
- Elaborative interrogation rated as having "moderate utility" for learning
- Self-explanation rated as having "moderate utility"
- Both significantly outperform highlighting, re-reading, and summarization

**Effect Sizes:**
- Elaborative interrogation: d = 0.40-0.60 across studies (Pressley et al., 1992)
- Self-explanation: d = 0.50-0.80 (Chi et al., 1989)

**Cena Implementation -- Prompt Templates:**

```
After Correct Answer:
  "Why does [answer] make sense here?"
  "Can you explain this in your own words?"
  "How is this different from [common misconception]?"

After Incorrect Answer:
  "Why did you choose [selected answer]?"
  "What would need to be true for [selected answer] to be correct?"
  "What concept is this question testing?"

After Mastering a Concept:
  "Explain [concept] as if teaching a friend"
  "Give a real-world example where [concept] applies"
  "How does [concept] connect to [prerequisite concept]?"
```

These prompts feed into the Feynman Protocol Actor (`TASK-IQ-08`) and the `TutorActor` conversational system.

### 5.2 Teach-Back Feature (Peer Explanation)

**The Research (Chi et al., 2014):** Students who explain material to peers learn more than those who study alone, even if the peer is not paying attention. The act of generating an explanation forces:
1. Knowledge organization
2. Gap detection (realizing you cannot explain something reveals gaps)
3. Elaboration (connecting new knowledge to prior knowledge)

**Cena "Teach-Back" Feature Design:**

```
Teach-Back Mode (unlocked at mastery > 0.80):
+----------------------------------------------+
|  Teach This Concept                           |
|                                               |
|  Concept: Quadratic Formula                   |
|                                               |
|  Write an explanation that would help a        |
|  classmate understand this concept.            |
|  Include:                                      |
|  - What the formula is                         |
|  - When to use it                              |
|  - A worked example                            |
|                                               |
|  [Text area - min 50 words]                    |
|                                               |
|  [Submit Explanation]                          |
+----------------------------------------------+

After Submission:
  - LLM evaluates explanation quality (L3 rubric)
  - Score dimensions: accuracy, completeness, clarity, examples
  - Feedback: "Great explanation! Consider also mentioning
    the discriminant and what it tells you about the roots."
  - XP bonus: +25 XP for teaching (2.5x normal question XP)
  - Badge: "Teacher" badge after 10 teach-back submissions
```

**Citations:**
- Chi, M.T.H. et al. (1989). Self-explanations: How students study and use examples in learning to solve problems. *Cognitive Science*, 13(2), 145-182. DOI: 10.1016/0364-0213(89)90002-5
- Dunlosky, J. et al. (2013). Improving students' learning with effective learning techniques. *Psychological Science in the Public Interest*, 14(1), 4-58. DOI: 10.1177/1529100612453266
- Chi, M.T.H. et al. (2014). Learning from observing a tutor. *Journal of Educational Psychology*, 106(3), 669-691.

---

## 6. Dual Coding & Multimedia Learning (Mayer)

### 6.1 Mayer's Principles Applied to Mobile

Richard Mayer's research provides the most rigorously tested design principles for educational multimedia. Here is each principle with its mobile implementation specification.

**Principle 1: Multimedia Principle**
People learn better from words AND pictures than from words alone.
- Effect size: d = 1.39 (Mayer, 2009, meta-analysis of 11 studies)
- Implementation: Every concept explanation should include a diagram, graph, or visual model
- Mobile: Use SVG/vector graphics for crisp rendering at any scale
- For math: LaTeX-rendered equations alongside visual geometric representations

**Principle 2: Spatial Contiguity**
People learn better when corresponding words and pictures are near each other rather than far apart.
- Effect size: d = 1.12 (Mayer, 2009)
- Mobile violation to avoid: Explanation text in a scrollable area BELOW a diagram that requires scrolling back
- Implementation: Labels integrated INTO diagrams, not listed separately. Text and diagrams must be visible simultaneously without scrolling.

```
WRONG:                         RIGHT:
+------------+                 +-------------------+
| [Diagram]  |                 |     y             |
+------------+                 |  ^   /\           |
| A is the   |                 |  |  /  \ <- f(x)  |
| vertex...  |                 |  | / max \        |
| B is the   |                 |  |/______\__> x   |
| x-intercept|                 |  A = vertex       |
+------------+                 +-------------------+
(must scroll to see both)      (integrated labels)
```

**Principle 3: Temporal Contiguity**
People learn better when corresponding narration and animation are presented simultaneously rather than successively.
- Effect size: d = 1.31 (Mayer, 2009)
- Mobile: When showing a step-by-step solution, each step should appear WITH its annotation simultaneously, not "first show all steps, then explain"
- Animation: Animated solution walkthroughs where each step highlights and explains at the same time

**Principle 4: Modality Principle**
People learn better from graphics + narration than from graphics + on-screen text.
- Effect size: d = 0.72 (Mayer, 2009)
- Mobile: Offer audio explanations that play while showing diagrams. Do NOT show the text transcript of the narration overlaid on the diagram.
- Implementation: TTS (text-to-speech) for explanations with visual-only diagrams. Toggle: "Read aloud" button.

**Principle 5: Redundancy Principle**
People learn better from graphics + narration than from graphics + narration + on-screen text.
- Effect size: d = 0.72 (Mayer, 2009)
- Mobile violation to avoid: Showing the same explanation as BOTH text and audio simultaneously
- Implementation: When audio is playing, hide the text explanation. Show text only if audio is off.

**Principle 6: Coherence Principle**
People learn better when extraneous material is excluded.
- Effect size: d = 0.97 (Mayer, 2009)
- Mobile: No decorative images, no background music during learning, no "fun facts" sidebars during problem-solving
- Gamification elements (XP, streaks) should appear BETWEEN questions, not during question presentation
- No ads. Ever. (Cena is subscription-based.)

**Principle 7: Signaling Principle**
People learn better when cues are added that highlight the organization of essential material.
- Effect size: d = 0.46 (Mayer, 2009)
- Mobile: Use color-coding for different parts of equations, numbered steps in solutions, highlighted key terms
- LaTeX rendering: color-code variables (x in blue, constants in black, operators in gray)

**Principle 8: Segmenting Principle**
People learn better when a lesson is presented in learner-paced segments rather than as a continuous unit.
- Effect size: d = 0.79 (Mayer, 2009)
- Mobile: Present solutions step-by-step with "Next Step" buttons, not all at once
- Allow students to control pacing -- each step reveals on tap/swipe

**Screen Template -- Multimedia Explanation:**
```
+----------------------------------------------+
|  Chain Rule Explained                         |
|                                               |
|  Step 1 of 4                                  |
|                                               |
|  [Interactive diagram showing f(g(x))]        |
|  [Arrows showing "outer" and "inner" funcs]   |
|                                               |
|  The chain rule says:                         |
|  differentiate the OUTER function first,      |
|  then multiply by the derivative of the       |
|  INNER function.                              |
|                                               |
|  [Play Audio]              [Next Step ->]     |
+----------------------------------------------+
```

**Citations:**
- Mayer, R.E. (2009). *Multimedia Learning* (2nd ed.). Cambridge University Press.
- Mayer, R.E. (2014). Cambridge Handbook of Multimedia Learning (2nd ed.). Cambridge University Press.
- Mayer, R.E. & Fiorella, L. (2014). Principles for reducing extraneous processing. *Cambridge Handbook of Multimedia Learning*, 279-315.

---

## 7. Metacognition & Self-Regulated Learning

### 7.1 Confidence Ratings ("How Sure Are You?")

**The Research:**
Metacognitive monitoring -- the ability to accurately assess one's own knowledge -- is a strong predictor of academic success (Dunlosky & Rawson, 2012). Calibration improves with practice.

**Judgment of Learning (JOL) Protocol:**
After answering a question, students rate their confidence:
```
"How confident are you in your answer?"
[Not at all] [Slightly] [Somewhat] [Very] [Completely]
    0.2         0.4        0.6       0.8       1.0
```

**What to Do With Confidence Data:**

1. **Calibration Tracking:**
   ```
   For each confidence level, track:
   - average_confidence at that level
   - actual_accuracy at that level

   Perfect calibration: confidence == accuracy
   Overconfident: confidence > accuracy (most students, most of the time)
   Underconfident: confidence < accuracy (rare, usually high achievers)
   ```

2. **Metacognitive Feedback:**
   "You rated yourself 'Very Confident' on 8 questions. You got 5 of those right (63%). Your confidence is running ahead of your knowledge -- keep practicing!"

3. **Prioritize Overconfident Items:**
   Items where students are confident but wrong are the most dangerous -- they represent unknown unknowns. These should be prioritized in SRS review.

**Mobile UI -- Confidence Slider:**
```
+----------------------------------------------+
|  How sure are you?                            |
|                                               |
|  [====O-----------]  40%                      |
|  Not sure    <->    Very sure                 |
|                                               |
|  [Submit Answer]                              |
+----------------------------------------------+

After accumulating data:
+----------------------------------------------+
|  Your Calibration                             |
|                                               |
|  Confidence |  Accuracy  | Calibration        |
|  90%+       |  72%       | Overconfident      |
|  70-90%     |  68%       | Slightly over      |
|  50-70%     |  55%       | Well calibrated    |
|  <50%       |  31%       | Well calibrated    |
|                                               |
|  Tip: When you feel "very sure," double-check |
|  your work -- you may be overconfident.       |
+----------------------------------------------+
```

**ConceptMasteryState already has this field:**
```csharp
// src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs
public float SelfConfidence { get; init; }  // 0.0-1.0, student self-assessment
```

---

### 7.2 Knowledge Maps

**Purpose:** Visual representation of what the student knows vs. does not know, enabling self-directed study planning.

**Cena Already Has This:**
The knowledge graph visualization is specified in `mastery-engine-architecture.md` and implemented in the Flutter app at `src/mobile/lib/features/knowledge_graph/`.

**Enhancement -- Metacognitive Knowledge Map:**
```
Standard map: nodes colored by mastery level
Enhanced map: nodes colored by CALIBRATION
  - Green: high mastery + high recall + well-calibrated
  - Yellow: moderate mastery, needs more practice
  - Orange: HIGH CONFIDENCE + LOW MASTERY (dangerous!)
  - Red: low mastery + low recall
  - Pulsing: decaying (mastered but recall dropping)

Student action: "I want to study [concept]" -> tap node to start session
System suggestion: "These 3 concepts are your biggest blind spots" (high confidence, low accuracy)
```

---

### 7.3 Calibration Graph (Confidence vs. Performance)

**The Most Powerful Metacognitive Tool:**
A scatter plot where:
- X-axis = student's confidence rating for each question
- Y-axis = whether they got it right (0 or 1, averaged into bins)
- Perfect calibration = 45-degree line
- Above the line = overconfident
- Below the line = underconfident

**Implementation:**
```
CalibrationActor (child of StudentActor):
  State:
    - confidence_bins: Map<int, {total: int, correct: int}>
    - bins: [0-10%, 10-20%, ..., 90-100%]

  On each answer with confidence rating:
    - bin = floor(confidence * 10)
    - bins[bin].total++
    - if correct: bins[bin].correct++

  Query:
    - For each bin: accuracy = correct / total
    - Return [(bin_center, accuracy)] for charting
```

**Mobile UI -- Calibration Chart:**
```
+----------------------------------------------+
|  Do You Know What You Know?                   |
|                                               |
|  100%|          * .                            |
|   80%|        *   .                            |
|   60%|     *     .                             |
|   40%|   *      .  <- You're here              |
|   20%|  *     .    (overconfident)             |
|    0%|-------.------                           |
|      0%  20%  40%  60%  80% 100%              |
|        Your Confidence Rating                  |
|                                               |
|  The dotted line is perfect calibration.       |
|  Your dots are ABOVE the line -- you often     |
|  think you know things you don't yet.          |
|                                               |
|  Tip: Practice the concepts where you feel     |
|  most confident -- you might be surprised!     |
+----------------------------------------------+
```

**Citations:**
- Dunlosky, J. & Rawson, K.A. (2012). Overconfidence produces underachievement: Inaccurate self evaluations undermine students' learning and retention of material. *Learning and Instruction*, 22(4), 271-280. DOI: 10.1016/j.learninstruc.2011.08.003
- Hacker, D.J. et al. (2008). Test prediction and performance in a classroom context. *Journal of Educational Psychology*, 100(1), 150-163.

---

## 8. Bloom's Taxonomy in App Design

### 8.1 The Six Levels Applied to Mobile Activities

**Cena uses Bloom's 0-6 scale**, already tracked in `ConceptMasteryState.BloomLevel` and progression logic specified in `TASK-IQ-04`.

| Level | Name | Activity Type | Question Format | Example (Calculus) |
|-------|------|--------------|-----------------|-------------------|
| 1 | Remember | Recall facts, definitions, formulas | MCQ, fill-blank, flashcard | "State the chain rule formula" |
| 2 | Understand | Explain, paraphrase, interpret | Explain-in-own-words, match | "In your own words, what does the chain rule do?" |
| 3 | Apply | Use in routine problems | Numeric entry, expression | "Find d/dx of sin(3x)" |
| 4 | Analyze | Break down, compare, contrast | Multi-step, compare approaches | "Compare the chain rule and product rule -- when would you use each?" |
| 5 | Evaluate | Justify, critique, assess | Critique a solution, find errors | "This solution claims d/dx(sin(x^2)) = cos(2x). Find the error." |
| 6 | Create | Design, construct, prove | Open-ended, proof, novel problem | "Create a function that requires both chain rule and quotient rule to differentiate" |

### 8.2 Progressive Question Complexity

**Mastery-Gated Bloom's Progression:**
```
OnConceptAttempted:
  if evaluationScore >= 0.7 AND questionBloomLevel > currentBloomLevel:
    BloomLevel = min(currentBloomLevel + 1, 6)
    emit BloomProgressionUpdated_V1

Question Selection by Bloom's Level:
  - Present questions at currentBloomLevel +/- 1
  - Prefer questions at currentBloomLevel (consolidation)
  - Include 20% questions at currentBloomLevel + 1 (challenge)
  - Never skip levels (no jumping from Remember to Analyze)
```

**Mobile UI -- Bloom's Progress Indicator:**
```
+----------------------------------------------+
|  Chain Rule                                   |
|  Mastery: 78%  |  Bloom's: Apply (3/6)       |
|                                               |
|  [R] [U] [A] [An] [E] [C]                    |
|  [*] [*] [*] [ ]  [ ] [ ]                    |
|   ^   ^   ^                                   |
|  done done current                            |
|                                               |
|  Next challenge: Analyze level questions      |
|  (Compare chain rule with product rule)       |
+----------------------------------------------+
```

---

## 9. Zone of Proximal Development (Vygotsky)

### 9.1 Adaptive Difficulty as Scaffolding

**Vygotsky's ZPD (1978):** The zone between what a learner can do independently and what they can do with guidance. Learning is most effective when it targets the ZPD.

**The 85% Rule (Wilson et al., 2019, *Nature Communications*):**
Computational modeling across diverse learning environments found that the optimal error rate for learning is approximately 15% -- meaning learners should get about 85% of items correct. This maximizes information gain per trial.

**Cena's Implementation:**

Already specified in `mastery-engine-architecture.md`:
```
Item Selection Target: P(correct) ~ 0.85
Elo-predicted correctness: E = 1 / (1 + 10^((D_item - theta_student) / 400))
Pick item where |E - 0.85| is minimized
```

And in `RefreshConceptQueue()` in `learning_session_actor.cs`:
```csharp
// Zone of proximal development: peak priority at P(known) = 0.5
// Uses inverted parabola: priority = 1 - 4 * (p - 0.5)^2
double zpd = 1.0 - 4.0 * Math.Pow(pKnown - 0.5, 2);
```

**Scaffolding Levels (from `ScaffoldingService.cs`):**
```
mastery < 0.20 AND PSI < 0.80 -> Full scaffolding (worked example)
mastery < 0.40 AND PSI >= 0.80 -> Partial scaffolding (faded example)
mastery < 0.70                 -> Hints on request
mastery >= 0.70                -> No scaffolding (independent practice)
```

### 9.2 When to Help vs. Let Student Struggle

**Productive Failure (Kapur, 2008):**
Already deeply integrated into Cena's design (see `docs/focus-degradation-research.md` Section 2.3).

Decision Matrix:
```
                     Accuracy IMPROVING    Accuracy FLAT/DECLINING
Errors VARIED        Productive struggle   Mixed signals --
(exploring)          -> DO NOT INTERVENE   monitor 2 more attempts

Errors REPETITIVE    Methodology issue     Unproductive frustration
(stuck on same       -> Switch method      -> Reduce difficulty
 error pattern)      (MCM routing)         OR switch methodology
```

**Mobile UI -- Struggle Detection:**
```
When productive struggle detected:
  - DO NOT show hint overlay
  - DO NOT suggest easier question
  - Small encouraging message: "Keep going -- trying different
    approaches is how deep learning happens"
  - Track solution diversity (different approaches attempted)

When unproductive frustration detected:
  - Proactive hint: "Would you like a hint?" (not forced)
  - After 3 failed attempts on same error:
    "Let's try a different approach" -> methodology switch
  - Never say "This is too hard for you" -- say
    "Let's build up to this from a different angle"
```

### 9.3 Peer Scaffolding Features

**Research (Vygotsky, 1978; Webb, 1989):**
Peer interaction within the ZPD accelerates learning. The student who explains benefits as much as the student who receives the explanation.

**Feature Design -- Study Partners (Future, requires scale):**
```
Pairing Algorithm:
  - Match students where Student A's mastered concepts
    overlap with Student B's learning frontier
  - Student A "teaches" concept to Student B
  - Both students earn XP
  - Moderated by AI safety guardrails (TutorSafetyGuard)

Async Teaching:
  - Student A writes explanation for concept C
  - Student B (anonymous) reads and rates explanation
  - If rated helpful: Student A earns "Tutor" badge
  - Student A's explanation quality tracked over time
```

**Citations:**
- Vygotsky, L.S. (1978). *Mind in Society: The Development of Higher Psychological Processes*. Harvard University Press.
- Wilson, R.C. et al. (2019). The Eighty Five Percent Rule for optimal learning. *Nature Communications*, 10, 4646. DOI: 10.1038/s41467-019-12552-4
- Kapur, M. (2008). Productive failure in mathematical problem solving. *Cognition and Instruction*, 26(3), 379-424. DOI: 10.1080/07370000802212669

---

## 10. Transfer of Learning

### 10.1 Near Transfer vs. Far Transfer

**Definitions:**
- **Near transfer:** Applying knowledge to a similar context (learned quadratic formula in class -> use on homework)
- **Far transfer:** Applying knowledge to a different context (learned optimization in calculus -> apply to maximize profit in economics)

**The Problem:** Far transfer is notoriously difficult to achieve. Barnett & Ceci (2002) found that transfer success drops dramatically as the distance between learning and application contexts increases.

**What Promotes Transfer (Perkins & Salomon, 1992):**
1. **Varied practice contexts:** Practice the same concept in multiple different problem formats
2. **Explicit abstraction:** Help students identify the underlying principle, not just the procedure
3. **Bridging:** Explicitly connect new concepts to prior knowledge
4. **Hugging:** Practice in contexts as close to the target application as possible

### 10.2 Designing for Transfer in Cena

**Multi-Context Practice:**
```
For each concept, maintain a question pool with diverse contexts:
  Chain Rule questions:
    - Pure math: "Find d/dx of sin(3x^2)"
    - Physics: "A ball's position is s(t) = sin(3t^2). Find velocity."
    - Economics: "Revenue R(q) = 100*ln(q^2 + 1). Find marginal revenue."
    - Geometry: "The radius of a circle changes as r(t) = sqrt(t).
                 How fast is the area changing?"

Question metadata: context_tag (pure_math, physics, economics, geometry, biology)
Selection: prefer contexts the student hasn't seen for this concept
```

**Cross-Subject Connections:**
```
KnowledgeMapActor enhancement:
  When a concept is mastered in one subject, check for related concepts
  in other subjects:

  "You mastered exponential functions in math.
   Did you know radioactive decay in physics uses the same math?
   [Try a physics problem]"

  Cross-links stored in Neo4j:
  (:Concept {subject: "math"})-[:RELATED_TO {type: "application"}]->
    (:Concept {subject: "physics"})
```

**Transfer Assessment Questions:**
Questions tagged with `transfer_type: "near"` or `transfer_type: "far"` in question metadata. Far-transfer questions are Bloom's level 5-6 and apply concepts in novel contexts. Mastery of a concept should require at least one successful far-transfer question.

**Citations:**
- Barnett, S.M. & Ceci, S.J. (2002). When and where do we apply what we learn? A taxonomy for far transfer. *Psychological Bulletin*, 128(4), 612-637. DOI: 10.1037/0033-2909.128.4.612
- Perkins, D.N. & Salomon, G. (1992). Transfer of learning. *International Encyclopedia of Education* (2nd ed.). Pergamon Press.

---

## 11. Motivation & Learning Science Intersection

### 11.1 Growth Mindset Integration (Dweck)

**Core Finding (Dweck, 2006):** Students who believe intelligence is malleable (growth mindset) outperform those who believe it is fixed, particularly when facing challenges.

**Implementation in Cena's Feedback System:**

**Praise effort, strategy, and process -- never intelligence:**
```
WRONG feedback:
  "You're so smart!"
  "You're a natural at math!"
  "This is easy for you!"

RIGHT feedback:
  "Great persistence -- you tried 3 different approaches before finding it!"
  "Your strategy of breaking the problem into parts worked well."
  "That was a hard problem and you stuck with it -- that's how learning happens."
  "You improved from 40% to 75% on algebra this week -- your practice is paying off."
```

**Attribution Theory (Weiner, 1985) in Error Feedback:**
```
After incorrect answer:

Fixed mindset trigger (AVOID):
  "This concept is difficult."
  "Don't worry, not everyone gets this."

Growth mindset trigger (USE):
  "This concept takes practice. Let's try a different approach."
  "Most students need 5-6 attempts to master this. You're on attempt 3."
  "The error you made is common -- here's what it tells us about your thinking."
```

### 11.2 Mastery Orientation vs. Performance Orientation

**Mastery-oriented students** focus on learning and understanding. They persist through difficulty and seek challenges.

**Performance-oriented students** focus on looking smart and outperforming others. They avoid challenges and give up when struggling.

**Cena's Design for Mastery Orientation:**

1. **Show progress, not rankings:** "You mastered 3 new concepts this week" > "You're ranked #5 in class"
2. **Effort-based XP:** XP for completing sessions and attempting hard problems, not just correct answers
3. **No class leaderboards for mastery:** Leaderboards only for XP/engagement, never for accuracy or mastery level
4. **Normalize mistakes:** "You've attempted 847 questions. 312 were wrong on the first try. That's 312 learning opportunities." Show mistakes as a positive.
5. **Personal bests:** "Your best streak on Algebra is 12 correct in a row. Can you beat it?" (competing with yourself, not others)

**Gamification already implements this in `gamification_state.dart`:**
- XP awards for correct answers + session completion + streak
- Badges for engagement milestones, not performance percentiles
- Level progression based on cumulative XP, not accuracy rank

**Citations:**
- Dweck, C.S. (2006). *Mindset: The New Psychology of Success*. Random House.
- Weiner, B. (1985). An attributional theory of achievement motivation and emotion. *Psychological Review*, 92(4), 548-573.
- Yeager, D.S. et al. (2019). A national experiment reveals where a growth mindset improves achievement. *Nature*, 573(7774), 364-369. DOI: 10.1038/s41586-019-1466-y

---

## 12. Sleep & Learning

### 12.1 Memory Consolidation During Sleep

**The Science:**
Sleep plays a critical role in memory consolidation. The hippocampus replays recently learned information during slow-wave sleep, transferring it to long-term storage in the neocortex (Walker, 2017).

**Key Findings:**
- **Gais et al. (2006):** Studying before sleep improved recall by 20.9% compared to studying in the morning and being tested in the evening
- **Stickgold (2005):** Sleep within 24 hours of learning is critical -- without it, the memory trace degrades significantly more than the same time awake
- **Wilhelm et al. (2011):** Only memories that are tagged as "important" or "relevant for the future" are preferentially consolidated during sleep -- telling students "this will be on the test" improves sleep-based consolidation

### 12.2 Evening Review Design

**"Night Review" Feature:**
```
Trigger: Push notification at student's preferred evening time
  (default: 9:00 PM, configurable)

Content: Short review session (5-7 questions, ~5 minutes)
  - Only review questions (concepts already learned, R < 0.90)
  - Low cognitive load (Bloom's level 1-3 only)
  - No new concepts
  - Calm UI: dark mode, no animations, reduced visual stimulation

After completion:
  "Great evening review! Your brain will strengthen these memories
   while you sleep tonight. Good night!"

Push notification copy:
  "5 quick review questions before bed -- helps your brain remember
   what you learned today."
```

### 12.3 Morning Quiz for Overnight Consolidation Check

**"Morning Check" Feature:**
```
Trigger: Push notification at student's preferred morning time
  (default: 7:30 AM on school days, 9:00 AM on weekends)

Content: Quick recall check (3-5 questions, ~3 minutes)
  - Same concepts reviewed last evening
  - Tests overnight consolidation
  - Correct answers = great consolidation!
  - Incorrect answers = schedule for review today

After completion:
  "You remembered 4/5 from last night -- your sleep did its job!
   The one you missed is queued for review today."
```

### 12.4 Notification Timing Algorithm

```
OptimalNotificationTime(student):

  // Evening review
  eveningTime = student.preferredEveningTime ?? 21:00
  if student.lastActiveTime > eveningTime - 2h:
    // Student was recently active, delay slightly
    eveningTime = student.lastActiveTime + 30min

  // Morning check
  morningTime = student.preferredMorningTime ?? 07:30
  if isDayOff(tomorrow):
    morningTime += 90min  // Later on weekends

  // Do-not-disturb integration
  if student.dndStart <= time <= student.dndEnd:
    skip notification

  // Shabbat handling (Israel-specific)
  if isFridayEvening(now) || isSaturday(now):
    skip notification (Shabbat observant setting)

  // Study-before-sleep window
  // Optimal window: 1-2 hours before habitual bedtime
  // (Scullin et al., 2019)
  studyWindow = student.estimatedBedtime - 90min
```

**Do-Not-Disturb Integration:**
```
FlutterLocalNotificationsPlugin:
  - Respect system DND settings
  - App-level DND: configurable quiet hours
  - Exam-week mode: increase notification frequency
  - Vacation mode: suppress all notifications
```

**Citations:**
- Walker, M.P. (2017). *Why We Sleep*. Scribner.
- Gais, S. et al. (2006). Sleep after learning aids memory recall. *Learning & Memory*, 13(3), 259-262. DOI: 10.1101/lm.132106
- Stickgold, R. (2005). Sleep-dependent memory consolidation. *Nature*, 437(7063), 1272-1278. DOI: 10.1038/nature04286
- Wilhelm, I. et al. (2011). Sleep selectively enhances memory expected to be of future relevance. *Journal of Neuroscience*, 31(5), 1563-1569. DOI: 10.1523/JNEUROSCI.3575-10.2011
- Scullin, M.K. et al. (2019). The effects of bedtime writing on difficulty falling asleep. *Journal of Experimental Psychology: General*, 148(1), 139-146.

---

## 13. Actor Mapping

### How Learning Science Principles Map to Cena's Actor System

| Learning Principle | Actor | Responsibility | Key State |
|-------------------|-------|---------------|-----------|
| Spaced Repetition | **SRSActor** (new, child of StudentActor) | FSRS computation, review scheduling, notification timing | `Map<ConceptId, {S, D, R, lastReview, reps}>`, `FsrsWeights[15]` |
| Knowledge State Tracking | **KnowledgeMapActor** (new or extend StudentActor) | Knowledge graph overlay, prerequisite tracking, learning frontier | `Map<ConceptId, ConceptMasteryState>` (existing `MasteryOverlay`) |
| Adaptive Difficulty | **AdaptiveActor** (extend LearningSessionActor) | ZPD targeting (85% rule), scaffolding level, Bloom's progression, interleaving | Item selection queue, scaffold level, interleave probability |
| BKT Mastery | **StudentActor** (existing) | BKT update per attempt, mastery threshold crossing events | `MasteryMap`, `MasteryOverlay` |
| HLR/FSRS Decay | **StudentActor** + **SRSActor** | Half-life computation, decay scanning, review priority | `HlrTimers`, FSRS state |
| Methodology Switching | **StagnationDetectorActor** (existing) | Stagnation composite score, productive failure classification | Sliding window signals, MCM graph |
| Cognitive Load | **LearningSessionActor** (existing) | Fatigue monitoring, session termination, break suggestions | `FatigueScore`, `FatigueWindow` |
| Metacognition | **MetacognitionActor** (new, child of StudentActor) | Confidence calibration, overconfidence detection, JOL tracking | Calibration bins, confidence-accuracy pairs |
| Gamification | **EngagementActor** (existing in concept) | XP, streaks, badges, level progression | Already in Flutter `gamification_state.dart` |
| Sleep/Review Timing | **OutreachActor** (existing in concept) | Evening review scheduling, morning check, DND respect | Notification queue, timing preferences |
| Explanation Quality | **TutorActor** (existing) | Teach-back evaluation, Feynman protocol, L3 explanations | Conversation history, rubric scores |
| Content Selection | **QuestionPoolActor** (existing) | Multi-format selection by mastery phase, context variety | Question index by type, concept, Bloom's level |

### Proposed SRSActor Lifecycle

```
StudentActor
  |
  +-- (child) SRSActor
  |     |
  |     +-- State: FSRS parameters per concept
  |     +-- Handles: ReviewCompleted, GetReviewQueue, TrainWeights
  |     +-- Emits: SpacedRepetitionScheduled, ReviewDue
  |     +-- Timer: Every 6 hours, scan for R < 0.90
  |
  +-- (child) LearningSessionActor  (already exists)
  |     |
  |     +-- Item selection (ZPD + interleaving + SRS queue)
  |     +-- BKT update (inline, hot path)
  |     +-- Fatigue monitoring
  |
  +-- (child) StagnationDetectorActor  (already exists)
  |     |
  |     +-- Methodology switching
  |     +-- Productive failure classification
  |
  +-- (child) MetacognitionActor  (new)
  |     |
  |     +-- Confidence calibration tracking
  |     +-- Overconfidence alerting
  |     +-- Self-regulation prompts
  |
  +-- (child) TutorActor  (already exists)
        |
        +-- Conversational tutoring
        +-- Teach-back evaluation
        +-- L3 personalized explanations
```

### KnowledgeMapActor Design

The KnowledgeMapActor is an extension of the existing `StudentActor.Mastery.cs` partial class. It does not need to be a separate actor because the mastery overlay IS the knowledge map.

```
KnowledgeMapActor responsibilities (in StudentActor):

  Already implemented:
  - MasteryOverlay: per-concept rich state
  - PrerequisiteSatisfactionIndex
  - LearningFrontierCalculator
  - MasteryDecayScanner
  - EffectiveMasteryCalculator

  To add:
  - Transfer tracking: cross-subject concept linkage
  - Metacognitive overlay: confidence vs. actual per concept
  - Context diversity: which problem contexts used per concept
  - Bloom's progression visualization data
```

---

## 14. Comparative Analysis of Existing Apps

### 14.1 Anki

| Feature | Implementation | Learning Science Basis | Cena Comparison |
|---------|---------------|----------------------|-----------------|
| SRS algorithm | SM-2 (default), FSRS (opt-in) | Ebbinghaus, Wozniak | HLR + FSRS migration path |
| Self-rating | 4-button (Again/Hard/Good/Easy) | SM-2 quality scale | Automatic grading (better) |
| Card format | Front/back flashcard | Active recall | Multi-format questions (richer) |
| Interleaving | None (within-deck only) | N/A | Adaptive interleaving (better) |
| Metacognition | None | N/A | Confidence calibration (new) |
| Difficulty | Manual tagging | N/A | Elo-calibrated, adaptive |
| Multimedia | Images, audio, video | Partial Mayer | Full Mayer compliance (better) |
| Scaffolding | None | N/A | 4-level adaptive (better) |
| Knowledge graph | None (flat deck) | N/A | Full prerequisite graph (better) |

**What to Borrow:** FSRS algorithm, spaced repetition UX patterns, "desired retention" concept.
**What to Improve:** Remove self-rating, add adaptive difficulty, add knowledge graph.

### 14.2 Quizlet

| Feature | Implementation | Learning Science Basis |
|---------|---------------|----------------------|
| Learn mode | Adaptive MCQ cycling | Retrieval practice |
| Match game | Timed matching | Gamified recall |
| Test mode | Mixed format (MCQ, written, T/F) | Testing effect |
| Spaced repetition | SM-2 variant (Quizlet Plus) | Ebbinghaus |
| Explanations | AI-generated (Q-Chat) | Elaborative interrogation |
| Social | Share decks, class sets | Peer learning |

**What to Borrow:** Mixed question formats, AI-generated explanations.
**What to Improve:** Add prerequisite-aware sequencing, remove study-by-recognition modes (passive).

### 14.3 Brainscape

| Feature | Implementation | Learning Science Basis |
|---------|---------------|----------------------|
| CBR algorithm | Confidence-Based Repetition | Leitner + metacognition |
| Self-rating | 5-point confidence scale | Metacognitive monitoring |
| Adaptive spacing | Based on confidence + correctness | Modified SM-2 |
| Class system | Teacher-created decks, student progress | Classroom integration |

**What to Borrow:** The concept of confidence-based repetition (but automate confidence detection).
**What to Improve:** Brainscape's CBR still relies on self-rating (unreliable -- see Section 2.4).

### 14.4 RemNote

| Feature | Implementation | Learning Science Basis |
|---------|---------------|----------------------|
| Notes + SRS | Inline flashcards within notes | Elaborative encoding + retrieval |
| FSRS | Default algorithm (2024+) | Modern SRS |
| Knowledge graph | Linked notes create a graph | Knowledge organization |
| PDF annotation | Create cards from PDFs | Active reading |
| Concept mapping | Visual node-link diagrams | Dual coding |

**What to Borrow:** Inline card creation from study notes, linked knowledge graph.
**What to Improve:** RemNote is general-purpose; Cena is domain-specific with curriculum alignment.

### 14.5 Obsidian (with SRS plugins)

| Feature | Implementation | Learning Science Basis |
|---------|---------------|----------------------|
| Spaced Repetition | Plugin (Obsidian SR) using SM-2 | Ebbinghaus |
| Knowledge graph | Bidirectional links visualized as graph | Knowledge organization |
| Active recall | Flashcards embedded in notes | Retrieval practice |
| Review queue | Plugin-managed review scheduling | SRS |

**What to Borrow:** The knowledge graph visualization of interconnected concepts.
**What NOT to borrow:** Obsidian is a tool for power users; Cena's audience is 16-18 year old exam-prep students who need everything automated.

---

## Appendix A: Algorithm Specification Summary

### A.1 BKT Update (Already Implemented)

```
Location: src/actors/Cena.Actors/Mastery/BktTracer.cs
Performance: ~100ns per call, zero heap allocation

P(L|correct) = (1-P_S)*P_L / [(1-P_S)*P_L + P_G*(1-P_L)]
P(L|incorrect) = P_S*P_L / [P_S*P_L + (1-P_G)*(1-P_L)]
P(L_next) = P(L|obs) + (1 - P(L|obs)) * P_T
Clamp to [0.01, 0.99]
```

### A.2 HLR Decay (Already Implemented)

```
Location: src/actors/Cena.Actors/Mastery/HlrCalculator.cs

Half-life: h = 2^(theta dot x + bias)
  Clamped to [1 hour, 1 year]

Recall: p(t) = 2^(-elapsed / h)

Schedule: t_next = -h * log2(threshold)
  Default threshold: 0.85
```

### A.3 FSRS (To Implement, Phase 3)

```
Retrievability: R(t, S) = (1 + t / (9 * S))^(-1)

Stability (correct): S_new = S * (1 + e^w8 * (11-D) * S^(-w9) * (e^(w10*(1-R)) - 1))

Stability (lapse): S_new = w11 * D^(-w12) * ((S+1)^w13 - 1) * e^(w14*(1-R))

Difficulty: D_new = D - w6 * (grade - 3)

Parameters: w_0 through w_14 (15 learnable parameters)
Training: gradient descent on review history, ~20 reviews per card to converge
```

### A.4 Elo Item Selection (Already Implemented)

```
Location: src/actors/Cena.Actors/Mastery/EloScoring.cs

Expected: E = 1 / (1 + 10^((D_item - theta_student) / 400))
Target: |E - 0.85| minimized (the 85% rule)

Student update: theta_new = theta + K_s * (actual - E)
Item update: D_new = D + K_i * (E - actual)
```

### A.5 Interleaving Probability (To Implement)

```
interleave_prob(concept_c) =
  mastery(c) < 0.30: 0.0   // block for novices
  mastery(c) < 0.60: 0.3   // light interleaving
  mastery(c) < 0.80: 0.5   // moderate
  mastery(c) >= 0.80: 0.7  // heavy interleaving for deep learning
```

### A.6 Calibration Score (To Implement)

```
For each confidence bin b in {0-10%, 10-20%, ..., 90-100%}:
  calibration_error(b) = |mean_confidence(b) - mean_accuracy(b)|

Overall calibration = 1 - mean(calibration_error across bins)
  Range: 0.0 (maximally miscalibrated) to 1.0 (perfect calibration)

Overconfidence index = mean(confidence - accuracy) across all responses
  > 0: overconfident
  < 0: underconfident
  ~ 0: well calibrated
```

---

## Appendix B: Learning Activity Design Templates

### B.1 Spaced Review Session

```yaml
template: spaced_review
duration: 5-10 minutes
question_count: 5-12
selection:
  source: SRS queue (R < 0.90, sorted by review_priority descending)
  format: match to mastery level (MCQ for novice, free recall for proficient)
  interleaving: high (concepts from different topics)
grading: automatic (BKT + HLR/FSRS update)
feedback: immediate correctness + brief explanation
post_session:
  show: concepts reviewed, average R improvement, XP earned
  animate: memory curves extending (half-lives growing)
  schedule: next review date for each concept
```

### B.2 New Concept Introduction

```yaml
template: new_concept
duration: 15-25 minutes
phases:
  1_prettest:
    questions: 2-3 about the new concept (expect failure)
    bkt_update: false (pre-test, don't count)
    purpose: prime retrieval routes, identify prior knowledge
  2_instruction:
    format: multimedia explanation (Mayer principles)
    methodology: from MCM routing (WorkedExample for procedural, Socratic for conceptual)
    scaffolding: full (worked examples with step-by-step reveal)
  3_guided_practice:
    questions: 3-5 at Bloom's level 1-2
    hints: available (3 levels)
    bkt_update: true
    target_accuracy: 0.70-0.85
  4_independent_practice:
    questions: 3-5 at Bloom's level 2-3
    hints: on request only
    bkt_update: true
    interleaving: 0.2 (light mixing with review items)
  5_summary:
    show: mastery progress, concepts learned, connection to graph
    schedule: first review in SRS queue (interval = 1 day)
```

### B.3 Deep Practice Session (Mastered Concepts)

```yaml
template: deep_practice
duration: 20-30 minutes
prerequisite: concept mastery >= 0.80
phases:
  1_warmup:
    questions: 3 review items (R < 0.90) at Bloom's level 2-3
  2_challenge:
    questions: 5-8 at Bloom's level 4-5 (Analyze, Evaluate)
    format: multi-step problems, error-finding, comparison
    interleaving: 0.7 (heavy mixing across topics)
    scaffolding: none
  3_transfer:
    questions: 2-3 at Bloom's level 5-6 (Evaluate, Create)
    format: novel contexts, cross-subject applications
    bkt_update: true (with transfer_type tag)
  4_reflection:
    prompt: "Which concept was hardest? Why?"
    confidence: self-assessment per concept
    purpose: metacognitive calibration
```

### B.4 Evening Review Session

```yaml
template: evening_review
duration: 3-7 minutes
trigger: push notification at preferred evening time
questions: 5-7 review items only
selection: highest priority from SRS queue
bloom_level: 1-3 only (low cognitive load)
ui: dark mode, minimal animations
feedback: brief, encouraging
post_session: "Your brain will consolidate these tonight. Sleep well!"
no_new_concepts: true
```

### B.5 Teach-Back Session

```yaml
template: teach_back
duration: 10-15 minutes
prerequisite: concept mastery >= 0.80
phases:
  1_select:
    system selects concept from student's mastered set
    prioritize: recently mastered, not yet taught back
  2_explain:
    prompt: "Explain [concept] as if teaching a friend"
    format: text area (min 50 words)
    optional: include a worked example
  3_evaluate:
    evaluator: LLM rubric (accuracy, completeness, clarity, examples)
    feedback: specific improvement suggestions
  4_revise:
    if score < 0.6: opportunity to revise explanation
    if score >= 0.6: "Great teaching! Here's one thing to add..."
xp_bonus: 2.5x normal (teaching is harder than answering)
badge: "Teacher" badge after 10 successful teach-backs
```

---

## Appendix C: Screen Design Specifications

### C.1 Home Screen -- Daily Review Badge

```
+----------------------------------------------+
|  [Knowledge Graph Mini-View]                   |
|  3 nodes pulsing (need review)                |
|                                               |
|  +------ Daily Review ------+                 |
|  |  7 concepts need review  |                 |
|  |  Est. time: ~8 min       |                 |
|  |  [Start Review]          |                 |
|  +---------------------------+                |
|                                               |
|  Today's Progress:                            |
|  [====>---------] 40%                         |
|  2 sessions | 45 XP today                     |
+----------------------------------------------+
```

### C.2 Review Session -- Question with Recall Indicator

```
+----------------------------------------------+
|  Review 3/7          [Recall: 62%]            |
|  Concept: Integration by Parts                |
|                                               |
|  Evaluate: integral of x*e^x dx              |
|                                               |
|  [Text input with LaTeX rendering]            |
|                                               |
|  [Check] [Hint 1/3] [Skip]                   |
|                                               |
|  Memory: [=====--------] was fading           |
|  After review: [===========--] strengthened!  |
+----------------------------------------------+
```

### C.3 Knowledge Map with Metacognitive Overlay

```
+----------------------------------------------+
|  Knowledge Map           [Filter: All]        |
|                                               |
|    [Algebra]--->[Functions]--->[Calculus]      |
|       |              |             |          |
|   [Geometry]    [Trig]        [Vectors]       |
|                                               |
|  Legend:                                      |
|  Green = Mastered (>90%)                      |
|  Blue  = Proficient (70-90%)                  |
|  Yellow = Developing (40-70%)                 |
|  Gray  = Not Started                          |
|  Orange pulse = Decaying                      |
|  Red outline = Overconfident!                 |
|                                               |
|  Tap any concept for details                  |
+----------------------------------------------+
```

### C.4 Calibration Dashboard

```
+----------------------------------------------+
|  Do You Know What You Know?                   |
|                                               |
|  [Calibration scatter plot]                   |
|  X: Your confidence                           |
|  Y: Your actual accuracy                      |
|                                               |
|  Your calibration score: 0.72                 |
|  (Good! Most students score 0.55-0.65)        |
|                                               |
|  Biggest blind spot:                          |
|  "Trigonometric Identities"                   |
|  You rated 85% confident, actual: 45%         |
|  [Practice This Now]                          |
+----------------------------------------------+
```

### C.5 Post-Session Summary

```
+----------------------------------------------+
|  Session Complete!                 +45 XP     |
|                                               |
|  Questions: 12 attempted                      |
|  Correct: 9 (75%)                             |
|  Topics covered: 4 (interleaved!)             |
|                                               |
|  Concepts Improved:                           |
|  Chain Rule     72% -> 81%                    |
|  Integration    65% -> 73%                    |
|  Trig Identities 45% -> 52%                  |
|                                               |
|  Bloom's Progress:                            |
|  Chain Rule: Apply -> Analyze (Level up!)     |
|                                               |
|  3 concepts added to review queue             |
|  Next review: Tomorrow morning                |
|                                               |
|  [Continue Learning] [Done for Now]           |
+----------------------------------------------+
```

---

## Appendix D: Research Citation Index

### Spaced Repetition & Memory
- Ebbinghaus (1885). *Memory*. DOI: 10.1037/10011-000
- Wozniak (1990). *Optimization of Repetition Spacing*
- Settles & Meeder (2016). A Trainable Spaced Repetition Model. *ACL HLR*
- Ye (2022). FSRS. arXiv:2402.01032
- Wixted & Ebbesen (1991). On the form of forgetting. DOI: 10.3758/BF03202914
- Murre & Dros (2015). Replication of Ebbinghaus. DOI: 10.1371/journal.pone.0120644
- Pavlik & Anderson (2005). Practice and forgetting effects. DOI: 10.1037/0278-7393.31.5.930

### Active Recall & Testing Effect
- Karpicke & Blunt (2011). Retrieval practice > concept mapping. *Science*. DOI: 10.1126/science.1199327
- Roediger & Karpicke (2006). Test-enhanced learning. DOI: 10.1111/j.1467-9280.2006.01693.x
- Rowland (2014). Meta-analysis of testing effect. DOI: 10.1037/a0037559
- Adesope et al. (2017). Rethinking the use of tests. DOI: 10.3102/0034654316689306
- McDaniel et al. (2007). Testing the testing effect. DOI: 10.1027/1016-9040.12.3.200
- Richland et al. (2009). The pretesting effect. DOI: 10.1037/a0016496

### Interleaving & Desirable Difficulties
- Rohrer & Taylor (2007). Shuffling of math problems. DOI: 10.1007/s11251-007-9015-8
- Brunmair & Richter (2019). Meta-analysis of interleaved learning. DOI: 10.1037/bul0000209
- Kornell & Bjork (2008). Spacing and induction. DOI: 10.1111/j.1467-9280.2008.02127.x
- Bjork (1994). Memory and metamemory considerations. MIT Press.

### Multimedia Learning
- Mayer (2009). *Multimedia Learning* (2nd ed.). Cambridge University Press.
- Mayer (2014). *Cambridge Handbook of Multimedia Learning* (2nd ed.).

### Metacognition
- Dunlosky & Rawson (2012). Overconfidence and underachievement. DOI: 10.1016/j.learninstruc.2011.08.003
- Kruger & Dunning (1999). Unskilled and unaware. DOI: 10.1037/0022-3514.77.6.1121
- Bjork, Dunlosky & Kornell (2013). Self-regulated learning. DOI: 10.1146/annurev-psych-113011-143823
- Kornell & Bjork (2007). Promise and perils of self-regulated study. DOI: 10.3758/BF03194055

### Bloom's Taxonomy
- Bloom et al. (1956). *Taxonomy of Educational Objectives*. Longmans, Green.
- Anderson & Krathwohl (2001). *A Taxonomy for Learning, Teaching, and Assessing*. Pearson.

### Zone of Proximal Development
- Vygotsky (1978). *Mind in Society*. Harvard University Press.
- Wilson et al. (2019). The 85% rule. *Nature Communications*. DOI: 10.1038/s41467-019-12552-4
- Kapur (2008). Productive failure. DOI: 10.1080/07370000802212669
- Sinha & Kapur (2021). Productive failure meta-analysis. DOI: 10.3102/00346543211019105

### Transfer
- Barnett & Ceci (2002). Taxonomy for far transfer. DOI: 10.1037/0033-2909.128.4.612
- Perkins & Salomon (1992). Transfer of learning. *International Encyclopedia of Education*.

### Motivation
- Dweck (2006). *Mindset*. Random House.
- Yeager et al. (2019). Growth mindset experiment. *Nature*. DOI: 10.1038/s41586-019-1466-y
- Weiner (1985). Attribution theory. *Psychological Review*, 92(4), 548-573.

### Sleep & Learning
- Walker (2017). *Why We Sleep*. Scribner.
- Gais et al. (2006). Sleep after learning. DOI: 10.1101/lm.132106
- Stickgold (2005). Sleep-dependent memory. *Nature*. DOI: 10.1038/nature04286
- Wilhelm et al. (2011). Sleep consolidation of relevant memories. DOI: 10.1523/JNEUROSCI.3575-10.2011

### Knowledge Tracing
- Corbett & Anderson (1994). Knowledge tracing. *User Modeling and User-Adapted Interaction*, 5(4), 253-278.
- Pelanek (2016). Applications of the Elo rating system. DOI: 10.1016/j.compedu.2016.03.017
- Pavlik et al. (2009). Performance factors analysis. DOI: 10.3233/978-1-60750-028-5-531

### Flow & Attention
- Csikszentmihalyi (1990). *Flow*. Harper & Row.
- Esterman et al. (2013). In the zone or zoning out. *Cerebral Cortex*. DOI: 10.1093/cercor/bhs261
- Thomson et al. (2022). Vigilance decrement. *Psychonomic Bulletin & Review*. DOI: 10.3758/s13423-022-02089-x

### Productive Failure & Grit
- Kapur (2008). Productive failure in mathematical problem solving. DOI: 10.1080/07370000802212669
- Sinha & Kapur (2021). PF meta-analysis. DOI: 10.3102/00346543211019105
- Duckworth et al. (2007). Grit. DOI: 10.1037/0022-3514.92.6.1087
- Crede et al. (2017). Much ado about grit. DOI: 10.1037/pspp0000102

### Platform Architecture
- Duolingo HLR (GitHub): https://github.com/duolingo/halflife-regression
- FSRS (GitHub): https://github.com/open-spaced-repetition/fsrs4anki
- OATutor (GitHub): https://github.com/CAHLR/OATutor
- pyBKT (GitHub): https://github.com/CAHLR/pyBKT
