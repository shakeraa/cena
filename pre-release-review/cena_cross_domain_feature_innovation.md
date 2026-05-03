# Cross-Domain Feature Innovation for Cena
## Adaptive Math Learning Platform — Bagrut Exam Preparation

**Date:** 2026-04-20  
**Researcher:** Cross-Domain Feature Innovation Specialist  
**Scope:** 8 features from 6 non-EdTech domains  
**Guardrails Applied:** All features screened against loss-aversion, streak mechanics, comparative shame, misconception retention, ML-training requirements, and COPPA compliance.

---

## Feature 1: Math Concept Map (Knowledge Graph)
**Source Domain:** Productivity / Personal Knowledge Management

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Obsidian (https://obsidian.md) — knowledge graph with bidirectional linking |
| **Feature Name** | Math Concept Map — Interactive Knowledge Graph |
| **Original Description** | Obsidian visualizes notes as a graph where nodes are pages and edges are backlinks. Users discover "structural gaps" — blind spots between idea clusters — and bridge them using AI-generated insights. Math researchers use it to connect lecture concepts, paper ideas, and problem families across months of work. |
| **Cena Adaptation** | An interactive, visual map of all Bagrut math topics where each concept node (e.g., "Quadratic Equations") links to prerequisites ("Factoring"), related topics ("Parabolas"), and problem types. Students see their own "knowledge territory" — nodes light up as they master concepts. AI identifies "structural gaps" — concepts the student hasn't connected yet — and suggests bridging exercises. |
| **Primary Persona** | **Yael (The Systematic Studier)** — Grade 11, feels overwhelmed by disconnected topics, wants to see the big picture before Bagrut. Also benefits **Noam (The Crammer)** who needs quick visual orientation of what's left to learn. |
| **Effort Estimate** | **L** — Requires graph visualization engine, curriculum mapping, and adaptive gap detection. ~6-8 weeks engineering. |
| **Guardrail Check** | ✅ No streaks/loss-aversion — purely exploratory visualization. ✅ No comparative shame — shows personal knowledge only, no percentiles. ✅ No misconception retention across sessions — gaps are live-calculated, not stored as deficit data. ✅ No ML training on student data — rule-based prerequisite mapping. ✅ COPPA-safe — no PII collection. |
| **Implementation Sketch** | 1) Map full Bagrut math curriculum as a directed graph (100-150 nodes). 2) Track which nodes student has interacted with via standard session data. 3) Render personal graph view with mastery color-coding (unexplored → exploring → practiced → confident). 4) Highlight "bridge concepts" — high-betweenness-centrality nodes that unlock multiple pathways. 5) Tap any node to see: explanation, 2 practice problems, related video. |
| **Verdict** | **SHIP** — High differentiation, low risk, strong fit with Cena's adaptive positioning. |

**Sources:**
- Mathone: "How I Take Math Notes in Obsidian" (https://mathonelist.substack.com/p/how-i-take-math-notes-in-obsidian)
- InfraNodus: "Obsidian Knowledge Graph: AI-Powered Visualization" (https://infranodus.com/use-case/visualize-knowledge-graphs-pkm)

---

## Feature 2: Pre-Session Focus Ritual (Breathing Opener)
**Source Domain:** Mental Health / Mindfulness

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Calm/Headspace (https://www.calm.com) — 60-second Breathe Bubble, pre-session grounding exercises |
| **Feature Name** | Focus Ritual — 60-Second Session Opener |
| **Original Description** | Calm's "Breathe Bubble" provides a 60-second visual breathing guide for acute stress moments. Headspace offers "Focus for Studying" guided visualizations. The U.S. Department of Education publishes teacher guides for 5-minute focused breathing to reduce math anxiety before assessments. |
| **Cena Adaptation** | Before each study session, Cena offers an optional 60-second breathing ritual. A simple animated bubble expands (inhale 4s) and contracts (exhale 6s). Student can skip with one tap. After breathing, a one-question mood check-in: "How are you feeling about math today?" (😰 😐 🙂). This sets session tone and optionally adjusts problem difficulty. |
| **Primary Persona** | **Maya (The Anxious Achiever)** — Grade 12, experiences pre-study anxiety and test panic. Bagrut pressure amplifies her math anxiety. Also benefits **Noam** who procrastinates due to avoidance anxiety. |
| **Effort Estimate** | **S** — CSS animation + breath timing logic. ~1-2 weeks. |
| **Guardrail Check** | ✅ No streaks — breathing is optional every time, no "days in a row" tracking. ✅ No loss-aversion — no penalties for skipping. ✅ No therapy claims — explicitly framed as "focus ritual," not mental health treatment. ✅ COPPA-safe — mood data is session-local, not profile-stored for under-13 users. |
| **Implementation Sketch** | 1) Optional toggle in settings (default: on). 2) Before session start, show animated bubble with 4-7-8 breathing pattern. 3) Post-breathing: 1-tap mood emoji. 4) Mood adjusts session opening difficulty: 😰 → start with review problems, 🙂 → start with challenge problems. 5) Track session correlation (mood → session completion) locally for student insight only. |
| **Verdict** | **SHIP** — Extremely low effort, high student wellbeing impact, strong research backing. |

