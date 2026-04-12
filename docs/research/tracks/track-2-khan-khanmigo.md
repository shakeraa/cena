# Track 2 — Khan Academy + Khanmigo

**Sweep**: 10-track parallel research validation for Cena proposal
**Date**: 2026-04-11
**Scope**: Khan Academy mastery system, skill graph, Khanmigo tutor, efficacy evidence, gamification philosophy, Schoolhouse.world peer tutoring

---

## 1. Mastery learning model (VERIFIED)

Khan Academy's mastery system uses a five-level progression per skill, scored out of 100 Mastery Points:

| Level | Points | How to reach it |
|---|---|---|
| Not Started | 0 | Default |
| Attempted | 0 | First attempt <70% on exercise/quiz/test/challenge |
| Familiar | 50 | 70–85% on an exercise, OR all questions about the skill correct on a quiz/test/challenge |
| Proficient | 80 | Starting from Familiar, all questions about the skill right on exercise/quiz/test/challenge |
| Mastered | 100 | Starting from Proficient, all questions about the skill right on a **Unit Test or Course Challenge** |

The crucial design choice: **Mastered can only be earned via a Unit Test or Course Challenge**, not an isolated practice set. This forces retrieval-under-mixed-context, which is Khan's implicit gating mechanism against cramming one skill at a time.

Mastery levels are **bidirectional** — a skill can go *down* if the student misses questions about it on later unit tests or course challenges. This is Cena-relevant: it's the opposite of Duolingo's "crown" ratchet, which only goes up.

### Skill graph and prerequisites (VERIFIED)

"Quizzes, unit tests, and course challenges on Khan Academy include skills that build on other prerequisite skills, and when more difficult skills are completed, it also impacts the mastery level of prerequisite skills" (Khan Academy Help Center). Missing a skill on a unit test surfaces the prerequisite chain — the system explicitly tells the student to "work backward by practicing the prerequisite skills listed on the skill page." This is a real DAG, not an XP ladder.

### Course Challenge

A sampling assessment covering the entire course. Usable at the *start* of a course to bypass material you already know (credit-out) or at the *end* for mastery credit. This is important: Khan treats the course challenge as both diagnostic AND mastery gate, not just a final exam.

### Learn–Practice–Review loop

The unit structure is: videos/articles (Learn) → practice exercises → quizzes → unit test → course challenge. Review is implicit via the bidirectional mastery decay — you don't have a dedicated "Review" tab the way Anki does; instead, later unit tests pull in prerequisite skills and decay them if you've forgotten.

---

## 2. Khanmigo — design philosophy and guardrails (VERIFIED)

Launched in private pilot March 2023, built on GPT-4 via a custom partnership with OpenAI. User growth from ~68K in the 2023–24 pilot to 700K+ in 2024–25.

### Socratic system prompt

Khan Academy's publicly stated core prompt: *"You are a Socratic tutor. I am a student. Don't give me answers to my questions but lead me to get to them myself."* The tutor is modelled on Socrates. Khan Academy has published a **7-step prompt engineering approach** for designing Khanmigo behaviours (see citation #6).

### Answer-refusal behaviour

Reported consistently: when a student says "just tell me the answer," Khanmigo redirects with something like *"I want to help you figure this out yourself"* and asks for the student's first step. Responses are grounded in Khan Academy content (the tutor knows the course context and the current exercise) and filtered by moderation to stay on-topic.

### Cheating prevention

In the Writing Coach specifically, if a student pastes 15+ words from outside the coach into an essay, the section is flagged for teacher review. This is a concrete, copyable anti-cheat pattern.

### Efficacy

Pilot reports (Khan Academy, press coverage) claim "mastery gains comparable to human tutors" and "reduced frustration in challenging subjects like math" — **these are Khan Academy self-reported claims and should be treated as UNVERIFIED by independent peer review**. Newark Public Schools, a prominent pilot district, reportedly looked for a new AI tutor after its Khanmigo pilot (Chalkbeat 2024), which suggests the tutor has not yet achieved universal pilot acclaim.

---

## 3. Efficacy studies — independent and first-party

### SRI / Murphy 2014 (VERIFIED, independent, Gates-funded)

- 2 years, 9 sites, 20 schools, ~70 teachers, ~2,000 students, grades 5–10, 2011–2013
- 85% of teachers said Khan had a positive impact; 71% of students liked the lessons; 32% of students said they liked math *more* as a result
- "At two research sites, increased time on Khan Academy was associated with better-than-expected test scores, decreased math anxiety, and increased confidence in math ability"
- **Caveat**: only 25% of teachers thought Khan was very effective for students who are *behind*, vs 74% for students *ahead* — suggests Khan's model favors already-motivated learners. This is critical for Cena, which targets Bagrut/AP/SAT (already-motivated) but also needs to avoid abandoning strugglers.

