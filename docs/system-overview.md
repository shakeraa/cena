# Cena - Personal Student Mentor System

## Overview

Cena is a personal mentor system designed for high-grade students. It serves as an intelligent companion that remembers, organizes, and builds upon each student's learning journey.

## Core Capabilities

### Memory & Knowledge Tracking
- Remembers learned concepts, ideas, thoughts, and skills
- Organizes knowledge into structured, interconnected maps
- Tracks progression and mastery levels per topic
- Identifies knowledge gaps and areas for reinforcement

### Personalized Mentoring
- Adapts to each student's learning style and pace
- Applies specific methodologies to enhance skills and knowledge
- Provides targeted exercises and challenges
- Offers contextual guidance based on the student's history

### Skill Enhancement
- Monitors skill development over time
- Suggests next steps based on current proficiency
- Connects new material to previously learned concepts
- Reinforces weak areas through spaced repetition and practice

## Target Users

- High-grade students seeking structured personal mentorship
- Students who want to organize and retain their learning effectively

## Methodology Approach

### Adaptive Methodology Selection
- The system selects the best methodology per student automatically
- Supported methods include: Socratic method, spaced repetition, project-based learning, Bloom's taxonomy progression, Feynman technique, worked examples with fading, analogy-based instruction, and retrieval practice
- The system profiles each student to determine which method is most effective for them
- Tracks per-student method effectiveness over time

### Stagnation Detection & Switching
- Monitors progress and detects when a student plateaus
- Automatically switches to a different methodology when stagnation is detected
- The switch happens **seamlessly** — the student is not made aware of the methodology change
- Stagnation detection uses a **composite score** from five signals:
  1. **Accuracy plateau**: Less than 5% improvement over the last 10 attempts on a concept cluster
  2. **Response time drift**: Rolling average response time increases by more than 20% compared to the student's baseline for similar-difficulty problems
  3. **Session abandonment**: Student ends sessions more than 30% earlier than their personal average session duration
  4. **Error type repetition**: The same error pattern (classified by error taxonomy) recurs 3 or more times across sessions
  5. **Annotation sentiment**: NLP analysis of student annotations detects frustration or confusion signals
- A methodology switch is triggered when the composite stagnation score exceeds a threshold of 0.7 (on a 0–1 normalized scale) for 3 consecutive sessions
- The switching strategy is error-type-driven:
  - Rule-based/procedural errors → switch to drill-and-practice or spaced repetition
  - Conceptual misunderstanding → switch to Socratic dialogue or Feynman technique
  - Motivational stagnation → switch to project-based learning or real-world application contexts

### Student Control
- Students can request a different learning approach at any time (e.g., "I'd rather learn this through a project")
- The system honors the request without exposing internal methodology labels
- Request mechanism: a persistent "Change approach" button in the learning session UI opens a natural-language picker with student-friendly labels (e.g., "Explain it differently", "Give me practice problems", "Show me a real-world example", "Let me explain it back")
- Each student-friendly label maps to an internal methodology: "Explain it differently" -> Feynman technique, "Give me practice problems" -> drill-and-practice, "Show me a real-world example" -> project-based learning, "Let me explain it back" -> retrieval practice
- Student-initiated switches are logged as `MethodologySwitched` events with trigger = `student_requested` and are weighted higher in the per-student methodology effectiveness profile

## Knowledge Storage

### Knowledge Graph
- Student knowledge is stored as a **knowledge graph**
- Concepts are nodes, relationships are edges (prerequisite, builds-on, related-to)
- Each node tracks: mastery level, date learned, review history, method effectiveness
- The graph powers gap detection, learning path suggestions, and concept connections

### Visualization
- Students see an interactive visual map of their knowledge graph
- Node colors/sizes reflect mastery level (mastered, in progress, weak)
- Edges show how concepts relate to each other
- Clusters group related topics naturally
- Graph grows visibly over time as the student learns — serves as motivation

## Subjects & Scope

