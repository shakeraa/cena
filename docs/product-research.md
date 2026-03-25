# Cena Product Research

## 1. Competitor Analysis

### Direct Competitors

**Squirrel AI (China)**
- Closest architectural analog to Cena
- Decomposes subjects into 10,000-30,000 "nano-level knowledge points" per subject
- Knowledge graph mapping prerequisite relationships
- Probabilistic Knowledge State (PKS) diagnoses mastery after only 15-20 questions
- Beat human tutors in controlled studies (+5.4 points vs +0.7 for humans)
- Weaknesses: locked to Chinese curriculum, tied to physical tutoring centers, no student-facing knowledge graph visualization, no methodology switching

**Brilliant.org**
- Problem-first pedagogy: students attempt problems before instruction
- Personalized practice predicts "optimal next problem"
- Strong gamification: streaks, XP, levels, characters ("Blorbs")
- Targets grades 3-12 in math, CS, science
- Weaknesses: no knowledge graph visualization, no adaptive methodology switching, not exam-aligned

**Khan Academy (Khanmigo)**
- Free, comprehensive content library
- Khanmigo uses LLMs as Socratic tutor (guides without giving answers)
- Adaptive assessment adjusts difficulty in real time
- Weaknesses: not deeply personalized at methodology level, no knowledge graph visualization, minimal gamification

**ALEKS (McGraw Hill)**
- Built on Knowledge Space Theory (KST)
- Uses ~25-30 adaptive questions to map student knowledge state
- Mastery-based: one topic at a time until demonstrated mastery
- "ALEKS Pie" provides simple visual of progress
- Weaknesses: limited subjects, simple pie chart visualization, no methodology switching, no gamification

**Duolingo**
- Gold standard for retention mechanics
- Streaks (7-day streak users 3.6x more likely to stay)
- Leagues/leaderboards (+25% lesson completion)
- DAU grew 4.5x over 4 years to 34M
- Weaknesses: language-only, shallow pedagogy, no knowledge graph

**Anki / Quizlet**
- Spaced repetition tools with strong retention for factual recall
- "Tools" not "mentors" — no pedagogical guidance, no adaptation, no knowledge graph

**SmartyMe**
- Engaging visual diagram style for physics/engineering
- Addictive gamified learning framing
- Weaknesses: no adaptive system, no knowledge graph, no deep personalization

### Gaps Cena Fills

| Gap | Who Has It? | Cena's Approach |
|-----|------------|-----------------|
| Student-facing knowledge graph visualization | Nobody does this well | Interactive visual map where growth is visible and motivating |
| Adaptive methodology switching | Nobody | Seamlessly switches between Socratic, spaced repetition, project-based, Feynman, worked examples, analogy-based, and retrieval practice |
| Cognitive load personalization with per-student thresholds | Squirrel AI partially | Per-student fatigue threshold detection and session calibration |
| Exam-aligned + curriculum-agnostic | Most are either generic or locked to one system | Syllabus-agnostic architecture with Bagrut as first target |
| Mentor persona (not just a tool) | Khanmigo partially | Full mentor relationship: remembers history, thoughts, annotations |
| Combined gamification + deep pedagogy | Duolingo has gamification; Squirrel AI has pedagogy | Both in one system |

---

## 2. Gamification Best Practices

### What Works
- **Streaks** are the single most powerful retention mechanic (Duolingo: 7-day streak = 3.6x long-term retention, 55% of users return daily to maintain streaks)
- **Progress visualization** (XP bars, leveling, unlocking) — courses under 5 min had 74% completion vs 36% for 15+ min
- **Milestone celebrations** — Duolingo's animations increased learning time by 17%
- **Leagues/leaderboards** — +25% lesson completion, but must be optional

### Risks
- **Extrinsic crowding out intrinsic**: Extrinsic rewards harm academic performance in highly intrinsically motivated students while helping low-motivation students
- **Novelty effect decay**: High engagement early, then drop-off with prolonged exposure
- **Leaderboard anxiety**: Students in low positions may disengage
- **Dependency on instant gratification**: Students accustomed to gamified learning may struggle with non-gamified content

### Recommendations for Cena
1. **"Stealth gamification"**: Embed rewards into learning process naturally — the knowledge graph growing *is itself* a reward
2. **Adaptive gamification intensity**: More game elements for struggling students; less for intrinsically motivated ones
3. **Loss aversion > reward seeking**: Streak mechanics and "protect your progress" framing outperform pure rewards
4. **Social proof without toxic competition**: "X students mastered this concept this week" rather than rank-ordered leaderboards
5. **Meaningful badges**: Represent intellectual achievement ("Mastered Integration," "Connected 50 Concepts") not just activity

---

## 3. Adaptive Learning & Stagnation Detection

