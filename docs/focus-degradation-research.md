# Focus Degradation & Student Resilience — Research Foundation

> **Status:** Research document
> **Applies to:** `src/actors/Cena.Actors/Services/FocusDegradationService.cs`
> **Purpose:** Isolate the science behind Cena's focus/resilience model for independent review and validation

---

## 1. The Problem Cena Solves

Students studying for Bagrut exams on a mobile app face a unique challenge: **no teacher is watching them.** Unlike a classroom where a teacher notices glazed eyes, in-app learning must detect focus degradation algorithmically and respond in real-time.

Current EdTech platforms (Khan Academy, ALEKS, Duolingo) adjust **difficulty** but don't model **attention**. They treat every question equally regardless of whether the student is focused or zoning out. Cena's hypothesis: modeling focus state produces better learning outcomes than modeling difficulty alone.

---

## 2. Verified Research Citations

### 2.1 Vigilance Decrement Theory

**Warm, J.S. (1984).** "An Introduction to Vigilance." In Warm (Ed.), *Sustained Attention in Human Performance*, pp. 1-14. Wiley.

**Parasuraman, R. (1986).** "Vigilance, monitoring, and search." In Boff, Kaufman & Thomas (Eds.), *Handbook of Human Perception and Performance*, Vol. II, pp. 41-1–41-49. Wiley.

**Key finding:** Sustained attention degrades over time-on-task. The "vigilance decrement" is the most commonly observed effect in attention research — detection accuracy drops as time increases, typically showing significant decline after 15-20 minutes.

**How Cena uses this:** Our `vigilanceScore` signal models the logarithmic attention decay curve. Students have a personal "peak attention" time (default 15 min, personalized over sessions). After peak, focus decays as:
```
decayFactor = 1.0 - 0.3 * ln(1 + (minutesActive - peakMinutes) / peakMinutes)
```

**Open question:** Warm's research was on passive monitoring tasks (radar screens). Is the decay curve the same for active problem-solving? Classroom research (Wilson & Conyers, 2020; Bunce et al., 2010) suggests active tasks have a shallower decay, but the same overall pattern holds.

**Papers to read for validation:**
- Bunce, D.M., Flens, E.A. & Neiles, K.Y. (2010). "How Long Can Students Pay Attention in Class?" *Journal of Chemical Education*, 87(12), 1438-1443.
- Wilson, K. & Korn, J.H. (2007). "Attention During Lectures: Beyond Ten Minutes." *Teaching of Psychology*, 34(2), 85-89.

---

### 2.2 Flow State (Challenge-Skill Balance)

**Csikszentmihalyi, M. (1990).** *Flow: The Psychology of Optimal Experience*. Harper & Row. 303 pages.

**Key finding:** Flow occurs when challenge matches skill. Too easy = boredom. Too hard = anxiety. The "flow channel" is a narrow band where both challenge and skill are high and balanced.

**9 conditions for flow:**
1. Challenge-skill balance
2. Merging of action and awareness
3. Clear goals
4. Immediate feedback
5. Concentration on the task
6. Sense of control
7. Loss of self-consciousness
8. Transformation of time
9. Autotelic experience

**How Cena uses this:** Our `FocusLevel.Flow` (score >= 0.8) maps to Csikszentmihalyi's flow state. When detected, Cena:
- Does NOT interrupt (no break suggestions)
- Increases challenge slightly (keeps the student in the flow channel)
- Tracks flow duration as a positive engagement metric

**Open question:** Can we reliably detect flow from behavioral signals alone (no EEG/biometrics)? Our proxy (high attention + high engagement + improving accuracy + sustained vigilance) is plausible but unvalidated.

**Papers to read for validation:**
- Shernoff, D.J. et al. (2003). "Student Engagement in High School Classrooms from the Perspective of Flow Theory." *School Psychology Quarterly*, 18(2), 158-176.
- Hamari, J. et al. (2016). "Challenging Games Help Students Learn." *Computers in Human Behavior*, 54, 170-179.

---

### 2.3 Productive Failure

**Kapur, M. (2008).** "Productive Failure in Mathematical Problem Solving." *Cognition and Instruction*, 26(3), 379-424. DOI: 10.1080/07370000802212669

**Key finding:** Students who struggle with problems BEFORE receiving instruction demonstrate significantly greater conceptual understanding and transfer ability than students who receive direct instruction first. "Failing" at problem-solving is productive for learning — under certain conditions.

**Conditions for productive failure to work:**
1. Problem must be within reach (not impossibly hard)
2. Student must generate multiple solution attempts (exploring the problem space)
3. Consolidation/instruction must follow the struggle phase
4. The failure must be followed by structured learning, not just more failure

