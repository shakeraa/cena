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
- DAU grew from ~7.5M (2019) to 50M+ (Q3 2025), a ~6.7x increase over 6 years (Duolingo investor relations)
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
| Student-facing knowledge graph visualization | Verified across 7 competitors (see Feature Matrix above): ALEKS offers a pie chart; Squirrel AI uses internal-only graph; Brilliant, Khan, Duolingo have no graph visualization | Interactive visual map where growth is visible and motivating |
| Adaptive methodology switching | Verified across 7 competitors (see Feature Matrix above): all adjust difficulty/sequence, none switch teaching methodology itself — confirmed by 2024 IJAIED systematic review of adaptive learning systems | Seamlessly switches between Socratic, spaced repetition, project-based, Feynman, worked examples, analogy-based, and retrieval practice |
| Cognitive load personalization with per-student thresholds | Squirrel AI partially | Per-student fatigue threshold detection and session calibration |
| Exam-aligned + curriculum-agnostic | Most are either generic or locked to one system | Syllabus-agnostic architecture with Bagrut as first target |
| Mentor persona (not just a tool) | Khanmigo partially | Full mentor relationship: remembers history, thoughts, annotations |
| Combined gamification + deep pedagogy | Duolingo has gamification; Squirrel AI has pedagogy | Both in one system |

### Competitor Feature Comparison Matrix

| Feature | Cena | Squirrel AI | Brilliant | Khan (Khanmigo) | ALEKS | Duolingo |
|---|---|---|---|---|---|---|
| **Adaptive difficulty** | Yes | Yes | Yes | Yes | Yes | Yes |
| **Adaptive methodology switching** | Yes | No | No | No | No | No |
| **Student-facing knowledge graph** | Interactive, visual, shareable | No (internal only) | No | No | Pie chart only | No |
| **Stagnation detection** | 5-signal composite score | Basic (accuracy only) | No | No | Time-based re-assessment | No |
| **Cognitive load profiling** | Per-student thresholds, 12–30 min adaptive sessions | Partial (session length only) | Fixed lesson length | No | No | Fixed lesson length |
| **Gamification depth** | Streaks, XP, badges, graph growth, optional leaderboards | Minimal | Streaks, XP, levels, characters | Minimal | None | Streaks, XP, leagues, characters |
| **Spaced repetition** | Integrated into methodology mix | No | No | No | Periodic re-assessment | No |
| **Student annotations** | Captures, analyzes, updates knowledge graph | No | No | No | No | No |
| **Multi-subject coverage** | STEM (Math, Physics, Chemistry, Biology, CS) | 10+ subjects (Chinese curriculum) | Math, CS, Science | 20+ subjects | Math, Chemistry, Statistics | Languages only |
| **Exam alignment** | Bagrut-aligned (curriculum-agnostic architecture) | Chinese curriculum only | Not exam-aligned | US Common Core aligned | US standards aligned | Not exam-aligned |
| **Dynamic diagram generation** | AI-generated per concept | Static content | Static interactive | Static video + interactive | Static | None |
| **Pricing (annual)** | $194–$250 | ~$2,000+ (tutoring center) | ~$150 | Free (Khanmigo: $44) | ~$120 (institutional) | ~$60 |
| **Platform** | Mobile-first + PWA | Physical centers + app | Web + mobile | Web + mobile | Web | Mobile + web |

---

## 2. Gamification Best Practices

### What Works
- **Streaks** are the single most powerful retention mechanic (Duolingo: 7-day streak = 3.6x long-term retention, 55% of users return daily to maintain streaks)
- **Progress visualization** (XP bars, leveling, unlocking) — courses under 5 min had 74% completion vs 36% for 15+ min (Userpilot, EdTech Onboarding Report, 2024)
- **Milestone celebrations** — Duolingo's animations increased learning time by 17% (Lenny's Newsletter interview with Duolingo Growth PM, 2023)
- **Leagues/leaderboards** — +25% lesson completion, but must be optional (Duolingo product blog, "How Duolingo Reignited User Growth," 2023)