- **Initial focus: STEM** (Science, Technology, Engineering, Mathematics)
- Syllabus-based — aligned to high-grade curriculum standards
- **Depth levels** — each topic has multiple levels of depth, allowing students to go from surface understanding to deep mastery
- Syllabus will be provided to the system as structured input

## Interaction & Platform

### Multi-Level Interaction
- Interaction is not a single mode — the system dynamically presents what's relevant
- **Interactive sessions** — guided mentoring conversations
- **Concept visualization** — knowledge graph, concept maps, relationships shown in context
- **Thought tracking** — surfaces the student's own recorded thoughts and ideas
- **Data presentation** — progress metrics, mastery levels, learning patterns
- The UI is a **living workspace** that adapts based on what the student is doing

### Student Annotations
- Students can annotate concepts — add their own thoughts, notes, and ideas
- Annotations are captured by the system as input to understand the student's thinking
- The knowledge graph is updated based on annotations (new connections, thoughts, insights)

### Platforms
- **Mobile app** (primary platform, mobile-first): React Native for iOS and Android from a single codebase — developed using AI coding agents (Claude Code, Kimi) for 3–5× development velocity
- **Web app** (secondary platform): React-based progressive web app (PWA) sharing component library with mobile — AI agents generate shared component library across both platforms
- Consistent experience across both platforms via shared design system and API layer
- Offline support: core learning sessions and knowledge graph browsable offline, synced when connectivity returns
- Push notifications for streak reminders, session nudges, and spaced repetition review alerts
- Minimum supported versions: iOS 15+, Android 10+, modern evergreen browsers (Chrome, Safari, Firefox, Edge)

### Performance SLAs

| Operation | P50 | P95 | P99 | Notes |
|---|---|---|---|---|
| Question generation (Socratic) | < 1.5s | < 3s | < 5s | Includes LLM call via Sonnet; user sees typing indicator |
| Answer evaluation | < 800ms | < 2s | < 4s | BKT update + LLM grading; optimistic UI shows "checking..." |
| Knowledge graph render (initial) | < 500ms | < 1.2s | < 2s | Client-side rendering of pre-fetched graph data |
| Knowledge graph update (after mastery) | < 200ms | < 500ms | < 1s | SignalR push + client-side animation |
| Page load (cold start) | < 2s | < 4s | < 6s | Service worker caches shell; data fetched in parallel |
| Diagnostic quiz question selection | < 100ms | < 300ms | < 500ms | KST posterior update is O(n) on concept count |
| Offline sync (50 events) | < 2s | < 5s | < 10s | Bulk replay; larger batches processed async via SignalR push |

**Measurement**: All SLAs are measured end-to-end from client perspective using OpenTelemetry distributed tracing (see `docs/operations.md` Section 3). P99 violations trigger on-call alerts when sustained for > 5 minutes.

## Target Audience

### Syllabus-Agnostic Design
- The system is designed to support **any country's syllabus** as structured input
- Syllabus defines the curriculum tree, subjects, and depth levels for that country
- The architecture does not hardcode any specific curriculum

### Initial Target: Bagrut (Israeli Matriculation)
- First deployment targets students preparing for **Bagrut** exams
- Syllabus follows the Israeli Ministry of Education curriculum
- STEM subjects: Mathematics, Physics, Chemistry, Biology, Computer Science
- Depth levels align with the Bagrut study units system (3/4/5 units per subject)

## Visual Design

### Graphic Style
- **Flat illustration style** for concept diagrams — clean, clear layouts with arrows and graphic elements showing processes and relationships (similar to FigureLabs scientific figures)
- **Colorful icon cards** for topic navigation — each concept/topic gets a distinct colored card with a simple illustrative icon and label (similar to smartyme_physics grid style)
- **Tech-inspired visuals** for the knowledge graph and brand — glowing network nodes, interconnected data points, dark backgrounds with vibrant highlights (similar to Technion/HUJI AI course visuals)
- White/clean backgrounds for learning content; dark/immersive backgrounds for the knowledge graph visualization