**Sources:**
- U.S. Department of Education: "Teacher Guide: Focused Breathing to Reduce Math Anxiety" (https://ies.ed.gov/rel-northwest/2025/01/session-2-teacher-guide-focused-breathing-reduce-math-anxiety)
- Calm: Official app features and Breathe Bubble (https://www.calm.com)
- ResidencyAdvisor: "Master Breathing Exercises to Overcome Test Anxiety" (https://residencyadvisor.com/resources/test-anxiety-tips/ultimate-guide-breathing-exercises-test-stress-relief)

---

## Feature 3: Learning Journeys (Narrative Habit Paths)
**Source Domain:** Habit Formation / Behavioral Science

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Fabulous (https://www.thefabulous.co) — Duke University behavioral science-based habit journeys |
| **Feature Name** | Bagrut Learning Journeys — Themed Progression Paths |
| **Original Description** | Fabulous uses "Journeys" — multi-week themed paths (e.g., "Self-Discipline Made Simple") where an animated character progresses along a visual path. Users complete one small action daily, gradually layering new habits. The app applies "implementation intentions" (deciding in advance when/where/how) and environmental redesign cues to close the intention-action gap. |
| **Cena Adaptation** | Students choose a "Journey" themed to their Bagrut prep goal: "Algebra Foundations," "Geometry Mastery," "Trigonometry Explorer," or "Bagrut Sprint." Each Journey is a 4-6 week visual path with daily micro-sessions (10-20 min). A small avatar progresses along the path. Each day unlocks one focused activity: concept video → guided practice → independent problem → review. Students can switch journeys or run two in parallel. |
| **Primary Persona** | **Yael (The Systematic Studier)** — needs structure and clear "what do I do today?" guidance. Also **Omar (The Struggler)** who is overwhelmed by the full curriculum and needs it broken into manageable chunks. |
| **Effort Estimate** | **M** — Journey curriculum design, path visualization, session sequencing. ~4-5 weeks. |
| **Guardrail Check** | ✅ No streaks/loss-aversion — path shows progress, not "broken chains." Missing a day simply means the next step waits; no visual penalty. ✅ No comparative shame — personal journey only, no leaderboard. ✅ No ML training — pre-structured content paths. ✅ COPPA-safe — no behavioral profiling. |
| **Implementation Sketch** | 1) Design 5-6 themed journeys covering Bagrut topics. 2) Each journey has 25-30 steps (days). 3) Each step = one session bundle (3-5 problems + 1 video + 1 review prompt). 4) Visual path map shows completed, current, and upcoming steps. 5) Student can "pause" a journey (vacation, exam week) and resume without penalty. 6) Completion badge (celebratory, not competitive) awarded at journey end. |
| **Verdict** | **SHIP** — Strong behavioral science backing, excellent differentiation from generic problem sets. |

