# CENA Competitive UX Analysis — Example Image Insights

> **Date:** 2026-03-31
> **Source:** 36 screenshot references from `docs/example-images/`
> **Scope:** Extract UI/UX patterns from competitor apps and map to CENA mobile implementation

---

## 1. Sources Identified

| Source | Count | Category | What They Do |
|--------|-------|----------|-------------|
| **SmartyMe** | ~18 | EdTech gamification | Physics/Math/Engineering "addictive game" cards — topic grids, formula cards with real-world photos, tiered difficulty progression |
| **FigureLabs** | ~12 | AI diagram generation | AI-generated scientific diagrams — labeled lab setups, bio/chem process flows, editable SVG with text replacement |
| **GPAI** | 1 | AI diagram quality | Text-to-technical diagram — physics inclined-plane schematic, quality comparison (ChatGPT vs GPAI). Note: screenshot labels "ChatGPT 5.2" — version unverified, likely marketing claim |
| **Nibble/bb** | 1 | Micro-learning | Engineering micro-lessons — video-based with bridge/circuit diagrams |
| **erkanhoca_mat** | 1 | Social math | Turkish math qualifying test — integral calculus MCQ with LaTeX, social engagement via comments |
| **UI reference** | 1 | Design reference | Neuromorphic login form — glassmorphism + neumorphism design inspiration |
| **AI Medical** | 1 | Dashboard UI | Arabic AI medical ad — futuristic dark-theme dashboard |

---

## 2. Key UX Patterns Extracted

### 2.1 SmartyMe's "Addictive" Card Grid

**Pattern:** Topic selection grid where each card shows:
- Topic name (e.g. "Electrical Engineering", "Series Circuit")
- Core formula rendered large (V = IR, F = kx, sigma = F/A)
- Real-world photo showing the concept physically
- Brief 1-2 line explanation
- Tiered progression — cards unlock sequentially (DIA 1 through DIA 14)

**Visual layout:**
```
+------------------------+
|  TOPIC NAME            |
|  ---                   |
|  FORMULA (hero size)   |
|  Brief explanation     |
|  [Real-world photo]    |
|  ---                   |
|  "Learn how..."        |
+------------------------+
```

**Hook messaging:** Every ad uses "ADDICTED" in red. Their positioning:
- "Replace doomscrolling with engineering micro-lessons"
- "Just 10 minutes a day to train your logic, curiosity, and..."
- Sequential card unlocking creates progression dopamine