### Dynamic Diagram Generation
- The system must be capable of **generating or serving concept diagrams on-the-fly** for any topic in the syllabus
- **Concept cards** — labeled illustrations paired with core formulas/equations (e.g., aerodynamics wing diagram + L = 1/2pv^2SC_L)
- **Process flow diagrams** — step-by-step visual flows with arrows showing how things work (chemical reactions, biological processes, circuit flows)
- **Icon-based topic grids** — color-coded navigational cards per topic area, each with a distinct icon
- Diagrams are not static assets — they are generated/composed per concept so the system can scale across any syllabus without manually creating thousands of images
- **Implementation approach**: Hybrid system — a templated SVG engine (using D3.js and React components) composes diagrams from a curated asset library of ~200 base shapes and symbols per subject, combined with LLM-generated layout instructions (Kimi K2.5 outputs structured JSON describing element placement, labels, and connections). For concepts where templates are insufficient, the LLM generates raw SVG markup validated by a deterministic sanitizer before rendering. This hybrid avoids the quality risk of pure AI generation while scaling beyond a fixed asset library

### Design Principles
- Visuals should make complex concepts feel approachable and clear — measured by A/B testing diagram comprehension rates (target: >80% of students correctly interpret a diagram's key relationship on first exposure)
- Every diagram, figure, and concept card follows a single illustrative style guide: flat vector style, 4-color palette per subject (e.g., Math = blue/teal, Physics = orange/amber, Chemistry = green/emerald, Biology = purple/violet, CS = gray/slate), consistent stroke width (2px), rounded corners (8px radius), and Hebrew-first text with LTR fallback for formulas
- The overall aesthetic targets the visual language of apps that Israeli teens already use (Duolingo, Instagram, TikTok) — high contrast, generous whitespace, micro-animations on state transitions, dark mode support from launch

## Gamification

- Learning progress is gamified to keep students motivated and engaged
- Elements include: XP/points for completing concepts, streaks for daily engagement, badges/achievements for milestones
- Leveling system tied to mastery depth — unlocking deeper levels feels like progression
- Leaderboards (optional) for friendly competition
- The knowledge graph itself serves as a visual reward — watching it grow is inherently motivating

## Cognitive Load Management

### Quantum Learning (Small Units of Information)
- Content is delivered in **small, digestible quants** — bite-sized pieces that prevent cognitive overload
- Each learning session is calibrated to avoid exhausting the student
- The system learns each student's **personal thresholds** — how much new information they can absorb before fatigue

### Threshold Detection
- Monitors engagement signals (response time, accuracy drop-off, session duration patterns)
- Builds a per-student cognitive load profile over time
- Adjusts session length and content density dynamically based on the student's current state
- **Cognitive load threshold formula**: The system computes a real-time fatigue score per session:

  ```text
  fatigue_score = w1 * accuracy_drop + w2 * rt_increase + w3 * time_fraction
  ```

  Where:
  - `accuracy_drop` = max(0, baseline_accuracy - rolling_accuracy_last_5) / baseline_accuracy, normalized to [0, 1]
  - `rt_increase` = max(0, rolling_rt_last_5 - baseline_rt) / baseline_rt, normalized to [0, 1]
  - `time_fraction` = elapsed_minutes / student_max_session_minutes (from per-student profile, default 25 min, range 12–30 min)
  - Default weights: w1=0.4, w2=0.3, w3=0.3
  - **Session end trigger**: fatigue_score > 0.7 for 2 consecutive questions → system suggests a break and emits `CognitiveLoadCooldownComplete` after a configurable cooldown (default: 15 min)
  - See `docs/engagement-signals-research.md` for signal calibration research

### Estimated Timelines
- The system provides **personalized time estimates** for achieving specific goals (e.g., "Master 5-unit Math by Bagrut exam date")
- Timelines factor in: current knowledge level, learning pace, available study time, and historical performance
- Estimates update dynamically as the student progresses

## AI/LLM Integration Strategy

### Model Architecture
- **Tiered multi-model routing** behind an Anti-Corruption Layer (see `docs/llm-routing-strategy.md` for full analysis):
  - **Fast/Cheap tier**: Kimi K2.5 (Moonshot AI) for classification, extraction, structured evaluation — $0.45/MTok input, no PII sent
  - **Balanced tier**: Claude Sonnet 4.6 (Anthropic) for real-time tutoring, Socratic dialogue, explanation generation — $3.00/MTok input
  - **Reasoning tier**: Claude Opus 4.6 (Anthropic) for methodology switching decisions, complex pedagogical reasoning — $5.00/MTok input
- **Embedding model**: text-embedding-3-small (OpenAI) for knowledge graph semantic search and concept similarity scoring (see `docs/intelligence-layer.md`)
- The system is designed with a **model-agnostic abstraction layer** — all LLM calls go through a unified interface (Python FastAPI Anti-Corruption Layer) that handles prompt formatting, token tracking, model routing, and provider fallback

### LLM Use Cases
1. **Socratic tutoring conversations**: Multi-turn dialogue with per-student context window (learning history, current mastery, active methodology)
2. **Knowledge graph construction**: LLM extracts concept nodes and prerequisite edges from syllabus documents and textbooks (batch processing, not real-time)
3. **Stagnation analysis**: LLM classifies error types from student responses (procedural vs. conceptual vs. motivational) to inform methodology switching
4. **Annotation understanding**: NLP analysis of student-written notes to detect confusion, insight, or frustration signals
5. **Dynamic diagram generation**: LLM generates SVG diagram descriptions from concept definitions, rendered by a deterministic SVG engine
6. **Personalized explanation generation**: Adapts explanation depth and analogy selection based on student's knowledge graph state

### Cost Management
- **Per-student monthly LLM cost**: ~$13.32/month with tiered routing (~$10.26 Sonnet + $2.40 Opus + $0.66 Kimi), approximately 40% cheaper than a Sonnet-only approach ($22.05). See `docs/llm-routing-strategy.md` Section 4 for full breakdown.
- **Token optimization**: Prompt caching (60% cache hit target on tutoring context reduces Sonnet costs by 30-40%); Kimi K2.5 for high-frequency classification tasks at 6.7x lower cost; batch API for async tasks at 50% discount
- **Batch vs. real-time**: Knowledge graph construction and content pre-generation run as offline batch jobs; only tutoring conversations and annotation analysis are real-time
- **Rate limiting**: Students limited to 50 LLM-powered interactions per day (sufficient for 2–3 learning sessions); prevents abuse while keeping costs predictable

### Prompt Engineering
- **System prompts per methodology**: Each teaching method (Socratic, Feynman, spaced repetition) has a dedicated system prompt that shapes the LLM's behavior
- **Student context injection**: Each request includes a compressed summary of the student's knowledge state (mastered concepts, current gaps, recent errors, active methodology)
- **Safety rails**: Content filtered to stay within curriculum scope; LLM cannot provide direct answers during Socratic mode; age-appropriate language enforcement

## Security & Privacy

### Data Protection
- **Student data classification**: All student data (learning history, annotations, knowledge graph state, engagement metrics) classified as **PII** and treated accordingly
- **Encryption**: AES-256 at rest for all databases; TLS 1.3 in transit for all API communication; field-level encryption for sensitive student identifiers
- **Data residency**: Primary storage in AWS eu-west-1 (Ireland) or il-central-1 (Tel Aviv) to comply with Israeli Privacy Protection Law (5741-1981)
- **Data retention**: Active student data retained for duration of subscription + 12 months; anonymized aggregate data retained indefinitely for model improvement; full deletion within 30 days of request

### Regulatory Compliance
- **Israeli Privacy Protection Law (5741-1981)**: Registration with the Israeli Law, Information and Technology Authority (ILITA) database registry; compliance with data processing requirements
- **GDPR readiness**: Architecture designed for GDPR compliance from day one (EU expansion planned within 18 months) — data portability, right to erasure, consent management, DPO appointment
- **COPPA considerations**: While primary target is ages 16-18 (Bagrut), system architecture supports parental consent flows for any future expansion to younger students (<16 under Israeli law, <13 under COPPA)
- **Ministry of Education**: No formal approval required for supplementary ed-tech tools in Israel, but content alignment with official Bagrut syllabi will be independently verified by licensed teachers

### Authentication & Access Control
- **Student authentication**: Email/password with bcrypt hashing + optional social login (Google); mandatory email verification
- **MFA**: Optional TOTP-based MFA for students; mandatory for admin and teacher accounts
- **Session management**: JWT tokens with 1-hour expiry, refresh tokens with 30-day expiry, device-based session tracking with anomaly detection
- **Role-based access**: Four roles — student, parent (read-only view of child's progress), teacher (class dashboard), admin (system management)

### LLM-Specific Security
- **No student PII in LLM prompts**: Student context passed to LLMs uses anonymized identifiers; real names, emails, and school names are never included in API calls to third-party model providers
- **Prompt injection protection**: Input sanitization layer between student text input and LLM prompts; output validation ensures responses stay within educational context
- **Audit logging**: All LLM interactions logged (prompt hash + response hash, not full content) for abuse detection and cost monitoring

## Onboarding Flow Specification

### Design Principle
- The first 5 minutes must deliver immediate value — the student sees their knowledge graph populated and growing before being asked to pay or commit
- Total onboarding target: under 4 minutes to first "aha moment" (seeing their personal knowledge map for the first time)

### Step-by-Step Flow

**Step 1: Signup (30 seconds)**
- Email + password or Google social login
- Name, grade level (11th/12th), and primary Bagrut subjects (multi-select from: Mathematics, Physics, Chemistry, Biology, Computer Science)
- No school name required (reduces friction); optional field for cohort features later

**Step 2: Diagnostic Assessment (2–3 minutes)**
- Adaptive 10–15 question diagnostic quiz on the selected primary subject
- Questions span Bloom's taxonomy levels (recall → application → analysis) to map depth, not just breadth
- Uses the ALEKS-inspired Knowledge Space approach: each answer eliminates a cluster of concepts from "unknown" status
- UI: clean, one-question-at-a-time, progress bar showing "Building your knowledge map..."
- Student can skip questions they don't understand (skip = signal of gap, not penalized)

**Step 3: Knowledge Graph Reveal (30 seconds)**
- Animated reveal of the student's personal knowledge graph based on diagnostic results
- Mastered concepts glow green, gaps shown as dim/gray nodes, connections animate in
- Key moment: student sees the visual scope of what they know vs. what they need to learn
- Call to action: "Let's light up your next node" → starts first micro-lesson

**Step 4: First Micro-Lesson (2–3 minutes)**
- System selects the highest-impact concept adjacent to an existing mastered node (minimizes cognitive load, maximizes perceived progress)
- Lesson uses the methodology best suited to the student's diagnostic error patterns (default: Socratic for conceptual, spaced repetition for factual)
- Lesson completes with a mastery check (2–3 questions)
- On success: knowledge graph animates — new node lights up green, edge connects to existing knowledge
- This is the "aha moment": the student visually sees their graph grow from their own effort

**Step 5: Session Summary & Hook (30 seconds)**
- "You mastered [concept name] and connected it to [existing concept]. Your graph grew by 1 node."
- Streak initialized: "Day 1 — come back tomorrow to keep your streak alive"
- Free tier limit explained: "You have 2 more concepts today, or unlock unlimited with Premium"
- Push notification permission request (framed as "streak reminders")

### Onboarding Success Criteria
- **Completion rate target**: >75% of signups complete through Step 4 (first micro-lesson)
- **Time to value**: <5 minutes from signup to first knowledge graph node earned
- **Day 1 → Day 2 return rate**: >50% (driven by streak initialization and push notification opt-in)
- **Diagnostic skip rate**: <20% of questions skipped (if higher, diagnostic is too hard or too long)
