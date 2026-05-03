# CENA Feature Extraction — Consolidated from Competitive Research

> **Extracted:** 2026-03-31
> **Source:** 7 Kimi swarm reports + eSelf deep dive, 25+ competitors analyzed
> **Purpose:** Actionable feature backlog with ROI scoring

---

## Feature Inventory

### Legend
- **Status**: HAVE (CENA has it) | GAP (missing) | PARTIAL (partially implemented)
- **ROI**: 1-10 composite score (market validation + demand + revenue impact + differentiation)
- **Size**: S (2-4w) | M (4-6w) | L (8-12w) | XL (12w+)
- **Priority**: P0 (ship now) | P1 (next quarter) | P2 (backlog) | P3 (monitor)
- **Source**: Which competitor(s) validated this feature

---

## A. LEARNING ENGINE

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| A1 | Camera/OCR scan-to-solve | GAP | 9.2 | L | P0 | Photomath (220M DL), Mathway, Brainly, GPAI | #1 missing feature. Buy Mathpix API ($0.02/scan). +30-40% user acquisition |
| A2 | AI tutoring sessions | HAVE | -- | -- | -- | Synthesis, Khanmigo | CENA already has this |
| A3 | Step-by-step solutions | HAVE | -- | -- | -- | Photomath, Wolfram Alpha | CENA already has this |
| A4 | Adaptive difficulty | HAVE | -- | -- | -- | Synthesis, Duolingo, Brilliant | CENA already has this |
| A5 | Multiple question types (MCQ, free-response, drag-drop) | HAVE | -- | -- | -- | Duolingo (12+ types), Kahoot! (14+) | CENA already has this |
| A6 | Video explanations | GAP | 8.0 | L | P1 | Khan Academy (10-min videos), Numerade (100M+ videos), Doping Hafiza (70K+) | 60% of users prefer video for complex topics. Hybrid: license + AI-generate |
| A7 | Animated whiteboard tutorials | GAP | 7.2 | L | P1 | Photomath Plus, Khan Academy | Whiteboard-style with voiceover. Premium gate opportunity |
| A8 | AI content generation (notes -> flashcards/quizzes) | GAP | 7.8 | M | P1 | Quizlet (Magic Notes), Kahoot!, GPAI (Cheatsheet Builder) | Upload PDF/notes -> auto-generate study materials. GPT-4 API |
| A9 | Textbook solutions library | GAP | 5.5 | XL | P2 | Photomath, Mathway, Brainly | Pre-solved problems from popular textbooks. Content licensing needed |
| A10 | Offline content download | HAVE | -- | -- | -- | Duolingo (Super), Anki, Coursera | CENA already has this |
| A11 | Multiple solution methods | GAP | 6.0 | M | P2 | Photomath (algebraic + graphical), Wolfram Alpha | Show different approaches to same problem |
| A12 | Micro-lesson format (3-10 min) | HAVE | -- | -- | -- | Nibble, SmartyMe, Duolingo (5 min) | CENA sessions are configurable |
| A13 | Deep study mode | HAVE | -- | -- | -- | None | Unique to CENA |
| A14 | Podcasts / audio lessons | GAP | 4.5 | L | P3 | Duolingo (award-winning), SmartyMe (audio-first) | Audio-first for commuters. Low priority for STEM |
| A15 | Interactive stories/scenarios | GAP | 5.0 | L | P2 | Duolingo (Stories feature, 12 exercise types) | Narrative-based learning embedded in path |
| A16 | Triple cross-check AI verification | GAP | 6.5 | M | P2 | GPAI (GPT-5 + Gemini + Deepseek R1) | Run answer through 3 LLMs for accuracy. Trust signal |

---

