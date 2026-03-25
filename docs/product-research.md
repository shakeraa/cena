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
| **Pricing (annual)** | $190–$245 | ~$2,000+ (tutoring center) | $150 | Free (Khanmigo: $44) | ~$120 (institutional) | $84 |
| **Platform** | Mobile-first + PWA | Physical centers + app | Web + mobile | Web + mobile | Web | Mobile + web |

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

### Pricing Sensitivity Analysis

**Price Anchoring**
- Primary anchor: Private Bagrut tutoring at ~3,000 NIS/subject (~$800). For 5 STEM subjects = 15,000 NIS/year ($4,100)
- Cena at 899 NIS/year covers ALL subjects = 94% savings vs. private tutoring — this is the core value proposition
- Secondary anchor: Brilliant.org at $150/year, Duolingo at $84/year — Cena's premium positioning is justified by exam alignment and deeper personalization

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
- **ARPU (Annual Revenue Per User)**: 699–899 NIS/year ($190–$245) for premium subscribers
- **Blended ARPU** (accounting for free tier): Assuming 8% free-to-paid conversion (EdTech benchmark: 5–10%), blended ARPU = ~56–72 NIS/year across all users
- **Family plan discount**: 30% off second student → effective ARPU per family of 2 = ~1,190 NIS/year

### Cost Per User (Monthly)
| Cost Component | Per User/Month | Notes |
|---|---|---|
| LLM API costs | 3–8 NIS ($0.80–$2.20) | 50 interactions/day cap; cached common explanations |
| Cloud infrastructure | 1–2 NIS ($0.25–$0.55) | Compute, storage, CDN (amortized at 10K users) |
| Knowledge graph maintenance | 0.5 NIS ($0.14) | Amortized annual syllabus update cost |
| Payment processing | 2–3% of revenue | Stripe/local processor fees |
| Customer support | 1–2 NIS ($0.25–$0.55) | Primarily automated; human escalation for <5% of issues |
| **Total variable cost** | **~7–15 NIS/month** | |

### Margin Analysis
- **Gross margin at 89 NIS/month (mid-tier)**: ~83–92% (7–15 NIS variable costs)
- **Target gross margin at scale (50K+ users)**: 88%+ as LLM costs decrease and caching improves
- **Comparison**: Duolingo gross margin = 73%; Chegg = 74%; Coursera = 59% — Cena's higher margin driven by AI-native architecture without human instructor costs

### Customer Acquisition Cost (CAC)
- **Target CAC**: <150 NIS ($40) per paid subscriber
- **Primary channels**: Organic (SEO, knowledge graph sharing virality), school partnerships (B2B2C), Bagrut study groups on social media (Telegram, WhatsApp)
- **Estimated channel mix**: 40% organic/viral, 30% school partnerships, 20% paid social (Instagram/TikTok targeting ages 16–18), 10% referral program

### Lifetime Value (LTV)
- **Average subscription duration**: 18 months (targeting above EdTech average of ~4 months through knowledge graph lock-in and methodology adaptation)
- **LTV at 89 NIS/month × 18 months**: ~1,600 NIS ($435)
- **LTV:CAC ratio target**: >10:1 (1,600/150) — well above the 3:1 minimum for healthy SaaS
- **Payback period**: <2 months (CAC 150 NIS ÷ 89 NIS/month gross contribution)

### Break-Even Analysis
- **Monthly fixed costs (estimated)**: ~120,000 NIS/month (team of 6 + infrastructure baseline + office)
- **Contribution margin per premium user**: ~74–82 NIS/month
- **Break-even subscribers**: ~1,500–1,600 premium users
- **At 8% conversion from 100K addressable Bagrut students**: theoretical maximum of 8,000 premium users in Year 1, reaching break-even at ~20% of that target

---

## 9. Go-to-Market Strategy

### Phase 1: Bagrut Beachhead (Months 1–6)
- **Target segment**: 11th–12th grade STEM students preparing for 5-unit Bagrut exams (Mathematics and Physics first — highest private tutoring spend)
- **Addressable market**: ~100,000 Bagrut exam takers/year (CBS 2011 baseline: 85,100; estimated ~100,000 by 2025 based on population growth); ~15,000–20,000 taking 5-unit Math (Taub Center 2016: 13.8% of cohort); ~8,500–10,000 taking 5-unit Physics (estimated from Bank of Israel scientific reserves data)
- **Launch channel**: Direct-to-student via social media (Instagram, TikTok, Telegram study groups) with "your knowledge graph" as the viral hook
- **Pricing launch**: Free tier (2 concepts/day) + 30-day full trial on signup; conversion push at day 14 with personalized progress report showing "here's what you'd lose"
- **Goal**: 5,000 registered users, 400 paid subscribers by Month 6

### Phase 2: School Partnerships (Months 6–12)
- **B2B2C model**: Partner with 10–20 high schools to offer Cena as a supplementary tool; teacher dashboards show class-level knowledge gaps
- **Pricing**: School license at 50 NIS/student/year (subsidized vs. direct pricing to drive adoption); upsell individual students to premium features
- **Referral loop**: Students in partner schools invite friends from other schools via knowledge graph sharing ("compare your graph with mine")
- **Goal**: 20,000 registered users, 1,600 paid subscribers (break-even) by Month 12

### Phase 3: National Expansion (Months 12–18)
- **Subject expansion**: Add Chemistry, Biology, Computer Science to cover full STEM Bagrut suite
- **Channel expansion**: Partnerships with private tutoring centers (position as "between sessions" tool), parent-focused marketing (WhatsApp parent groups)
- **Seasonal campaigns**: 3-month intensive bundles before Bagrut exam periods (January and June)
- **Goal**: 50,000 registered users, 4,000 paid subscribers by Month 18