### College Board / Khan Academy SAT (VERIFIED, first-party + College Board)

- Partnership launched June 2015 (Official SAT Practice on Khan Academy)
- 2017 College Board analysis: **20 hours of personalized practice → 115-point average PSAT→SAT gain** vs 60-point gain for non-users
- 6–8 hours of practice → ~90-point average gain
- Holds across gender, race, income, GPA
- 10M+ students have used OSP since launch
- **This is the strongest published causal claim Khan has.** It's first-party but the sample was 500K+.

### Long Beach Unified (VERIFIED, correlational, first-party)

- Summer 2017 partnership
- Khan users gained 6% in math vs 3% district average
- Using Khan 30+ min/week → additional 22 points on CA state math assessment
- Launched Khan's district-partnership business model

### 3-year Newark study (VERIFIED, first-party longitudinal)

- ~5,200 students, 13 North Ward schools, quasi-experimental controlling for student/teacher fixed effects
- Every 10 skills learned → ~1-point gain on NJ PARCC/NJSLA
- Effect is small but robust

### MAP Growth 2022–23 (VERIFIED, first-party)

- ~350K students, grades 3–8
- 30+ min/week OR 18+ hours/year → ~20% greater-than-expected learning gains
- **Effect size 0.36** — this is genuinely meaningful in education research

---

## 4. Gamification restraint — explicit philosophy (PARTIALLY VERIFIED)

Khan Academy uses **energy points** and **badges** (Meteorite → Sun → Black Hole, themed as celestial bodies). But the philosophy is explicitly restrained: Khan Academy community/support documents state "*We don't want to gamify it too much, which would discourage the learner from focusing on the subjects themselves rather than on the game-like elements around it.*"

No daily streaks with loss-aversion framing. No leaderboards that pit classmates against each other competitively. No variable-ratio chest/reward mechanics. Energy points accumulate but don't unlock content or rank users publicly. Badges are static achievements, not a gating mechanism.

**Contrast with Duolingo**: Duolingo pairs "levels, XP, badges, *public leaderboards*, daily goals, skill trees, avatars, a mascot" (Prodwrks analysis) with explicit loss-aversion hooks (streak freeze as paid feature, streak loss emails). Khan has none of that. Khan is "mastery-first with gamification as garnish"; Duolingo is "gamification-first with learning embedded."

**I could not find a single verbatim Sal Khan quote** explicitly rejecting variable-ratio rewards or streak-guilt mechanics — the restraint is observable from product behaviour and community documents rather than a published manifesto. Mark as **PARTIALLY VERIFIED**: the stance is real, the smoking-gun quote is not in my search results.

---

## 5. Schoolhouse.world (VERIFIED)

- Founded 2020 by Sal Khan (separate nonprofit from Khan Academy)
- 174,000+ students from 180+ countries
- 25,000+ peer tutors; SAT tutors scored in top 5%, trained and certified
- 100% free, live over Zoom, synchronous small groups
- Offerings: SAT Bootcamps, subject tutoring, college admissions mentorship, "Dialogues" program for civil discourse
- College Board co-launched free peer-to-peer SAT tutoring on Schoolhouse

**This is the strongest existence proof for Cena's co-op (not PvP) proposal**: Khan built an entirely separate nonprofit whose entire thesis is that peers helping peers synchronously is both effective AND scalable. Tutors are certified via mastery checks before they can tutor.

---

## 6. SAT partnership and test-prep

Khan's approach to test prep is **not a separate product** — Official SAT Practice lives inside the normal Khan Academy platform, uses the same mastery engine, and pulls personalized practice based on PSAT/linked scores. This validates a key Cena decision: Bagrut/AP/SAT prep should NOT be a separate app; it should be the same platform with a test-prep *mode*.

---

## Top 3 findings

1. **Mastery is gated by mixed-context assessment, and can decay.** The "Mastered" level specifically requires a Unit Test or Course Challenge (mixed-skill), and mastery moves down when later tests show forgetting. This is fundamentally different from XP-only systems and from Duolingo crowns. Cena's proposal point 10 (boss fights as Bagrut-style synthesizers) maps directly onto this: the "boss" IS the unit test / course challenge, i.e. the mixed-retrieval integration assessment.

2. **Khanmigo's Socratic refusal is a deliberate architectural constraint, not a prompt-level hint.** Khan runs a 7-step prompt engineering process, bakes refusal into the system prompt, filters responses, grounds them in Khan content, and for Writing Coach *actively detects* pasted text over 15 words for teacher flagging. This is Cena-relevant: a "misconception-as-enemy" mechanic can reuse the same architectural pattern — the AI never tells you the answer, it identifies which misconception you're running and spawns a targeted remediation encounter.