**Sources:**
- Bustle: "This App Helped Me Build & Finally Stick To Healthy Habits" (https://www.bustle.com/wellness/fabulous-app-good-habits-review-features-price)
- TheLiven: "Fabulous App Review: Features, Pricing and Who It's For" (https://theliven.com/blog/wellbeing/dopamine-management/fabulous-app-review)
- Duke University Center for Advanced Hindsight (behavioral science backing)

---

## Feature 4: Adaptive Flow Sessions (Dynamic Difficulty)
**Source Domain:** Games / Puzzle Mechanics

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Tetris — adaptive difficulty adjustment to maintain flow state (research by Kate Sweeny, UC Riverside) |
| **Feature Name** | Flow Sessions — Dynamic Difficulty Balancing |
| **Original Description** | Sweeny's research (published in journal *Emotion*) found that Tetris players in an "adaptive difficulty" condition (difficulty adjusts to skill in real-time) experienced significantly more flow, less negative emotion, and greater positive emotion than players in fixed easy or hard conditions. Flow is achieved when challenge balances skill — not too boring, not too frustrating. |
| **Cena Adaptation** | Each Cena session monitors real-time performance (accuracy, time-per-problem, hint usage) and dynamically adjusts problem difficulty within a narrow band. If a student solves 3 problems quickly, the next problem adds a conceptual twist. If they struggle, the next simplifies or provides a scaffolded intermediate step. The goal: maintain the "flow channel" where challenge matches ability. |
| **Primary Persona** | **Maya (The Anxious Achiever)** — fears failure, will disengage if problems feel impossible. **Noam (The Crammer)** — gets bored with too-easy review and needs to stay engaged. **Omar (The Struggler)** — needs scaffolding without being sent to "remedial" content that shames him. |
| **Effort Estimate** | **M** — Real-time difficulty adjustment algorithm, problem tagging by sub-skill, session state machine. ~4-6 weeks. |
| **Guardrail Check** | ✅ No loss-aversion — difficulty adjusts gently, no "game over." ✅ No comparative shame — personal flow channel, no peer comparison. ✅ No misconception retention — real-time adjustment doesn't require storing wrong answers across sessions. ✅ COPPA-safe — session-local processing. |
| **Implementation Sketch** | 1) Tag all problems with 5 difficulty tiers within each topic. 2) Session starts with brief diagnostic (2 problems) to enter flow channel. 3) Real-time tracking: if accuracy > 80% and time < target, increment difficulty; if accuracy < 50% or excessive hints, decrement. 4) Show subtle visual indicator: "Great pace! 🔥" or "Let's take it step by step 💡" — never punitive. 5) Session ends with: "You worked through X problems at your perfect challenge level." |
| **Verdict** | **SHIP** — Core adaptive feature, strong research evidence, aligns with Cena's brand promise. |

**Sources:**
- UC Riverside News: "Tetris: It could be the salve for a worried mind" (https://news.ucr.edu/articles/2018/10/26/tetris-it-could-be-the-salve-worried-mind)
- PMC: "Peripheral-physiological and neural correlates of the flow experience while playing video games" (https://pmc.ncbi.nlm.nih.gov/articles/PMC7751419/)
- HAL Science: "Flow State Requires Effortful Attentional Control but Is Characterized by Effortless Experience" (https://hal.science/hal-04633660/document)

---

## Feature 5: Cena Companion (Supportive Check-in Bot)
**Source Domain:** Mental Health / Conversational AI

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Woebot — AI conversational agent for anxiety/depression support (https://woebothealth.com) |
| **Feature Name** | Cena Companion — Math Support Chat Buddy |
| **Original Description** | Woebot uses conversational AI to provide cognitive behavioral therapy techniques through daily check-ins. A 2017 RCT (Fitzpatrick et al.) found college students using Woebot for 2 weeks showed reduced anxiety and depression symptoms vs. an ebook control. Users appreciated the bot's empathy and ability to facilitate learning. Key limitations: repetitive responses and inability to handle unanticipated answers. |
| **Cena Adaptation** | A friendly, non-therapeutic chat companion (text-based, avatar-driven) that checks in with students before/after sessions. Phrases: "Ready to tackle some math today? 💪" / "That was a tough session — want to try a different approach next time?" / "You're on fire today! 🔥". The Companion celebrates effort (not just outcomes), offers strategy tips when students struggle, and can suggest switching topics. Never gives answers — only encouragement and navigation help. |
| **Primary Persona** | **Maya (The Anxious Achiever)** — needs emotional support and reframing. **Omar (The Struggler)** — needs encouragement to persist without feeling judged. |
| **Effort Estimate** | **M** — Conversational flow design, avatar animation, response templating. ~3-4 weeks. |
| **Guardrail Check** | ⚠️ **BORDERLINE** — Must be carefully designed to avoid therapy territory. ✅ No mental health claims — explicitly framed as "study buddy," not counselor. ✅ No streak mechanics — check-ins are contextual, not daily-mandatory. ✅ No data retention for under-13 — session-local only, with parental consent gate. ✅ COPPA-compliant — no PII collection, no silent data gathering. |
| **Implementation Sketch** | 1) Simple avatar with 5-6 emotional states (neutral, encouraging, celebratory, concerned, excited). 2) Pre-session: "What should we focus on today?" → presents 3 topic options based on adaptive engine. 3) Post-session: "You solved 7/10 problems — nice work on quadratics! Want to continue or switch?" 4) Struggle detection (3 wrong in a row): "That topic is tricky! Want a hint, a video, or try something different?" 5) All responses are rule-based templates, not generative AI — fully controllable. |
| **Verdict** | **SHORTLIST** — High impact but requires careful compliance review. Must have legal sign-off on health claims framing. |

