# Cena - Personal Student Mentor System

## Overview

Cena is a personal mentor system designed for high-grade students. It serves as an intelligent companion that remembers, organizes, and builds upon each student's learning journey. Unlike existing EdTech tools, Cena combines a visible knowledge graph, adaptive teaching methodology, cognitive load personalization, and curriculum-agnostic architecture into a single mentor experience.

---

## 1. Core Capabilities

### 1.1 Memory & Knowledge Tracking
- Remembers learned concepts, ideas, thoughts, and skills
- Organizes knowledge into structured, interconnected maps (knowledge graph)
- Tracks progression and mastery levels per topic
- Identifies knowledge gaps and areas for reinforcement
- Captures student annotations, thoughts, and insights as graph input

### 1.2 Personalized Mentoring
- Adapts to each student's learning style and pace
- Applies specific methodologies to enhance skills and knowledge
- Provides targeted exercises, challenges, and scenarios
- Offers contextual guidance based on the student's full history
- Acts as a persistent mentor relationship — not just a tool

### 1.3 Skill Enhancement
- Monitors skill development over time
- Suggests next steps based on current proficiency
- Connects new material to previously learned concepts
- Reinforces weak areas through spaced repetition and practice
- **Skill proficiency meters** — at-a-glance mastery percentages per domain (e.g., Systems Thinking 89.9%, Mechanics 91.4%)

---

## 2. Target Audience

### 2.1 Syllabus-Agnostic Design
- The system supports **any country's syllabus** as structured input
- Syllabus defines the curriculum tree, subjects, and depth levels for that country
- The architecture does not hardcode any specific curriculum

### 2.2 Initial Target: Bagrut (Israeli Matriculation)
- First deployment targets students preparing for **Bagrut** exams
- Syllabus follows the Israeli Ministry of Education curriculum
- STEM subjects: Mathematics, Physics, Chemistry, Biology, Computer Science
- Depth levels align with the Bagrut study units system (3/4/5 units per subject)

### 2.3 Expansion Path
- After Israel: AP exams (US), A-Levels (UK), JEE/NEET (India), Gaokao (China)
- Each market is massive and has expensive private tutoring alternatives

---

## 3. Methodology Approach

### 3.1 Adaptive Methodology Selection
- The system selects the best methodology per student **automatically**
- Supported methods include:
  - **Socratic method** — guiding through questions
  - **Spaced repetition** — revisiting at optimal intervals
  - **Project-based learning** — applying knowledge through real projects
  - **Bloom's taxonomy progression** — recall -> understanding -> application -> analysis -> evaluation -> creation
  - **Feynman technique** — explain in simple terms to reveal gaps
  - **Challenge-based / scenario learning** — system-based challenges and real-world problem scenarios
- The system profiles each student to determine which method is most effective
- Tracks per-student method effectiveness over time

### 3.2 Stagnation Detection & Switching
- Monitors progress and detects when a student plateaus
- Automatically switches to a different methodology when stagnation is detected
- The switch happens **seamlessly** — the student is not made aware of the methodology change
- **Composite stagnation score** based on:
  1. Accuracy plateau (<5% improvement over last N attempts)
  2. Response time drift (increasing average)
  3. Session abandonment (ending earlier than personal baseline)
  4. Error type repetition (same error pattern recurring)
  5. Annotation sentiment (frustration or confusion signals)
- **Multi-modal error analysis** drives method selection:
  - Rule-based errors -> drill/practice
  - Conceptual misunderstanding -> Socratic dialogue
  - Motivational stagnation -> project-based or Feynman technique
- Logs methodology at switch time to build per-student effectiveness profile

### 3.3 Student Control
- Students can request a different learning approach at any time (e.g., "I'd rather learn this through a project")
- The system honors the request without exposing internal methodology labels

---

## 4. Knowledge Storage & Visualization

### 4.1 Knowledge Graph
- Student knowledge is stored as a **knowledge graph**
- Concepts are nodes, relationships are edges
- **Edge types**: prerequisite, builds-on, related-to, conflicts-with (for common misconceptions)
- Each node tracks: mastery level, date learned, review history, method effectiveness
- **Two-layer architecture**:
  - **Domain graph** (shared) — what exists to learn in the syllabus
  - **Student overlay** (personal) — what this student knows, their mastery, their annotations
