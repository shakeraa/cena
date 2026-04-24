# Kimi Agent Swarm: Competitive Feature Extraction — Adaptive Learning & Exam Prep Market

> **Date:** 2026-03-31
> **Purpose:** Research and extract every feature that competing educational platforms offer, organized by category. Output a structured feature matrix we can compare against CENA's current mobile app capabilities and 9 interactive diagram/challenge widgets.

---

## SUPER PROMPT

You are a swarm of specialized research agents conducting competitive intelligence on the adaptive learning and exam preparation market. Your mission is to extract and catalog every feature, capability, and UX pattern from the leading platforms listed below.

### CONTEXT: What CENA Already Has

CENA is a full-stack adaptive AI learning platform for Israeli Bagrut STEM exam preparation (ages 14-18) with:

**Backend Architecture:**
| Component | Technology |
|-----------|-----------|
| Actor System | Akka.NET (StudentActor, SessionActor, TutorActor, etc.) |
| Messaging | NATS bus between Admin API and Actor Host |
| API | REST + SignalR WebSocket (real-time session events) |
| Auth | Firebase Auth (Google, Apple, Phone) |
| Pattern | CQRS/Event Sourcing with offline-first sync |

**5 STEM Subjects:**
Mathematics, Physics, Chemistry, Biology, Computer Science

**Mobile App (Flutter) — 15+ Screens:**
- Try-question (pre-signup value demo)
- Onboarding (5-page: language, subjects, grade, diagnostic, summary)
- Auth (Google, Apple, Phone with Israeli +972 validation)
- Home (bento dashboard with XP, streak, subjects, AI tutor shortcut)
- Session config (subject, duration, difficulty) → Active session → Summary
- AI Tutor chat (streaming, quick replies, LaTeX in bubbles)
- Knowledge Graph (concept map with mastery overlay)
- Gamification (XP, levels, streak/momentum, badges, achievements)
- Notifications, Profile, Settings
- Challenges grid (browse by subject, card chain progression)

**Interactive Diagram System (9 Widgets):**
| Widget | Purpose |
|--------|---------|
| InteractiveDiagramViewer | SVG + hotspots + pinch-zoom + formula bar |
| DragLabelDiagram | Drag-and-drop physics labels |
| GraphInsightViewer | Tap graph plots for crosshair + value tooltips |
| FillInLabelsDiagram | Quiz mode — type hidden labels |
| ChallengeCardWidget | Game card with tier glow, 5 answer types |
| ConceptSummaryCard | Formula-first concept intro |
| ComparativeDiagramViewer | Side-by-side diagram comparison |
| RiveDiagramViewer | Animated diagram player with scrub |
| DailyChallengeFeedCard | Social MCQ with anonymous class voting |

**Question Types:** MCQ, free-text, numeric, proof, diagram

**Gamification System:**
- XP with leveling (100 XP per level, exponential curve)
- Quality-gated streaks (3+ questions, >5s avg response)
- Momentum Meter alternative (7-day rolling, no reset anxiety)
- 10 badge categories (streak, mastery, engagement, special)
- 5-tier celebration system (micro → epic)
- Variable rewards (random bonus XP, mystery badges)

**Adaptive Learning:**
- FSRS-based spaced repetition scheduling
- Bayesian Knowledge Tracing (P(Known) per concept)
- Adaptive difficulty targeting P(correct) = 0.55-0.75
- Cognitive load breaks (fatigue threshold 0.7)
- Flow state detection (3+ consecutive correct → suppress UI chrome)
- 5 methodology modes (spaced repetition, interleaved, blocked, adaptive, Socratic)

**Localization:** English (primary), Arabic, Hebrew (hideable per market)

**Ethical Guardrails:**
- 9 PM-7 AM notification quiet hours
- 90/120/180-min study session limits
- No dark patterns, no loot boxes, no confirmshaming
- Anonymous-only social features (no named leaderboards)

---

### RESEARCH TARGETS

Research each competitor below. For each, extract EVERY feature they advertise, demonstrate in screenshots/docs, or that reviewers mention. Organize findings into the categories listed in the output format.

#### Tier 1 — Direct Competitors (Israeli Exam Prep)
1. **Matific** — gamified math learning, K-8, Israeli origin, adaptive
2. **MindCET** — Israeli EdTech innovation, assessment tools
3. **Genie (by Geniebook)** — AI-powered learning, worksheets, diagnostics
4. **Photomath** — camera-based math solving, step-by-step explanations
5. **StudyGo** — flashcards, practice tests, exam prep (European market)

#### Tier 2 — Global Adaptive Learning Leaders
6. **Khan Academy** — free STEM education, Khanmigo AI tutor, mastery-based progression
7. **Duolingo (for methodology)** — gamification, streaks, hearts, league system, micro-lessons
8. **IXL Learning** — adaptive math/science, diagnostic, skill plans, analytics
9. **ALEKS (McGraw-Hill)** — Knowledge Space Theory, adaptive assessment, pie chart mastery
10. **DreamBox Learning** — adaptive math, spatial reasoning, real-time difficulty adjustment