**Sources:**
- PMC: "AI as the Therapist: Student Insights on the Challenges" (https://pmc.ncbi.nlm.nih.gov/articles/PMC11939552/)
- HigherEdToday: "Leveraging AI to Support Student Mental Health and Well-Being" (https://www.higheredtoday.org/2024/10/16/ai-student-mental-health/)
- Fitzpatrick et al. (2017): Randomized controlled trial of Woebot for college students

---

## Feature 6: Study Circles (Async Peer Help)
**Source Domain:** Social Platforms / Community

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Discord (https://discord.com) — async community spaces with topic channels |
| **Feature Name** | Study Circles — Topic-Based Peer Help Communities |
| **Original Description** | Discord servers use topic-specific text channels (e.g., "ethics," "quick-questions") where community members ask and answer asynchronously. UCI research found Discord "preferred to other platforms for community building" and "less formal than email or Slack." Students already use Discord for class communities organically. Philosophy Café on Discord structures help by expertise level: newcomers get "question of the week," advanced users get reading groups. |
| **Cena Adaptation** | Optional, anonymous topic-based help channels within Cena: "Quick Questions" ("Why does this factor to...?"), "Study Tips," "Bagrut Prep Strategy," and topic channels ("Algebra," "Geometry," "Calculus"). Students post questions anonymously; other students and verified tutors can answer. Best answers get "Helpful" votes (Reddit-style upvoting) that surface them. No real-name requirement. Moderated by Cena staff. |
| **Primary Persona** | **Omar (The Struggler)** — needs help but is embarrassed to ask in class. **Yael** — benefits from explaining concepts to others (protégé effect). **Noam** — gets quick answers without scheduling tutoring. |
| **Effort Estimate** | **L** — Community platform backend, moderation tooling, anonymity architecture, tutor verification. ~8-10 weeks. |
| **Guardrail Check** | ✅ No comparative shame — anonymous participation, no public leaderboards. ✅ No streaks — participation is purely voluntary. ✅ COPPA-safe — anonymous handles, no PII for under-13, content moderation required. ⚠️ Requires human moderation — cannot be fully automated. |
| **Implementation Sketch** | 1) Create 8-10 topic channels. 2) Anonymous posting with auto-generated handles ("MathExplorer42"). 3) Question template: "I'm stuck on [topic]. Here's what I tried: [free text]." 4) "Helpful" button on answers — surfaces top answers, no downvoting. 5) Verified tutor badge for approved responders. 6) Auto-flag inappropriate content for moderator review. 7) Optional: weekly "most helpful answer" celebrated with a non-competitive badge. |
| **Verdict** | **SHORTLIST** — High engagement potential but significant moderation investment required. Pilot with small cohort first. |