### Current Landscape
- Every competitor adjusts content *difficulty* or *sequence*:
  - **Squirrel AI**: adjusts question difficulty and topic sequence based on probabilistic knowledge state; does not change pedagogical approach
  - **ALEKS**: selects the next topic based on Knowledge Space Theory readiness; always uses the same mastery-and-assess cycle
  - **Khanmigo**: adjusts hint depth and Socratic question phrasing; stays within the Socratic method exclusively
  - **Brilliant**: adjusts problem complexity and scaffolding; always uses problem-first pedagogy
- Nobody adjusts the *teaching method itself* — switching from Socratic dialogue to project-based learning to spaced repetition based on what works for each individual student is Cena's genuine innovation
- This creates a defensible moat: methodology switching requires both a pedagogical framework (mapping error types to methods) and per-student effectiveness tracking, which no current competitor has infrastructure for

### Behavioral Signals for Stagnation Detection
- Response time increasing on similar-difficulty problems
- Accuracy plateau or decline over N attempts on related concepts
- Session duration shortening (student giving up sooner)
- Increased time between sessions (disengagement)
- Error pattern analysis: same error types repeating suggests current method isn't working

### Recommended Composite Stagnation Score
1. **Accuracy plateau**: <5% improvement over last N attempts on a concept cluster
2. **Response time drift**: Increasing average response time
3. **Session abandonment**: Ending sessions earlier than personal baseline
4. **Error type repetition**: Same error pattern recurring
5. **Annotation sentiment**: Student annotations expressing frustration or confusion

### Methodology Switching Principles
- Seamlessness matters — student should not feel a jarring switch
- **Multi-modal error analysis**: Rule-based errors -> drill/practice; conceptual misunderstanding -> Socratic dialogue; motivational stagnation -> project-based or Feynman technique
- Log which methodology was active at switch time to build per-student effectiveness profile

---

## 4. Knowledge Graph Approaches