#### Tier 3 — AI-Powered Tutoring & Study Platforms
11. **Khanmigo (Khan Academy AI)** — Socratic AI tutor, writing coach, debate partner
12. **Synthesis** — collaborative STEM games, team problem-solving, space exploration theme
13. **Brilliant.org** — interactive STEM courses, visual problem-solving, daily challenges
14. **SmartyMe** — physics/engineering game cards, micro-lessons, addictive progression
15. **Socratic (Google)** — camera-based homework help, step-by-step explanations

#### Tier 4 — Exam-Specific & Test Prep
16. **Magoosh** — GRE/GMAT/SAT prep, video lessons, adaptive practice
17. **PrepScholar** — SAT/ACT prep, customized study plans, score guarantees
18. **Quizlet** — flashcards, Learn mode, spaced repetition, collaborative sets
19. **Anki** — spaced repetition, open-source, custom deck ecosystem
20. **Brainscape** — confidence-based repetition, CBR algorithm, shared flashcards

#### Tier 5 — Diagram & Visual Learning
21. **FigureLabs** — AI-generated scientific diagrams, labeled SVG, text replacement
22. **GPAI** — text-to-technical diagram, physics schematics, publication quality
23. **PhET Interactive Simulations** — physics/chem/bio simulations, University of Colorado
24. **GeoGebra** — interactive math, graphing, geometry, statistics
25. **Desmos** — graphing calculator, activity builder, teacher tools

---

### FEATURE CATEGORIES TO EXTRACT

For each competitor, extract features into these categories:

#### A. Content & Question Types
For each content format the platform supports:
- Content type (MCQ, free-text, numeric, proof, diagram, simulation, video, flashcard)
- LaTeX/math rendering support
- Diagram/visual question support
- Interactive simulation capabilities
- Question bank size and subject coverage
- AI-generated content vs hand-authored
- Multi-language support

#### B. Adaptive Learning Engine
- Difficulty adjustment algorithm (IRT, BKT, FSRS, Elo, proprietary)
- Knowledge state modeling (per-concept, per-skill, per-standard)
- Spaced repetition implementation
- Interleaving strategy
- Mastery threshold and progression model
- Prerequisite graph / knowledge map
- Learning path personalization
- Forgetting curve modeling

#### C. AI & Tutoring
- AI tutor capabilities (Socratic, direct instruction, hints)
- Natural language understanding for math
- Step-by-step solution generation
- Error diagnosis (conceptual vs procedural vs careless)
- Conversational AI (chat-based, streaming)
- Camera/OCR-based question input
- AI-generated explanations quality
- LLM model used (GPT-4, Claude, Gemini, custom)

#### D. Gamification & Engagement
- XP / points system
- Leveling mechanism
- Streak system (daily, quality-gated, or rolling)
- Badge / achievement system
- Leaderboards (class, school, global, anonymous)
- Hearts / energy system (lives, cooldowns)
- Leagues / competitions
- Celebration animations / micro-interactions
- Variable rewards (random bonuses, mystery items)
- Social features (friends, challenges, study groups)

#### E. Visualization & Interactive Diagrams
- Knowledge graph / concept map visualization
- Interactive math graphs (plot, trace, zoom)
- Physics simulations (circuits, forces, waves)
- Chemistry visualizations (molecular, reactions)
- Biology diagrams (cell, anatomy, ecology)
- Drag-and-drop interactions
- Step-by-step worked example animations
- Diagram generation method (AI, hand-drawn, simulation engine)

#### F. Assessment & Analytics
- Diagnostic assessment (initial placement)
- Formative assessment (in-session feedback)
- Summative assessment (practice exams, mock tests)
- Student analytics dashboard
- Parent/teacher analytics
- Performance prediction
- Skill gap identification
- Comparative analytics (class/cohort benchmarks)
- Learning time tracking

#### G. Onboarding & First-Time UX
- Time-to-value (how fast to first learning activity)
- Sign-up friction (social login, guest mode, try before signup)
- Onboarding flow length
- Personalization during onboarding (subjects, level, goals)
- Diagnostic placement test
- Progressive feature disclosure
- Tutorial / walkthrough system

#### H. Offline & Technical
- Offline mode support
- Download for offline learning
- Sync strategy (last-write-wins, CRDT, event-sourcing)
- Push notifications
- Cross-platform (web, iOS, Android, tablet)
- Accessibility features (screen reader, font size, contrast)
- RTL language support
- Performance / load speed

#### I. Monetization & Access
- Free tier features
- Premium tier features
- Pricing model (subscription, per-course, freemium, school license)
- Family plans
- School/institution licensing
- Content unlock model (all-access vs gated)
- Ad-supported option

#### J. Teacher & Parent Tools
- Teacher dashboard
- Assignment creation
- Class management
- Progress reports for parents
- Curriculum alignment (standards mapping)
- Content authoring tools
- LMS integration (Google Classroom, Canvas, Schoology)

