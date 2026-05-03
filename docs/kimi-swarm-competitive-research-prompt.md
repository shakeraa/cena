# Kimi Agent Swarm: Competitive Feature Extraction — Educational STEM Learning Market

> **Date:** 2026-03-31
> **Purpose:** Research and extract every feature that competing educational platforms offer, organized by category. Output a structured feature matrix we can compare against CENA's current mobile app capabilities and planned roadmap.

---

## SUPER PROMPT

You are a swarm of specialized research agents conducting competitive intelligence on the educational technology / gamified STEM learning market. Your mission is to extract and catalog every feature, capability, UX pattern, and monetization model from the leading platforms listed below.

### CONTEXT: What CENA Already Has

CENA is a Flutter-based mobile educational app (iOS + Android) targeting K-12 and university students with gamified, AI-powered learning. Current capabilities:

**Core Learning Engine:**
| Category | Features |
|----------|----------|
| Sessions | AI-powered tutoring sessions, adaptive question flow, confidence rating, teach-back prompts |
| Spaced Repetition | FSRS scheduler, review-due badges, adaptive interleaving across topics |
| Knowledge Graph | Visual concept mapping, prerequisite tracking, mastery progression |
| Diagrams | Interactive educational diagrams within sessions |
| Deep Study | Configurable deep-dive mode, deep-dive sheets, extended focus sessions |

**Gamification System:**
| Category | Features |
|----------|----------|
| Streaks | Quality-gated streaks (not just login-based) |
| Celebrations | Multi-tier celebration overlays, sound design, haptic feedback |
| Badges | Expanded badge system with achievement categories |
| Quests | Quest system with progressive objectives |
| Boss Battles | Challenge-mode boss battles with result screens |
| Leaderboards | Class-level social feed and peer comparisons |

**Engagement & Retention:**
| Category | Features |
|----------|----------|
| Onboarding | Multi-step onboarding v2 with progressive disclosure |
| Training Wheels | Guided first-use experience with coach marks |
| Habit Stacking | Routine profile service, habit formation triggers |
| Flow State | Flow monitor service, ambient indicators, immersive session mode |
| Wellbeing | Wellbeing actor, bedtime mode, session time limits |
| Notifications | Intelligent notification system, re-engagement service |

**Social & Safety:**
| Category | Features |
|----------|----------|
| Social | Peer solutions sharing, class social feed |
| Safety | Age-tiered safety, content moderation pipeline |
| Confidence | Confidence calibration tracking |

**Platform & UX:**
| Category | Features |
|----------|----------|
| i18n | English (primary), Arabic, Hebrew — full RTL support |
| Performance | Riverpod state management, skeleton screens, optimized selectors |
| Accessibility | Accessibility audit compliance |
| Thumb Zone | Touch-target optimization for mobile |

---

### RESEARCH TARGETS

Research each competitor below. For each, extract EVERY feature they advertise, demonstrate in screenshots/docs/App Store listings, or that reviewers mention. Organize findings into the categories listed in the output format.

#### Tier 1 — Direct Competitors (Gamified STEM Micro-Learning Apps)
1. **SmartyMe** (smartyme_physics / smartyme.mathgames / smartyme.electricity / smartyme.physicsgames) — Gamified engineering, physics, math, and calculus micro-lessons. "Replace doomscrolling with addictive learning." Sub-brands for Physics, Math, Electricity, Engineering. Ads show structural/electrical/mechanical/civil engineering, control systems, quantum physics, bridge building, microcontrollers, software engineering. Heavy social media ad presence (Instagram, Facebook, TikTok).
2. **Brilliant** — Interactive STEM learning (math, science, CS). Daily problems, guided courses, visual/interactive approach. Major player.
3. **Khan Academy** — Free comprehensive education. Khanmigo AI tutor. Video-based + practice. Mastery-based progression.
4. **Photomath** — AI-powered math solver and tutor (acquired by Google). Camera-scan equations, step-by-step solutions.

#### Tier 2 — Gamified Language/Learning Platforms (UX & Gamification Benchmarks)
5. **Duolingo** — Gold standard for gamified mobile learning UX. Streaks, hearts, leagues, XP, stories, AI features. Not STEM but the benchmark for engagement mechanics.
6. **Quizlet** — Flashcards, spaced repetition, AI-generated study sets. "Q-Chat" AI tutor. Test prep.
7. **Anki** — Open-source spaced repetition. Power-user SRS. Plugin ecosystem. The SRS benchmark.