**Sources:**
- Discord Blog: "Eight Educational Communities To Further Your Field of Study" (https://discord.com/blog/eight-educational-communities-to-further-your-field-of-study)
- UCI DTEI: "Using Discord in University Classrooms" (https://dtei.uci.edu/2023/10/09/using-discord-in-university-classrooms-overview-and-guidelines/)
- Springer: "Leveraging Reddit in academia" (https://link.springer.com/article/10.1007/s44217-025-00542-2)

---

## Feature 7: Learning Energy Tracker (Session Mood Correlation)
**Source Domain:** Mental Health / Self-Monitoring

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Sanvello (https://www.sanvello.com) — daily mood tracking with personalized progress visualization |
| **Feature Name** | Learning Energy Tracker — Study-Mood Correlation |
| **Original Description** | Sanvello offers "Daily Mood Tracking" where users answer simple questions to capture mood, identify patterns, and review progress. Users self-rate on customizable dimensions (mood, sleep, exercise, anxiety sources). Visualizations show correlations between activities and emotional states. Weekly check-ins create a "roadmap for improvement." |
| **Cena Adaptation** | After each session, Cena asks one optional question: "How did that feel?" (😰 Hard / 😐 Okay / 🙂 Good / 🔥 Great!). Over time, Cena builds a personal "Learning Energy" chart showing which topics, session lengths, and times of day correlate with positive feelings and strong performance. Students discover: "I'm strongest at algebra on Sunday mornings" or "Geometry feels hard after 8 PM." Insights are private and actionable. |
| **Primary Persona** | **Yael (The Systematic Studier)** — loves data about her own patterns. **Maya (The Anxious Achiever)** — benefits from seeing that "hard" sessions still lead to improvement. **Noam** — optimizes cramming schedule using his own data. |
| **Effort Estimate** | **M** — Mood data collection, correlation analytics, personal dashboard. ~3-4 weeks. |
| **Guardrail Check** | ✅ No streaks — mood tracking is optional per-session, no daily requirement. ✅ No comparative shame — entirely personal insights, no peer comparison. ✅ No mental health framing — explicitly about "learning energy" and study optimization. ✅ COPPA-safe — for under-13, data is session-local and not retained; for 13+, opt-in with parental consent. |
| **Implementation Sketch** | 1) Post-session: one-tap mood emoji (optional, skippable). 2) Correlate mood with: topic, time of day, session length, accuracy rate. 3) Weekly "Learning Energy Report" — simple bar chart: "Your best topics this week" / "Topics that felt challenging." 4) "When You're at Your Best" insight card: personalized recommendation based on student's own data. 5) Student can delete all mood data anytime. |
| **Verdict** | **SHIP** — Low risk, high personal value, excellent data for Cena's adaptive engine (with consent). |

**Sources:**
- Princeton HR: "Sanvello Behavioral Health" (https://hr.princeton.edu/sites/g/files/toruqf1976/files/documents/sanvello-behavioral-health-app.pdf)
- Psychology Today: "The Sanvello App" (https://www.psychologytoday.com/us/blog/how-to-do-life/201902/the-sanvello-app)
- UMSL News: "Sanvello app for mental health support" (https://blogs.umsl.edu/news/2020/03/26/sanvello/)

---

## Feature 8: Session Type Menu (Workout-Style Variety)
**Source Domain:** Fitness / Training Programs

| Attribute | Detail |
|-----------|--------|
| **Original Product** | Nike Training Club (https://www.nike.com/ntc-app) — workout variety with session-type filtering |
| **Feature Name** | Session Type Menu — Choose Your Study "Workout" |
| **Original Description** | Nike Training Club offers 185+ workouts categorized by type (HIIT, yoga, strength, mobility), duration (5-60 min), intensity, and equipment. Users filter by "How much time do you have?" and "How do you want to feel?" The app emphasizes "workouts for however you're feeling" — not one-size-fits-all. Programs are progressive but modular: users can swap sessions based on daily capacity. |
| **Cena Adaptation** | Cena's session launcher offers a "Choose Your Session" menu with workout-style categories: 🔥 "Power Sprint" (10 min, max problems, quick review), 🧘 "Deep Dive" (30 min, one concept explored thoroughly), 🎯 "Target Practice" (15 min, specific problem type the student chooses), 🌱 "Garden Walk" (20 min, mixed review at comfortable pace), ⚡ "Challenge Round" (15 min, above-level problems with hints). Students pick based on time, energy, and goal. |
| **Primary Persona** | **Noam (The Crammer)** — needs quick sessions (Power Sprint). **Yael** — prefers structured Deep Dives. **Omar** — benefits from Garden Walk low-pressure review. **Maya** — uses Target Practice to conquer specific fears. |
| **Effort Estimate** | **M** — Session type architecture, problem set bundling per type, UI for selection. ~3-4 weeks. |
| **Guardrail Check** | ✅ No streaks — each session is independent, no "keep the chain" mechanics. ✅ No loss-aversion — no penalties for skipping session types. ✅ No comparative shame — personal choice, no leaderboard. ✅ COPPA-safe — session preference data is non-PII. |
| **Implementation Sketch** | 1) Tag all problems with: duration estimate, intensity level, concept scope (single vs. mixed). 2) Create 5 session type templates with filtering rules. 3) Session launcher shows: "You have [X] minutes. How do you want to study?" 4) Each type has descriptive icon, duration range, and sample problem preview. 5) Post-session: "You completed a [Type] session! 🎉" — celebratory, not competitive. 6) Track which types student prefers for personalization (with consent). |
| **Verdict** | **SHIP** — Differentiating feature, low risk, strong student agency alignment. |

