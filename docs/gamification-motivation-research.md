# Gamification & Motivation Psychology for Educational Mobile Apps

## Comprehensive Research for CENA Platform

**Research Date:** 2026-03-31
**Scope:** Self-Determination Theory, Octalysis Framework, reward schedules, XP systems, achievement design, leaderboards, quest systems, ethical guidelines, actor architecture mapping
**Applicability:** CENA Flutter mobile app, Proto.Actor backend, event-sourced student state

---

## Table of Contents

1. [Self-Determination Theory (SDT)](#1-self-determination-theory-sdt)
2. [Octalysis Framework — All 8 Core Drives](#2-octalysis-framework--all-8-core-drives)
3. [Intrinsic vs Extrinsic Motivation](#3-intrinsic-vs-extrinsic-motivation)
4. [XP and Level Systems for Learning](#4-xp-and-level-systems-for-learning)
5. [Achievement and Badge Systems](#5-achievement-and-badge-systems)
6. [Leaderboard Design — Ethical for Education](#6-leaderboard-design--ethical-for-education)
7. [Quest and Mission Systems](#7-quest-and-mission-systems)
8. [Reward Schedules — Behavioral Psychology](#8-reward-schedules--behavioral-psychology)
9. [Age-Appropriate Recommendations](#9-age-appropriate-recommendations)
10. [Ethical Guidelines — Harmful Gamification](#10-ethical-guidelines--harmful-gamification)
11. [Competitor Analysis](#11-competitor-analysis)
12. [Actor Architecture Mapping](#12-actor-architecture-mapping)
13. [CENA Current State and Gap Analysis](#13-cena-current-state-and-gap-analysis)

---

## 1. Self-Determination Theory (SDT)

### 1.1 Foundation

SDT (Deci & Ryan, 1985, 2000) posits that human motivation is driven by three innate psychological needs. When all three are satisfied, people experience autonomous motivation and deep engagement. When any one is thwarted, motivation degrades toward amotivation. For educational apps, SDT is the single most validated framework for sustained learning motivation.

**Key finding:** Environments that support autonomy, competence, and relatedness produce higher intrinsic motivation, deeper learning (conceptual understanding, not just rote recall), and greater persistence in the face of difficulty (Vansteenkiste et al., 2004).

### 1.2 Autonomy — The Need for Volition

**Definition:** Feeling that one's actions emanate from oneself rather than being externally controlled. Not independence — rather, a sense of willingness and choice.

**Research basis:** Controlling environments (forced schedules, mandatory participation, punitive deadlines) undermine intrinsic motivation even when the activity itself is interesting (Deci, Koestner, & Ryan, 1999 meta-analysis). Conversely, providing meaningful choices (even small ones) sustains motivation.

**CENA Mobile UI Implementations:**

**Learning Path Selection Screen:**
- Present 2-3 concept paths the student can choose from, all pedagogically valid (the KST prerequisite graph pre-filters to ensure any path is sound)
- Example: "What would you like to work on? [Fractions] [Geometry] [Review weak spots]"
- The student's choice is genuine — the system adapts regardless of which path they pick
- Never present a "right answer" among paths — all options lead to productive learning
- Screen layout: Full-width cards with concept icons, brief descriptions, and estimated time. Horizontal scroll for 3+ options. Bottom bar shows "The system chose these based on your progress" as a transparency note.

**Pace Controls:**
- A visible session-length selector: "Quick 5-min", "Standard 15-min", "Deep dive 30-min"
- Anytime pause button that does not penalize the student (no XP loss, no timer running)
- "I'm done for now" exit without guilt messaging — never "Are you sure? You'll lose your progress!"
- Implement as a slider or segmented control at session start

**Question Type Preferences:**
- Allow students to express preferences: "I learn best with [diagrams] [stories] [practice problems]"
- Map to methodology selection in the `LearningSessionActor` (the actor already supports methodology switching)
- Preferences are soft signals, not hard constraints — the system can still override when pedagogically necessary, but explains why: "This concept works best with diagrams, so we're trying that approach"

**Daily Goal Setting:**
- Student sets their own daily XP goal from a set of options (50, 100, 200, 300 XP)
- No "recommended" goal that guilt-trips — present all as equally valid
- Display progress toward their own goal, not an externally imposed one

**Autonomy-Supportive Language (all screens):**
- Use "you chose" instead of "you must"
- Use "try this" instead of "do this"
- Use "you've decided to practice math today" instead of "complete your math assignment"
- Avoid countdown timers, red warning text, and "X/Y required" framing

### 1.3 Competence — The Need for Effectance

**Definition:** Feeling effective in one's interactions with the environment and experiencing opportunities to express and develop one's capacities.

**Research basis:** Competence is satisfied when challenges are optimally matched to skill (Csikszentmihalyi's flow, 1990). Too easy = boredom, too hard = anxiety. The BKT mastery model in CENA already provides the engine for this — the UI must surface it clearly.

**CENA Mobile UI Implementations:**

**Skill Tree Visualization:**
- A tree or directed graph (based on the KST prerequisite graph) where nodes are concepts
- Nodes have 4 visual states: locked (prerequisites not met), available (ready to learn), in-progress (partially mastered), mastered (P(known) >= 0.85)
- Mastered nodes glow or have a checkmark; in-progress nodes show a radial progress ring
- Tapping a node shows: mastery %, time since last practice, recommended review date (from HLR)
- Never show concepts beyond 2 levels ahead of current mastery — prevents overwhelm while maintaining forward visibility
- Subject-specific trees: Math tree, Science tree, etc. (aligns with the Fortnite "profile multiplexing" pattern from the actor research)

**Mastery Progress Indicators:**
- Per-concept mastery as a 0-100% bar (maps directly to BKT P(known) * 100)
- Use colors: red (0-40%), amber (40-60%), green (60-85%), gold (85-100% = mastered)
- Micro-celebrations when crossing each threshold: subtle haptic buzz at 40%, confetti at 85%
- Show mastery trend: an up-arrow if improving, flat-line if stable, down-arrow if decaying (from HLR)

**Difficulty Scaling Feedback:**
- When the system increases difficulty (Recall -> Comprehension -> Application -> Analysis), tell the student: "You're ready for harder questions on this topic!"
- When decreasing difficulty, frame positively: "Let's reinforce the basics" rather than "You got too many wrong"
- Display the current difficulty tier as a small badge on each question: "Level 2: Comprehension"

**Immediate, Informational Feedback:**
- After each answer: clear correct/incorrect indicator
- For incorrect: show what the right answer was AND why (the explanation from the LLM ACL)
- For correct: show "+10 XP" popup (already implemented in `XpPopup`) plus a brief note: "You got this because [reason]"
- Never just say "Wrong" — always include the path forward
- The LearningSessionActor's `nextAction` field ("advance_concept", "provide_scaffolding") should drive UI framing

**Progress Dashboard (Home Screen):**
- Total concepts mastered out of total in curriculum: "47 of 312 concepts mastered"
- Weekly growth: "You mastered 4 new concepts this week" with a mini line chart
- Subject breakdown: per-subject mastery bar chart
- This dashboard gives students a concrete sense of their growing competence

### 1.4 Relatedness — The Need for Connection

**Definition:** Feeling connected to others, belonging, and mattering to others.

**Research basis:** Even in solo learning apps, relatedness can be supported through class cohorts, teacher connections, and peer awareness. Relatedness is the most underserved need in educational apps — most focus on autonomy and competence but neglect social connection (Rigby & Przybylski, 2009).

**CENA Mobile UI Implementations:**

**Class Cohort Awareness:**
- "Your class" section on the home screen showing aggregate stats: "Class average: 72% mastery in Fractions"
- Anonymous class activity feed: "3 classmates practiced today" (no names, just counts)
- This creates ambient awareness of shared effort without direct comparison

**Teacher Connection:**
- Teacher sends encouragement messages via the OutreachSchedulerActor
- Student sees teacher-sent messages in a dedicated inbox: "Great progress on geometry this week!"
- Teacher can set class-wide goals: "Our class goal: everyone masters fractions by April 15"
- The proactive system (OutreachSchedulerActor) becomes the teacher's amplifier

**Study Group Formation:**
- Students in the same class working on the same concept see: "2 others are also studying Fractions right now"
- Optional "Study together" sessions — synchronized question sets
- Post-session comparison (opt-in): "Your group answered 85% correctly on this topic"

**Mentorship (Peer Tutoring):**
- Students who master a concept get an optional "Help a classmate" prompt
- They can explain the concept in their own words (recorded as text or audio)
- These explanations are reviewed and can be offered as hints to struggling students
- The mentor earns "Mentor" badges and XP for contributions

**Shared Milestones:**
- Class-wide celebrations: "The whole class has now mastered basic addition!"
- Visible on a "Class Progress" map that shows collective advancement
- Individual contribution is acknowledged without ranking: "You contributed to this milestone"

---

## 2. Octalysis Framework — All 8 Core Drives

### 2.1 Overview

Yu-kai Chou's Octalysis Framework identifies 8 core drives that motivate human behavior. Each maps to specific game mechanics. The framework classifies drives as:

- **White Hat** (top): Epic Meaning, Development, Empowerment — make users feel powerful, smart, purposeful. Positive, sustainable motivation.
- **Black Hat** (bottom): Scarcity, Unpredictability, Loss/Avoidance — create urgency, curiosity, fear. Effective short-term, anxiety-inducing long-term.
- **Left Brain** (extrinsic): Development, Ownership, Scarcity — logic, reward, progress.
- **Right Brain** (intrinsic): Empowerment, Social, Unpredictability — creativity, connection, surprise.

**Critical principle for education:** Lean heavily toward White Hat drives. Use Black Hat drives sparingly and only with ethical guardrails. A student who studies out of fear of losing progress will burn out; a student who studies because they feel growing mastery will persist.

### 2.2 Core Drive 1: Epic Meaning & Calling

**Definition:** The drive to be part of something bigger than oneself. Users feel they are doing something meaningful.

**Educational application:**
- Frame learning as a mission, not a task
- Connect individual effort to collective goals
- Provide narrative context for why this knowledge matters

**CENA implementations:**

**Class Missions:**
- Teacher creates a "Mission" in the admin dashboard (e.g., "Mission: Master Fractions by March 31")
- Students see the mission on their home screen with a progress bar showing class-wide advancement
- Each student's individual progress feeds into the class total
- Completion triggers a class-wide reward (teacher sets: pizza party, free reading time, digital badge)
- Screen: Mission banner at top of home with progress bar, contributing students count, days remaining

**School-Level Campaigns:**
- Semester-long themes: "The Great Math Expedition" — each grade contributes to a school-wide goal
- Visual metaphor: a journey map where the school progresses across a virtual landscape as collective mastery grows
- Weekly updates: "Our school mastered 2,400 concepts this week"

**Purpose Framing:**
- When introducing a new topic, briefly explain real-world relevance: "Fractions help you split a pizza fairly, measure ingredients for cooking, and understand statistics"
- Keep this brief (one sentence) — avoid lecture-mode
- Sourced from the question metadata, stored in the event-sourced question bank

**Beginner's Luck Mechanic:**
- First 3 sessions give 2x XP (already partially implemented: `firstSessionBonusActiveProvider`)
- Frame as: "You're starting a learning journey! Bonus XP for your first steps"
- Creates immediate sense of momentum and meaning

### 2.3 Core Drive 2: Development & Accomplishment

**Definition:** The internal drive to make progress, develop skills, and achieve mastery. The most common gamification drive (points, badges, leaderboards all live here).

**Research note:** This is the drive that most educational gamification over-indexes on. PBL (Points, Badges, Leaderboards) alone is "shallow gamification" (Chou, 2016). Development works best when accomplishments are earned through genuine skill improvement, not just time spent.

**CENA implementations:**

**XP System (already implemented — analysis):**
- Current formula: `xpForLevel(n) = 100 * n * (1 + 0.1 * n)` — gentle exponential
- Level 1 = 110 XP, Level 5 = 750 XP, Level 10 = 2000 XP, Level 20 = 6000 XP
- This curve is well-designed: accessible early, progressively challenging, never impossible
- XP awards: correct answer = 10 XP, streak day = 50 XP, concept mastered = 200 XP
- First session bonus: 2x XP for first 5 questions

**Skill Trees (new — not yet built):**
- Per-subject skill trees derived from the KST prerequisite graph
- Each node = a concept or concept cluster
- Visual progression: unlocking nodes creates a satisfying "spreading light" animation
- Mastery of prerequisite nodes unlocks the next tier — mirrors the BKT-based concept queue already in the LearningSessionActor

**Level-Up Celebrations:**
- Full-screen confetti animation on level up (brief, 2 seconds, auto-dismissing)
- Sound effect (optional, respects device silent mode)
- Level badge updates in the app bar and profile
- Push notification: "You reached Level [N]!" (if notifications enabled)

**Progress Milestones:**
- At 25%, 50%, 75%, and 100% mastery of a subject, show milestone cards
- "You've mastered half of Algebra!" with a visual trophy
- Share button for parents/guardians (exports a simple image)

### 2.4 Core Drive 3: Empowerment of Creativity & Feedback

**Definition:** The drive to engage in a creative process, experiment, and try different combinations. Users need freedom to express themselves and see immediate results.

**Educational application:**
- Open-ended problem solving
- Student-generated content
- Multiple valid solution paths
- Sandbox modes

**CENA implementations:**

**Open-Ended Problem Mode:**
- For concepts at Application/Analysis difficulty, offer open-ended questions alongside MCQ
- "Explain in your own words why 1/2 + 1/3 is not 2/5"
- Evaluated by LLM (Kimi K2.5 via the LLM ACL) for correctness and depth
- Student sees their response compared to a model answer, not just right/wrong

**Solution Strategy Selection:**
- For multi-step problems, let students choose their approach: "Would you like to solve this with [algebra] [diagram] [estimation]?"
- Track which strategies the student prefers and succeeds with — feed into methodology selection

**Student-Generated Questions:**
- After mastering a concept, students can write their own practice question
- Their question goes through the auto-quality gate (same pipeline as teacher-authored questions)
- If approved, it joins the question bank with attribution: "Created by a student in your school"
- The creator earns "Creator" badges and 100 XP per approved question
- This is the ultimate empowerment: the student becomes a contributor, not just a consumer

**Customizable Study Space:**
- Virtual study room with themes the student earns through milestones
- Options: color themes, avatar, background image, widget layout on home screen
- Purely cosmetic — no gameplay advantage — but deeply personal
- Each theme earned (not purchased) reinforces accomplishment

### 2.5 Core Drive 4: Ownership & Possession

**Definition:** The drive to own things and improve them. When users feel they own something, they want to protect it, improve it, and get more.

**Educational application:**
- Customizable avatars and profiles
- Virtual collections (sticker albums, card sets)
- "My progress" feels like a personal artifact
- Portfolio of learning achievements

**CENA implementations:**

**Avatar System:**
- Simple avatar creator: face shape, hair, color, accessories
- New accessories unlocked through achievements (not purchased)
- Avatar appears on leaderboards, class views, and profile
- Age-appropriate: cartoon-style, not realistic

**Learning Portfolio:**
- A "My Learning" section that archives: concepts mastered, questions answered, badges earned, sessions completed
- Timeline view: scroll through learning history by date
- Exportable: parents/guardians can view; teachers can review
- This transforms ephemeral app interactions into a tangible artifact of learning

**Virtual Collectibles:**
- Concept cards: each mastered concept becomes a collectible card with the concept name, an illustration, and a fun fact
- Cards have rarity tiers based on difficulty of the concept: Common (Recall), Uncommon (Comprehension), Rare (Application), Legendary (Analysis)
- Collection screen shows "47/312 cards collected" with a grid of earned cards and grayed-out unearned ones
- No trading, no marketplace — purely personal collection. This avoids commodification.

**Streak Ownership:**
- The current streak is "yours" — framing: "You've built a 12-day streak" (not "You have a 12-day streak")
- The streak freeze is an owned item: "You have 2 freeze shields"
- This is already well-implemented in `streak_widget.dart` and `gamification_state.dart`

### 2.6 Core Drive 5: Social Influence & Relatedness

**Definition:** The drive related to social acceptance, competition, companionship, and mentorship. People are influenced by what others think, do, and say.

**Intersection with SDT:** Directly maps to the Relatedness need. Social features must be carefully designed for education — competition can motivate high performers but crush struggling students.

**CENA implementations:**

**Social Proof Nudges:**
- "15 students in your class practiced today" — creates normative pressure without shaming
- "Most students found this concept tricky at first" — normalizes struggle
- Anonymous and aggregate — never identifies individual students

**Team Challenges:**
- Teacher assigns students to balanced teams (automatic balancing based on current mastery)
- Teams compete on collective improvement (not absolute scores): "Team Alpha improved 12% this week vs Team Beta's 8%"
- Team members see each other's activity (opted-in): "Ahmad practiced 3 times this week"
- Team chat for study discussion (moderated, teacher-visible)

**Mentorship Matching:**
- Students who master a concept can be matched with students struggling with the same concept
- Matching is opt-in for both parties
- The mentor earns "Tutor XP" and a "Mentor" badge tier
- This creates virtuous cycles: teaching reinforces the mentor's own mastery (the protege effect)

**Social Leaderboards (careful design — see Section 6):**
- Improvement-based, not absolute-score-based
- Relative positioning: "You're in the top 40% of your class this week" (not "You're ranked #17")
- Opt-in visibility: students can choose to appear on leaderboards or not

### 2.7 Core Drive 6: Scarcity & Impatience

**Definition:** The drive of wanting something because you cannot have it immediately, or because there is a limited quantity.

**WARNING for education:** This is a Black Hat drive. Use with extreme care. Scarcity mechanics create urgency that can cause anxiety in students, especially younger ones. Never gate core learning content behind scarcity.

**CENA implementations (conservative, White-Hat-leaning):**

**Limited-Time Challenges:**
- Weekly "Challenge Mode" available only Friday-Sunday: special themed question sets
- Completion earns a time-limited badge (the badge stays, but the challenge is only available that week)
- Framing: "This week's special challenge: Geometry Puzzle Marathon!" — not "You have 48 hours or you miss out forever"
- Missing the challenge has ZERO penalty — no lost progress, no shame messaging

**Daily Specials:**
- One "Daily Bonus Question" per day — a curated, interesting question outside the student's normal path
- Answering it (correctly or incorrectly) earns a small bonus (25 XP)
- Creates a reason to log in daily without punishing absence

**Unlock Gates (Pedagogical, Not Artificial):**
- Certain concepts are naturally gated behind prerequisites (from the KST graph)
- Surface this as progression: "Master Fractions to unlock Ratios" — the gate IS the learning
- This is genuine scarcity — you cannot access advanced content without prerequisites
- Never create artificial gates (e.g., "Wait 24 hours to access the next unit")

**Streak Freeze Scarcity:**
- Already implemented: max 2 freezes stored, earned via 7-day streak completion
- This is well-designed scarcity: finite resource, meaningful to protect, earned through effort

### 2.8 Core Drive 7: Unpredictability & Curiosity

**Definition:** The drive to discover what happens next. When things are uncertain, the brain is engaged.

**Educational application:** Surprise rewards, mystery questions, discovery mechanics. These create the "one more try" feeling.

**CENA implementations:**

**Mystery Rewards:**
- After every 10th correct answer, a mystery reward box appears
- Rewards are varied but always positive: bonus XP (25-100), a new avatar accessory, a concept card, a custom badge
- The box itself has a brief opening animation
- Reward quality scales with streak and mastery — longer streaks and higher mastery yield better rewards
- CRITICAL: this is NOT a loot box. The student does NOT pay anything. They do NOT see the odds. It is simply a surprise gift for effort.

**Discovery Badges (Hidden Achievements):**
- Some badges are not listed in the badge grid — they are discovered by accident
- Examples: "Night Owl" (practice after 9 PM), "Speed Demon" (answer 5 questions correctly in under 30 seconds), "Perfectionist" (100% accuracy in a 20-question session)
- When earned, they appear with a "Secret badge unlocked!" celebration
- The badge grid shows "? ?" slots for undiscovered hidden badges — creating curiosity

**Curiosity Hooks on the Home Screen:**
- "Did you know?" daily fact related to the student's current subject area
- "Try this!" link to an interesting question outside their normal path
- Rotating content to prevent staleness (leverages `GamificationRotationService` on the backend)

**Variable Question Types:**
- Mix MCQ, drag-and-drop, matching, short answer, diagram-based questions within a session
- The methodology system already supports this — surface the variety to prevent monotony
- "Surprise" question types that appear occasionally: riddles, visual puzzles, estimation challenges

### 2.9 Core Drive 8: Loss & Avoidance

**Definition:** The drive to avoid negative outcomes. Loss aversion (Kahneman & Tversky, 1979) means losses feel approximately 2x as painful as equivalent gains feel good.

**WARNING for education:** This is the most dangerous drive to use with students. Fear of loss can motivate short-term behavior but causes long-term anxiety, avoidance, and disengagement. Use only with strong guardrails.

**CENA implementations (minimal, carefully bounded):**

**Streak Protection (already implemented well):**
- Streak at risk warning: "Streak at risk! Practice now or a freeze will be used." (already in `streak_widget.dart`)
- Automatic freeze usage — the student does not have to manually deploy a freeze
- Vacation mode pauses the streak entirely — this is the escape valve
- CRITICAL: a broken streak NEVER triggers shame messaging. When a streak breaks: "Your streak was 12 days! Start a new one today."

**HLR Decay Visualization (gentle):**
- The HLR (Half-Life Regression) system already tracks mastery decay
- Show this as a gentle "Review recommended" indicator, not "You're losing mastery!"
- Framing: "It's been 5 days since you practiced Fractions. A quick review will keep you sharp."
- The skill tree shows concepts with decaying mastery as slightly dimmed, not red or alarming

**Session Completion Encouragement:**
- If a student starts a session and tries to exit after 2 questions: "You're making progress! Finish 3 more questions to earn your daily XP."
- This is a soft nudge, NOT a gate — the exit button remains active
- Never: "You'll lose XP if you leave now"

**What CENA must NEVER do with Loss/Avoidance:**
- Never deduct XP for wrong answers (XP only goes up)
- Never deduct XP for inactivity
- Never show a "Your ranking dropped" notification
- Never show "X classmates passed you" messaging
- Never lock content behind daily activity requirements
- Never use "You're falling behind" language

### 2.10 White Hat vs Black Hat Balance

**The ethical ratio for CENA:**

| Drive Category | Drives | Target Usage |
|---|---|---|
| White Hat (top) | Epic Meaning, Development, Empowerment | 70% of gamification surface area |
| Left/Right neutral | Ownership, Social | 20% of gamification surface area |
| Black Hat (bottom) | Scarcity, Unpredictability, Loss | 10% of gamification surface area |

**Implementation rule:** Every Black Hat mechanic must have an explicit escape valve:
- Scarcity (limited-time challenge) -> "Challenges repeat on a rotation — you'll see this one again"
- Unpredictability (mystery box) -> always positive, never empty
- Loss (streak break) -> vacation mode + freeze + no-shame messaging

---

## 3. Intrinsic vs Extrinsic Motivation

### 3.1 The Overjustification Effect

**Definition (Lepper, Greene, & Nisbett, 1973):** When an external reward is provided for an activity that is already intrinsically motivating, the person may attribute their motivation to the reward rather than the activity itself. If the reward is later removed, motivation drops BELOW the original level.

**The classic study:** Children who drew because they enjoyed it were offered a "Good Player" certificate for drawing. After the certificate was removed, the rewarded children drew LESS than children who were never rewarded.

**Implications for CENA:**
- XP and badges must NOT become the reason students learn
- XP and badges must be framed as markers of progress, not the goal itself
- The system must gradually shift emphasis from extrinsic markers to intrinsic mastery signals

### 3.2 The Extrinsic-to-Intrinsic Transition Strategy

**Phase 1: Onboarding (First 2 weeks)**
- Heavy use of extrinsic rewards: frequent XP popups, badge unlocks, level-ups
- These serve as scaffolding — they make the unfamiliar app feel rewarding before the student develops intrinsic interest
- First session bonus (2x XP) is correctly implemented for this phase

**Phase 2: Habit Formation (Weeks 2-8)**
- Gradually reduce the frequency of extrinsic cues
- Shift emphasis to competence feedback: mastery %, skill tree progress
- Introduce social features: class awareness, team challenges
- XP popups become smaller and less frequent
- Progress dashboard becomes the primary home screen element

**Phase 3: Mastery Orientation (Month 2+)**
- Primary UI emphasis on: "What have I learned?" (mastery) over "How many points do I have?" (XP)
- XP continues to accumulate but is de-emphasized in the UI (moved from the main view to a secondary tab)
- Skill tree and concept cards become the dominant feedback mechanism
- This is where the `GamificationRotationService` (FOC-011) is critical — it rotates extrinsic elements while keeping mastery signals stable

**Implementation via GamificationRotationService:**
The existing service already tracks per-student tenure and engagement decay per element. The transition strategy maps directly:
- `DailyStreak` weight: 0.15 (month 1) -> 0.10 (month 3) -> 0.05 (month 6+)
- This is already implemented in `GetStreakWeight()`
- Extend this pattern to ALL gamification elements: XP popups, badges, leaderboard prominence

### 3.3 Informational vs Controlling Rewards

**Key distinction (Deci & Ryan):**
- **Informational rewards** tell you about your competence: "You mastered fractions!" -> supports intrinsic motivation
- **Controlling rewards** pressure you to behave: "Complete 5 more questions to get your badge!" -> undermines intrinsic motivation

**CENA rule:** Every reward must be framed informationally:
- DO: "Badge earned: Fraction Master" (celebrates competence)
- DO NOT: "3 more correct answers and you'll earn the Fraction Master badge!" (creates contingency pressure)
- DO: "You've been practicing consistently for 7 days" (observes behavior)
- DO NOT: "Keep your streak or lose it!" (threatens loss)

### 3.4 Age-Appropriate Motivation Strategies

| Age Group | Grade | Extrinsic Emphasis | Intrinsic Emphasis | Key Strategy |
|---|---|---|---|---|
| K-5 (5-11) | Elementary | High — visual, immediate | Curiosity, exploration | Sticker books, character growth, story worlds |
| 6-8 (11-14) | Middle | Moderate — peer-visible | Competence, social | Team challenges, skill trees, relative rankings |
| 9-12 (14-18) | High | Low — subtle | Mastery, purpose | Portfolio building, real-world connections, mentorship |
| College (18+) | Post-secondary | Minimal — opt-in only | Self-directed mastery | Progress analytics, spaced repetition stats, efficiency metrics |

---

## 4. XP and Level Systems for Learning

### 4.1 Skill-Based XP vs Time-Based XP

**Principle:** XP should reward learning outcomes, not time spent. A student who masters a concept in 5 minutes should earn more XP than a student who spends 30 minutes without mastering it. Time-based XP (e.g., "1 XP per minute online") incentivizes idling, not learning.

**CENA's current model (well-designed):**
```
Correct answer:     10 XP (base)
Streak day:         50 XP
Concept mastered:   200 XP (the big reward — mastery, not volume)
First session bonus: 2x XP for first 5 questions of the day
Hint penalty:       -2 XP per hint level (from LearningSessionActor: max(2, 10 - hintLevel * 2))
```

**Recommended additions:**

**Difficulty Bonus:**
```
Recall difficulty:         10 XP base
Comprehension difficulty:  15 XP base
Application difficulty:    20 XP base
Analysis difficulty:       30 XP base
```
Rationale: harder questions should be worth more. The BKT system already scales difficulty based on mastery, so higher-mastery students naturally encounter harder (higher-XP) questions.

**Speed Bonus (careful):**
- If the student answers correctly in the top 20th percentile of response time for that concept, award +5 bonus XP
- Use their OWN historical response time distribution, NOT a global benchmark
- This rewards personal improvement, not raw speed
- Disabled for students under age 10 (speed pressure is inappropriate for young children)

**Review Bonus:**
- Reviewing a previously mastered concept (triggered by HLR decay) earns 15 XP instead of the normal 10
- This incentivizes the spaced repetition that the HLR system recommends

### 4.2 XP Formula Analysis

**Current:** `xpForLevel(n) = 100 * n * (1 + 0.1 * n)` = `100n + 10n^2`

| Level | XP Required | Cumulative XP | Sessions at ~200 XP/session |
|---|---|---|---|
| 1 | 110 | 110 | <1 |
| 2 | 240 | 350 | ~2 |
| 5 | 750 | 2,250 | ~11 |
| 10 | 2,000 | 10,500 | ~53 |
| 15 | 3,750 | 27,750 | ~139 |
| 20 | 6,000 | 60,000 | ~300 |
| 30 | 12,000 | 172,500 | ~863 |

**Assessment:** The curve is gentle enough that students level up frequently in the first week (motivating) but levels still feel meaningful at month 3+. Level 10 requires roughly 2 months of daily practice — a reasonable "veteran" marker.

**Potential issue: XP inflation at scale.** After 6 months, highly active students could be level 25+. Solutions:

**Prestige System (optional, high-school+ only):**
- At level 20, offer "Prestige" — reset to level 1 with a visible prestige star next to the level badge
- Prestige students earn XP at the same rate but accumulate prestige stars
- Prestige stars are permanent, visible on profile and leaderboards
- This creates an infinite progression system without inflating the level cap
- Never force prestige — it must be the student's choice (autonomy)

**Subject-Specific XP Tracks:**
- Instead of one global XP pool, split into Math XP, Science XP, etc.
- Each subject has its own level
- Total XP (sum of all subjects) determines the global level
- This prevents students from leveling up by grinding their strongest subject while ignoring weak areas

### 4.3 Anti-Inflation Measures

**XP caps per activity type per day:**
```
Max XP from correct answers: 500/day (50 questions)
Max XP from streaks: 50/day (1 streak day)
Max XP from concept mastery: no cap (mastery is genuine achievement)
Max XP from reviews: 150/day (10 review questions)
```

**Diminishing returns on repetition:**
- If a student answers questions on the same concept more than 5 times in a session after already mastering it, XP drops to 2 per correct answer
- This prevents XP farming by grinding already-mastered easy content

### 4.4 Event-Sourced XP Tracking

The `XpAwarded_V1` event is already in the student actor. Extend it:

```csharp
public record XpAwarded_V1(
    string StudentId,
    int XpDelta,          // amount earned
    int TotalXp,          // new total
    string Source,        // "correct_answer", "streak_day", "concept_mastered", "review_bonus"
    string? ConceptId,    // if applicable
    string? SessionId,    // if during a session
    DateTimeOffset Timestamp);
```

This event structure enables analytics: which XP sources drive engagement? Is review XP motivating return visits? Are students gaming the system?

---

## 5. Achievement and Badge Systems

### 5.1 Meaningful vs Meaningless Badges

**Meaningless badges (avoid):**
- "Logged in today" — rewards mere presence, not effort
- "Answered 1 question" — trivially achievable, no competence signal
- "Used a hint" — rewards dependency, not mastery

**Meaningful badges (implement):**
- Badges that reflect genuine skill milestones (mastering a concept, completing a subject)
- Badges that reflect sustained effort (streak milestones, consistent practice over weeks)
- Badges that reflect exploration (trying new subjects, new question types)
- Badges that reflect social contribution (mentoring, creating questions)

**CENA's current badge catalogue (already well-designed):**
- Streak badges: 3, 7, 30 days (effort persistence)
- Mastery badges: 1, 5, 20 concepts mastered (competence milestones)
- Engagement badges: First session, 10 sessions (participation depth)
- Special badges: Renaissance (all subjects in a week), Level 10 (overall commitment)

### 5.2 Recommended Badge Expansion

**Subject Mastery Badges:**
```
"Math Apprentice"  — Master 10 math concepts
"Math Scholar"     — Master 50 math concepts
"Math Expert"      — Master all math concepts in a grade level
(Repeat for each subject)
```

**Learning Behavior Badges:**
```
"Comeback Kid"     — Return after 7+ days of inactivity and complete a session
"Night Owl"        — Practice after 9 PM (hidden badge)
"Early Bird"       — Practice before 7 AM (hidden badge)
"Marathon Runner"  — Complete a 30-minute session
"Sprinter"         — Complete 10 questions correctly in under 5 minutes
"No Hints Needed"  — Answer 20 consecutive questions without using a hint
"Error Hunter"     — Get a question wrong, review the explanation, then get a similar question right
"Perfectionist"    — 100% accuracy in a 20-question session (hidden badge)
```

**Social Badges:**
```
"Team Player"      — Participate in 3 team challenges
"Mentor"           — Help 3 classmates via peer tutoring
"Question Creator" — Create a question that passes quality gate
"Class MVP"        — Most improvement in class this week
```

**Meta Badges:**
```
"Collector"        — Earn 10 badges of any kind
"Diverse Learner"  — Earn at least 1 badge from every category
"Badge Hunter"     — Discover a hidden badge
```

### 5.3 Badge Rarity Tiers

| Tier | Color | Glow Effect | Criteria | % of Students Who Earn |
|---|---|---|---|---|
| Common | Bronze/gray | None | Basic milestones | >80% |
| Uncommon | Silver/blue | Subtle shimmer | Moderate effort | 40-80% |
| Rare | Gold | Pulsing glow | Significant achievement | 10-40% |
| Legendary | Purple/diamond | Particle effects | Exceptional dedication | <10% |
| Secret | Rainbow/iridescent | Reveal animation | Hidden achievements discovered by accident | Unknown |

**Implementation:** Add a `BadgeRarity` enum to `BadgeDefinition`:
```dart
enum BadgeRarity { common, uncommon, rare, legendary, secret }
```

Rarity determines the visual treatment in the badge grid: border color, glow effect, animation on earn.

### 5.4 Badge Display

**Profile Badge Showcase:**
- Students can choose 3 "pinned" badges to display on their profile
- Pinned badges appear on leaderboards and class views
- This creates ownership (Octalysis Drive 4) — the badge selection is a personal expression

**Badge Detail Dialog (already implemented in `badge_detail_dialog.dart`):**
- Extend to show: earn date, rarity tier, percentage of classmates who have earned it
- For unearned badges: show progress toward earning (e.g., "7/20 concepts mastered — 13 more to go")
- For hidden badges: show "???" and a cryptic hint: "Some achievements are found by those who practice at unusual hours..."

---

## 6. Leaderboard Design — Ethical for Education

### 6.1 Why Traditional Leaderboards Harm Education

**Research (Hanus & Fox, 2015; Dominguez et al., 2013):**
- Traditional "Top 10" leaderboards consistently demotivate students in the bottom 50%
- Students who see they are far from the top feel hopelessness, not motivation
- Competitive leaderboards increase anxiety and reduce help-seeking behavior
- Gender effects: competitive leaderboards disproportionately demotivate female students in STEM subjects

**The fundamental problem:** A leaderboard that shows absolute scores creates a zero-sum perception — your success requires someone else's relative failure.

### 6.2 Ethical Leaderboard Patterns for CENA

**Pattern 1: Relative Positioning (show nearby, not top)**
- Instead of showing ranks 1-10, show the student and the 2 students immediately above and below
- "You're rank #15 of 30" with avatars at #13, #14, #15 (you), #16, #17
- This creates a local competitive context without exposing the full hierarchy
- Students near the bottom see only their immediate neighbors, not how far they are from the top

**Screen mockup:**
```
+----------------------------------+
| This Week's Progress             |
|                                  |
|  #13  Ahmad    +320 XP   [===]  |
|  #14  Noor     +290 XP   [===]  |
|  #15  YOU      +270 XP   [==]   |  <-- highlighted
|  #16  Sara     +250 XP   [==]   |
|  #17  Omar     +230 XP   [=]    |
|                                  |
| Your rank: #15 of 30             |
+----------------------------------+
```

**Pattern 2: Improvement-Based Leaderboard**
- Rank by IMPROVEMENT this week, not absolute scores
- A student who went from 40% to 50% mastery ranks above a student who stayed at 95%
- This rewards effort and growth, not prior knowledge
- Resets weekly so no one is permanently ahead

**Screen mockup:**
```
+-----------------------------------+
| Most Improved This Week           |
|                                   |
|  1. Layla    +8% mastery growth   |
|  2. YOU      +6% mastery growth   |  <-- highlighted
|  3. Khaled   +5% mastery growth   |
|                                   |
| Keep it up! You grew more than    |
| 80% of your classmates this week. |
+-----------------------------------+
```

**Pattern 3: Team Leaderboards**
- Rank teams, not individuals
- Teams are balanced by the system (teacher can adjust)
- Team score = sum of individual improvement
- This creates collaborative motivation: help your teammates improve

**Pattern 4: Opt-In Visibility**
- Students can choose: "Show me on class leaderboards" (default: OFF)
- Students can choose: "Show me my class ranking" (default: ON but shows relative position only)
- Teachers can disable leaderboards entirely for their class

### 6.3 Anti-Bullying Safeguards

- No "bottom of the leaderboard" visibility — never show the lowest-ranked students
- No "dropped X ranks" notifications
- No identification of students who are struggling
- Leaderboard pseudonymity option: students can appear as their avatar name instead of real name
- Teacher moderation: teacher can see the full leaderboard but students only see their local window
- Report mechanism: if a student feels pressure from leaderboard dynamics, they can report to teacher with one tap

### 6.4 When to Show vs Hide Leaderboards

| Scenario | Show? | Reason |
|---|---|---|
| Class with balanced skill levels | Yes (improvement-based) | Healthy competition |
| Class with wide skill gaps | Team only | Individual ranking would demoralize |
| Student repeatedly checking rankings | Throttle (1x/day) | Obsessive checking indicates anxiety |
| Student in bottom 10% | Hide individual, show team | Protect self-efficacy |
| First 2 weeks of app use | Hide all | Let students build confidence first |
| Teacher disabled leaderboards | Hide all | Respect teacher judgment |

---

## 7. Quest and Mission Systems

### 7.1 Quest Hierarchy for Learning

**Daily Quests (1-2 per day):**
- Small, achievable goals that create daily login motivation
- Examples: "Practice for 10 minutes", "Answer 5 questions on any topic", "Review 1 decaying concept"
- Reward: 25-50 bonus XP
- Always achievable — never "Score 100% on 10 questions" (sets up failure)
- Generate dynamically based on the student's current state (use the `ConceptQueue` and HLR timers)

**Screen mockup:**
```
+--------------------------------------+
| Today's Quests                       |
|                                      |
| [ ] Practice for 10 minutes   +25XP |
|     [=====----] 5/10 min            |
|                                      |
| [ ] Review a decaying concept  +25XP |
|     [not started]                    |
|                                      |
| [x] Answer 5 questions        +50XP |
|     [complete!] CLAIMED              |
+--------------------------------------+
```

**Weekly Missions (2-3 per week):**
- Larger goals that encourage sustained engagement
- Examples: "Master 2 new concepts", "Practice in 3 different subjects", "Maintain your streak for 5 consecutive days"
- Reward: 100-200 bonus XP + a badge component (collect 4 weekly badges for a monthly badge)
- Reset every Monday
- At least 1 mission should be social: "Participate in a team challenge"

**Monthly Campaigns:**
- Themed month-long narratives
- Example: "The Algebra Adventure" — complete 4 weekly missions to unlock a narrative chapter
- Each chapter reveals a piece of a story or map
- Completing all 4 chapters earns a Legendary badge
- Campaigns repeat on a semester cycle (aligned with GamificationRotationService rotation)

**Semester-Long Campaigns (Epic Quests):**
- Tie into the school's academic calendar
- Example: "Master 50 concepts by the end of semester" — a long arc that feels achievable at 2-3 concepts/week
- Progress bar visible on the home screen throughout the semester
- Completion earns the rarest badges and a "congratulations" from the teacher (triggered via OutreachSchedulerActor)

### 7.2 Side Quests for Enrichment

**Challenge Mode:**
- Optional, harder-than-normal questions on topics the student has already mastered
- Framed as "Brain Teasers" or "Challenge Corner"
- No penalty for getting them wrong — they are explicitly framed as stretch goals
- Reward: rare concept cards, hidden badge progress

**Cross-Subject Quests:**
- "The Connection Quest" — answer questions that bridge two subjects (math in science, reading in social studies)
- These require the question bank to tag questions with multiple subjects
- Reward: "Renaissance" badge progression

**Creativity Quests:**
- "Create a question about [concept]" — student writes a question for the question bank
- "Explain [concept] to a younger student" — produces a simplified explanation
- Evaluated by LLM + human review pipeline
- Reward: "Creator" badge, 100 XP per approved creation

### 7.3 Boss Battles (Major Assessments)

**Concept:** Transform major tests/assessments into "Boss Battles" — narrative-framed assessments.

**Design:**
- A Boss Battle covers all concepts from a unit or chapter
- It has a visual "boss" character (a friendly one — not scary for young students): "The Fraction Dragon", "The Geometry Giant"
- The boss has "HP" that decreases with each correct answer
- Incorrect answers do NOT increase the boss's HP — they simply don't reduce it (avoid punishment)
- The battle has 15-25 questions covering the full range of difficulty
- Students can "retreat" and practice more before retrying (no penalty for retreating)
- Defeating the boss earns a unique boss badge and significant XP (500+)

**Screen mockup:**
```
+----------------------------------------+
| BOSS BATTLE: The Fraction Dragon       |
|                                        |
|            [dragon illustration]       |
|           HP: [========--] 80%         |
|                                        |
| Question 4 of 20                       |
|                                        |
| What is 3/4 + 1/2?                     |
|                                        |
| ( ) 4/6                               |
| ( ) 1 1/4                             |
| ( ) 5/4                               |
| ( ) 1/3                               |
|                                        |
| [Retreat and Practice]   [Answer]      |
+----------------------------------------+
```

**Boss Battle Schedule:**
- End of each concept cluster (natural assessment point)
- Teachers can trigger boss battles for specific dates (aligned with school test schedules)
- Students can voluntarily attempt boss battles anytime for concepts they've studied

### 7.4 Narrative-Driven Learning Paths

**Concept:** Wrap the curriculum in a loose story that provides context for each unit.

**Implementation considerations:**
- The narrative is a thin wrapper, not a distraction — 1-2 sentences of story per unit
- "You've arrived at the Valley of Variables. To cross it, you must understand how x changes..."
- The story provides Epic Meaning (Octalysis Drive 1) without consuming session time
- Age-adapted: younger students get character-driven stories; older students get real-world scenario framing

**Content delivery:**
- Narrative text stored as metadata on concept clusters in the question bank
- Displayed as a brief interstitial between units (skippable)
- Characters can be customized with the student's avatar

---

## 8. Reward Schedules — Behavioral Psychology

### 8.1 Schedule Types and Optimal Uses

**Fixed Ratio (FR):** Reward after every N responses.
- Example: Every 5 correct answers = bonus XP
- Produces a "post-reinforcement pause" — student works fast until reward, then slows down
- **Best for:** Early engagement, onboarding phase
- **CENA use:** First session bonus (2x XP for first 5 questions) is effectively FR-5

**Variable Ratio (VR):** Reward after an unpredictable number of responses.
- Example: Mystery reward appears after ~10 correct answers, but could be 7 or 13
- Produces the highest, most consistent response rate
- **This is the most powerful schedule** — slot machines use it
- **CENA use:** Mystery rewards after variable intervals; hidden badge unlocks at unknown thresholds
- **Ethical constraint:** NEVER use variable ratio with paid currency or real-money purchases. Only with XP and virtual items.

**Fixed Interval (FI):** Reward available after a fixed time period.
- Example: Daily login reward available every 24 hours
- Produces "scalloping" — low activity just after reward, increasing as next reward approaches
- **Best for:** Daily engagement hooks
- **CENA use:** Daily quests (reset every 24 hours), weekly missions (reset every Monday)

**Variable Interval (VI):** Reward available after unpredictable time periods.
- Example: Teacher sends encouragement at random times
- Produces a steady, moderate response rate
- **Best for:** Maintaining baseline engagement without urgency
- **CENA use:** Teacher outreach via OutreachSchedulerActor (already designed for variable-interval engagement)

### 8.2 Schedule Mapping to CENA Activities

| Activity | Schedule Type | Rationale |
|---|---|---|
| Correct answer XP | Continuous (FR-1) | Every correct answer must feel rewarded |
| Streak day bonus | Fixed Interval (FI-24h) | Daily, predictable, habit-forming |
| Mystery rewards | Variable Ratio (VR-~10) | Curiosity, "one more try" motivation |
| Badge unlocks | Fixed Ratio (varies) | Known thresholds create clear goals |
| Hidden badges | Variable Ratio (unknown) | Discovery, surprise, curiosity |
| Daily quests | Fixed Interval (FI-24h) | Regular engagement cadence |
| Weekly missions | Fixed Interval (FI-7d) | Sustained weekly engagement |
| Teacher outreach | Variable Interval (VI) | Ambient connection, not predictable |
| Boss battles | Fixed Ratio (end of unit) | Natural assessment rhythm |
| Level-up celebrations | Variable Ratio (XP-dependent) | XP accumulation varies by student effort |

### 8.3 Loot Box Ethics in Education

**Absolute rule: CENA will not implement loot boxes.**

A loot box requires:
1. A paid currency or real-money purchase
2. Randomized rewards of varying value
3. Unknown odds

CENA's mystery rewards are NOT loot boxes because:
- They cost nothing (no currency, no payment)
- All rewards are positive (there is no "bad" outcome)
- They are earned through learning (correct answers), not purchased
- The student never chooses to "open" a box — it appears automatically as a surprise

If CENA ever introduces premium features, they must NEVER be random-reward-based. Premium features should be transparent: "Pay X for feature Y."

---

## 9. Age-Appropriate Recommendations

### 9.1 K-5 (Ages 5-11, Elementary)

**Cognitive development (Piaget):** Concrete operational stage. Children understand rules and categories but struggle with abstract reasoning.

**Motivation profile:** Highly responsive to immediate, visual, concrete rewards. Strong need for autonomy-support because school is heavily controlled. Social needs centered on teacher approval and peer belonging.

**Gamification design:**

| Element | K-5 Implementation |
|---|---|
| XP display | Large, colorful numbers with star bursts on increment |
| Levels | Named levels with character growth: "Seedling", "Sprout", "Sapling", "Tree" |
| Badges | Large icons with bright colors, animate on earn, show on avatar |
| Streaks | Flame icon with size that grows (small flame at 1 day, large at 7+) |
| Leaderboards | TEAM ONLY. No individual rankings. Class-wide "we did it together" |
| Quests | Story-based: "Help the math wizard find the missing numbers!" |
| Boss battles | Friendly characters, never scary. "The Counting Cloud" not "The Math Dragon" |
| Loss avoidance | NONE. No streak pressure, no decay visibility, no ranking drops |
| Feedback | "Great job!" with animations. Never just "Wrong." Always "Not quite, but..." |
| Social | Class-wide goals. No peer comparison. Teacher messages. |
| Rewards | Immediate, every question. Confetti, sounds, character reactions |
| Session length | 5-10 minutes max. "You did amazing! Come back tomorrow!" |
| Avatar | Simple, cute, cartoon. Limited options to avoid decision paralysis |

**What to avoid for K-5:**
- No timers or speed pressure
- No visible rankings against peers
- No loss mechanics of any kind
- No text-heavy achievements — use icons and short labels
- No complex navigation — 2 taps maximum to reach any feature

### 9.2 Grades 6-8 (Ages 11-14, Middle School)

**Cognitive development:** Transitioning to formal operational stage. Beginning to understand abstract concepts. Strong peer orientation. Identity formation begins.

**Motivation profile:** Peak responsiveness to social features. High sensitivity to social comparison (both motivating and threatening). Beginning to value competence-based esteem. May resist activities perceived as "babyish."

**Gamification design:**

| Element | 6-8 Implementation |
|---|---|
| XP display | Compact, integrated into progress bar. Less "party" animation |
| Levels | Numbered levels with titles: "Level 12: Analyst" |
| Badges | Badge showcase on profile. Rarity tiers visible. Peer-visible |
| Streaks | Calendar strip (already implemented). Streak freeze as strategic resource |
| Leaderboards | Improvement-based + team. Individual opt-in. Relative positioning |
| Quests | Weekly missions with tangible goals. Team challenges central |
| Boss battles | Unit tests reframed. Competitive mode (opt-in): fastest completion |
| Loss avoidance | Minimal: streak at risk warning only. Vacation mode available |
| Feedback | Detailed explanations. Error classification. "Here's what to review" |
| Social | Team challenges, mentor matching, class activity feed |
| Rewards | Variable ratio mystery rewards. Badge rarity creates social currency |
| Session length | 15-20 minutes. "Challenge yourself to one more mission?" |
| Avatar | More customization. Accessories earned through achievements |

**What to avoid for 6-8:**
- Avoid "childish" language and visuals — this age group is hyper-sensitive to being condescended to
- Avoid forced social exposure — always opt-in
- Avoid public displays of failure
- Avoid overly long narrative intros — get to the learning quickly

### 9.3 Grades 9-12 (Ages 14-18, High School)

**Cognitive development:** Full formal operational. Abstract reasoning, hypothetical thinking, metacognition. Future-oriented. Beginning to internalize academic goals.

**Motivation profile:** Increasing intrinsic motivation for subjects of personal interest. Decreasing responsiveness to "game" framing. Value efficiency and control. Peer influence remains strong but shifts from social approval to shared identity.

**Gamification design:**

| Element | 9-12 Implementation |
|---|---|
| XP display | De-emphasized. Shown in settings/profile, not on home screen |
| Levels | Optional. Show mastery % and concept count instead |
| Badges | Portfolio-style. "Your transcript shows 142 concepts mastered" |
| Streaks | Maintained but not emphasized. No pressure messaging |
| Leaderboards | Opt-in only. Improvement-based. Subject-specific |
| Quests | Goal-setting: "I want to master Calculus by March" (student-defined) |
| Boss battles | Comprehensive review sessions. Practice exam mode |
| Loss avoidance | Only HLR review reminders. Framed as "spaced repetition science" |
| Feedback | Analytical: accuracy trends, response time trends, mastery velocity |
| Social | Study groups, mentorship, question creation |
| Rewards | Competence-based: "You can now skip the basics and go straight to advanced" |
| Session length | 20-30 minutes. Student controls pace completely |
| Avatar | Minimal or realistic. Profile photo option |

**What to avoid for 9-12:**
- Avoid anything that feels like a "kids' game" — visual tone should be clean, modern, minimal
- Avoid excessive celebration animations — a subtle checkmark is better than confetti for this age group
- Avoid mandatory social features — high schoolers value privacy
- Avoid XP framing — talk about "mastery" and "concepts" instead

### 9.4 College (Ages 18+)

**Motivation profile:** Fully self-directed. Strong need for efficiency. Motivated by competence, career relevance, and intellectual curiosity. Gamification is tolerated only if it provides genuine value (progress tracking, spaced repetition optimization).

**Gamification design:**

| Element | College Implementation |
|---|---|
| XP display | Hidden by default. Show study time and mastery stats |
| Levels | Not applicable. Show: "312 concepts mastered, 89% retention" |
| Badges | Not applicable unless context-specific (certification progress) |
| Streaks | Show as "study consistency" metric in analytics view |
| Leaderboards | Not applicable in default mode. Optional competitive mode |
| Quests | Self-defined study plans. "I have an exam on April 15" |
| Boss battles | Practice exam mode with detailed analytics |
| Loss avoidance | None. Adults manage their own schedules |
| Feedback | Data-rich: accuracy by topic, time efficiency, predicted retention |
| Social | Study groups, peer review of explanations |
| Rewards | Competence feedback: "Your predicted exam readiness: 78%" |
| Session length | Student-controlled, no prompts |
| Avatar | Not applicable |

---

## 10. Ethical Guidelines — Harmful Gamification

### 10.1 Gamification That Harms Students

The following patterns are documented as harmful in educational contexts and must NEVER be implemented in CENA:

**10.1.1 Punitive Loss Mechanics**
- Deducting XP for wrong answers or inactivity
- Mandatory daily minimums with penalties for missing
- "Your mastery is decaying!" warnings with red/alarm visuals
- Taking away earned badges or achievements
- Research: Punitive mechanics increase anxiety and avoidance behavior (Deterding, 2012)

**10.1.2 Exploitative Engagement Loops**
- Dark patterns that make it hard to stop (e.g., auto-playing the next question without pause)
- "Just one more!" prompts designed to override the student's judgment
- Notifications that create FOMO (fear of missing out)
- Streak mechanics designed to create guilt rather than pride
- Research: Children under 13 cannot distinguish persuasive design from genuine engagement (Radesky et al., 2020)

**10.1.3 Harmful Social Comparison**
- Public display of bottom rankings
- "X students passed you" notifications
- Comparing absolute scores between students of different ability levels
- Public display of wrong answers or mistakes
- Research: Social comparison increases test anxiety and decreases self-efficacy in low-performing students (Müller-Kalthoff et al., 2017)

**10.1.4 Addiction Patterns**
- Variable ratio rewards tied to real money or scarce virtual currency
- Loot boxes or gacha mechanics
- FOMO-driven limited availability that punishes absence
- Infinite scroll / autoplay patterns
- Research: WHO recognized gaming disorder (ICD-11, 2019); educational apps must not replicate these patterns

**10.1.5 Monetization Tied to Progress**
- Pay-to-win: purchasing XP, levels, or badges
- Pay-to-skip: purchasing ability to skip prerequisite content
- Premium features that give competitive advantage
- Paywalls on core educational content

### 10.2 CENA's Ethical Commitment

**Design principles:**
1. **Every student can succeed without spending money.** All core learning features are free. Premium features (if any) are convenience-only (e.g., themes, advanced analytics for parents).
2. **Every gamification element has an off switch.** Teachers can disable any element for their class. Students can hide elements in settings.
3. **No element creates anxiety.** If telemetry shows a feature correlated with increased disengagement, it is flagged for review.
4. **All rewards are earned through learning.** No random chance determines access to content or features.
5. **Progress is permanent.** Earned mastery, XP, and badges are never taken away. Decay in the HLR system reduces the review recommendation priority, not the badge.
6. **Session boundaries are respected.** The app actively encourages breaks: "Great session! Your brain needs rest to consolidate learning."
7. **Parental transparency.** Parents can see all gamification elements and their child's engagement patterns.
8. **Alignment with research.** Gamification design is reviewed against current meta-analyses. The `GamificationRotationService` embodies this: when research shows diminishing returns (Zeng et al., 2024, g=0.822 overall but negligible after 1 semester), the system adapts.

### 10.3 Ethical Review Checklist

Before implementing any new gamification feature, answer:

1. Does this feature work equally well for high-performing and struggling students?
2. Can a student ignore this feature entirely and still access all learning content?
3. Does this feature support at least one of the three SDT needs (autonomy, competence, relatedness)?
4. Is the feature transparent (students understand how it works)?
5. Would a child psychologist approve this feature for the target age group?
6. Does the feature have a natural stopping point (not infinite engagement)?
7. Is the feature equally fair regardless of learning speed?
8. Can a teacher disable this feature?

If any answer is "no," the feature needs redesign before implementation.

---

## 11. Competitor Analysis

### 11.1 Duolingo

**What they do well:**
- Streak mechanics are the primary engagement driver — incredibly effective at daily habit formation
- The XP system is simple and transparent (10-20 XP per question)
- "Streak Freeze" and "Streak Society" (social streak sharing) are retention powerhouses
- Skill tree is clear and satisfying — nodes light up as you progress
- Hearts system creates loss avoidance without being too punitive (refill over time)
- Daily leaderboards in "Leagues" create competitive motivation
- Push notifications are masterfully timed (personalized based on usage patterns)

**What they do poorly (and CENA must avoid):**
- The hearts system (limited attempts) punishes mistakes — directly contradicts learning research
- League demotion creates anxiety — getting demoted from Diamond to Obsidian feels terrible
- The monetization model pressures users to buy Super Duolingo to remove hearts
- Notifications become aggressive if you miss days: "We miss you!" is fine, but 3 notifications per day is manipulative
- The gamification sometimes prioritizes engagement over learning — students optimize for streak maintenance, not language acquisition

**CENA takeaways:**
- Steal: Streak freeze mechanic (already done), skill tree visualization, daily quest structure
- Avoid: Hearts/limited attempts, league demotion, aggressive re-engagement notifications
- Adapt: Streak society concept -> CENA's "class streak" (X students in the class maintained their streak today)

### 11.2 ClassDojo

**What they do well:**
- Class-level community building is excellent — parents, teachers, students on one platform
- Points system is teacher-controlled — the teacher decides what earns points
- "Class Story" creates shared narrative and belonging (Relatedness)
- Monster avatars are charming and age-appropriate for elementary
- Behavior tracking gamification empowers teachers

**What they do poorly:**
- Points can be deducted — teachers can take away points for bad behavior, which mixes behavior management with learning gamification (toxic combination)
- Public display of points creates social comparison
- Heavily teacher-dependent — gamification stops when the teacher stops using it
- No mastery or learning progress — purely behavioral

**CENA takeaways:**
- Steal: Class-level shared experiences, parent visibility, avatar system
- Avoid: Point deduction, public behavior display
- Adapt: Teacher-set missions -> CENA missions, but points are never deducted

### 11.3 Kahoot!

**What they do well:**
- Real-time classroom competition is viscerally engaging
- Speed-based scoring creates excitement and urgency
- The "podium" at the end is a celebratory moment
- Music and visual design create genuine excitement
- Simple, accessible — works on any device

**What they do poorly:**
- Speed-based scoring penalizes careful thinkers and students with learning differences
- Only the top 3 get recognition — everyone else is implicitly a loser
- No long-term progression — each game is independent
- Assessment validity is low — students optimize for speed, not understanding
- Can create anxiety for students who are slow readers

**CENA takeaways:**
- Steal: Real-time classroom events as a special mode, celebratory moments
- Avoid: Speed-based scoring (or make it opt-in and use personal bests, not global competition)
- Adapt: "Live challenge" mode where the class does the same questions simultaneously, but scoring is accuracy-based with personal improvement tracking

### 11.4 Prodigy (Math Game)

**What they do well:**
- Full RPG world: avatar, pets, quests, storyline — deeply immersive
- Math questions are embedded in gameplay — feels like playing, not studying
- Adaptive difficulty based on student performance
- Curriculum-aligned (Common Core, state standards)
- Teacher dashboard shows learning analytics

**What they do poorly:**
- Heavy monetization: premium membership required for best pets, costumes, and gameplay features
- Pay-to-win: premium players have better items that affect gameplay performance
- The game world can be more engaging than the math itself — students may focus on collecting pets rather than learning
- Limited subjects (math only at launch, recently expanding)

**CENA takeaways:**
- Steal: RPG progression wrapped around learning content, adaptive difficulty, curriculum alignment
- Avoid: Pay-to-win monetization, game mechanics that distract from learning
- Adapt: Concept cards as a collectible system that IS the learning content (each card represents a mastered concept)

### 11.5 DragonBox

**What they do well:**
- Brilliant concept: abstract algebra concepts represented as visual puzzles
- No text, no numbers initially — pure visual manipulation that teaches algebraic thinking
- Progressive revelation: visual symbols gradually transform into real numbers and variables
- Self-paced, no penalties, no time pressure
- Based on solid pedagogical research (Sorbonne University collaboration)

**What they do poorly:**
- No social features — completely solo experience
- Limited replay value once all puzzles are solved
- No progression system beyond puzzle completion
- Expensive as a standalone app with limited content
- No teacher integration

**CENA takeaways:**
- Steal: Progressive abstraction (visual -> symbolic), penalty-free exploration
- Avoid: Being only a solo experience
- Adapt: DragonBox-style visual puzzles as a special question type for abstract math concepts

---

## 12. Actor Architecture Mapping

### 12.1 Current Architecture (Proto.Actor, Event-Sourced)

```
StudentActor (virtual, event-sourced, per-student)
  |-- LearningSessionActor (classic child, session-scoped)
  |-- StagnationDetectorActor (classic child, monitoring)
  |-- OutreachSchedulerActor (classic child, proactive)
```

Gamification state is currently stored in `StudentState`:
- `TotalXp`, `CurrentStreak`, `LongestStreak`, `LastActivityDate`
- Events: `XpAwarded_V1`, `StreakUpdated_V1`

Backend service: `GamificationRotationService` (stateless, called by StudentActor)

### 12.2 Proposed Gamification Actor Architecture

**Principle: Keep gamification state in the StudentActor aggregate, but extract behavior into child actors.**

The StudentActor already owns XP and streak state. Adding quest/badge/reward logic directly into the StudentActor would bloat it beyond the 500KB memory budget and violate single responsibility. Instead, follow the Fortnite pattern: stateless children, stateful parent.

```
StudentActor (virtual, event-sourced)
  |-- LearningSessionActor (existing)
  |-- StagnationDetectorActor (existing)
  |-- OutreachSchedulerActor (existing)
  |
  |-- GamificationActor (NEW — classic child, long-lived)
        |
        |-- Responsibilities:
        |     - Badge evaluation (check conditions after each attempt event)
        |     - Quest tracking (daily/weekly/monthly progress)
        |     - Reward schedule management (variable ratio tracking)
        |     - Rotation engine invocation (calls GamificationRotationService)
        |     - Leaderboard position updates (publishes to NATS)
        |
        |-- State: NONE (reads from parent's StudentState, emits events back)
        |-- Pattern: event-driven reactor
```

**Why a single GamificationActor, not separate QuestActor/BadgeActor/RewardActor:**

1. **Memory budget:** Each classic child actor has overhead (~2-5KB). Three separate actors = 6-15KB per student. With 10,000 concurrent students, that is 60-150MB just for gamification actor overhead. A single GamificationActor: 2-5KB per student.

2. **Co-location:** Quest completion often triggers badge evaluation which triggers reward distribution. If these are separate actors, you need inter-actor messaging for what should be a single synchronous evaluation. A single actor handles the chain: `OnConceptMastered -> CheckBadges -> CheckQuests -> EmitRewards`.

3. **Simplicity:** Proto.Actor classic children are lightweight message handlers. One actor with clear internal method dispatch is more maintainable than three actors with cross-communication.

### 12.3 GamificationActor Design

```csharp
public sealed class GamificationActor : IActor
{
    private readonly StudentState _studentState; // read-only reference to parent
    private readonly IGamificationRotationService _rotationService;
    private readonly ILogger<GamificationActor> _logger;

    // No mutable state — all state is in the parent's StudentState
    // This actor is a pure reactor: receive events, evaluate conditions, emit events back

    public Task ReceiveAsync(IContext context) => context.Message switch
    {
        // Triggered by parent after each attempt
        EvaluateGamification cmd => HandleEvaluateGamification(context, cmd),

        // Triggered by parent once per day (on first session start)
        EvaluateDailyReset cmd => HandleDailyReset(context, cmd),

        // Triggered by parent on session end
        EvaluateSessionComplete cmd => HandleSessionComplete(context, cmd),

        // Triggered by parent periodically (weekly)
        EvaluateQuestProgress cmd => HandleQuestProgress(context, cmd),

        _ => Task.CompletedTask
    };
}
```

**Event flow:**

```
Student answers question
  -> LearningSessionActor evaluates answer
  -> LearningSessionActor sends EvaluateAnswer result to parent (StudentActor)
  -> StudentActor persists ConceptAttempted_V1 event
  -> StudentActor sends EvaluateGamification to GamificationActor
  -> GamificationActor checks:
       - Badge conditions (streak thresholds, mastery counts, etc.)
       - Daily quest progress
       - Mystery reward trigger (variable ratio check)
       - Rotation freshness
  -> GamificationActor sends back 0-N events:
       - BadgeEarned_V1 (if threshold crossed)
       - QuestProgressUpdated_V1 (quest step completed)
       - MysteryRewardTriggered_V1 (variable ratio hit)
       - XpBonusAwarded_V1 (quest completion bonus, review bonus, etc.)
  -> StudentActor persists all events to Marten
  -> StudentActor publishes events to NATS for real-time client updates
  -> Flutter client receives events via SignalR -> updates gamification_state providers
```

### 12.4 Event Schema Extensions

```csharp
// New events for the gamification domain

public record BadgeEarned_V1(
    string StudentId,
    string BadgeId,
    string BadgeName,
    BadgeRarity Rarity,
    string Category,        // "streak", "mastery", "engagement", "special", "secret"
    DateTimeOffset EarnedAt);

public record QuestAccepted_V1(
    string StudentId,
    string QuestId,
    string QuestType,       // "daily", "weekly", "monthly", "semester"
    string Description,
    int TargetValue,
    DateTimeOffset ExpiresAt);

public record QuestProgressUpdated_V1(
    string StudentId,
    string QuestId,
    int CurrentValue,
    int TargetValue,
    bool IsComplete);

public record QuestCompleted_V1(
    string StudentId,
    string QuestId,
    int XpReward,
    string? BadgeReward,
    DateTimeOffset CompletedAt);

public record MysteryRewardTriggered_V1(
    string StudentId,
    string RewardType,      // "bonus_xp", "avatar_item", "concept_card", "badge"
    string RewardId,
    int? XpAmount,
    DateTimeOffset TriggeredAt);

public record LeaderboardPositionUpdated_V1(
    string StudentId,
    string LeaderboardType, // "improvement_weekly", "team_weekly"
    int Position,
    int TotalParticipants,
    DateTimeOffset CalculatedAt);

public record GamificationRotated_V1(
    string StudentId,
    string PreviousPrimary,
    string NewPrimary,
    string PreviousSecondary,
    string NewSecondary,
    string RotationReason,
    DateTimeOffset RotatedAt);
```

### 12.5 Flutter State Integration

The existing `gamification_state.dart` providers map well to the event-driven architecture. Extend with:

```dart
// Quest state providers
final activeQuestsProvider = StateProvider<List<Quest>>((ref) => []);
final completedQuestsProvider = StateProvider<List<Quest>>((ref) => []);

// Mystery reward state
final pendingRewardProvider = StateProvider<MysteryReward?>((ref) => null);

// Leaderboard state
final leaderboardPositionProvider = StateProvider<LeaderboardPosition?>((ref) => null);

// Gamification rotation state
final currentRotationProvider = StateProvider<GamificationRotation?>((ref) => null);
```

These providers are updated when the Flutter client receives events via SignalR from the NATS-backed event stream.

### 12.6 NATS Subject Mapping

```
cena.gamification.badge.earned.{studentId}      -> Badge earned events
cena.gamification.quest.progress.{studentId}     -> Quest progress updates
cena.gamification.quest.complete.{studentId}     -> Quest completion
cena.gamification.reward.mystery.{studentId}     -> Mystery reward triggers
cena.gamification.leaderboard.{classId}          -> Class leaderboard updates
cena.gamification.rotation.{studentId}           -> Rotation events
```

---

## 13. CENA Current State and Gap Analysis

### 13.1 What Is Already Built (Well)

| Feature | Location | Quality |
|---|---|---|
| XP system with leveling formula | `gamification_state.dart` | Solid — formula is well-balanced |
| Streak counter with animation | `streak_widget.dart` | Excellent — pulsing flame, calendar strip, at-risk warning |
| Streak freeze (max 2, earned) | `gamification_state.dart` | Well-designed scarcity mechanic |
| Vacation mode | `gamification_state.dart` | Good safety valve against streak anxiety |
| Badge catalogue (10 badges) | `gamification_state.dart` | Good start, needs expansion |
| Badge grid UI | `gamification_screen.dart` | Clean implementation, category colors, lock icons |
| XP popup animation | `xp_popup.dart` | Polished — bounce-in + float-up + fade-out |
| XP progress bar | `gamification_screen.dart` | Animated, shows level context |
| Recent achievements feed | `gamification_screen.dart` | Relative timestamps, XP deltas |
| First session bonus | `gamification_state.dart` | 2x XP for first 5 questions per day |
| Gamification rotation engine | `GamificationRotationService.cs` | Research-backed (Zeng et al.), tenure-based weight decay |
| XP events in student actor | `student_actor.cs` | Event-sourced: `XpAwarded_V1`, `StreakUpdated_V1` |

### 13.2 Gaps to Fill

| Gap | Priority | SDT Need | Octalysis Drive | Effort |
|---|---|---|---|---|
| Skill tree visualization | P1 | Competence | Development | 3 days |
| Quest/mission system | P1 | Autonomy | Epic Meaning | 5 days |
| GamificationActor (backend) | P1 | All | All | 3 days |
| Badge expansion (30+ badges) | P2 | Competence | Development | 2 days |
| Badge rarity tiers | P2 | Ownership | Unpredictability | 1 day |
| Hidden/secret badges | P2 | Curiosity | Unpredictability | 1 day |
| Mystery rewards | P2 | Curiosity | Unpredictability | 2 days |
| Improvement-based leaderboard | P2 | Relatedness | Social | 3 days |
| Team challenges | P2 | Relatedness | Social | 4 days |
| Boss battles | P3 | Competence | Development | 3 days |
| Avatar system | P3 | Ownership | Ownership | 3 days |
| Concept cards (collectibles) | P3 | Ownership | Ownership | 2 days |
| Student-generated questions | P3 | Autonomy | Empowerment | 4 days |
| Narrative learning paths | P3 | Meaning | Epic Meaning | 3 days |
| Class missions | P3 | Relatedness | Epic Meaning | 2 days |
| Mentor matching | P4 | Relatedness | Social | 4 days |
| Learning portfolio | P4 | Ownership | Ownership | 2 days |
| Age-adaptive gamification UI | P4 | All | All | 5 days |

### 13.3 Implementation Order Recommendation

**Wave 1 (P1 — blocks engagement):**
1. GamificationActor — backend foundation for all new features
2. Quest/mission system — daily/weekly structure drives retention
3. Skill tree visualization — the missing competence feedback loop

**Wave 2 (P2 — enhances retention):**
4. Badge expansion + rarity tiers + hidden badges
5. Mystery rewards (variable ratio schedule)
6. Improvement-based leaderboard

**Wave 3 (P3 — deepens engagement):**
7. Boss battles
8. Avatar system + concept cards
9. Class missions + narrative paths

**Wave 4 (P4 — long-term differentiation):**
10. Mentor matching + student-generated questions
11. Age-adaptive gamification UI
12. Learning portfolio

---

## References

- Chou, Y. (2016). Actionable Gamification: Beyond Points, Badges, and Leaderboards. Packt Publishing.
- Csikszentmihalyi, M. (1990). Flow: The Psychology of Optimal Experience. Harper & Row.
- Deci, E. L., Koestner, R., & Ryan, R. M. (1999). A meta-analytic review of experiments examining the effects of extrinsic rewards on intrinsic motivation. Psychological Bulletin, 125(6), 627-668.
- Deci, E. L., & Ryan, R. M. (2000). The "what" and "why" of goal pursuits: Human needs and the self-determination of behavior. Psychological Inquiry, 11(4), 227-268.
- Deterding, S. (2012). Gamification: Designing for motivation. Interactions, 19(4), 14-17.
- Dominguez, A., et al. (2013). Gamifying learning experiences: Practical implications and outcomes. Computers & Education, 63, 380-392.
- Hanus, M. D., & Fox, J. (2015). Assessing the effects of gamification in the classroom: A longitudinal study on intrinsic motivation, social comparison, satisfaction, effort, and academic performance. Computers & Education, 80, 152-161.
- Kahneman, D., & Tversky, A. (1979). Prospect theory: An analysis of decision under risk. Econometrica, 47(2), 263-292.
- Lepper, M. R., Greene, D., & Nisbett, R. E. (1973). Undermining children's intrinsic interest with extrinsic reward. Journal of Personality and Social Psychology, 28(1), 129-137.
- Rigby, S., & Przybylski, A. K. (2009). Virtual worlds and the learner hero. Theory and Research in Education, 7(2), 214-223.
- Vansteenkiste, M., Simons, J., Lens, W., Sheldon, K. M., & Deci, E. L. (2004). Motivating learning, performance, and persistence: The synergistic effects of intrinsic goal contents and autonomy-supportive contexts. Journal of Personality and Social Psychology, 87(2), 246-260.
- Zeng, J., et al. (2024). Effects of gamification on K-12 students' learning: A meta-analysis. (cited in FOC-011)