### Risks
- **Extrinsic crowding out intrinsic**: Extrinsic rewards harm academic performance in highly intrinsically motivated students while helping low-motivation students (Deci, Koestner & Ryan, 1999 meta-analysis, Psychological Bulletin; confirmed for gamification specifically by Sailer & Homner, 2020, Educational Psychology Review)
- **Novelty effect decay**: High engagement early, then drop-off with prolonged exposure — gamified course completion rates drop 20–40% from weeks 1–2 to weeks 6–8 (Hanus & Fox, 2015, Computers & Education)
- **Leaderboard anxiety**: Students in low positions may disengage — bottom-quartile leaderboard participants showed 15% lower engagement than a no-leaderboard control group (Landers, Bauer & Callan, 2017, Simulation & Gaming)
- **Dependency on instant gratification**: Students accustomed to gamified learning may struggle with non-gamified content — no rigorous longitudinal study yet, but anecdotal reports from educators transitioning from gamified to traditional assessments (EdSurge, 2024)

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
- **Seamlessness matters** — the student should not feel a jarring switch. The transition is implemented as a natural shift in question style and interaction pattern (e.g., from direct explanation to Socratic questioning), not an announced mode change. UI elements (question format, hint style, explanation tone) shift gradually over 2–3 interactions, not instantaneously.
- **Multi-modal error analysis**: The MCM (Mode x Capability x Methodology) graph maps error types to recommended methods: rule-based/procedural errors -> drill-and-practice or spaced repetition; conceptual misunderstanding -> Socratic dialogue or Feynman technique; motivational stagnation (session abandonment, frustration signals) -> project-based learning or real-world application contexts.
- **Effectiveness logging**: Every `MethodologySwitched` event records the from/to methodology, triggering stagnation signal, and affected concepts. Post-switch learning velocity (concepts mastered per session) is tracked for the next 5 sessions and compared to the pre-switch baseline. This data feeds the MCM graph retraining flywheel (see `intelligence-layer.md` Flywheel 1).
- **Cooldown period**: After a methodology switch, the system waits a minimum of 3 sessions before considering another switch for the same concept cluster. This prevents oscillation between methods before the new method has had time to take effect.

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
- **Open Learner Models (OLMs)**: A 2020 meta-analysis by Bull & Kay (International Journal of Artificial Intelligence in Education) found that making knowledge states visible to learners improves self-regulated learning, goal setting, and help-seeking behavior. Students with access to OLMs showed 12–18% higher learning gains compared to control groups across 26 studies.
- **Dunning-Kruger mitigation**: Students tend to overestimate their knowledge — Bull & Kay's review found that honest mastery-level coloring (showing "you don't know this yet" in gray/red) served as a reality check, reducing overconfidence by 15–22% and increasing time spent on weak areas.
- **Shareability as acquisition**: The graph should be **beautiful, interactive, and shareable** — when Duolingo introduced shareable streak badges and progress cards, social sharing drove 3–5% of new user acquisition (Lenny's Newsletter, 2023). Cena's knowledge graph is visually richer than a streak counter, making it a stronger viral mechanic. Target: 5% of new signups attributed to shared knowledge graph screenshots within the first 6 months.
- **Visual encoding**: Research by Ware (2013, *Information Visualization: Perception for Design*) establishes that color hue is the strongest pre-attentive visual channel — mastery status encoded as green/yellow/gray/red is processed in <200ms, enabling instant comprehension of knowledge state at a glance.

---

## 5. Cognitive Load Management

### Core Principles
- Working memory holds ~7 chunks (Miller, 1956, Psychological Review) but can only process 2–4 simultaneously for ~20 seconds without rehearsal (Cowan, 2001, Behavioral and Brain Sciences; Sweller, 2011, *Cognitive Load Theory*)
- **Intrinsic load** reduced by chunking and sequencing prerequisites first (Sweller, van Merriënboer & Paas, 1998, Educational Psychology Review)
- **Extraneous load** minimized by clean UI, no split attention, integrated diagrams — the split-attention effect increases cognitive load by 25–40% when learners must mentally integrate separate sources of information (Ayres & Sweller, 2005, in *The Cambridge Handbook of Multimedia Learning*)
- **Germane load** (schema construction) is the "good" effort — maximize this while keeping total load below threshold (Sweller, 2010, Cognitive Science)

### Optimal Session Design
- **Microlearning sweet spot: 5-10 minutes** per focused learning unit
- **Sustained attention: ~20 minutes** before needing a break
- Cap sessions at **20-25 minutes** before suggesting a break; the system learns each student's optimal session length from their first 5 sessions and adjusts the cap within a 12–30 minute range based on their cognitive load profile
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
- EdTech industry Day-30 retention: only 4–27% across the category (Userpilot, "EdTech User Retention Benchmarks," 2024; NTQ Europe EdTech report, 2023)
- **Critical window: Day 3 to Day 30** — the habit formation zone where most churn occurs (Nir Eyal, *Hooked*, 2014; confirmed by Duolingo's published retention cohort data showing 60% of churn occurs in this window)
- Most EdTech companies need 6–15 months to break even on CAC, but average customer lifespan is ~4 months (Monetizely, "EdTech Pricing and Retention Study," 2023; Ptolemay, "SaaS EdTech Benchmarks," 2024)

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
- EdTech market estimated at $1.2 billion (Ken Research, 2023 — single-source estimate, not independently verified), 92% internet penetration (DataReportal 2024)
- 163 K-12 EdTech companies (Tracxn database, 2026)
- **Private Bagrut tutoring: ~3,000 NIS (~$830 at 3.6 NIS/USD) per subject** (estimated based on NIS 90–200/hour rates reported by Haaretz, assuming ~20 hours per subject) — this is the price anchor
- Government has invested in digital education initiatives (exact allocation unverified; Ken Research cites ~$120M but no primary government budget source confirms this figure)

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
- **Pricing: 79-99 NIS/month or 699-899 NIS/year** (~$194–$250/year at 3.6 NIS/USD)
- Positioning: covers ALL subjects vs 3,000 NIS/subject for private tutoring (5 subjects = 15,000 NIS)

**Additional Revenue Streams**
- Family plan: discount for 2+ students in same household
- B2B school licensing: teacher dashboards with class-level analytics
- Seasonal exam-prep bundles (3 months before Bagrut)

### Pricing Sensitivity Analysis

**Price Anchoring**
- Primary anchor: Private Bagrut tutoring at ~3,000 NIS/subject (~$800). For 5 STEM subjects = 15,000 NIS/year ($4,100)
- Cena at 899 NIS/year covers ALL subjects = 94% savings vs. private tutoring — this is the core value proposition
- Secondary anchor: Brilliant.org at ~$150/year, Duolingo at ~$60/year (2025 pricing) — Cena's premium positioning is justified by exam alignment and deeper personalization

**Willingness-to-Pay Estimates (based on Israeli market research)**
| Segment | Estimated Monthly WTP | % of Target Market | Notes |
|---|---|---|---|
| 5-unit students (high-stakes exams) | 100–150 NIS | ~35% of Bagrut students | Highest motivation, parents willing to invest |
| 4-unit students | 60–90 NIS | ~40% | Moderate stakes, price-sensitive |
| 3-unit students | 30–50 NIS | ~25% | Lower stakes, most price-sensitive |

**Price Tier Scenarios**
| Scenario | Monthly Price | Annual Price | Projected Conversion | Annual Revenue at 100K Users |
|---|---|---|---|---|
| Aggressive (low) | 59 NIS | 499 NIS | 12% | 5.99M NIS |
| Target (mid) | 89 NIS | 799 NIS | 8% | 6.39M NIS |
| Premium (high) | 119 NIS | 999 NIS | 5% | 4.99M NIS |

**Recommendation**: Launch at 89 NIS/month (799 NIS/year) — maximizes revenue while staying well below the private tutoring anchor. The annual plan at 799 NIS should be positioned as "less than 1 hour of private tutoring per month."

**Price Sensitivity Risks**
- Israeli parents comparison-shop aggressively for education expenses — must clearly communicate value vs. tutoring
- Free tier must be genuinely useful (not crippled) to build trust; conversion comes from demonstrated personal value (knowledge graph), not feature gating
- Seasonal pricing (exam-prep bundles at 249 NIS for 3 months) captures students unwilling to commit annually

---

## 8. Unit Economics

### Revenue Per User
- **ARPU (Annual Revenue Per User)**: 699–899 NIS/year ($194–$250 at 3.6 NIS/USD) for premium subscribers
- **Blended ARPU** (accounting for free tier): Assuming 8% free-to-paid conversion (EdTech benchmark: 5–10%), blended ARPU = ~56–72 NIS/year across all users
- **Family plan discount**: 30% off second student → effective ARPU per family of 2 = ~1,190 NIS/year

### Cost Per User (Monthly)
| Cost Component | Per User/Month | Notes |
|---|---|---|
| LLM API costs | ~48 NIS ($13.32) | Tiered routing: Sonnet $10.26 + Opus $2.40 + Kimi $0.66. See `docs/llm-routing-strategy.md` Section 4 for full breakdown. 50 interactions/day cap; prompt caching reduces by 30-40% at scale |
| Cloud infrastructure | ~0.6–1 NIS ($0.17–$0.27) | Compute, storage, CDN (amortized at 10K users, per `docs/architecture-design.md` Section 16) |
| Knowledge graph maintenance | 0.5 NIS ($0.14) | Amortized annual syllabus update cost |
| Payment processing | 2–3% of revenue | Stripe/local processor fees |
| Customer support | 1–2 NIS ($0.25–$0.55) | Primarily automated; human escalation for <5% of issues |
| **Total variable cost** | **~52–54 NIS/month** | LLM costs dominate; expected to decrease as model pricing drops ~50%/year |

### Margin Analysis
- **Gross margin at 89 NIS/month (mid-tier)**: ~39–42% (52–54 NIS variable costs). LLM costs are the dominant variable expense
- **Target gross margin at scale (50K+ users)**: 60%+ as LLM costs decrease (~50%/year industry trend), caching improves, and volume pricing is negotiated with Anthropic/Moonshot
- **Comparison**: Duolingo gross margin = 73% (FY2023 GAAP); Chegg = 68% (FY2023 GAAP); Coursera = 53% (FY2023 GAAP) — Cena's current margin is lower due to heavy LLM usage, but the trajectory is favorable as model costs decline rapidly and caching matures
- **Margin improvement levers**: (1) Prompt caching target 60%+ hit rate saves 30-40% on Sonnet costs; (2) Batch API for async tasks saves 50%; (3) Shift more tasks to Kimi tier as quality improves; (4) LLM pricing drops ~50%/year — at that rate, 60%+ gross margin by Month 12–18

### Customer Acquisition Cost (CAC)
- **Target CAC**: <150 NIS ($40) per paid subscriber
- **Primary channels**: Organic (SEO, knowledge graph sharing virality), school partnerships (B2B2C), Bagrut study groups on social media (Telegram, WhatsApp)
- **Estimated channel mix**: 40% organic/viral, 30% school partnerships, 20% paid social (Instagram/TikTok targeting ages 16–18), 10% referral program

### Lifetime Value (LTV)
- **Realistic subscription duration**: 6–12 months (Bagrut students prep for specific exams with a bounded study window — see `docs/business-viability-assessment.md` Section 6 on structural churn). The 6-month floor assumes exam-focused students who subscribe for one testing cycle; the 12-month ceiling assumes students who start early or continue for a second subject
- **Aspirational ceiling**: 18–24 months for multi-subject students (Math + Physics + Chemistry) who stay through two Bagrut cycles. This is a retention target, not a planning assumption
- **Conservative LTV at 89 NIS/month × 9 months** (midpoint): ~800 NIS ($222)
- **Gross contribution per month**: ~35–37 NIS (89 NIS revenue - ~52–54 NIS variable costs)
- **Conservative LTV (gross contribution)**: ~315–333 NIS over 9 months
- **LTV:CAC ratio**: ~2.1–2.2:1 (315–333 / 150) — below the 3:1 SaaS benchmark at launch. Reaches 3:1 only with 12+ month retention or LLM cost reductions
- **Payback period**: ~4 months (CAC 150 NIS ÷ ~37 NIS/month gross contribution) — healthy, but only meaningful if students stay >4 months
- **Path to 3:1 LTV:CAC**: (1) Multi-subject upsell extends lifetime (adding Physics to Math student adds 3–6 months); (2) LLM cost drops ~50%/year double contribution margin by Month 12–18; (3) Knowledge graph lock-in creates switching cost that delays churn. Target: 12-month average retention by Month 18
- **Honest risk**: If average retention is only 6 months, LTV:CAC drops to ~1.4:1 and the business is not venture-scale. This is the single most important metric to validate in the first 1,000 users

### Break-Even Analysis (AI-Lean Team Model)
- **Monthly fixed costs (estimated)**: ~58,000–79,000 NIS/month (3-person core team ~53K–72K + cloud infrastructure ~5K–7K per `docs/architecture-design.md` Section 16)
- **Note on infrastructure**: The ~17K–33K infrastructure figure from the initial estimate included AI development tools (~2K–3K NIS), LLM API costs at scale (now counted as variable per-user costs above), and buffer. Pure cloud infrastructure at 10K users is ~5K–7K NIS/month. AI development tools (~2K–3K) are a fixed cost addition, bringing total fixed costs to ~60K–82K NIS/month
- **Contribution margin per premium user**: ~35–37 NIS/month (89 NIS revenue - ~52–54 NIS variable costs)
- **Break-even subscribers**: ~1,620–2,340 premium users
- **At 8% conversion from ~85K–100K addressable Bagrut students** (CBS 2011 baseline: 85,100; estimated up to ~100K by 2025 based on population growth): theoretical maximum of 6,800–8,000 premium users in Year 1
- **Break-even timeline**: Month 8–12 — achievable within 18-month runway. As LLM costs decline (~50%/year), contribution margin improves and break-even threshold drops to ~1,000–1,400 subscribers by Month 12–18
- **Key insight**: The AI-augmented model keeps the team lean (3 people, not 6), and while LLM costs are currently the dominant variable expense, the industry-wide pricing decline creates a natural margin expansion tailwind. The business becomes increasingly profitable over time without any operational changes

---

## 9. Go-to-Market Strategy

### Development Velocity Advantage
- AI coding agents (Claude Code, Kimi) compress the traditional development timeline by 3–5×
- An experienced architect with AI agents ships working features in days, not weeks: full API endpoints, React Native screens, database migrations, test suites — all generated in pair-programming sessions
- Knowledge graph construction pipeline: architect + Claude Code builds the extraction → validation → QA pipeline in 1–2 weeks per subject (vs. 4 weeks with a traditional team)

### Phase 1: Build + Bagrut Beachhead (Months 1–3)
- **Month 1**: MVP core — onboarding flow, diagnostic quiz, knowledge graph (Math only), Socratic tutoring mode. Architect + AI agents deliver full-stack in ~4 weeks
- **Month 2**: Gamification layer (streaks, XP, badges), spaced repetition, stagnation detection v1. Add Physics knowledge graph (1–2 weeks with established pipeline)
- **Month 3**: Beta launch to 200–500 students from Bagrut study groups (Telegram, WhatsApp). Iterate on retention and onboarding based on real data
- **Target segment**: 11th–12th grade STEM students preparing for 5-unit Bagrut exams (Mathematics and Physics first)
- **Addressable market**: ~85,000–100,000 Bagrut exam takers/year (CBS 2011 baseline: 85,100; estimated up to 100K by 2025)
  - 5-unit Math: ~15,000–18,000 students (Taub Center 2016: 13.8% of cohort)
  - 5-unit Physics: ~8,500–10,000 students (estimated from Bank of Israel scientific reserves data)
- **Goal**: 2,000 registered users, 160 paid subscribers by Month 3

### Phase 2: Growth + School Partnerships (Months 3–8)
- **Launch channel**: Direct-to-student via social media (Instagram, TikTok, Telegram study groups) with "your knowledge graph" as the viral hook
- **Pricing launch**: Free tier (2 concepts/day) + 30-day full trial on signup; conversion push at day 14
- **B2B2C model**: Partner with 10–20 high schools; teacher dashboards show class-level knowledge gaps
- **School pricing**: 50 NIS/student/year (subsidized to drive adoption); upsell individuals to premium
- **Referral loop**: Knowledge graph sharing ("compare your graph with mine")
- **Goal**: 15,000 registered users, 1,200 paid subscribers by Month 8 (break-even)

### Phase 3: National + Subject Expansion (Months 8–14)
- **Subject expansion**: Add Chemistry, Biology, Computer Science — 1–2 weeks per subject using the established KG pipeline + AI agents
- **Channel expansion**: Private tutoring center partnerships, parent-focused WhatsApp marketing
- **Seasonal campaigns**: 3-month intensive bundles before Bagrut exam periods (January and June)
- **Goal**: 35,000 registered users, 3,000 paid subscribers by Month 14

### Phase 4: International Readiness (Months 14–20)
- **First international target**: AP exams (US) — largest English-speaking standardized exam market
- **Localization**: English UI, AP syllabus knowledge graph construction (1–2 weeks per subject with AI agents), US-market payment processing
- **Go-to-market in US**: AP tutoring service partnerships, Reddit r/APStudents, College Confidential
- **Second target**: A-Levels (UK) — similar exam structure, English-language, Commonwealth path
- **Goal**: 60,000+ registered users globally, 5,000+ paid subscribers by Month 20

### Distribution Channels (Ranked by Expected ROI)
1. **Organic/viral** (40% of acquisition): Knowledge graph screenshots shared on social media; "challenge a friend" comparison feature; shareable milestone badges
2. **School partnerships** (30%): B2B2C with teacher dashboards; school-wide adoption drives volume at lower CAC
3. **Paid social** (20%): Instagram and TikTok ads targeting ages 16–18 with "stop wasting money on private tutors" messaging; retargeting free-tier users who hit the daily limit
4. **Referral program** (10%): Give 1 month free for each referred paid subscriber; referred user gets extended 60-day trial

---

## 10. Success Metrics & KPIs

### North Star Metric
- **Weekly Active Learners (WAL)**: Students who complete at least 1 learning session (≥3 concept interactions) in a given week
- Why WAL over DAU: Learning is not a daily habit for all students; weekly captures consistent engagement without penalizing healthy study patterns

### Acquisition KPIs
| Metric | Month 6 Target | Month 12 Target | Month 18 Target |
|---|---|---|---|
| Registered users | 5,000 | 20,000 | 50,000 |
| Paid subscribers | 400 | 1,600 | 4,000 |
| Free-to-paid conversion rate | 8% | 8% | 8% |
| CAC (paid channels) | <200 NIS | <150 NIS | <120 NIS |
| Organic signup share | 30% | 40% | 50% |

### Engagement KPIs
| Metric | Target | Benchmark Source |
|---|---|---|
| WAL / MAU ratio | >60% | Duolingo: ~52% |
| Average sessions per week (active users) | ≥3 | Internal goal |
| Average session duration | 12–20 minutes | Cognitive load research optimal range |
| Knowledge graph nodes added per session | ≥2 | Internal goal |
| 7-day streak retention | >40% | Duolingo: ~35% |
| Methodology switch rate (per 100 active students/month) | 5–15 | No benchmark — novel metric |

### Retention KPIs
| Metric | Target | EdTech Benchmark |
|---|---|---|
| Day 1 retention | >70% | EdTech avg: 40–50% |
| Day 7 retention | >45% | EdTech avg: 20–30% |
| Day 30 retention | >30% | EdTech avg: 10–15% |
| 3-month subscription renewal rate | >75% | EdTech avg: 50–60% |
| 12-month subscription renewal rate | >55% | EdTech avg: 25–35% |

### Learning Outcome KPIs
| Metric | Target | Measurement Method |
|---|---|---|
| Concept mastery rate | >70% of attempted concepts reach "mastered" within 30 days | Knowledge graph node status tracking |
| Knowledge retention (30-day recall) | >80% accuracy on spaced repetition reviews | Automated review quiz scores |
| Bagrut score correlation | Positive correlation (r > 0.3) between Cena engagement and Bagrut scores | Post-exam survey + opt-in score sharing (Year 2+) |
| Student self-reported confidence | >4.0/5.0 average | In-app quarterly survey |

### Business Health KPIs
| Metric | Target | Notes |
|---|---|---|
| Gross margin | >40% at launch, >60% by Month 12–18 | Improves as LLM costs decline ~50%/year |
| LTV:CAC ratio | >4:1 at launch, >8:1 by Month 18 | Driven by LLM cost reduction trajectory |
| Monthly burn rate | <82,000 NIS pre-break-even | 3-person team + infrastructure + dev tools |
| Months to break-even | 8–12 (AI-lean team) | Accelerates as contribution margin improves |
| NPS (Net Promoter Score) | >50 (EdTech avg: 30–40) | |

---

## 11. Team & Resource Requirements

### AI-Augmented Development Model
- The founding team leverages **AI coding agents** (Claude Code, Kimi Code) as force multipliers — an experienced architect with these tools matches the output of a traditional 4-5 person engineering team
- This is not theoretical: AI agents handle full-stack implementation (React Native, backend APIs, database schemas), test generation, code review, refactoring, and documentation in real-time pair-programming sessions
- The founder brings deep enterprise architectural knowledge across the full technology stack, enabling AI agents to operate at maximum effectiveness (clear specifications → higher-quality AI output)

### Core Team (Pre-Launch, Months 1–3)
| Role | Count | Key Responsibilities | Monthly Cost (NIS) |
|---|---|---|---|
| Technical Founder / Architect | 1 | Architecture, full-stack development (with Claude Code + Kimi agents), AI/LLM integration, infrastructure, knowledge graph pipeline | 25,000–35,000 |
| Product Designer (UI/UX) | 1 | Knowledge graph visualization, onboarding flow, gamification design, mobile-first responsive design | 20,000–25,000 |
| Education Domain Expert (part-time) | 1 | Bagrut syllabus validation, pedagogical methodology review, content QA | 8,000–12,000 |
| **Total pre-launch team** | **3** | | **~53,000–72,000/month** |

### Why 3 People, Not 6
- **No dedicated full-stack engineers**: The architect + AI coding agents (Claude Code, Kimi) produce React Native mobile, React PWA, Node.js/Python backend, and database layer at 3–5× the speed of manual development. AI agents handle boilerplate, testing, and refactoring autonomously
- **No dedicated ML/AI engineer**: LLM integration (prompt engineering, knowledge graph extraction pipelines, stagnation detection) is the architect's core competency — AI agents accelerate implementation but the design decisions are architectural, not ML-research
- **Designer is essential**: AI agents cannot replace visual design judgment for the knowledge graph visualization, which is the product's hero feature. This is the one role that must be human

### Growth Team (Post-Launch, Months 3–12)
| Role | Count | Key Responsibilities |
|---|---|---|
| Growth Marketer | 1 | Paid social campaigns, school outreach, content marketing, community management |
| **Total growth additions** | **1** | |

- Customer support is primarily automated (AI-powered FAQ + chatbot); human escalation handled by the founder until scale demands a dedicated hire
- Content engineering (knowledge graph expansion to new subjects) is handled by the architect + AI agents using the established extraction pipeline
- Community management combined with growth marketing role — Israeli student market is compact enough for one person

### Advisors (Non-Salaried)
- **Education advisor**: Licensed Bagrut teacher with 10+ years experience — validates pedagogical approach and syllabus alignment
- **EdTech founder advisor**: Operator who has scaled an EdTech product past 100K users — guides GTM and retention strategy

### Infrastructure Costs (Monthly, at 10K Users)
| Resource | Estimated Monthly Cost |
|---|---|
| Cloud compute (AWS/GCP) | 3,000–5,000 NIS |
| LLM API costs (production — student-facing) | 8,000–20,000 NIS |
| AI development tools (Claude Max, Kimi Pro, GitHub Copilot) | 2,000–3,000 NIS |
| Database (managed PostgreSQL + graph DB) | 1,500–3,000 NIS |
| CDN + storage | 500–1,000 NIS |
| Monitoring + observability | 500–1,000 NIS |
| **Total infrastructure** | **~17,000–33,000 NIS/month** |

### Funding Requirements
- **Pre-seed target**: 1.2–1.8M NIS ($330K–$500K at 3.6 NIS/USD) for 18 months of runway
- **Covers**: 3-person core team for 18 months (~53K–72K/month × 18 = 954K–1,296K NIS), infrastructure (17K–33K/month × 18 = 306K–594K NIS), marketing budget (100K NIS), legal/compliance (40K NIS)
- **Why this is enough**: AI coding agents reduce the team from 6→3 people, cutting monthly burn by ~60%. The architect's enterprise experience eliminates the need for senior engineering hires. The technical risk is lower because the architect has built similar systems before
- **Milestone for seed round**: 1,600+ paid subscribers (break-even), validated retention metrics (D30 >30%), positive Bagrut score correlation signal

---

## 12. Unique Value Proposition & Strategy

### Cena's Four Pillars (Validated Against 7 Competitors — See Feature Matrix)
1. **Student-facing knowledge graph visualization** — the graph IS the product (ALEKS has a pie chart; Squirrel AI's graph is internal-only; the remaining 5 competitors have no student-facing graph)
2. **Adaptive methodology switching** — validated as novel: all 7 competitors adjust difficulty or sequence, but none switch the teaching method itself (confirmed by 2024 IJAIED systematic review of adaptive learning systems)
3. **Cognitive load personalization** — per-student fatigue thresholds
4. **Exam-aligned, curriculum-agnostic architecture** — Bagrut first, then any country

### Where to Double Down
1. **Knowledge graph as hero product** — beautiful, interactive, shareable (viral acquisition)
2. **Methodology switching as "invisible magic"** — "this app just gets how I need to learn"
3. **Mentor memory creates switching costs** — leaving Cena = losing your entire learning history
4. **Bagrut as wedge, not ceiling** — then expand to AP (US), A-Levels (UK), JEE/NEET (India), Gaokao (China)
5. **Cognitive load management as care signal** — "the app told me to take a break" builds trust

### Risk Mitigation Matrix

| Risk | Likelihood | Impact | Mitigation Strategy | Contingency |
|---|---|---|---|---|
| **Knowledge graph construction takes longer than estimated** | Low | Medium | AI-agent-assisted extraction pipeline (Claude Code + structured prompts) reduces per-subject construction to 1–2 weeks; architect builds and validates the pipeline on Math first, then replicates for each additional subject | Launch with Math-only MVP; add subjects incrementally post-launch |
| **Day 3–30 retention cliff** | High | Critical | Onboarding designed for <5 min to first value; streak mechanics from Day 1; push notification opt-in during onboarding; methodology switching prevents early frustration | A/B test 3 onboarding variants in first 1,000 users; iterate weekly on retention cohort data |
| **LLM costs exceed budget at scale** | Medium | High | Aggressive caching of common explanations; Haiku-class models for classification; 50 interaction/day cap; batch pre-generation of content | Negotiate volume pricing with Anthropic/OpenAI at 10K+ user tier; build fine-tuned smaller model for high-frequency interactions |
| **Hebrew-first market too small for VC returns** | Low | High | Architecture is curriculum-agnostic from Day 1; AP expansion planned at Month 18; Bagrut is proof-of-concept, not ceiling | Accelerate international timeline if Israeli traction validates product-market fit faster than expected |
| **Gamification reduces intrinsic motivation** | Medium | Medium | "Stealth gamification" — knowledge graph growth IS the reward; adaptive intensity (less gamification for intrinsically motivated students); meaningful badges tied to intellectual milestones | A/B test gamification-on vs. gamification-off cohorts; measure learning outcomes, not just engagement |
| **Competitor launches similar features** | Low | Medium | Methodology switching + knowledge graph visualization combination creates compound defensibility; per-student learning history creates switching costs | Accelerate feature development on most defensible pillar (methodology switching — hardest to replicate) |
| **Regulatory changes (Israeli privacy law tightening)** | Low | Medium | GDPR-ready architecture from Day 1 exceeds current Israeli requirements; DPO role planned for post-seed | Legal advisor on retainer; data architecture supports any jurisdiction's deletion/portability requirements |
| **LLM provider API disruption or pricing change** | Medium | Medium | Model-agnostic abstraction layer; tri-provider setup (Claude Sonnet/Opus primary, Kimi K2.5 fast/cheap tier — see `docs/llm-routing-strategy.md`); no fine-tuned models that lock to one provider | Switch primary provider within 48 hours; cached content continues serving during transition |

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