**How Cena uses this:** Our `ClassifyStruggle()` method distinguishes:
- **Productive struggle:** accuracy is low but IMPROVING, errors are VARIED (exploring), RT is STABLE (engaged). → DO NOT intervene. Let the student struggle.
- **Unproductive frustration:** accuracy is flat, errors are REPETITIVE (stuck), RT is ERRATIC (distracted). → Switch methodology or reduce difficulty.

**Critical design decision:** When stagnation is detected AND the student is in productive struggle, Cena does NOT switch methodology. This is the opposite of the naive approach (always switch on stagnation). Kapur's research says: sometimes struggle IS the learning.

**Open question:** Our behavioral signals for classifying struggle (error variety, RT stability, accuracy slope) are heuristics, not validated against actual learning outcomes. The A/B test in `product-research.md` should include a "productive struggle classification accuracy" metric.

**Papers to read for validation:**
- Kapur, M. & Bielaczyc, K. (2012). "Designing for Productive Failure." *Journal of the Learning Sciences*, 21(1), 45-83.
- Loibl, K., Roll, I. & Rummel, N. (2017). "Towards a Theory of When and How Problem Solving Followed by Instruction Supports Learning." *Educational Psychology Review*, 29(4), 693-715.

---

### 2.4 Grit and Resilience

**Duckworth, A.L., Peterson, C., Matthews, M.D. & Kelly, D.R. (2007).** "Grit: Perseverance and Passion for Long-Term Goals." *Journal of Personality and Social Psychology*, 92(6), 1087-1101. DOI: 10.1037/0022-3514.92.6.1087

**Key finding:** Grit (sustained effort and interest toward long-term goals) predicts achievement above and beyond IQ and conscientiousness. 6 studies (N > 4,000) showed grit predicted educational attainment, GPA, retention at West Point, and spelling bee ranking.

**IMPORTANT CRITIQUE:**
**Credé, M., Tynan, M.C. & Harms, P.D. (2017).** "Much Ado About Grit: A Meta-Analytic Synthesis of the Grit Literature." *Journal of Personality and Social Psychology*, 113(3), 492-511.

This meta-analysis found:
- Grit's incremental validity over conscientiousness is weak (ρ = 0.02)
- The "perseverance" facet predicts performance; the "passion" facet does not
- Grit is largely redundant with conscientiousness

**How Cena uses this (with caution):** Our `ResilienceScore` is NOT a pure grit measure. It's a 4-factor composite:
- **Persistence** (0.35): sessions completed vs abandoned — behavioral, not self-reported
- **Recovery** (0.25): returning after a bad session — directly observable
- **Challenge seeking** (0.25): attempting harder problems — behavioral
- **Streak consistency** (0.15): lowest weight, most "gamification-dependent"

We avoid the grit critique by measuring BEHAVIOR (what the student does), not PERSONALITY (what they say they are). Duckworth's grit scale is a self-report questionnaire; our resilience score is computed from actual learning data.

**Open question:** Does our behavioral resilience score predict actual Bagrut exam performance? This must be validated.

---

### 2.5 Response Time Variance as Attention Proxy

**Esterman, M., Noonan, S.K., Rosenberg, M. & DeGutis, J. (2013).** "In the Zone or Zoning Out? Tracking Behavioral and Neural Fluctuations During Sustained Attention." *Cerebral Cortex*, 23(11), 2712-2723.

**Key finding:** Response time VARIABILITY (not just speed) is a reliable behavioral marker of attentional state. High RT variability = "out of the zone" (attention lapses). Low RT variability = "in the zone" (sustained focus). This was validated against neural markers (fMRI).

**How Cena uses this:** Our `attentionScore` signal computes RT variance over the last 5 questions and compares it to the student's baseline variance. High variance relative to baseline = attention is drifting.

```
attentionScore = 1.0 - (currentRtVariance - baselineRtVariance) / (baselineRtVariance * 2)
```

**This is well-supported.** RT variability as an attention marker has been replicated in multiple studies and is used in clinical ADHD assessment (Leth-Steensen et al., 2000).

---

## 3. Cena-Proprietary Models (No External Citation)

These models are designed by Cena's team and are NOT backed by published research. They need empirical validation through the A/B testing framework.

### 3.1 Engagement-Adjusted Decay Rate

**Model:** Focus decays at ~3% per question at baseline. Engaged students (requesting hints, adding annotations, changing approach) decay slower.

```
decayPerQuestion = 0.03 * (1.0 - engagementScore * 0.5)
```

**Rationale:** Voluntary interactions indicate active processing, which counteracts passive attention decay. This is plausible from vigilance research (active tasks show shallower decrement) but the specific formula (3% baseline, 50% engagement discount) is not from any paper.

**Validation plan:** Compare predicted vs actual focus drop across 1,000 sessions. Adjust coefficients via regression.

### 3.2 Remaining Productive Questions Prediction

**Model:** Given current focus score and decay rate, predict how many questions before focus drops below 0.4 (Drifting threshold).