#### K. Social & Community
- Study groups / class features
- Peer challenges / duels
- Activity feed / social proof
- Content sharing
- Discussion forums
- Collaborative problem-solving
- Teacher-student messaging

#### L. Platform & UX Design
- Design system (Material, Cupertino, custom)
- Dark mode
- Theming / branding (white-label)
- Animation quality (micro-interactions, celebrations)
- Sound design
- Haptic feedback
- Navigation pattern (tabs, drawer, stack)
- Responsive / adaptive layout

---

### OUTPUT FORMAT

For each competitor, produce a structured report:

```markdown
## [Competitor Name]

### Overview
- **Category:** (Adaptive Learning / Exam Prep / AI Tutor / Gamified / Simulation / etc.)
- **Target audience:** (age range, subjects, exams)
- **Deployment:** (Web / iOS / Android / Cross-platform)
- **Pricing model:** (Freemium / Subscription / Per-course / Free)
- **Tech stack (if known):** (React Native / Flutter / Swift / etc.)

### Content & Question Types
| # | Type | Interactive? | AI-Generated? | Notes |
|---|------|-------------|---------------|-------|
| 1 | ...  | ...         | ...           | ...   |

### Feature Matrix
| Category | Feature | Has It? | Notes |
|----------|---------|---------|-------|
| Adaptive | Spaced Repetition | Yes/No/Partial | ... |
| ... | ... | ... | ... |

### Unique Features (not in CENA)
- Feature 1: description
- Feature 2: description

### UX Patterns Worth Adopting
- Pattern 1: description
- Pattern 2: description

### Weaknesses / Gaps vs CENA
- Gap 1: description
```

---

### FINAL DELIVERABLE

After all competitor reports, produce a consolidated:

1. **Master Feature Matrix** — all competitors as columns, all features as rows, cells = Yes/No/Partial
2. **Gap Analysis** — features competitors have that CENA does NOT have yet
3. **CENA Advantages** — features CENA has that competitors lack
4. **Adaptive Engine Comparison** — side-by-side of which learning algorithm each platform uses (BKT, IRT, FSRS, Elo, etc.)
5. **Gamification Comparison** — detailed comparison of gamification mechanics (XP, streaks, badges, leagues, hearts)
6. **Diagram & Interactivity Comparison** — which platforms have interactive STEM diagrams (CENA's hero differentiator)
7. **Priority Recommendations** — top 10 features to add to CENA, ranked by:
   - How many competitors have it
   - Student demand signals (from app reviews, Reddit, forums)
   - Impact on retention / learning outcomes
   - Implementation complexity estimate
   - Market differentiation potential

---

### AGENT ASSIGNMENT

| Agent | Responsibility | Competitors |
|-------|---------------|-------------|
| **Agent 1: Israeli Market Analyst** | Direct competitors & local market | Matific, MindCET, Genie, Photomath, StudyGo |
| **Agent 2: Adaptive Learning Analyst** | Global adaptive learning leaders | Khan Academy, Duolingo, IXL, ALEKS, DreamBox |
| **Agent 3: AI Tutoring Analyst** | AI-powered tutoring platforms | Khanmigo, Synthesis, Brilliant, SmartyMe, Socratic |
| **Agent 4: Exam Prep Analyst** | Test prep & spaced repetition | Magoosh, PrepScholar, Quizlet, Anki, Brainscape |
| **Agent 5: Visual Learning Analyst** | Diagram & simulation platforms | FigureLabs, GPAI, PhET, GeoGebra, Desmos |
| **Agent 6: Consolidator** | Merge all reports, produce master matrix, gap analysis, and priority recommendations | All |

### RESEARCH METHODS

Each agent should:
1. Search the competitor's official website, feature pages, and documentation
2. Download and test the mobile app (iOS/Android) — document the onboarding flow and first 5 minutes
3. Search G2, App Store reviews, Google Play reviews, and Common Sense Media for real user feedback
4. Search for comparison articles (e.g., "Khan Academy vs IXL", "Duolingo gamification analysis")
5. Check for research papers or blog posts about the platform's adaptive algorithm
6. Search Product Hunt, TechCrunch, and EdSurge for launch coverage and feature announcements
7. Look for demo videos, YouTube tutorials, and teacher review channels
8. Search Reddit (r/learnmath, r/education, r/edtech) for user discussions

### CONSTRAINTS

- Do NOT fabricate features. If uncertain, mark as "Unverified" with source
- Include source URLs for every major claim
- Focus on features relevant to an adaptive learning platform — skip generic app features (like "has a settings page")
- Pay special attention to:
  - **Gamification mechanics** — CENA's engagement layer competes with Duolingo-caliber design
  - **Interactive diagrams** — CENA's hero differentiator; few competitors have this
  - **AI tutoring quality** — Socratic method, error diagnosis, LaTeX rendering
  - **Offline-first** — critical for Israeli classroom use (connectivity gaps)
  - **RTL support** — Hebrew/Arabic; most Western platforms lack this
- Prioritize depth over breadth — a detailed analysis of 15 competitors beats a shallow look at 50
- Output in markdown, ready to drop into `/docs/competitive-analysis/`