**Subject coverage observed:**
- Calculus (limits, derivatives, integrals, multivariable)
- Electricity (Ohm's law, circuits, resistors, capacitors, logic gates)
- Engineering (structural, mechanical, civil, control systems)
- Quantum physics (superposition, entanglement, tunneling, qubits)
- Software engineering (architecture, testing, design patterns)
- Microcontrollers (signals, embedded systems, FPGA)

### 2.2 FigureLabs' Labeled Scientific Diagrams

**Pattern:** AI-generated vector diagrams with:
- Labeled components — callout labels pointing to each element
- Flow arrows showing process direction (left-to-right for chemistry setups)
- AI text replacement — tap a label, edit it, regenerate ("1-Click AI Text Replacement")
- Multi-panel layouts — upper/lower or left/right comparative diagrams
- Bio process flows — cell biology pathways, molecular mechanisms with numbered steps

**Diagram types observed:**
- Laboratory equipment setups (beakers, pumps, pressure gauges, data loggers)
- Chemical reaction devices (manifold systems, rotary evaporators)
- Biomedical process flows (complement inhibition pathways, immunology)
- Cell biology (animal cells, dendrite cells with organelle labels)
- Molecular mechanisms (ZIF clusters, MOF frameworks)

**Key differentiator:** "Hours of drawing, Seconds of AI generation, Minutes of PPT tweaking" — positions AI diagrams as a time-saver for academic publishing.

### 2.3 GPAI Text-to-Technical Diagram

**Pattern:** Side-by-side quality comparison showing the same physics problem:
- ChatGPT 5.2: Basic hand-drawn-style sketch
- GPAI: Publication-ready 3D-rendered diagram with proper force vectors, angles, labels

**Tagline:** "Text to Technical Diagram — Built for Accuracy, Built for STEM"

### 2.4 Social Math Questions (erkanhoca_mat)

**Pattern:** Math integral/calculus MCQ posted directly on Instagram:
- Clean LaTeX-rendered math (integrals, fractions)
- Multiple choice answers (A through E)
- Students comment their answers — 36 comments = organic engagement
- No app required — the social platform IS the learning surface

### 2.5 Neuromorphic UI Reference

**Pattern:** Login form with glassmorphism + soft shadows:
- Frosted glass card on teal gradient background
- Soft neumorphic input fields
- Smooth slide animation between Sign In / Sign Up panels

---

## 3. Mapping to CENA Architecture

### 3.1 What CENA Already Has

| Competitor Feature | CENA Implementation | Status |
|---|---|---|
| Formula rendering (SmartyMe) | `MathText` widget with `$...$` LaTeX | Built |
| Interactive diagrams (FigureLabs) | `InteractiveDiagramViewer` with hotspots, zoom/pan | Built |
| Drag-and-drop labels (SmartyMe circuits) | `DragLabelDiagram` widget | Built |
| Graph insights (SmartyMe calculus) | `GraphInsightViewer` with crosshair + tooltip | Built |
| Challenge cards (SmartyMe game cards) | `ChallengeCardWidget` with tier glow, 5 answer types | Built |
| AI diagram generation pipeline | Kimi K2.5 batch pipeline, `ConceptDiagram` model | Model defined |
| Glassmorphism UI (neuromorphic ref) | `GlassCard`, `GlassChip`, `GlassProgressRing` | Built |
| Gamification (XP, streaks, badges) | Full gamification system with celebration tiers | Built |
| Subject color palettes | `SubjectDiagramPalette` + `SubjectColorTokens` | Built |
| Diagram quality gate | `DiagramReviewStatus` (pending/approved/rejected/autoApproved) | Model defined |

### 3.2 Gaps Identified

| Gap | Competitor Reference | Impact | CENA Status |
|-----|---------------------|--------|-------------|
| **Browse Challenges grid screen** | SmartyMe topic grid (3x5 card layout) | High — no discovery entry point for challenges | Missing |
| **Real-world photos on cards** | SmartyMe shows actual breadboards, bridges, gears | High — makes abstract concepts tangible | No `realWorldImageUrl` field |
| **Concept Summary Card** (pre-question) | SmartyMe formula card before practice | Medium — students need context before questions | Missing |
| **Hero-sized formula display** | SmartyMe renders formulas at 40pt+ | Medium — our `_FormulaChipBar` is small chips | Enhancement needed |
| **Sequential card chain visualization** | SmartyMe's DIA 1-14 progression | Medium — `nextCardId` exists but no visual chain | Missing UI |
| **Multi-panel comparative diagrams** | FigureLabs before/after, series vs parallel | Medium — single diagram only | Enhancement needed |
| **Daily challenge social post** | erkanhoca_mat Instagram MCQ pattern | Low — drives organic engagement | Missing |
| **AI text editing on diagram labels** | FigureLabs "1-Click AI Text Replacement" | Low — premium feature for active learning | Missing |
| **Rive animation renderer** | Flow diagrams, circuit animations | Low — model exists, renderer doesn't | Missing |
| **Side-by-side quality comparison** | GPAI before/after diagram quality | Low — marketing, not in-app feature | N/A |

---

## 4. Design Principles Extracted

### From SmartyMe (Engagement)
1. **Formula-first, not question-first** — show the concept/formula before asking students to apply it
2. **Real-world anchoring** — every abstract formula paired with a physical photo
3. **Sequential unlocking** — visible progression chain creates "one more card" dopamine
4. **Micro-sessions** — "Just 10 minutes a day" messaging validates CENA's 12-30 min session range
5. **Subject as identity** — each domain has its own visual brand (colors, icons, real-world imagery)

### From FigureLabs (Diagram Quality)
6. **Publication-ready quality** — diagrams should look like textbook illustrations, not sketches
7. **Labels are interactive** — every label is a learning opportunity (tap to explain)
8. **Flow direction matters** — arrows, numbering, and spatial layout guide comprehension
9. **Editable content** — students who can modify diagrams learn deeper (active vs passive)

### From Social Math (erkanhoca_mat)
10. **The question IS the content** — a well-crafted MCQ with beautiful LaTeX is inherently shareable
11. **Comments = engagement** — letting students discuss answers drives retention

---

## 5. Extracted Tasks

> **Implementation Status:** All 9 tasks implemented on 2026-03-31. `flutter analyze` passes with 0 errors.
> See Section 6 for file manifest.

### MOB-VIS-010: Browse Challenges Grid Screen
**Priority:** P0
**Depends on:** MOB-VIS-011 (real-world images on cards)
**Description:** Build a SmartyMe-style topic grid screen where students browse available challenge cards by subject. Each card shows the topic name, hero formula, subject color, tier badge, and completion status.
**Acceptance Criteria:**
- Grid layout (2 columns) of `ChallengeCard` previews
- Filter by subject (Math, Physics, Chemistry, Biology, CS)
- Cards show: topic name, hero formula (MathText), tier glow color, completion badge, real-world thumbnail (if available)
- Locked cards show lock icon + prerequisite info
- Completed cards show checkmark + XP earned
- Route: add `CenaRoutes.challenges = '/challenges'` constant AND corresponding `GoRoute` entry in router
- Tab or entry point from Home screen

### MOB-VIS-011: Real-World Image Support in ConceptDiagram
**Priority:** P0
**Description:** Add `realWorldImageUrl` field to `ConceptDiagram` model so challenge cards can show a real-world photo alongside the technical diagram (e.g., actual breadboard next to circuit schematic).
**Acceptance Criteria:**
- Add optional `String? realWorldImageUrl` to `ConceptDiagram` in `diagram_models.dart`
- Add optional `String? realWorldThumbnailUrl` for grid preview
- `InteractiveDiagramViewer` renders real-world image in a collapsible panel below diagram
- `ChallengeCardWidget` shows real-world photo as background with overlay
- Run `build_runner` to regenerate freezed/json

### MOB-VIS-012: Concept Summary Card (Pre-Question)
**Priority:** P1
**Backend dependency:** Requires new `ConceptIntroduced` SignalR event from backend (does not exist yet — must be added to the `ICenaClient` contract and `SessionActor` before frontend integration)
**Description:** Before presenting the first question on a new concept, show a "concept card" that displays the formula, a brief explanation, and optionally a diagram. Student taps "I'm ready" to start questions. Matches SmartyMe's formula-first pattern.
**Acceptance Criteria:**
- New `ConceptSummaryCard` widget
- Shows: concept name, hero-sized formula (MathText at `TypographyTokens.displayMedium` = 28pt), 1-2 line explanation, optional diagram thumbnail
- "I understand, let's practice" CTA button
- Shown once per concept per session (not on review questions)
- Add `ConceptIntroduced` case to `SessionNotifier._handleMessage()` — widget renders when this event arrives
- **Fallback (no backend yet):** Can be triggered client-side when `Exercise.conceptId` changes from previous exercise (different concept = show summary)

### MOB-VIS-013: Hero Formula Display
**Priority:** P1
**Description:** Upgrade `_FormulaChipBar` in `InteractiveDiagramViewer` to support a "hero mode" where the primary formula is displayed large above the diagram, not just as a small scrollable chip. Requires refactoring `_FormulaChipBar` (currently private, 10pt `labelSmall` chips in a 36px `SizedBox`) into a public widget with two rendering modes.
**Acceptance Criteria:**
- When diagram has exactly 1 formula: render it hero-sized (centered, `TypographyTokens.displayMedium` = 28pt, prominent subject color)
- When diagram has 2+ formulas: first formula is hero, rest are chips below
- Hero formula rendered inside `GlassContainer` with subject-colored background via `SubjectDiagramPalette.forSubject()`
- Tap hero formula to copy LaTeX to clipboard

### MOB-VIS-014: Card Chain Progression Visualization
**Priority:** P1
**Depends on:** MOB-VIS-010 (Browse Challenges screen)
**Description:** Build a visual progression chain for sequential challenge cards (SmartyMe's DIA 1-14 pattern). Shows completed/current/locked cards in a horizontal scrollable chain.

**Acceptance Criteria:**

- New `CardChainProgress` widget
- Horizontal scroll of card nodes: completed (green check), current (glowing), locked (grey lock)
- Connecting lines between nodes (solid for completed, dashed for upcoming)
- Shows "7 of 14 completed" label
- Tapping a completed card shows its summary; tapping current navigates to it
- Integrated into Browse Challenges screen header per topic

### MOB-VIS-015: Multi-Panel Comparative Diagram Layout
**Priority:** P2
**Description:** Support side-by-side or stacked diagram comparisons (e.g., series vs parallel circuit, before/after chemical reaction). FigureLabs shows upper/lower comparative layouts.

**Acceptance Criteria:**

- New `ComparativeDiagramViewer` widget
- Accepts 2 `ConceptDiagram` instances + comparison labels
- Layout: side-by-side on landscape/tablet, stacked on phone portrait
- Shared zoom: requires a shared `TransformationController` driving both `InteractiveViewer` instances (custom gesture routing — not two independent viewers)
- Highlight differences: hotspots unique to one diagram glow

### MOB-VIS-016: Daily Challenge Social Post
**Priority:** P2
**Description:** Surface a "Challenge of the Day" in the ClassActivityFeed (social tab). A well-crafted MCQ with LaTeX rendering that classmates can discuss, inspired by erkanhoca_mat's Instagram engagement pattern.
**Acceptance Criteria:**
- Backend selects daily challenge from question bank
- Feed card shows: question with MathText, 4 MCQ options, class vote percentages (anonymous)
- Students tap to vote, see aggregate results after voting
- No named attribution — aggregate only (Blueprint Principle 6)
- Optional: "Challenge a friend" share button

### MOB-VIS-017: Rive Animation Renderer for Flow Diagrams
**Priority:** P2 (escalated from P3 — `DiagramFormat.rive` is already in the production data model; diagrams tagged as Rive will silently fail without a renderer)
**Description:** Add Rive runtime support for animated diagrams (circuit current flow, wave motion, chemical reaction sequences). The `DiagramFormat.rive` enum value exists but has no renderer.

**Acceptance Criteria:**
- Add `rive` package dependency to pubspec.yaml
- New `RiveDiagramViewer` widget that loads `.riv` files from CDN URL
- Supports play/pause/scrub controls
- Interactive: tap hotspot regions during animation to pause + explain
- Fallback: if Rive file fails to load, render static SVG/PNG

### MOB-VIS-018: AI Label Editing on Diagrams
**Priority:** P3
**Description:** Allow students to tap a diagram label and fill in the blank (FigureLabs-style "1-Click AI Text Replacement" adapted for learning). Student types what they think the label should be, system validates against the correct label.
**Acceptance Criteria:**
- New interaction mode in `InteractiveDiagramViewer`: "Fill in Labels"
- Hotspot labels are hidden; student taps a hotspot, types the label
- Correct: green highlight + haptic
- Wrong: show correct label + brief explanation
- Track completion: "8 of 12 labels correct"
- Works offline — validation is client-side **natural language label matching** (case-insensitive, trimmed), NOT LaTeX comparison (LaTeX equivalences like `\frac{1}{2}` vs `\frac12` make string matching unreliable for math expressions)

---

## 6. Implementation Manifest

All tasks implemented 2026-03-31. `flutter analyze`: 0 errors.

| Task | File(s) Created/Modified | Lines |
|------|-------------------------|-------|
| MOB-VIS-010 | `lib/features/challenges/challenges_screen.dart` (new) | ~280 |
| MOB-VIS-011 | `lib/features/diagrams/models/diagram_models.dart` (modified) + freezed/json regenerated | +4 |
| MOB-VIS-012 | `lib/features/session/widgets/concept_summary_card.dart` (new) | ~160 |
| MOB-VIS-013 | `lib/features/diagrams/diagram_viewer.dart` — `_FormulaChipBar` upgraded to hero mode | ~50 changed |
| MOB-VIS-014 | `lib/features/challenges/card_chain_progress.dart` (new) | ~170 |
| MOB-VIS-015 | `lib/features/diagrams/comparative_diagram_viewer.dart` (new) | ~150 |
| MOB-VIS-016 | `lib/features/social/class_activity_feed.dart` — `DailyChallengeFeedCard` added | ~120 |
| MOB-VIS-017 | `lib/features/diagrams/rive_diagram_viewer.dart` (new) + `rive: ^0.13.0` in pubspec | ~180 |
| MOB-VIS-018 | `lib/features/diagrams/diagram_viewer.dart` — `FillInLabelsDiagram` added | ~140 |
| Routing | `lib/core/router.dart` — `CenaRoutes.challenges` + GoRoute entry | +10 |
| L10n | `lib/l10n/app_en.arb`, `app_he.arb`, `app_ar.arb` — 8 new keys | +24 |