### Phase 4: International Readiness (Months 18–24)
- **First international target**: AP exams (US) — largest English-speaking standardized exam market, strong overlap with STEM focus
- **Localization requirements**: English UI (already partially built), AP syllabus knowledge graph construction (LLM-assisted, ~4 weeks per subject), US-market payment processing
- **Go-to-market in US**: Partnership with AP tutoring services, Reddit r/APStudents community, College Confidential forums
- **Second target**: A-Levels (UK) — similar exam structure, English-language, Commonwealth expansion path

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
| Metric | Target |
|---|---|
| Gross margin | >85% |
| LTV:CAC ratio | >10:1 |
| Monthly burn rate | <150,000 NIS pre-break-even |
| Months to break-even | ≤12 |
| NPS (Net Promoter Score) | >50 (EdTech avg: 30–40) |

---

## 11. Team & Resource Requirements

### Core Team (Pre-Launch, Months 1–6)
| Role | Count | Key Responsibilities | Monthly Cost (NIS) |
|---|---|---|---|
| Technical Co-founder / CTO | 1 | Architecture, AI/LLM integration, infrastructure | 25,000–35,000 |
| Full-Stack Engineer | 2 | React Native mobile app, React web app, API layer | 22,000–28,000 each |
| ML/AI Engineer | 1 | Knowledge graph pipeline, LLM prompt engineering, stagnation detection models | 25,000–32,000 |
| Product Designer (UI/UX) | 1 | Knowledge graph visualization, onboarding flow, gamification design | 20,000–25,000 |
| Education Domain Expert (part-time) | 1 | Bagrut syllabus validation, pedagogical methodology review, content QA | 8,000–12,000 |
| **Total pre-launch team** | **6** | | **~120,000–160,000/month** |

### Growth Team (Post-Launch, Months 6–12)
| Role | Count | Key Responsibilities |
|---|---|---|
| Growth Marketer | 1 | Paid social campaigns, school outreach, content marketing |
| Community Manager | 1 | Student community on Telegram/Discord, social media presence, school partnerships |
| Customer Support (part-time) | 1 | Ticket triage, escalation for technical issues |
| Content Engineer | 1 | Knowledge graph expansion to new subjects, diagram template creation |

### Advisors (Non-Salaried)
- **Education advisor**: Licensed Bagrut teacher with 10+ years experience — validates pedagogical approach and syllabus alignment
- **EdTech founder advisor**: Operator who has scaled an EdTech product past 100K users — guides GTM and retention strategy
- **AI/ML advisor**: Researcher or practitioner with experience in adaptive learning systems — reviews model architecture decisions

### Infrastructure Costs (Monthly, at 10K Users)
| Resource | Estimated Monthly Cost |
|---|---|
| Cloud compute (AWS/GCP) | 3,000–5,000 NIS |
| LLM API costs | 8,000–20,000 NIS |
| Database (managed PostgreSQL + graph DB) | 1,500–3,000 NIS |
| CDN + storage | 500–1,000 NIS |
| Monitoring + observability | 500–1,000 NIS |
| **Total infrastructure** | **~15,000–30,000 NIS/month** |

### Funding Requirements
- **Pre-seed target**: 1.5–2.5M NIS ($400K–$680K) for 18 months of runway
- **Covers**: 6-person team (12 months), infrastructure, initial marketing budget (100K NIS), legal/compliance setup
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
| **Knowledge graph construction takes longer than estimated** | Medium | High | LLM-assisted extraction pipeline reduces per-subject construction from ~6 months (manual) to ~4 weeks; validate with 1 subject (Math) before committing to full suite | Launch with Math-only MVP; add subjects incrementally post-launch |
| **Day 3–30 retention cliff** | High | Critical | Onboarding designed for <5 min to first value; streak mechanics from Day 1; push notification opt-in during onboarding; methodology switching prevents early frustration | A/B test 3 onboarding variants in first 1,000 users; iterate weekly on retention cohort data |
| **LLM costs exceed budget at scale** | Medium | High | Aggressive caching of common explanations; Haiku-class models for classification; 50 interaction/day cap; batch pre-generation of content | Negotiate volume pricing with Anthropic/OpenAI at 10K+ user tier; build fine-tuned smaller model for high-frequency interactions |
| **Hebrew-first market too small for VC returns** | Low | High | Architecture is curriculum-agnostic from Day 1; AP expansion planned at Month 18; Bagrut is proof-of-concept, not ceiling | Accelerate international timeline if Israeli traction validates product-market fit faster than expected |
| **Gamification reduces intrinsic motivation** | Medium | Medium | "Stealth gamification" — knowledge graph growth IS the reward; adaptive intensity (less gamification for intrinsically motivated students); meaningful badges tied to intellectual milestones | A/B test gamification-on vs. gamification-off cohorts; measure learning outcomes, not just engagement |
| **Competitor launches similar features** | Low | Medium | Methodology switching + knowledge graph visualization combination creates compound defensibility; per-student learning history creates switching costs | Accelerate feature development on most defensible pillar (methodology switching — hardest to replicate) |
| **Regulatory changes (Israeli privacy law tightening)** | Low | Medium | GDPR-ready architecture from Day 1 exceeds current Israeli requirements; DPO role planned for post-seed | Legal advisor on retainer; data architecture supports any jurisdiction's deletion/portability requirements |
| **LLM provider API disruption or pricing change** | Medium | Medium | Model-agnostic abstraction layer; dual-provider setup (Claude primary, GPT-4o fallback); no fine-tuned models that lock to one provider | Switch primary provider within 48 hours; cached content continues serving during transition |

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