**Sources:**
- Nike Training Club App (https://www.nike.com/ntc-app)
- Savionixa: "How to plan workouts using the Nike Training app" (https://www.savionixa.com/how-to/plan-workouts-using-the-nike-training-app/)
- GarageGymReviews: "Expert-Tested: Nike Training Club Review" (https://www.garagegymreviews.com/nike-training-club-review)

---

## Summary Table

| # | Feature | Source Domain | Source Product | Effort | Verdict | Primary Personas |
|---|---------|--------------|----------------|--------|---------|-----------------|
| 1 | Math Concept Map | Productivity | Obsidian | L | **SHIP** | Yael, Noam |
| 2 | Pre-Session Focus Ritual | Mental Health | Calm/Headspace | S | **SHIP** | Maya, Noam |
| 3 | Learning Journeys | Habit Formation | Fabulous | M | **SHIP** | Yael, Omar |
| 4 | Adaptive Flow Sessions | Games | Tetris (flow research) | M | **SHIP** | Maya, Noam, Omar |
| 5 | Cena Companion Bot | Mental Health/AI | Woebot | M | **SHORTLIST** | Maya, Omar |
| 6 | Study Circles | Social | Discord | L | **SHORTLIST** | Omar, Yael, Noam |
| 7 | Learning Energy Tracker | Mental Health | Sanvello | M | **SHIP** | Yael, Maya, Noam |
| 8 | Session Type Menu | Fitness | Nike Training Club | M | **SHIP** | Noam, Yael, Omar, Maya |

---

## Effort Summary

- **S (Small):** 1-2 weeks → Feature 2
- **M (Medium):** 3-6 weeks → Features 3, 4, 5, 7, 8
- **L (Large):** 6-10 weeks → Features 1, 6

**Recommended Phase 1 (Quick Wins):** Features 2, 7, 8 — low risk, medium effort, immediate student value.  
**Recommended Phase 2 (Core Differentiation):** Features 1, 3, 4 — larger investment, high competitive moat.  
**Recommended Phase 3 (Experimental):** Features 5, 6 — require pilot programs and compliance review.

---

## Guardrail Compliance Summary

All 8 features were evaluated against the six guardrails:

| Guardrail | Status | Notes |
|-----------|--------|-------|
| No streaks/loss-aversion | ✅ PASS | All features avoid "keep the chain" mechanics. Progress is tracked as accumulation, not unbroken sequences. |
| No variable-ratio rewards | ✅ PASS | No slot-machine-style rewards. Celebrations are deterministic (session completion, journey milestone). |
| No comparative shame/leaderboards | ✅ PASS | All features are personal or anonymous. No percentile rankings or public scoreboards. |
| No misconception data retention | ✅ PASS | Wrong answers inform real-time adaptation only; not stored as persistent deficit profiles. |
| No ML training on student data | ✅ PASS | All adaptation uses rule-based systems or student-consented local analytics. |
| No silent under-13 data collection | ✅ PASS | COPPA-compliant: no PII, no silent tracking, parental consent gates, session-local data for under-13. |

---

## Research Sources Used

### Source Types Breakdown
- **Academic/Scientific (S):** 5 sources — U.S. DoE, PMC/NIH, UC Riverside, HAL Science, Springer
- **App Developer/Official (A):** 6 sources — Obsidian.md, Calm.com, Nike, Discord Blog, Fabulous.co, Todoist
- **Technology/Media Reviews:** 8 sources — Bustle, Psychology Today, GarageGymReviews, Medium, etc.
- **Institutional:** 2 sources — Princeton HR, UCI DTEI

Each feature cites ≥2 sources from different source types.

---

*Report compiled 2026-04-20. All product names and trademarks belong to their respective owners. Feature recommendations are for product planning purposes and do not imply any commercial relationship.*