### Construction Strategy
- **LLM-assisted extraction** from Bagrut syllabi and textbooks with human expert validation
- Far more scalable than Squirrel AI's fully manual approach (10,000-30,000 points per subject)
- **Pipeline**: (1) Feed official Bagrut syllabus PDFs and approved textbooks to LLM → (2) Extract concept nodes with prerequisite relationships → (3) Human subject-matter expert reviews and corrects edges → (4) QA pass validates prerequisite ordering against actual curriculum sequence
- **Estimated density**: 500–2,000 concept nodes per subject (vs. Squirrel AI's 10K–30K), optimized for meaningful pedagogical granularity rather than maximum decomposition
- **Maintenance**: Re-run extraction pipeline annually when Ministry of Education updates syllabi; delta-diff against existing graph to preserve student overlay data
- **Quality gate**: No concept node enters the production graph without at least one expert-validated prerequisite chain from a foundational node

### Architecture
- **Two-layer model**: Domain knowledge graph (shared, what exists to learn) + Student knowledge overlay (personal, what this student knows)
- **Edge types**: prerequisite, builds-on, related-to, conflicts-with (for common misconceptions)
- **Temporal tracking**: When each node was learned, last reviewed, mastery trajectory over time
- Enables spaced repetition scheduling and decay prediction

### Visualization Research
- Open Learner Models: Making knowledge states visible improves self-regulated learning, goal setting, and help-seeking behavior
- Students tend to overestimate their knowledge — honest mastery-level coloring serves as a reality check
- The graph should be **beautiful, interactive, and shareable** — viral acquisition mechanic

---

## 5. Cognitive Load Management

### Core Principles
- Working memory holds ~7 chunks but can only process 2-4 simultaneously (~20 seconds without rehearsal)
- **Intrinsic load** reduced by chunking and sequencing prerequisites first
- **Extraneous load** minimized by clean UI, no split attention, integrated diagrams
- **Germane load** (schema construction) is the "good" effort — maximize this while keeping total load below threshold

### Optimal Session Design
- **Microlearning sweet spot: 5-10 minutes** per focused learning unit
- **Sustained attention: ~20 minutes** before needing a break
- Cap sessions at **20-25 minutes** before suggesting a break (adjustable per student)
- Student session tolerance varies widely (12–30 minutes based on age, subject difficulty, and time of day) — the system builds individual cognitive load profiles from the first 5 sessions and refines continuously

### Fatigue Detection Metrics
1. Response time increase (rolling average drift)
2. Accuracy drop-off within a session
3. Interaction pattern changes (less scrolling, shorter answers, more re-reads)
4. Session duration patterns vs personal baseline
5. Time-of-day effects (circadian rhythm performance variation)

### Fatigue Response
- Don't hard-stop — offer lighter "cooldown" activities (review mastered content, explore knowledge graph, read own annotations)
- Ensure learning quanta are genuinely atomic (each quantum introduces at most 2 new ideas, keeping total working memory demand within the 2–4 simultaneous chunk processing limit)

---

## 6. Retention & Engagement

### The Retention Crisis
- EdTech industry retention: only 4-27%
- **Critical window: Day 3 to Day 30** — the habit formation zone where most churn occurs
- Most companies need 6-15 months to break even, but average customer lifespan is ~4 months

### What Keeps Students Coming Back
1. **Daily habit loop**: Streak + notification + short session + reward
2. **Early wins**: First session must demonstrate value — diagnostic -> "here's your knowledge map" -> first micro-lesson -> graph grows
3. **Visible progress**: Knowledge graph growing is Cena's secret weapon — every session should end with new nodes/connections
4. **Personalized timeline**: "You're 67% of the way to mastering 5-unit Math" with date estimate
5. **Social proof**: "3 students in your class also mastered this today" (cohort models see 2-3x higher retention)
6. **Proactive re-engagement**: Detect declining engagement and intervene before student disappears
7. **Emotional design**: Celebrations, encouragement, personality in the mentor

### Common Drop-Off Points
- After diagnostic/onboarding (if too long or no immediate value)
- Day 3-7 (novelty wears off, habit not formed)
- After first difficult concept (without methodology switching, students hit wall and leave)
- Week 3-4 (if no tangible progress toward goal is visible)
- After exam season (if perceived as exam-prep only)

### Recommendations
- Design the **first 5 minutes obsessively**: diagnostic -> knowledge map reveal -> first micro-lesson -> graph grows
- Implement streaks from day 1 with streak freeze as premium feature
- Weekly progress summaries: "This week you learned 12 new concepts and strengthened 8"
- After Bagrut exams, pivot messaging to "continue growing" — position as lifelong learning tool

---

## 7. Monetization Strategy

### Israeli Market Context
- EdTech market valued at $1.2 billion, 90% internet penetration
- 350+ active e-learning platforms, 163 K-12 EdTech companies
- **Private Bagrut tutoring: ~3,000 NIS (~$800) per subject** — this is the price anchor
- Government allocated ~$120M for digital education initiatives

### Recommended Model

**Freemium Core (Free Tier)**
- Limited daily learning units (2-3 concepts/day)
- Basic knowledge graph view
- Basic gamification

**Premium Subscription**
- Unlimited learning, full knowledge graph with visualization
- Advanced gamification (leagues, detailed badges)
- Personalized timeline estimates
- Methodology adaptation and cognitive load management
- **Pricing: 79-99 NIS/month or 699-899 NIS/year** (~$200-250/year)
- Positioning: covers ALL subjects vs 3,000 NIS/subject for private tutoring (5 subjects = 15,000 NIS)

**Additional Revenue Streams**
- Family plan: discount for 2+ students in same household
- B2B school licensing: teacher dashboards with class-level analytics
- Seasonal exam-prep bundles (3 months before Bagrut)

---

## 8. Unique Value Proposition & Strategy

### Cena's Four Pillars (No Competitor Has All Four)
1. **Student-facing knowledge graph visualization** — the graph IS the product
2. **Adaptive methodology switching** — genuinely novel, no competitor does this
3. **Cognitive load personalization** — per-student fatigue thresholds
4. **Exam-aligned, curriculum-agnostic architecture** — Bagrut first, then any country

### Where to Double Down
1. **Knowledge graph as hero product** — beautiful, interactive, shareable (viral acquisition)
2. **Methodology switching as "invisible magic"** — "this app just gets how I need to learn"
3. **Mentor memory creates switching costs** — leaving Cena = losing your entire learning history
4. **Bagrut as wedge, not ceiling** — then expand to AP (US), A-Levels (UK), JEE/NEET (India), Gaokao (China)
5. **Cognitive load management as care signal** — "the app told me to take a break" builds trust

### Key Risks
- Knowledge graph construction cost — mitigate with LLM-assisted extraction
- Gamification backlash — keep it "stealth," weight toward intrinsic rewards
- Day 3-30 retention cliff — invest disproportionately in onboarding
- Hebrew-first market size (~100K Bagrut students/year) — architecture must be ready for international expansion within 12-18 months

---

## Sources
- LeveragAI, Lionwood, Cleveroad, Gloobia — EdTech landscape reviews
- PMC, Springer, Frontiers — gamification systematic reviews and meta-analyses
- Springer IJAIED, arXiv, CZI — knowledge graph research
- MDPI Education Sciences, Brain Sciences — cognitive load theory
- Userpilot, NTQ Europe, Loyalty.cx — EdTech retention data
- Lenny's Newsletter, Sensor Tower — Duolingo case studies
- Monetizely, Ptolemay — EdTech pricing models
- Ken Research, Tracxn — Israeli market data
- MIT Technology Review, HundrED — Squirrel AI analysis