3. **Khan's gamification restraint is an observable product stance with strong efficacy evidence behind it.** Energy points + static badges, no streak guilt, no variable-ratio loot, no competitive leaderboards — AND independently-validated learning gains (SRI 2014), 115-point SAT gains (College Board 2017), 0.36 effect size on MAP Growth (2023). The "restrained gamification + rigorous mastery" stack is *empirically the winning education stack* among at-scale products. This is strong support for Cena proposal point 7 (zero streak guilt, zero confetti inflation, zero variable-ratio rewards).

---

## Contradictions with Cena proposal

### Validates (strongly)

- **Point 7 (zero streak guilt, zero confetti inflation, zero variable-ratio rewards)** — Khan's mastery-first restrained-gamification model is the winning stack by independent efficacy evidence. Duolingo-style mechanics are conspicuously absent from the product that has real learning gains.
- **Point 9 (reject Duolingo streak loss-aversion)** — Khan has no streak feature at all in the loss-aversion sense. Validated.
- **Point 5 (co-op not PvP)** — Schoolhouse.world is existence proof that peer-cooperation-at-scale works for test prep specifically. 174K students, SAT-focused, all free, all synchronous, all cooperative.
- **Point 10 (boss fights as Bagrut-style synthesizers)** — Khan's unit test + course challenge ARE already the synthesizer mechanic, the one and only way to reach "Mastered." This is a validated design pattern, not a novel one.

### Partial / requires care

- **Point 2 (misconception-as-enemy)** — Khan does NOT reify misconceptions as adversarial entities. Khanmigo handles misconceptions conversationally and individually, not as tracked "enemies" with a bestiary/codex. There's no evidence this reification helps learning and no evidence it hurts. Cena is in novel territory here; Khan's Socratic model is a *safer* baseline. **Don't copy Khanmigo's conversational-only approach; Cena's reified-enemy version is a bet, not a validated pattern.**
- **Point 3 (named tutor character per language)** — Khanmigo is a *single* tutor persona. Khan Academy does NOT localize the tutor into multiple named characters per language. Cena's three-tutor-per-language model is a differentiator but is NOT validated by Khan.

### Neutral / no signal

- Point 1 (session-as-game-loop with world map) — Khan has no world map. No validation, no contradiction.
- Point 4 (sandbox physics à la PhET) — out of Khan's scope; Khan embeds PhET content but doesn't build its own.
- Point 6 (daily challenge as Wordle) — Khan has no daily challenge feature at all.
- Point 8 (MathLive keyboard + haptics + sound) — Khan uses its own math input, no public haptic/sound signature.

---

## Mechanics to COPY from Khan

1. **Mastered-only-via-mixed-test gating.** Cena's "mastered" state should require a mixed-skill unit synthesizer, not repeated single-skill practice. This is directly from Khan and empirically validated.
2. **Bidirectional mastery decay** — missing a prerequisite on a later test drops you from Mastered back to Proficient or lower on the prerequisite. Fights crystallized forgetting.
3. **Course Challenge as both diagnostic and credit-out.** A single assessment that lets a student either skip ahead or consolidate mastery. Cena should ship this from day 1 — it's a huge retention booster for already-motivated Bagrut students who don't want to grind baby material.
4. **Socratic system-prompt with refusal-to-answer and 15-word-paste detection** for the writing/derivation components of physics and calculus.
5. **Peer tutoring as a separate-but-integrated product** (Schoolhouse model). Cena can start with a co-op study-group mode and later extend to peer-verified tutoring with mastery-gated tutor certification.
6. **Energy-point-like accumulation without leaderboards.** Non-competitive, non-resettable progress currency.
7. **Khan's "skills-to-proficient" metric** — the core impact measure. Cena should adopt this (or a direct analog) as its primary learning KPI rather than time-in-app or streak-length.

## Mechanics to AVOID from Khan

1. **Video-first "Learn" step.** Khan is famous for lecture videos; recent research (not in this search scope but widely cited) suggests passive video is the weakest part of Khan's loop. Cena should front-load retrieval practice, not video.
2. **Badge system as decoration.** Khan's Meteorite→Black Hole badges are widely ignored by students and are effectively dead weight. If Cena ships badges, make them functionally load-bearing or skip them.
3. **Treating test prep as a separate walled garden.** Actually — Khan does NOT do this, and Cena should not either. This is a mechanic to copy: unify test prep with regular mastery flow.
4. **Monolithic tutor persona.** Cena's three-tutor-per-language plan is better than Khanmigo's single persona for a multilingual product. Don't regress to Khan's monolith.