#### Tier 3 — AI-Powered Education & Tutoring
8. **Khanmigo (Khan Academy's AI)** — GPT-4 powered tutor, Socratic method, writing coach, teacher tools.
9. **Synthesis** — AI-powered math and critical thinking for kids. Game-based collaborative learning.
10. **Mathway** — AI math problem solver. Step-by-step for algebra through calculus.
11. **Wolfram Alpha** — Computational knowledge engine. Step-by-step solutions. Pro features.

#### Tier 4 — STEM Visualization & Diagram Tools (Observed in Competitor Ads)
12. **FigureLabs** (figurelabs.ai) — AI scientific illustration generator. Text-to-figure, "Nano Banana Pro" engine, editable PPTX export, 1-click AI text replacement, redraw selection for scientific figures. Heavy Instagram/Facebook ad spend.
13. **GPAI** (official.gpai) — "Text to Technical Diagram — Built for Accuracy, Built for STEM." Compares favorably to ChatGPT for diagram accuracy. "GPAI Pro" tier.
14. **Nibble** (nibblecommunity) — Engineering micro-lessons. "Replace Doomscrolling with Engineering Micro-Lessons." CAD modelling, 3D visualization, heat transfer content.

#### Tier 5 — Emerging / Regional / Niche
15. **erkanhoca_mat** — Turkish math educator / problem platform. Math analysis, qualifying test content. Regional Turkish market.
16. **Brainly** — Community Q&A for homework help. AI answers. Massive user base in emerging markets.
17. **Socratic by Google** — AI homework helper. Camera-scan questions, visual explanations.
18. **Numerade** — Video-based STEM tutoring, AI "Ace" tutor, step-by-step video solutions.
19. **Coursera / edX mobile apps** — University-level courses on mobile. Certification. Specializations.
20. **Kahoot!** — Game-based learning platform. Quizzes, classroom engagement. Social/competitive mechanics.

---

### FEATURE CATEGORIES TO EXTRACT

For each competitor, extract features into these categories:

#### A. Learning Engine & Content Delivery
- Content format (video, interactive simulation, text, cards, games, AR/VR)
- Subject coverage (math, physics, chemistry, biology, CS, engineering branches)
- Grade/level range (K-5, 6-8, 9-12, university, professional)
- Adaptive difficulty (static, adaptive, AI-powered)
- Question types (MCQ, free-response, drawing, drag-drop, coding, simulation)
- AI tutoring model (Socratic, direct instruction, hint-based, conversational)
- Step-by-step solution generation
- Camera/OCR scan-to-solve
- Offline mode / downloadable content
- Content update frequency

#### B. Spaced Repetition & Memory Science
- SRS algorithm (SM-2, FSRS, proprietary, none)
- Review scheduling (manual, automatic, adaptive)
- Forgetting curve visualization
- Interleaving (within-topic, cross-topic, adaptive)
- Retrieval practice modes
- Confidence-based scheduling
- Knowledge gap detection
- Mastery thresholds and progression gates

#### C. Gamification & Engagement Mechanics
- Streak system (login-based, quality-gated, freeze available)
- XP / point system
- Levels / progression tiers
- Badges / achievements (count, categories)
- Leaderboards (global, friends, class, weekly)
- Leagues / competitive tiers (Duolingo-style)
- Daily challenges / problems of the day
- Boss battles / challenge modes
- Quest / mission system
- Virtual currency / gems / coins
- Lives / hearts system
- Power-ups / boosts
- Loot boxes / mystery rewards
- Celebration animations (confetti, sounds, haptics)
- Avatar / character customization
- Pet / companion system

#### D. Onboarding & First-Use Experience
- Placement test / diagnostic
- Goal setting during onboarding
- Learning pace selection
- Subject/topic selection
- Guided tutorial / coach marks
- Progressive disclosure of features
- Time commitment selection
- Notification preference setup
- Social connection prompts

#### E. Social & Community Features
- Friend system / social graph
- Class / group creation
- Teacher dashboard / classroom tools
- Peer solution sharing
- Discussion forums / Q&A
- Collaborative challenges
- Parent dashboard / progress sharing
- Social feed / activity stream
- Study groups
- Live sessions / tutoring

#### F. AI & Personalization
- AI tutor / chatbot (model, capabilities)
- Personalized learning path generation
- Adaptive content sequencing
- AI-generated explanations
- AI-generated practice problems
- Natural language question answering
- Concept prerequisite mapping
- Learning style adaptation
- Weakness detection and remediation
- AI-generated study plans
- Voice interaction / speech-to-text

#### G. Visualization & Interactive Content
- Interactive diagrams
- Knowledge graph / concept maps (visible to student?)
- 3D models / simulations
- Animation-based explanations
- Graphing calculator / tools
- Virtual labs / experiments
- AR/VR content
- Whiteboard / drawing tools
- Code playground / IDE

#### H. Wellbeing & Healthy Learning
- Session time limits / break reminders
- Bedtime mode / quiet hours
- Screen time tracking
- Difficulty frustration detection
- Encouragement for struggle (growth mindset messaging)
- Parental controls
- Age-appropriate content filtering
- Eye strain / rest reminders
- Learning pace warnings (too fast / too slow)

#### I. Retention & Re-engagement
- Push notification strategy (types, frequency, personalization)
- Email reminders
- Streak repair / freeze mechanics
- Re-engagement campaigns (come-back rewards)
- Habit formation features (time-of-day anchoring, routine building)
- Widget (iOS/Android home screen)
- Apple Watch / wearable integration
- Daily digest / summary

#### J. Platform & Technical
- Platforms (iOS, Android, Web, Desktop, Tablet-optimized)
- Offline support
- Cross-device sync
- Performance (app size, load times, animations)
- Accessibility (screen reader, font size, color blindness, dyslexia mode)
- Localization (languages supported, RTL)
- Dark mode
- Low-bandwidth mode
- Widget support

#### K. Monetization & Pricing
- Free tier features
- Premium tier features and price (monthly, annual)
- Family plan
- Student discount
- Freemium model details (what's gated?)
- Ad-supported tier
- In-app purchases (consumables, cosmetics)
- Institutional / school licensing
- Free trial duration
- Lifetime purchase option
- Referral program / rewards

#### L. Content Creation & Community
- User-generated content (can users create courses/quizzes?)
- Teacher content creation tools
- Community-contributed content moderation
- API for content integration
- LMS integration (Google Classroom, Canvas, Schoology)
- Import/export capabilities

#### M. Analytics & Progress Tracking
- Student progress dashboard
- Learning time tracking
- Mastery percentage per topic
- Weak area identification
- Goal progress visualization
- Historical performance trends
- Predicted exam scores / readiness
- Exportable progress reports
- Teacher/parent analytics dashboard

---

### OUTPUT FORMAT

For each competitor, produce a structured report:

```markdown
## [Competitor Name]

### Overview
- **Category:** (Gamified Learning / AI Tutor / SRS / Visualization / etc.)
- **Platforms:** (iOS / Android / Web / Desktop)
- **Target Audience:** (K-5 / 6-8 / 9-12 / University / Professional / All)
- **Subject Focus:** (Math / Physics / Engineering / All STEM / General)
- **Pricing Model:** (Freemium / Subscription / One-time / Free)
- **Estimated MAU:** (if available from public data)
- **App Store Rating:** (iOS / Android)
- **Social Media Presence:** (follower counts, ad frequency observations)

### Feature Matrix
| Category | Feature | Has It? | Implementation Quality (1-5) | Notes |
|----------|---------|---------|------------------------------|-------|
| Learning Engine | Adaptive difficulty | Yes/No/Partial | 4 | ... |
| Gamification | Streak system | Yes/No/Partial | 5 | ... |
| ... | ... | ... | ... | ... |

### Unique Features (not in CENA)
- Feature 1: description + estimated ROI impact (High/Medium/Low)
- Feature 2: description + estimated ROI impact

### UX Patterns Worth Adopting
- Pattern 1: description + screenshot reference if available
- Pattern 2: description

### Monetization Deep-Dive
- Free tier: what's included
- Paid tier(s): price points, what's gated
- Conversion triggers observed
- Revenue estimate if public

### Weaknesses / Gaps vs CENA
- Gap 1: description
```

---

### FINAL DELIVERABLE

After all competitor reports, produce a consolidated:

1. **Master Feature Matrix** — all competitors as columns, all features as rows, cells = Yes/No/Partial
2. **Gap Analysis** — features competitors have that CENA does NOT have yet
3. **CENA Advantages** — features CENA has that competitors lack
4. **Gamification Depth Comparison** — side-by-side of engagement mechanics per platform
5. **SRS/Learning Science Comparison** — which platforms use evidence-based learning science
6. **Monetization Comparison** — pricing tiers, conversion strategies, revenue models
7. **Priority Recommendations** — top 15 features to add to CENA, ranked by:
   - **ROI Score** (composite of below factors, 1-10):
     - How many competitors have it (market validation)
     - Customer demand signals (App Store reviews, Reddit, forums, social comments)
     - Implementation complexity estimate (T-shirt: S/M/L/XL)
     - Revenue impact potential (Direct monetization, retention lift, acquisition boost)
     - Differentiation value (commodity feature vs unique advantage)
     - User engagement lift (DAU/MAU impact estimate)
   - **Build vs Buy recommendation**
   - **Suggested priority** (P0 ship-now / P1 next-quarter / P2 backlog)

---

### AGENT ASSIGNMENT

| Agent | Responsibility | Competitors |
|-------|---------------|-------------|
| **Agent 1: Direct STEM Competitor Analyst** | Gamified STEM micro-learning apps — the closest competitors to CENA | SmartyMe (all sub-brands), Brilliant, Synthesis |
| **Agent 2: Education Giant Analyst** | Major education platforms with massive user bases | Khan Academy, Khanmigo, Duolingo (gamification benchmark), Kahoot! |
| **Agent 3: AI Tutor & Solver Analyst** | AI-powered tutoring and problem-solving tools | Photomath, Mathway, Wolfram Alpha, Socratic by Google, Numerade |
| **Agent 4: SRS & Study Tool Analyst** | Spaced repetition and flashcard-based learning platforms | Quizlet, Anki, Brainly |
| **Agent 5: STEM Visualization Analyst** | Scientific diagram and visualization tools observed in competitor ads | FigureLabs, GPAI, Nibble |
| **Agent 6: Regional & Emerging Analyst** | Regional players and emerging ed-tech competitors | erkanhoca_mat, Coursera/edX mobile, + any new entrants found during research |
| **Agent 7: Consolidator** | Merge all reports, produce master matrix, gap analysis, ROI-ranked priority recommendations | All |

---

### RESEARCH METHODS

Each agent should:
1. **App Store Mining** — Download or review iOS App Store and Google Play listings. Extract feature lists from descriptions, screenshots, "What's New" changelogs, and developer responses to reviews
2. **User Review Analysis** — Search App Store reviews (1-star and 5-star), G2, Trustpilot, Reddit (r/learnmath, r/learnprogramming, r/languagelearning, r/edtech), and Twitter/X for real user feedback mentioning specific features, complaints, and praise
3. **Official Documentation** — Feature pages, help centers, API docs, blog posts announcing new features
4. **Comparison Articles** — Search for "Brilliant vs Khan Academy", "Duolingo gamification breakdown", "best math learning apps 2025/2026"
5. **Social Media Ad Analysis** — Instagram, Facebook, TikTok ad libraries. Note: SmartyMe and FigureLabs are running heavy paid social campaigns (multiple creatives observed). Extract messaging, positioning, and feature claims from ads
6. **Pricing Pages** — Current pricing, historical price changes, promotional offers
7. **Conference Talks & Interviews** — EdTech conferences, founder interviews, product demos on YouTube
8. **Academic Research** — Papers on gamification in education, spaced repetition effectiveness, AI tutoring outcomes — to validate which features are evidence-based vs hype
9. **Crunchbase / LinkedIn** — Funding rounds, team size, hiring patterns (what roles are they hiring for = what they're building next)

### SPECIAL FOCUS AREAS

Given CENA's positioning, pay extra attention to:

1. **SmartyMe Deep Dive** — This is the most visible direct competitor in the ad screenshots. Multiple sub-brands (physics, math, electricity, engineering). Extract EVERY subject they cover, every game mechanic, every ad creative angle. How do they position "addictive learning"? What's their conversion funnel from ad to app?

2. **FigureLabs Deep Dive** — Heavy ad spend on AI scientific illustration. Is this a feature CENA should integrate (AI diagram generation for study notes)? Or a separate tool category? What's their pricing model?

3. **Duolingo Gamification Blueprint** — Extract the COMPLETE gamification system: streaks, hearts, gems, leagues, XP, stories, Super Duolingo, family plan, streak freeze, friend quests, leaderboard tiers, push notification copy, re-engagement flow. This is the benchmark.

4. **Arabic/Hebrew Market** — Which competitors have Arabic or Hebrew localization? What's the competitive landscape in MENA and Israeli ed-tech specifically? CENA has a language advantage here.

5. **Evidence-Based Features** — For each recommended feature, cite whether there's learning science research supporting its effectiveness (not just "competitors have it")

### CONSTRAINTS

- Do NOT fabricate features. If uncertain, mark as "Unverified" with source
- Include source URLs for every major claim
- Include App Store links for all mobile apps
- Focus on features relevant to a gamified STEM learning app — skip generic enterprise/LMS features unless they have a mobile learning angle
- Prioritize depth over breadth — a detailed analysis of 12 competitors beats a shallow look at 30
- For ROI estimates, be explicit about assumptions (e.g., "assuming 10% conversion lift based on Duolingo's reported streak impact")
- Output in markdown, ready to drop into `/docs/competitive-analysis/`
- All monetary figures in USD
- Note any features that are specifically relevant to the Arabic-speaking or Hebrew-speaking educational market