- **Temporal tracking**: when each node was learned, last reviewed, mastery trajectory over time
- Enables spaced repetition scheduling and knowledge decay prediction
- **Construction**: LLM-assisted extraction from syllabi and textbooks + human expert validation (scalable alternative to Squirrel AI's fully manual 30K points per subject)

### 4.2 Interactive Visualization
- Students see an **interactive visual map** of their knowledge graph
- Node colors/sizes reflect mastery level (mastered, in progress, weak)
- Edges show how concepts relate to each other
- Clusters group related topics naturally
- Graph **grows visibly** over time as the student learns — serves as motivation and reward
- The graph is **beautiful, shareable** — students screenshot and share (viral acquisition mechanic)
- Research confirms: visible knowledge states improve self-regulated learning, goal setting, and help-seeking behavior
- Honest mastery-level coloring serves as a reality check (students tend to overestimate their knowledge)

### 4.3 Skill Proficiency Meters
- Quick at-a-glance **mastery percentages per domain** alongside the knowledge graph
- E.g., "Systems Thinking 89.9% | Mechanics 91.4% | Logic & Circuits 37.5%"
- Provides a simpler view for quick progress checks without diving into the full graph

---

## 5. Interaction & Platform

### 5.1 Multi-Level Interaction
- Interaction is not a single mode — the system dynamically presents what's relevant
- **Interactive sessions** — guided mentoring conversations
- **Concept visualization** — knowledge graph, concept maps, relationships shown in context
- **Thought tracking** — surfaces the student's own recorded thoughts and ideas
- **Data presentation** — progress metrics, mastery levels, learning patterns
- **Challenge-based scenarios** — real-world problem-solving exercises
- The UI is a **living workspace** that adapts based on what the student is doing

### 5.2 Student Annotations
- Students can annotate concepts — add their own thoughts, notes, and ideas
- Annotations are captured by the system as input to understand the student's thinking
- The knowledge graph is updated based on annotations (new connections, thoughts, insights)

### 5.3 Platforms
- **Web app** and **mobile app** (mobile-first approach)
- Consistent experience across both platforms
- Mobile positioning: **"Replace scrolling with learning"** — learning as a replacement for doomscrolling, not an addition to the schedule

---

## 6. Subjects & Scope

- **Initial focus: STEM** (Science, Technology, Engineering, Mathematics)
- Syllabus-based — aligned to high-grade curriculum standards
- **Depth levels** — each topic has multiple levels of depth, allowing students to go from surface understanding to deep mastery
- Syllabus will be provided to the system as structured input

---

## 7. Visual Design

### 7.1 Graphic Style
- **Flat illustration style** for concept diagrams — clean, clear layouts with arrows and graphic elements showing processes and relationships (FigureLabs reference)
- **Concept cards with formulas** — labeled illustrations paired with core equations, each concept gets a visual + its key formula (SmartyMe reference: aerodynamics wing + L=1/2pv^2SC_L)
- **Colorful icon cards** for topic navigation — each concept/topic gets a distinct colored card with a simple illustrative icon and label (SmartyMe grid reference)
- **Tech-inspired visuals** for the knowledge graph and brand — glowing network nodes, interconnected data points, dark backgrounds with vibrant highlights (Technion/HUJI reference)
- **Skill proficiency meters** — clean icon + percentage display per domain (Nibble reference)
- White/clean backgrounds for learning content; dark/immersive backgrounds for the knowledge graph visualization

### 7.2 Dynamic Diagram Generation
- The system must be capable of **generating or serving concept diagrams on-the-fly** for any topic in the syllabus
- **Concept cards** — labeled illustrations paired with core formulas/equations
- **Process flow diagrams** — step-by-step visual flows with arrows showing how things work (chemical reactions, biological processes, circuit flows)
- **Icon-based topic grids** — color-coded navigational cards per topic area, each with a distinct icon
- Diagrams are not static assets — they are generated/composed per concept so the system can scale across any syllabus without manually creating thousands of images
- Potential approaches: AI-generated SVGs, templated illustration engine, or a hybrid with a curated asset library + dynamic composition

### 7.3 Design Principles
- Visuals should make complex concepts feel **approachable and clear**
- Every diagram, figure, and concept should have a **consistent illustrative style**
- The overall aesthetic should feel **modern, tech-forward, and engaging** for students
- Concept diagrams should be **self-contained** — a single card should explain a concept visually without needing external context

### 7.4 Visual References

| Reference | Style | What to Adopt |
|-----------|-------|---------------|
| **FigureLabs** | Flat scientific illustration, clean white background, process flows with arrows | Concept diagram style for processes and relationships |
| **SmartyMe (Physics/Engineering)** | Colorful icon grids, concept cards with formulas, "addictive game" framing | Topic navigation grids, concept card format with illustration + equation |
| **Technion/HUJI ads** | Glowing network nodes, dark backgrounds, tech-inspired data visuals | Knowledge graph visualization aesthetic, brand imagery |
| **Nibble** | Skill proficiency meters with icons, "Replace Scrolling" positioning, challenge-based | Domain mastery meters, mobile positioning, scenario-based challenges |

---

## 8. Gamification

### 8.1 Core Elements
- **XP/points** for completing concepts
- **Streaks** for daily engagement (single most powerful retention mechanic — 7-day streak = 3.6x retention)
- **Badges/achievements** for milestones — representing intellectual achievement ("Mastered Integration," "Connected 50 Concepts") not just activity
- **Leveling system** tied to mastery depth — unlocking deeper levels feels like progression
- **Leaderboards** (optional) — leagues with promotion/demotion, not global rankings
- **Knowledge graph growth** as visual reward — watching it expand is inherently motivating

### 8.2 "Stealth Gamification" Approach
- Embed rewards into the learning process naturally, not bolted on
- The growing knowledge graph is the primary reward — lean into this heavily
- **Adaptive gamification intensity**: more game elements for struggling/unmotivated students; less for intrinsically motivated ones
- **Loss aversion > reward seeking**: streak mechanics and "protect your progress" framing outperform pure rewards
- **Social proof without toxic competition**: "X students mastered this concept this week" rather than rank-ordered leaderboards

### 8.3 Risks to Manage
- Extrinsic rewards can harm intrinsically motivated students — adapt intensity per student
- Novelty effect decay — gamification must evolve (new badge categories, seasonal events, changing challenges)
- Leaderboard anxiety — keep leaderboards opt-in
- Dependency on instant gratification — gradually shift emphasis from extrinsic to intrinsic rewards as mastery deepens

---

## 9. Cognitive Load Management

### 9.1 Quantum Learning (Small Units of Information)
- Content is delivered in **small, digestible quants** — bite-sized pieces that prevent cognitive overload
- **Microlearning sweet spot: 5-10 minutes** per focused learning unit
- Each learning session is calibrated to avoid exhausting the student
- Learning quanta must be genuinely atomic — don't require holding too many new ideas simultaneously
- **"5-minute lessons that actually stick"** — short enough to fit into any schedule (Nibble reference)

### 9.2 Threshold Detection
- The system learns each student's **personal thresholds** — how much new information they can absorb before fatigue
- **Fatigue detection metrics** (behavioral proxies):
  1. Response time increase (rolling average drift)
  2. Accuracy drop-off within a session
  3. Interaction pattern changes (less scrolling, shorter answers, more re-reads)
  4. Session duration patterns vs personal baseline
  5. Time-of-day effects (circadian rhythm performance variation)
- Builds a per-student cognitive load profile over time
- Cap sessions at **20-25 minutes** before suggesting a break (adjustable per student)
- Some students handle 30-min sessions; others fatigue at 12 minutes

### 9.3 Fatigue Response
- Don't hard-stop — offer lighter **"cooldown" activities** (review mastered content, explore knowledge graph, read own annotations)
- The system caring about the student's wellbeing ("the app told me to take a break") builds trust and differentiates from platforms that optimize for time-on-app

### 9.4 Estimated Timelines
- The system provides **personalized time estimates** for achieving specific goals (e.g., "Master 5-unit Math by Bagrut exam date")
- Timelines factor in: current knowledge level, learning pace, available study time, and historical performance
- Estimates update dynamically as the student progresses
- Connects daily effort to long-term goals: "You're 67% of the way to mastering 5-unit Math"

---

## 10. Key Takeaways & Decisions

| Decision | Detail |
|----------|--------|
| **Target** | Bagrut students (Israeli matriculation), syllabus-agnostic architecture |
| **Subjects** | STEM initially, depth levels aligned to study units |
| **Knowledge model** | Two-layer knowledge graph (domain + student overlay) with temporal tracking |
| **Visualization** | Interactive graph + skill proficiency meters, shareable |
| **Methodology** | Adaptive per-student, seamless switching on stagnation via composite score |
| **Student awareness** | Student is NOT aware of methodology switches; CAN request different approach |
| **Gamification** | Stealth gamification, adaptive intensity, knowledge graph growth as primary reward |
| **Cognitive load** | 5-10 min learning units, 20-25 min session cap, per-student fatigue profiles |
| **Timelines** | Personalized goal estimates, dynamically updated |
| **Platforms** | Web + mobile (mobile-first), "replace scrolling with learning" |
| **Visual style** | Flat illustrations + formula cards + icon grids + tech-inspired graph |
| **Diagram generation** | Dynamic/on-the-fly, not static assets, scalable across syllabi |
| **Annotations** | Students annotate freely; system captures and updates knowledge graph |
| **Interaction** | Living workspace — sessions, visualization, thought tracking, challenges |