**Rationale:** Allows the UI to show "5 more questions, then a break" instead of abruptly ending the session.

**Validation plan:** Compare predicted remaining questions vs actual point where student's accuracy drops below productive threshold. Calibrate.

### 3.3 Break Duration Formula

**Model:** Linear interpolation from focus level to break duration (5-30 min), with time-of-day and session-count adjustments.

```
base = focusLevel → {Drifting: 5min, Fatigued: 15min, Disengaged: 30min}
if lateEvening: base *= 1.5
if sessionsToday >= 3: base *= 1.3
```

**Rationale:** Longer breaks for deeper fatigue. Late-evening students need more recovery (circadian research supports this but we haven't cited a specific paper). Multiple sessions in one day compound fatigue.

**Validation plan:** A/B test break durations: fixed 10 min vs our adaptive formula. Measure return rate and post-break accuracy.

---

## 4. What We Don't Know (Research Gaps)

| Question | Why It Matters | How to Validate |
|---|---|---|
| Does RT variance reliably indicate attention in MATH problem-solving (vs simple reaction tasks)? | Esterman's study used simple detection tasks, not complex math | Compare RT variance with post-session self-reported attention |
| Is our productive struggle classifier accurate? | If we classify frustration as productive struggle, we harm the student | Compare classifier output vs expert teacher rating on recorded sessions |
| Do behavioral resilience scores predict exam performance? | If not, the score is a vanity metric | Correlate resilience score with Bagrut exam results (after 6 months) |
| Is the vigilance curve the same for Hebrew/Arabic RTL reading? | All vigilance research is in English/European contexts | Collect baseline attention data from Israeli students in both languages |
| Does the flow detection actually correspond to flow? | We have no biometric validation (no EEG, no heart rate) | Partner with university for a small-N physiological validation study |
| Are 15 minutes the right default peak attention time for 16-18 year olds? | Warm's research was on adults; Bunce's was on college students | A/B test different peak times (10, 15, 20 min) and measure accuracy curves |

---

## 5. Recommended Reading for Deep Validation

### Must-Read (directly relevant)

1. **Kapur, M. & Bielaczyc, K. (2012).** "Designing for Productive Failure." *Journal of the Learning Sciences*, 21(1), 45-83.
2. **Bunce, D.M., Flens, E.A. & Neiles, K.Y. (2010).** "How Long Can Students Pay Attention in Class?" *J. Chemical Education*, 87(12), 1438-1443.
3. **Esterman, M. et al. (2013).** "In the Zone or Zoning Out?" *Cerebral Cortex*, 23(11), 2712-2723.
4. **Credé, M. et al. (2017).** "Much Ado About Grit." *JPSP*, 113(3), 492-511. [Grit critique]
5. **D'Mello, S. & Graesser, A. (2012).** "Dynamics of Affective States during Complex Learning." *Learning and Instruction*, 22(2), 145-157. [Affect → engagement → learning model]

### Should-Read (supporting context)

6. **Baker, R.S.J.d. et al. (2010).** "Better to Be Frustrated than Bored." *International Journal of Human-Computer Studies*, 68(4), 223-241. [Frustration vs boredom in ITS]
7. **Pekrun, R. (2006).** "The Control-Value Theory of Achievement Emotions." *Educational Psychology Review*, 18(4), 315-341. [Emotions in learning]
8. **Hattie, J. & Timperley, H. (2007).** "The Power of Feedback." *Review of Educational Research*, 77(1), 81-112.
9. **Shernoff, D.J. et al. (2003).** "Student Engagement in High School Classrooms from the Perspective of Flow Theory." *School Psychology Quarterly*, 18(2), 158-176.
10. **Leth-Steensen, C., Elbaz, Z.K. & Douglas, V.I. (2000).** "Mean response times, variability, and skew in the responding of ADHD children." *Acta Psychologica*, 104(2), 167-190. [RT variability as clinical attention marker]

---

## 6. How This Feeds Into the A/B Test Plan

The methodology switching A/B test (from `docs/product-research.md`) should include these additional metrics:

| Metric | What It Validates | Collection Method |
|---|---|---|
| Focus state accuracy | Does our FocusLevel match self-reported attention? | Post-session 1-question survey: "How focused were you?" (1-5) |
| Productive struggle precision | Does our classifier correctly identify productive failure? | Expert teacher labels 50 recorded sessions, compare to classifier |
| Resilience → exam correlation | Does behavioral resilience predict Bagrut scores? | Correlate 6-month resilience scores with actual exam results |
| Break duration effectiveness | Do adaptive breaks improve post-break accuracy? | A/B: fixed 10min vs adaptive formula |
| Peak attention calibration | Is 15 min the right default for Israeli teens? | Measure accuracy curve across time-on-task for first 1,000 users |