## B. SPACED REPETITION & MEMORY SCIENCE

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| B1 | FSRS algorithm | HAVE | -- | -- | -- | Anki (only match) | CENA's strongest differentiator. 97.4% better than SM-2 |
| B2 | Forgetting curve visualization | HAVE | -- | -- | -- | Anki (via add-ons) | CENA already tracks this |
| B3 | Adaptive interleaving | HAVE | -- | -- | -- | Anki | CENA already has this |
| B4 | Confidence-based scheduling | HAVE | -- | -- | -- | Anki (4-button grading) | CENA already has this |
| B5 | Review heatmap (GitHub-style) | GAP | 6.0 | S | P1 | Anki (#1 most popular add-on) | Visual calendar of study activity. Highly motivating |
| B6 | Leech detection (flag problematic cards) | GAP | 5.0 | S | P2 | Anki | Auto-flag cards that keep failing. Triggers remediation |
| B7 | Cloze deletions (fill-in-blank) | GAP | 5.5 | S | P2 | Anki | Fill-in-the-blank card format for contextual learning |
| B8 | Image occlusion | GAP | 5.0 | S | P2 | Anki | Hide parts of images for anatomy/diagram study |
| B9 | DSR metrics visible to student | GAP | 4.5 | S | P3 | Anki (Difficulty, Stability, Retrievability) | Show memory model per concept. Power-user feature |

---

## C. GAMIFICATION & ENGAGEMENT

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| C1 | Quality-gated streaks | HAVE | -- | -- | -- | None | Unique to CENA. Better than login-based |
| C2 | Streak freeze (purchase protection) | GAP | 8.8 | S | P0 | Duolingo (200 gems) | Purchase before missing a day. Reduces churn 15-20% |
| C3 | Streak recovery (3-day window) | GAP | 8.8 | S | P0 | Duolingo | 3-day window to restore broken streak. Critical retention |
| C4 | Friend streaks (up to 5 friends) | GAP | 7.3 | S | P1 | Duolingo | Both friends must complete daily goal. Social accountability |
| C5 | 10+ tier league system | GAP | 9.0 | M | P0 | Duolingo (22 tiers), Brilliant (10 tiers) | Weekly competition, promotion/demotion. +60% engagement |
| C6 | Hearts/lives system | GAP | 8.5 | M | P0 | Duolingo (5 hearts) | Lose heart per wrong answer. Loss aversion. +40% answer quality |
| C7 | Heart regeneration (1 per 5h) | GAP | 8.5 | S | P0 | Duolingo | Automatic regen. Premium = unlimited hearts |
| C8 | Virtual currency (gems/coins) | GAP | 8.3 | M | P0 | Duolingo, Khan Academy (energy points), Brainly (points) | Multiple earning/spending paths. Full engagement economy |
| C9 | XP system | HAVE | -- | -- | -- | Duolingo, Brilliant, Numerade | CENA already has this |
| C10 | Badges / achievements | HAVE | -- | -- | -- | Duolingo, Brilliant, Khan Academy | CENA already has this |
| C11 | Boss battles | HAVE | -- | -- | -- | None | Unique to CENA |
| C12 | Quest system | HAVE | -- | -- | -- | Duolingo, Abwaab | CENA already has this |
| C13 | Leaderboards (class-level) | HAVE | -- | -- | -- | Duolingo (weekly), Brilliant, Kahoot! | CENA already has this |
| C14 | Daily challenges / problem of the day | HAVE | -- | -- | -- | Duolingo, Brilliant, Kahoot! | CENA already has this |
| C15 | Celebration animations | HAVE | -- | -- | -- | Duolingo (rich), Brilliant | CENA already has multi-tier |
| C16 | Power-ups / boosts (double XP, timer) | GAP | 6.5 | S | P1 | Duolingo (timer boost, double XP) | Purchasable with gems. Premium conversion driver |
| C17 | Avatar / character customization | GAP | 5.5 | M | P2 | Duolingo (outfits for Duo), Kahoot!, Khan Academy (creatures) | Cosmetic gem sink. Personalization |
| C18 | Mascot character | GAP | 6.0 | M | P2 | Duolingo (Duo the Owl -- viral), Brainly | Character-driven engagement. Viral marketing asset |
| C19 | Double or Nothing wager | GAP | 5.0 | S | P2 | Duolingo (maintain streak 7/14/30 days for gem reward) | Gamble gems on streak maintenance |
| C20 | Random reward drops (chests) | GAP | 5.0 | S | P2 | Duolingo | Variable reward schedule. Dopamine trigger |
| C21 | Diamond Tournament | GAP | 4.5 | M | P3 | Duolingo (3-week elite competition) | End-game content for top league players |
| C22 | Kahootopia meta-game (island building) | GAP | 3.5 | XL | P3 | Kahoot! | Island-building progression. Too complex for now |
| C23 | Live multiplayer competitions | GAP | 6.0 | L | P2 | Kahoot! (up to 2000 players) | Real-time quiz battles. Social engagement |
| C24 | Confidence mode (self-reported) | HAVE | -- | -- | -- | Kahoot! | CENA already has confidence calibration |
| C25 | "Brainliest" answer recognition | GAP | 4.0 | S | P3 | Brainly | Community voting on best peer solutions |

---

## D. ONBOARDING & FIRST-USE

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| D1 | Placement test / diagnostic | PARTIAL | 6.5 | M | P1 | Duolingo, Synthesis, Khan Academy | CENA has partial. Expand to full adaptive diagnostic |
| D2 | Goal setting | HAVE | -- | -- | -- | Duolingo, Quizlet | CENA already has this |
| D3 | Learning pace selection | HAVE | -- | -- | -- | Duolingo | CENA already has this |
| D4 | Progressive disclosure | HAVE | -- | -- | -- | Duolingo, Brilliant | CENA already has this |
| D5 | Time commitment selection | GAP | 5.0 | S | P2 | Duolingo (10/20/30/50 XP/day) | User picks daily time commitment. Sets expectations |
| D6 | Learning soundtrack selection | GAP | 4.0 | S | P3 | Synthesis | Students choose background music. Quick personalization win |

---

## E. SOCIAL & COMMUNITY

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| E1 | Peer solution sharing | HAVE | -- | -- | -- | Brainly, Noon Academy | CENA already has this |
| E2 | Class social feed | HAVE | -- | -- | -- | Noon Academy | CENA already has this |
| E3 | Friend system / social graph | GAP | 6.5 | M | P1 | Duolingo (follow, nudge, compete) | Follow friends, see progress, nudge to study |
| E4 | Teacher dashboard / classroom tools | GAP | 7.5 | L | P1 | Khan Academy, Khanmigo (FREE for teachers), Brilliant (Educators), Kahoot! | Assignment creation, class progress, K-12 free program |
| E5 | Parent dashboard | GAP | 7.5 | M | P1 | Synthesis (weekly reports), Doping Hafiza | Weekly AI-generated progress reports. Family plan enabler |
| E6 | Community Q&A forum | GAP | 4.5 | M | P3 | Brainly (350M users), Khan Academy | Community answers 24/7. Moderation overhead |
| E7 | Live tutoring sessions | GAP | 5.0 | XL | P3 | Brainly Tutor ($95.99/yr), Noon Academy | Live expert help. Expensive to operate |
| E8 | Study groups | GAP | 4.0 | M | P3 | Quizlet, Noon Academy | Small group study rooms |
| E9 | Team challenges (collaborative) | GAP | 5.5 | M | P2 | Kahoot! (team mode), Duolingo (friend quests) | Teams compete together. Balances competition + cooperation |
| E10 | Nudge feature (remind friends) | GAP | 5.5 | S | P1 | Duolingo | Send reminder to friends who haven't studied today |

---

## F. AI & PERSONALIZATION

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| F1 | AI tutor (Socratic method) | HAVE | -- | -- | -- | Khanmigo (best), Synthesis | CENA already has this |
| F2 | Personalized learning path | HAVE | -- | -- | -- | Brilliant, Synthesis, Numerade | CENA already has this |
| F3 | Adaptive content sequencing | HAVE | -- | -- | -- | Synthesis, Duolingo | CENA already has this |
| F4 | Knowledge graph / prerequisite mapping | HAVE | -- | -- | -- | None | Unique to CENA |
| F5 | Weakness detection & remediation | HAVE | -- | -- | -- | Synthesis, Duolingo, Doping Hafiza | CENA already has this |
| F6 | AI tutor personality (warm, encouraging, uses name) | PARTIAL | 7.0 | S | P1 | Synthesis ("world's first superhuman math tutor") | Celebrate effort over correctness. Growth mindset language |
| F7 | Voice interaction (speak + listen) | GAP | 6.2 | M | P2 | Duolingo (Max), Khanmigo, Loora | Speech-to-text + TTS for conversational practice |
| F8 | AI roleplay / scenario practice | GAP | 6.0 | L | P2 | Duolingo Max (cafe, shopping scenarios) | Conversational practice in real-world contexts |
| F9 | AI-generated practice problems | PARTIAL | 6.5 | M | P1 | Quizlet, Kahoot!, Numerade, GPAI | Generate custom practice from topic/difficulty. CENA has partial |
| F10 | AI study plan generation | GAP | 5.5 | M | P2 | Numerade, Brainly (Test Prep), Doping Hafiza | Upload exam date + topics -> daily study schedule |
| F11 | AI cheatsheet builder | GAP | 5.5 | M | P2 | GPAI, Quizlet (Magic Notes) | Upload lecture -> concise cheatsheet. Exam prep |
| F12 | Conversational AI avatar (video) | GAP | 5.0 | XL | P3 | eSelf AI/Kaltura (sub-1s latency, 30+ languages) | Avatar tutor with real-time video. Expensive to build |

---

## G. VISUALIZATION & INTERACTIVE CONTENT

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| G1 | Interactive diagrams | HAVE | -- | -- | -- | Brilliant, Synthesis, FigureLabs | CENA already has this |
| G2 | Knowledge graph visible to student | HAVE | -- | -- | -- | None | Unique to CENA |
| G3 | AI diagram generation (text-to-figure) | GAP | 6.5 | M | P2 | FigureLabs (Nano Banana Pro), GPAI (AI Visualizer) | Generate diagrams from text descriptions. Partner/integrate |
| G4 | Graphing calculator / tools | GAP | 5.0 | M | P2 | Khan Academy, Wolfram Alpha, Mathway | Built-in graphing for math topics |
| G5 | 3D models / simulations | GAP | 6.5 | XL | P2 | Brilliant, Wolfram Alpha | 3D manipulables for STEM. High differentiation |
| G6 | Virtual labs / experiments | GAP | 5.0 | XL | P3 | Brilliant, Khan Academy | Simulated experiments. Very expensive to build |
| G7 | Sketch-to-figure (hand-drawn -> professional) | GAP | 4.5 | L | P3 | FigureLabs | Transform sketches to publication-ready figures |
| G8 | Editable PPTX export of diagrams | GAP | 3.5 | M | P3 | FigureLabs | Export figures as editable slides. Niche |
| G9 | Code playground / IDE | GAP | 4.0 | L | P3 | Brilliant, Khan Academy (JS, HTML, Python, SQL) | In-app coding for CS topics |
| G10 | Animation-based explanations | PARTIAL | 6.0 | L | P2 | Brilliant (high quality), Doping Hafiza | Animated concept explanations. CENA has partial |

---

## H. WELLBEING & HEALTHY LEARNING

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| H1 | Session time limits | HAVE | -- | -- | -- | None (Synthesis partial) | Unique to CENA |
| H2 | Bedtime mode / quiet hours | HAVE | -- | -- | -- | None | Unique to CENA |
| H3 | Flow state monitoring | HAVE | -- | -- | -- | None | Unique to CENA |
| H4 | Age-tiered safety | HAVE | -- | -- | -- | Duolingo, Khan Academy | CENA already has this |
| H5 | Frustration detection | PARTIAL | 5.5 | M | P2 | Synthesis (AI adjusts tone) | Detect struggle and adjust difficulty/encouragement |
| H6 | Dyslexia-friendly font option | GAP | 5.5 | S | P1 | Synthesis (OpenDyslexic) | Toggle dyslexic font. Expands market to neurodiverse |
| H7 | Voice speed adjustment | GAP | 4.5 | S | P2 | Synthesis | Adjust AI speech rate. Accessibility feature |
| H8 | Eye strain / rest reminders | GAP | 3.5 | S | P3 | None | 20-20-20 rule reminder. Responsible design |
| H9 | Growth mindset messaging | PARTIAL | 5.0 | S | P1 | Synthesis ("mistakes are expected"), Khanmigo | Reframe errors as learning. CENA has partial |

---

## I. RETENTION & RE-ENGAGEMENT

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| I1 | Push notifications (intelligent) | HAVE | -- | -- | -- | Duolingo (industry-leading) | CENA already has this |
| I2 | Habit stacking / routine builder | HAVE | -- | -- | -- | None | Unique to CENA |
| I3 | Win-back campaigns (lapsed users) | GAP | 6.8 | M | P1 | Duolingo (escalating: guilt -> humor -> viral) | Day 1-3: reminders. Day 4-7: escalate. Day 15+: "we miss you" |
| I4 | Home screen widgets (iOS/Android) | GAP | 7.0 | S | P1 | Duolingo | Streak status, daily reminder on home screen. Daily visibility |
| I5 | Streak repair (earn back via practice) | GAP | 6.5 | S | P1 | Duolingo (practice to earn hearts back) | Complete makeup session to restore streak |
| I6 | Weekly progress email / digest | GAP | 5.5 | S | P2 | Duolingo, Coursera, Synthesis (parent reports) | Auto-generate weekly summary email |
| I7 | Wearable integration (Apple Watch) | GAP | 4.0 | M | P3 | Duolingo | Streak reminder on watch. Niche |
| I8 | "Death of mascot" viral campaigns | GAP | 3.0 | M | P3 | Duolingo (2025: "Death of Duo" drove 45% reactivation) | Viral social media events. Requires brand maturity |

---

## J. PLATFORM & TECHNICAL

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| J1 | iOS + Android native | HAVE | -- | -- | -- | All major competitors | CENA already has this (Flutter) |
| J2 | Offline mode | HAVE | -- | -- | -- | Anki, Duolingo (Super) | CENA already has this |
| J3 | Arabic + Hebrew RTL | HAVE | -- | -- | -- | None (Abwaab Arabic-only, Noon Arabic-only) | Unique dual-RTL. CENA's strongest regional advantage |
| J4 | Dark mode | HAVE | -- | -- | -- | Duolingo, Brilliant, all major | CENA already has this |
| J5 | Accessibility audit | HAVE | -- | -- | -- | Brilliant, Khan Academy | CENA already has this |
| J6 | Web version | GAP | 6.0 | XL | P2 | Duolingo, Khan Academy, Brilliant, Quizlet, all major | Web app for desktop study. Wide platform coverage |
| J7 | Low-bandwidth mode | GAP | 5.0 | M | P2 | Abwaab | Compressed content for slow connections. MENA market important |
| J8 | Color blindness mode | GAP | 4.0 | S | P2 | Duolingo (partial) | Color palette alternatives. Accessibility |
| J9 | Dyslexia mode (font + spacing) | GAP | 5.5 | S | P1 | Synthesis | OpenDyslexic font + increased spacing. Quick win |

---

## K. MONETIZATION

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| K1 | Freemium model (free tier with limits) | GAP | 9.0 | M | P0 | Duolingo, Brilliant, Quizlet | Limited daily sessions free + ads. Core conversion driver |
| K2 | Premium subscription ($7.99-11.99/mo) | GAP | 9.0 | M | P0 | Duolingo ($59.99/yr), Brilliant ($161.88/yr) | Unlimited AI, full SRS, no ads, offline. Target $79.99-99.99/yr |
| K3 | Family plan (up to 6 members) | GAP | 7.5 | M | P1 | Duolingo ($119.99-239.99/yr), Synthesis (7 kids), Khanmigo (10 kids) | 40% higher LTV than individual |
| K4 | In-app gem purchases | GAP | 6.5 | S | P1 | Duolingo | Buy gems for streak freezes, heart refills, boosts |
| K5 | Free trial (7 days) | GAP | 7.0 | S | P0 | Brilliant, Synthesis, Quizlet | 7-day full access. No credit card required (avoid SmartyMe backlash) |
| K6 | Student/teacher discount (FREE for K-12) | GAP | 7.0 | S | P1 | Brilliant (FREE for educators), Khanmigo (FREE for teachers) | Massive acquisition channel. Build loyalty early |
| K7 | Institutional / school licensing | GAP | 6.5 | L | P2 | Kahoot! ($36-708/yr), Coursera, Khan Academy | B2B revenue stream. $500-2000/yr per school |
| K8 | Referral program | GAP | 5.5 | S | P2 | Duolingo (100 gems per invite) | Viral growth loop. Gem reward for invites |
| K9 | Transparent pricing (no predatory billing) | GAP | 8.0 | S | P0 | Anti-SmartyMe, Anti-Nibble | Both have 2.3 Trustpilot for billing. Easy cancel. Clear terms. Trust = brand |

---

## L. CONTENT CREATION & ECOSYSTEM

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| L1 | Content moderation pipeline | HAVE | -- | -- | -- | Brainly (ML + volunteer) | CENA already has this |
| L2 | LMS integration (Google Classroom, Canvas) | GAP | 6.0 | L | P2 | Khan Academy, Kahoot!, Coursera, Numerade | School adoption driver. Google Classroom first |
| L3 | Teacher content creation tools | GAP | 6.5 | L | P1 | Kahoot! (drag-drop quiz builder), Khanmigo (lesson plan gen) | Teacher creates assignments from CENA content |
| L4 | AI quiz generation from PDF/URL | GAP | 6.0 | M | P2 | Kahoot! (PDF-to-quiz, URL-to-quiz) | Upload document -> auto-generate quiz |
| L5 | Community-contributed content (UGC) | GAP | 4.0 | L | P3 | Quizlet (millions of sets), Brainly (350M users) | User-created study sets. Moderation overhead |
| L6 | Import/export study materials | GAP | 4.5 | M | P3 | Anki (CSV, text, multi-format), Quizlet | Import from Anki/Quizlet. Reduce switching cost |

---

## M. ANALYTICS & PROGRESS

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| M1 | Student progress dashboard | HAVE | -- | -- | -- | All major competitors | CENA already has this |
| M2 | Mastery percentage per topic | HAVE | -- | -- | -- | Khan Academy (4-stage), Brilliant | CENA already has this |
| M3 | Weak area identification | HAVE | -- | -- | -- | Synthesis, Duolingo, Doping Hafiza | CENA already has this |
| M4 | Exportable progress reports (PDF) | GAP | 5.5 | S | P2 | Anki, Quizlet, Coursera | PDF/email-ready report for parents/teachers |
| M5 | Predicted exam scores / readiness | GAP | 5.0 | M | P2 | None (emerging) | AI predicts Bagrut score based on mastery. Unique differentiator |
| M6 | Learning time tracking | PARTIAL | 5.0 | S | P1 | Duolingo, Coursera, all major | CENA has partial. Make prominent in UI |
| M7 | Historical trends / progress over time | PARTIAL | 5.0 | S | P1 | Duolingo, Khan Academy | Charts showing improvement trajectory |

---

## N. REGIONAL & MARKET-SPECIFIC

| # | Feature | Status | ROI | Size | Priority | Source | Notes |
|---|---------|--------|-----|------|----------|--------|-------|
| N1 | Bagrut exam alignment (Israel) | PARTIAL | 8.0 | L | P0 | eSelf/CET (English oral), Abwaab (MENA exams) | CENA must cover Bagrut subjects. eSelf proved 3.94pt improvement |
| N2 | Arabic curriculum alignment | GAP | 7.0 | L | P1 | Abwaab ($27.5M funded, 12M DL), Noon Academy (16M users) | MENA market: 400M+ Arabic speakers. No competitor has Arabic + Hebrew |
| N3 | "Replace doomscrolling" positioning | GAP | 6.0 | S | P1 | SmartyMe, Nibble | Marketing messaging: "productive screen time." Resonates with parents |
| N4 | ADHD-friendly learning mode | GAP | 5.5 | M | P2 | SmartyMe, Synthesis (neurodiverse-first) | Short sessions, reduced distractions, focus mode |
| N5 | Neurodiverse-first design | PARTIAL | 6.0 | M | P1 | Synthesis (dyslexia, dyscalculia, ASD, ADHD, 2E) | OpenDyslexic font, voice speed, read-aloud. Expands addressable market |
| N6 | CET partnership exploration | GAP | 8.5 | -- | P0 | eSelf/CET | CET = Israel's K-12 content kingmaker. eSelf absorbed by Kaltura creates opening |
| N7 | Israel Innovation Authority sandbox | GAP | 7.0 | -- | P1 | Government program (NIS 10M) | Funding + school pilot access + regulatory support |
| N8 | "Personal advisor" feature | GAP | 5.0 | M | P2 | Abwaab (Morshed: personal advisor for course/career guidance) | AI advisor for educational path planning |

---

## SUMMARY COUNTS

| Status | Count | Notes |
|--------|-------|-------|
| **HAVE** (CENA advantage) | 42 | Already built. Defend and polish |
| **GAP** (missing features) | 78 | Prioritized by ROI below |
| **PARTIAL** (needs improvement) | 9 | Quick enhancement opportunities |
| **Total features extracted** | 129 | Across 14 categories |

---

## TOP 20 FEATURES BY ROI SCORE

| Rank | Feature | ROI | Size | Priority | Category |
|------|---------|-----|------|----------|----------|
| 1 | Camera/OCR scan-to-solve | 9.2 | L | P0 | Learning Engine |
| 2 | 10+ tier league system | 9.0 | M | P0 | Gamification |
| 3 | Freemium monetization model | 9.0 | M | P0 | Monetization |
| 4 | Premium subscription tier | 9.0 | M | P0 | Monetization |
| 5 | Streak freeze & recovery | 8.8 | S | P0 | Gamification |
| 6 | Hearts/lives system | 8.5 | M | P0 | Gamification |
| 7 | CET partnership exploration | 8.5 | -- | P0 | Regional |
| 8 | Virtual currency (gems) | 8.3 | M | P0 | Gamification |
| 9 | Bagrut exam alignment | 8.0 | L | P0 | Regional |
| 10 | Video explanations | 8.0 | L | P1 | Learning Engine |
| 11 | Transparent billing (anti-predatory) | 8.0 | S | P0 | Monetization |
| 12 | AI content generation (Magic Notes) | 7.8 | M | P1 | Learning Engine |
| 13 | Parent dashboard | 7.5 | M | P1 | Social |
| 14 | Teacher dashboard | 7.5 | L | P1 | Social |
| 15 | Family plan | 7.5 | M | P1 | Monetization |
| 16 | Friend streaks | 7.3 | S | P1 | Gamification |
| 17 | Animated whiteboard tutorials | 7.2 | L | P1 | Learning Engine |
| 18 | AI tutor personality (warm, named) | 7.0 | S | P1 | AI |
| 19 | Free trial (7-day, no CC) | 7.0 | S | P0 | Monetization |
| 20 | Home screen widgets | 7.0 | S | P1 | Retention |