---

## Confidence deltas (-2 to +2)

| Point | Before | Delta | After | Rationale |
|---|---|---|---|---|
| 2 — Misconception-as-enemy | baseline | **0** | unchanged | Khan doesn't reify misconceptions. No validation, no contradiction. Cena is betting independently here. |
| 3 — Named tutor per language | baseline | **0** | unchanged | Khanmigo is monolith; Cena's multi-tutor approach is a differentiator, but Khan offers neither validation nor contradiction. |
| 5 — Co-op not PvP | baseline | **+2** | strongly validated | Schoolhouse.world is existence proof at 174K-student scale, SAT-specific, backed by College Board. Highest-confidence validation in this track. |
| 7 — Zero streak guilt, zero confetti, zero variable-ratio | baseline | **+2** | strongly validated | Khan's restrained-gamification stack is the one with independent, replicated efficacy evidence. The evidence stack (SRI 2014 + College Board 2017 + Long Beach + Newark 3yr + MAP 2023) all applies to a product with none of Duolingo's guilt mechanics. |
| 9 — Reject Duolingo loss-aversion streaks | baseline | **+2** | strongly validated | Same evidence stack as point 7. Additionally, Khan explicitly avoids streaks and performs fine. |
| 10 — Boss fights as Bagrut-style synthesizers | baseline | **+1** | validated | Khan's unit test + course challenge is exactly this mechanic and is the sole gate to Mastered. The "boss fight" framing is novel but the underlying mechanic is validated. |

---

## Specific Khanmigo patterns worth studying

1. **The 7-step prompt engineering pipeline** (citation #6) — Khan publicly documents their methodology. Cena's tutor prompt should go through a similar formal design pipeline rather than an ad-hoc "you are a helpful tutor" prompt.
2. **The "what do YOU think is the first step?" redirect.** Concrete Socratic move that defuses the "just give me the answer" attack vector. Should be in Cena's tutor system prompt verbatim.
3. **Grounding in course content.** Khanmigo knows the current exercise, the current unit, the prerequisite chain, and the student's mastery state. It doesn't free-roam like ChatGPT. Cena's tutor must have the same hard grounding in the student's current session + skill graph + recent errors.
4. **Writing Coach's 15-word-paste detection.** Simple, concrete anti-cheat mechanism that can run client-side and flag for teacher review. Worth copying for Cena's physics derivation and proof-writing modes.
5. **Refusal to give answers is an architectural constraint, not a nudge.** The refusal is baked into system prompt + content filter + guardrail layer. Cena must treat this as a non-negotiable invariant at the system-prompt level.
6. **Tutor for teacher too.** Khanmigo has a *teacher* persona that drafts lesson plans, rubrics, and student progress summaries. For Cena's teacher-mode (if/when it ships), reuse this dual-persona pattern.

---

## Citations (5 verified)

1. Khan Academy Help Center — "How do Khan Academy's Mastery levels work?" — https://support.khanacademy.org/hc/en-us/articles/5548760867853
2. SRI International (Murphy et al. 2014) — "Research on the Use of Khan Academy in Schools: Implementation Report" — https://www.sri.com/wp-content/uploads/2021/12/2014-03-07_implementation_briefing.pdf
3. College Board Newsroom — "New Data Links 20 Hours of Personalized Official SAT Practice on Khan Academy to 115-Point Average Score Gains on Redesigned SAT" — https://newsroom.collegeboard.org/new-data-links-20-hours-personalized-official-sat-practice-khan-academy-115-point-average-score
4. Schoolhouse.world — "About" — https://schoolhouse.world/about
5. Khan Academy Blog — "Khan Academy's 7-Step Approach to Prompt Engineering for Khanmigo" — https://blog.khanacademy.org/khan-academys-7-step-approach-to-prompt-engineering-for-khanmigo/
6. Khan Academy Blog — "Khan Academy Efficacy Results, November 2024" — https://blog.khanacademy.org/khan-academy-efficacy-results-november-2024/
7. Chalkbeat Newark — "Newark Public Schools wants new AI tutor after pilot testing" (2024) — https://www.chalkbeat.org/newark/2024/05/13/artificial-intelligence-khanmigo-chatbot-tutor-pilot-testing-districtwide-expansion/

## UNVERIFIED claims (marked)

- Exact Khanmigo pilot effect sizes vs human tutors (Khan-reported, not independently peer-reviewed)
- A verbatim Sal Khan quote explicitly rejecting variable-ratio reward mechanics (the restraint is real, the quote is not in search results)
- That Khan Academy badges are "widely ignored" (commonly claimed but not independently measured in my search scope)
- Khanmigo's exact system-prompt text beyond the fragment "You are a Socratic tutor…"
