# AXIS 3 — Accessibility + Accommodations Research Findings
## Cena Adaptive Math Learning Platform | Israeli Bagrut Preparation
### Research Date: 2026-04-20 | Researcher: Accessibility Specialist

---

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Feature 1: Adaptive Subitizing & Magnitude Comparison Trainer (Dyscalculia)](#feature-1)
3. [Feature 2: Interactive Number Line with Audio-Haptic Feedback (Dyscalculia)](#feature-2)
4. [Feature 3: Digital Graph Paper with Smart Alignment Keypad (Dyscalculia/Dysgraphia)](#feature-3)
5. [Feature 4: ADHD Focus Session Builder with Visual Timer & Chunking](#feature-4)
6. [Feature 5: Color-Blind Safe Palette + Pattern Differentiation System](#feature-5)
7. [Feature 6: MathML-Native Screen Reader Pipeline with SpokenMath Labels](#feature-6)
8. [Feature 7: Arabic RTL Math Renderer with Direction Isolation](#feature-7)
9. [Feature 8: Anxiety-Reducing Predictable UI with Progressive Disclosure](#feature-8)
10. [Cross-Cutting Guardrail Analysis](#guardrail-analysis)
11. [Implementation Priority Matrix](#priority-matrix)

---

<a name="executive-summary"></a>
## Executive Summary

This document presents 8 substantial accessibility features for Cena, an adaptive math learning platform serving Israeli students ages 12-18 preparing for Bagrut exams. The student population includes learners with dyscalculia, ADHD, anxiety, learning disabilities, color blindness, and Arabic-speaking students requiring RTL math rendering.

**Distribution of Features:**
| Category | Count | Features |
|----------|-------|----------|
| Dyscalculia-specific | 3 | #1 Subitizing/Magnitude, #2 Number Line, #3 Digital Graph Paper |
| Beyond basic TTS + extended time | 6 | #1, #2, #4, #5, #7, #8 (all except #3 and #6) |
| ADHD-focused | 2 | #4 Session Chunking, #8 Anxiety-Reducing UI |
| Color-blind support | 1 | #5 Palette System |
| Screen reader support | 1 | #6 MathML Pipeline |
| Arabic RTL support | 1 | #7 RTL Renderer |
| Anxiety reduction | 1 | #8 Predictable UI |

**Verdict Summary:**
| Verdict | Count | Features |
|---------|-------|----------|
| **SHIP** | 3 | #2 Number Line, #5 Color-Blind Palette, #7 RTL Renderer |
| **SHORTLIST** | 3 | #1 Subitizing Trainer, #4 ADHD Session Builder, #6 MathML Pipeline |
| **DEFER** | 2 | #3 Digital Graph Paper, #8 Anxiety-Reducing UI |
| **REJECT** | 0 | — |

---

<a name="feature-1"></a>
## Feature 1: Adaptive Subitizing & Magnitude Comparison Trainer

### What It Is
A dedicated training module that presents dyscalculic students with dot-array magnitude comparison tasks, subitizing exercises (rapid quantity recognition for sets of 1-4 items), and symbolic-to-non-symbolic mapping activities. The system adapts difficulty based on response time and accuracy, using algorithms similar to Calcularis 2.0. Students see visual dot patterns and Arabic numerals side-by-side, building the critical link between approximate number system (ANS) and symbolic number representations. The module includes: (1) non-symbolic magnitude comparison (which has more dots?), (2) subitizing speed drills with automatic adjustment, (3) mixed symbolic-non-symbolic matching, and (4) quantity estimation tasks. Each session is capped at 8-10 minutes to prevent fatigue.

### Why It Moves the Needle
Research shows magnitude comparison and subitizing are core deficits in dyscalculia. The 2025 MDPI systematic review of serious game-based interventions found subitizing training produced medium effect sizes (Hedges' g = 0.32), while number comparison showed large effects (g = 0.52). Calcularis 2.0 demonstrated significant improvements in arithmetic operations with stable results after 3-month follow-up (Kucian et al., 2020). These foundational number sense skills are prerequisites for Bagrut-level mathematics, yet most adaptive platforms skip them entirely, jumping straight to procedural practice.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | MDPI Information journal, systematic review: "Early Detection and Intervention of Developmental Dyscalculia Using Serious Game-Based Digital Tools" | PEER-REVIEWED | 2025 |
| 2 | Kucian, K., et al. (2020). "Efficacy of a Computer-Based Learning Program in Children with Developmental Dyscalculia. What Influences Individual Responsiveness?" Frontiers in Psychology, 11, 1115. https://doi.org/10.3389/fpsyg.2020.01115 | PEER-REVIEWED | 2020 |
| 3 | Käser, T., et al. (2013). "Design and evaluation of the computer-based training program Calcularis for enhancing numerical cognition." Frontiers in Psychology, 4, 489. https://doi.org/10.3389/fpsyg.2013.00489 | PEER-REVIEWED | 2013 |
| 4 | Smartick — PEC (Programa de Entrenamiento Cognitivo) cognitive training for dyscalculia. Product: https://www.smartick.com/ | COMPETITIVE | 2024 |
| 5 | Calcularis by ETH Zurich / RehaKinetics. Product: https://www.calcularis.com/ | COMPETITIVE | 2024 |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **L** (Large — 3-4 sprints)
- Backend: Adaptive algorithm for dot pattern generation, performance tracking, difficulty calibration
- Frontend: Canvas/SVG dot rendering, touch-friendly interaction, animation system
- Data Model: Student subitizing accuracy profile, ANS strength metric, progress tracking

### Personas Benefited
- **Dyscalculia-Dana**: Primary beneficiary — addresses core magnitude processing deficit
- **Anxious-Avi**: Secondary — short sessions, low-stakes format reduces math anxiety
- **ADHD-Daniel**: Tertiary — visual, interactive, game-like format maintains engagement

### Implementation Sketch
```
Backend: Python service generating dot arrays with controlled visual properties
         (density, size, area controlled to prevent heuristic strategies)
         SymPy integration for symbolic number generation
Frontend: React component with Canvas rendering, < 100ms response time
         Adaptive difficulty: adjusts dot count, presentation time, feedback delay
Data Model: {
  student_id, timestamp, task_type (subitizing|comparison|mapping),
  stimulus: {dots_left, dots_right, presentation_ms},
  response: {selected, rt_ms, accuracy},
  adaptive_level: 1-15,  // current difficulty tier
  ans_strength_estimate: float  // latent trait estimate
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| No streak/loss-aversion mechanics | **SAFE** — Training mode uses mastery-based progression, not streaks |
| No variable-ratio rewards | **SAFE** — Fixed feedback schedule; rewards based on effort, not correctness |
| No leaderboards | **SAFE** — Individual progress tracking only |
| No misconception data retention | **BORDERLINE** — Must store accuracy patterns to adapt difficulty; COPPA requires parental consent for under-13. Mitigation: anonymize after 90 days, allow deletion |
| No ML training on student data | **SAFE** — Uses deterministic adaptive algorithm, not ML model training |
| No silent data collection from under-13 | **COMPLIANT** — Explicit parental consent required per COPPA 2024 updates |

### Verdict: **SHORTLIST**
Strong evidence base and competitive validation. However, requires significant investment in adaptive algorithm design and visual stimulus generation. Recommend pilot with small cohort before full rollout. Integrate with existing Cena adaptive engine rather than building standalone.

---

<a name="feature-2"></a>
## Feature 2: Interactive Number Line with Audio-Haptic Feedback

### What It Is
An interactive digital number line where students place numbers, fractions, and variables on a visual line spanning configurable ranges (0-10, 0-100, 0-1000, and -10 to +10). The number line provides: (1) visual feedback showing distance from correct position, (2) audio feedback where pitch rises/falls based on proximity to target (inspired by Desmos Audio Trace), (3) haptic feedback on mobile devices, (4) adaptive zoom that expands around the target area for precision placement, and (5) a "snap-to-tick" option for students with motor difficulties. For dyscalculic students, the number line includes colored magnitude bands and optional dot-cluster overlays showing the quantity each numeral represents.

### Why It Moves the Needle
Number line estimation is one of the most validated digital interventions for dyscalculia. Kucian et al. (2011) found digital number line training produced significant improvements in spatial number representation (0-100 range), addition, and subtraction, with accompanying changes in brain activation patterns in number-processing regions. Lin (2022) found digital number line gaming superior to non-number-line magnitude games for numerical learning. Desmos has proven that audio trace technology makes mathematical graphs accessible to blind students — extending this paradigm to number line placement creates a multi-sensory learning tool that serves visual, auditory, and kinesthetic learners simultaneously.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | Kucian, K., et al. (2011). "Mental number line training in children with developmental dyscalculia." NeuroImage, 57, 782–795. https://doi.org/10.1016/j.neuroimage.2011.05.023 | PEER-REVIEWED | 2011 |
| 2 | Kohn, J., et al. (2020). "Efficacy of a Computer-Based Learning Program in Children with Developmental Dyscalculia." Frontiers in Psychology, 11, 1115. https://doi.org/10.3389/fpsyg.2020.01115 | PEER-REVIEWED | 2020 |
| 3 | Lin, F. (2022). Digital number line gaming vs. magnitude games — dissertation research on numerical learning. | PEER-REVIEWED | 2022 |
| 4 | Desmos Audio Trace — sonified graph exploration. Product: https://www.desmos.com/accessibility | COMPETITIVE | 2025 |
| 5 | Lunardon, M., et al. (2023). "The Number Line" software training study. | PEER-REVIEWED | 2023 |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **M** (Medium — 2-3 sprints)
- Backend: Number line position scoring algorithm, adaptive range selection, SymPy integration for fraction/variable placement
- Frontend: Interactive SVG number line component, Web Audio API for sonification, vibration API for haptic feedback
- Data Model: Placement accuracy heatmap, number line estimation linearity score

### Personas Benefited
- **Dyscalculia-Dana**: Primary — directly trains impaired mental number line representation
- **Color-Blind-Chen**: Secondary — audio feedback provides non-color channel for information
- **Motor-Impaired-Moshe**: Secondary — snap-to-tick and large touch targets reduce precision requirements

### Implementation Sketch
```
Backend: Spring Boot service
  - GET /numberline/task?studentId=X&difficulty=Y → returns target number, range, hints
  - POST /numberline/response → accepts placement position, returns accuracy score + feedback
  - SymPy validates fraction/radical/expression equivalence
Frontend: React + SVG + Web Audio API
  - Touch/mouse drag with 60fps animation
  - Audio feedback: frequency mapped to distance from target (closer = higher pitch)
  - Magnitude bands: subtle color gradients + pattern fills (color-blind safe)
  - Zoom-on-approach: viewport scales 2x when within 10% of target
Data Model: {
  task_id, student_id, target_value, range_min, range_max,
  placement_position, accuracy_score, feedback_types_used [visual|audio|haptic],
  number_line_linearity_r2  // goodness of fit metric
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| All guardrails | **SAFE** — No gamification mechanics, no data retention concerns beyond adaptive needs, fully compliant with COPPA when parental consent obtained |

### Verdict: **SHIP**
Highest evidence-to-effort ratio among all features. Strong peer-reviewed backing, proven competitive implementation (Desmos Audio Trace), and direct relevance to dyscalculia core deficit. Audio-haptic feedback serves multiple disability types simultaneously. Recommend immediate implementation.

---

<a name="feature-3"></a>
## Feature 3: Digital Graph Paper with Smart Alignment Keypad

### What It Is
A specialized math input environment that replaces free-form equation entry with a structured digital graph paper grid and context-aware keypad. Students build equations by placing numbers and symbols on a virtual grid that automatically aligns digits by place value, keeps equations centered, and maintains proper spacing. The smart keypad: (1) only shows contextually relevant symbols (e.g., no multiplication symbol during addition-focused exercises), (2) provides large, high-contrast buttons with haptic feedback, (3) supports Hebrew and Arabic numerals, and (4) includes a "scratch work" area for side calculations. Work can be exported as PDF or shared with teachers.

### Why It Moves the Needle
Students with dysgraphia and dyscalculia frequently produce correct mathematical reasoning but lose points due to messy handwriting, misaligned columns, and transposition errors. ModMath, the leading app in this space, has been downloaded 970K+ times and is "life changing" according to teacher testimonials. The grid-based approach reduces working memory load by externalizing organization — students no longer need to simultaneously solve problems AND manage spatial layout. For Bagrut preparation, this ensures students are assessed on mathematical understanding, not handwriting ability.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | ModMath — assistive technology for math. Product: https://www.modmath.com/ | COMPETITIVE | 2025 |
| 2 | ModMath blog: "How Modmath Supports Students as an Accommodation in Special Education Plans." https://www.modmath.com/blog/ | COMPETITIVE | 2025 |
| 3 | Kucian, K. & von Aster, M. (2015). Calcularis research — spatial number representation training. | PEER-REVIEWED | 2015 |
| 4 | Pratt SI: "Assistive Technology: ModMath" — IXD analysis. https://ixd.prattsi.org/2023/02/assistive-technology-modmath/ | COMMUNITY | 2023 |

### Evidence Class: COMPETITIVE + COMMUNITY

### Effort Estimate: **M** (Medium — 2-3 sprints)
- Backend: Grid-based equation storage format, export to PDF/MathML
- Frontend: Custom grid renderer, context-aware keypad component, RTL support for Arabic digits
- Data Model: Grid state representation, teacher sharing permissions

### Personas Benefited
- **Dyscalculia-Dana**: Primary — reduces transcription errors, maintains alignment
- **Motor-Impaired-Moshe**: Primary — replaces handwriting with structured digital input
- **ADHD-Daniel**: Secondary — organized layout reduces visual overwhelm

### Implementation Sketch
```
Backend: Node.js service
  - Grid state stored as JSON: {cells: [{row, col, content, type}]}
  - Export: PDF generation (via Puppeteer), MathML conversion for screen readers
  - SymPy validates equation structure and can solve step-by-step
Frontend: React + CSS Grid
  - Configurable grid size (standard: 15 rows x 20 columns)
  - Context-aware keypad: symbol set changes based on exercise type
  - Place-value highlighting: ones, tens, hundreds columns subtly shaded
  - Hebrew/Arabic numeral support via Unicode
Data Model: {
  worksheet_id, student_id, grid_state, exercise_type,
  created_at, modified_at, teacher_share_token,
  accessibility_settings: {grid_size, color_scheme, keypad_layout}
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| All guardrails | **SAFE** — Pure accommodation tool, no gamification, no data collection concerns |

### Verdict: **DEFER**
Valuable feature but lower priority than #1 and #2 for dyscalculia. ModMath already serves this need well as a standalone tool; Cena integration would be a "nice to have" rather than a differentiator. Recommend revisiting after core adaptive math engine is mature. Consider partnership with ModMath rather than building from scratch.

---

<a name="feature-4"></a>
## Feature 4: ADHD Focus Session Builder with Visual Timer & Chunking

### What It Is
A session management system designed specifically for students with ADHD that breaks math practice into configurable micro-sessions. Core components: (1) **Focus Timer** — customizable Pomodoro-style timer (default: 15 min work + 5 min break, adjustable per student attention span), with a visual circular progress indicator and optional "focus music" background audio; (2) **Task Chunker** — automatically decomposes multi-step Bagrut problems into sub-tasks displayed as a checklist, with each micro-task requiring <3 minutes to complete; (3) **Distraction Shield** — full-screen focus mode that hides navigation, notifications, and non-essential UI elements during work periods; (4) **Body Doubling Mode** — optional virtual study partner feature showing a calming ambient video of another student studying; (5) **Energy Tracker** — student self-reports energy/focus level (1-5) before each session, system adjusts task difficulty and session length accordingly.

### Why It Moves the Needle
The Pomodoro Technique is specifically validated for ADHD — it transforms overwhelming tasks into "conquerable hills" by providing structured intervals with guaranteed breaks (Cirillo, 1980s; validated in 2025 Queen's Online School analysis). Chunking & micro-steps reduce overwhelm and improve momentum. Environmental design (distraction elimination) has sustained gains by reducing reliance on willpower. Importantly, Ersozlu's 2024 systematic review found that game-based learning and structured digital tools show positive results in reducing math anxiety for students with attention difficulties. The combination of time-boxing + task decomposition directly addresses executive function deficits common in ADHD.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | Queen's Online School. "10 ADHD Study Techniques That Actually Work in 2025." https://queensonlineschool.com/adhd-study-techniques/ | COMPETITIVE | 2025 |
| 2 | Ersozlu, Z. (2024). "The role of technology in reducing mathematics anxiety in primary school students." Contemporary Educational Technology, 16(3), ep517. https://doi.org/10.30935/cedtech/14717 | PEER-REVIEWED | 2024 |
| 3 | Forest app — focus timer with gamification. Product: https://www.forestapp.cc/ | COMPETITIVE | 2024 |
| 4 | Read&Write by Texthelp — screen masking for ADHD. Product: https://www.texthelp.com/ | COMPETITIVE | 2025 |
| 5 | BCU. "The Pomodoro technique for focus." https://www.bcu.ac.uk/exams-and-revision/time-management-tips/pomodoro-technique | COMMUNITY | 2025 |

### Evidence Class: PEER-REVIEWED + COMPETITIVE + COMMUNITY

### Effort Estimate: **M** (Medium — 2 sprints)
- Backend: Session state machine, focus mode toggle, task decomposition rules, energy correlation tracking
- Frontend: Visual timer component, full-screen focus overlay, task checklist, body doubling video player
- Data Model: Session logs, energy ratings, focus duration metrics, break frequency

### Personas Benefited
- **ADHD-Daniel**: Primary — directly addresses attention regulation and executive function
- **Anxious-Avi**: Secondary — predictable structure reduces uncertainty anxiety
- **Dyscalculia-Dana**: Tertiary — shorter sessions prevent fatigue from extra cognitive effort

### Implementation Sketch
```
Backend: Session management service
  - Focus mode state machine: IDLE → FOCUSING → BREAK → [repeat] → COMPLETED
  - Task decomposition: SymPy analyzes problem steps, generates sub-task list
  - Energy-adjusted difficulty: if energy < 3, reduce problem complexity by 1 tier
Frontend: React components
  - Circular SVG timer with smooth animation
  - Focus mode: CSS filter blur() on non-essential elements, position:fixed overlay
  - Task chunker: drag-to-reorder checklist with progress bar
  - Keyboard shortcut: Esc to pause, Space to toggle focus mode
Data Model: {
  session_id, student_id, session_config: {focus_min, break_min, cycles},
  energy_rating_pre, energy_rating_post,
  tasks: [{task_id, description, completed, duration_ms}],
  focus_events: [{timestamp, event_type: [start|pause|resume|break|end]}]
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| No streak/loss-aversion mechanics | **SAFE** — Timer is neutral, no streak counters or loss penalties |
| No variable-ratio rewards | **BORDERLINE** — Energy tracker could be gamified. Mitigation: keep it as self-regulation tool only, no rewards tied to focus metrics |
| No leaderboards | **SAFE** — Personal focus history only, no comparison |
| No misconception data retention | **SAFE** — Session timing data is behavioral, not misconception-related |
| No ML training on student data | **SAFE** — Deterministic rules for task decomposition and difficulty adjustment |
| No silent data collection from under-13 | **COMPLIANT** — COPPA-compliant with parental consent |

### Verdict: **SHORTLIST**
Strong practical value for ADHD students. Pomodoro is well-validated and implementation is straightforward. However, this is more of a "wrapper" around existing content rather than a core math learning feature. Recommend bundling with other focus features (screen masking, reduce motion) for maximum impact. Consider whether similar functionality exists in OS-level tools (iOS Focus Mode, Android Digital Wellbeing) before building custom.

---

<a name="feature-5"></a>
## Feature 5: Color-Blind Safe Palette + Pattern Differentiation System

### What It Is
A comprehensive color system for all Cena math visualizations that ensures accessibility for students with color vision deficiency (CVD — affects ~8% of males). Components: (1) **Default Palette** — uses viridis/cividis perceptually uniform colormaps as the default for all charts, progress bars, and graph elements, avoiding problematic red-green combinations; (2) **Pattern Overlay** — all data series in charts use distinct patterns (solid, dashed, dotted lines; different point shapes: circles, squares, triangles) in addition to color differences; (3) **Progress Indicators** — use shape + color + position (not color alone) to indicate status; (4) **Simulation Testing** — built-in CVD simulator allows designers to preview interfaces as seen with protanopia, deuteranopia, and tritanopia; (5) **Student Preference** — students can select "high contrast" mode that maximizes luminance differences regardless of hue. All palettes meet WCAG 2.1 AA contrast ratios (4.5:1 for text, 3:1 for UI components).

### Why It Moves the Needle
Color-blind students are frequently excluded from math learning when critical information is conveyed only through color. A 2018 PLoS ONE study by Nunez et al. demonstrated that cividis colormap optimization for CVD enables accurate scientific data interpretation. Matplotlib now uses viridis as its default colormap specifically for accessibility. For Cena, this means: students can correctly interpret graph legends, progress bars are readable regardless of CVD type, and error/success states don't rely on red/green coding alone. This is a "build once, benefit everyone" feature — high-contrast patterns improve readability for all students, not just those with CVD.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | Nunez, J.R., Anderton, C.R., Renslow, R.S. (2018). "Optimizing color maps with consideration for color vision deficiency to enable accurate interpretation of scientific data." PLoS ONE, 13(7), e0199239. https://doi.org/10.1371/journal.pone.0199239 | PEER-REVIEWED | 2018 |
| 2 | Cherenkov Telescope Array Observatory. "Best Practices for Colour Blind Friendly Publications." https://pos.sissa.it/guidelines.pdf | COMPETITIVE | 2023 |
| 3 | Matplotlib documentation — viridis/cividis default colormaps. https://matplotlib.org/ | COMPETITIVE | 2024 |
| 4 | Material UI. "Inclusive Hues: Designing Accessible Color Palettes for Diverse Student Learners." https://materialui.co/blog/accessible-color-palettes-for-inclusive-learning | COMPETITIVE | 2025 |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **S** (Small — 1 sprint)
- Backend: Color palette configuration API, CVD simulation endpoint
- Frontend: Theme system with CSS custom properties, pattern library for charts, contrast checker
- Data Model: Student color preference, palette definition files

### Personas Benefited
- **Color-Blind-Chen**: Primary — can now correctly interpret all visualizations
- **Dyscalculia-Dana**: Secondary — pattern differentiation reduces cognitive load
- **Low-Vision-Leah**: Secondary — high contrast mode improves readability

### Implementation Sketch
```
Backend: Static configuration
  - Palette definitions in JSON: {name, colors: [...], wcag_compliance, cvd_safe}
  - GET /theme/palettes → returns available palettes
Frontend: CSS custom properties + React context
  :root {
    --primary: #440154;       /* viridis purple */
    --secondary: #21918c;     /* viridis teal */
    --tertiary: #fde725;      /* viridis yellow */
    --success-pattern: solid;
    --warning-pattern: dashed;
    --error-pattern: dotted;
  }
  - All chart components: color + pattern + shape for every data series
  - Progress bars: segmented with icons (✓, ○, !) not just color
  - CVD simulator: CSS filter matrix for protanopia/deuteranopia/tritanopia simulation
Data Model: {
  student_id, preferred_palette, high_contrast: boolean,
  cvd_type: [none|protanopia|deuteranopia|tritanopia]  // self-reported or detected
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| All guardrails | **SAFE** — Pure presentation-layer change, no data collection, no gamification |

### Verdict: **SHIP**
Low effort, high impact, benefits all students. Well-supported by peer-reviewed research and industry standards. This is a foundational accessibility requirement that should be implemented before any charting or progress visualization features ship. The cost of getting color wrong is exclusion; the cost of doing it right is minimal.

---

<a name="feature-6"></a>
## Feature 6: MathML-Native Screen Reader Pipeline with SpokenMath Labels

### What It Is
A comprehensive screen reader support system for all mathematical content in Cena. The pipeline: (1) **MathML Generation** — all equations are rendered as MathML (not just images) using MathJax with assistive MathML output; (2) **ARIA Labels** — every equation has a human-readable `aria-label` generated via MathJax's Speech Rule Engine (SRE), speaking "x squared plus y squared equals z squared" instead of "x 2 plus y 2 equals z 2"; (3) **Interactive Navigation** — screen reader users can explore equations element-by-element using arrow keys (fractions, exponents, roots announced with proper nesting); (4) **Custom Hebrew/Arabic Labels** — spoken labels generated in student's UI language; (5) **Fallback Alt-Text** — for complex diagrams, SymPy generates descriptive text alternatives. This goes far beyond basic TTS — it provides structural, navigable access to mathematical notation.

### Why It Moves the Needle
Most digital math is completely inaccessible to screen reader users. Benetech's Math Detective/Page AI project converted 7 million equations from thousands of math books to MathML, recognizing this as "the next frontier" in accessibility. Khan Academy completely rebuilt its graphing exercises so students could "make shapes, move points, and do anything a mouse user could do" with keyboard navigation. Desmos Audio Trace proves that sonified math can be deeply intuitive. For Bagrut preparation, blind and visually impaired students currently rely on human readers — a MathML pipeline would enable independent study. The MathML 4.0 spec (2026) now includes explicit RTL support, making this future-proof for Arabic content.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | W3C. "MathML Accessibility Gap Analysis." https://w3c.github.io/mathml-docs/gap-analysis/ | PEER-REVIEWED | 2021 |
| 2 | W3C. "MathML Version 4.0." https://www.w3.org/TR/mathml4/ | STANDARD | 2026 |
| 3 | Benetech. "How AI can Unlock STEM Content for Students with Disabilities." https://benetech.org/ai-unlock-stem-content-students-disabilities/ | COMPETITIVE | 2021 |
| 4 | Khan Academy. "Rebuilding Graphs for Accessibility: Inside Khan Academy's Inclusive Design." https://blog.khanacademy.org/rebuilding-graphs-for-accessibility-inside-khan-academys-inclusive-design/ | COMPETITIVE | 2025 |
| 5 | Desmos. "What Accessibility features does Desmos offer?" https://help.desmos.com/hc/en-us/articles/4404860698253 | COMPETITIVE | 2025 |

### Evidence Class: STANDARD + PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **L** (Large — 3-4 sprints)
- Backend: MathML generation pipeline, speech rule engine integration, multi-language label generation
- Frontend: MathJax configuration with assistive MathML, keyboard navigation handlers, ARIA live regions
- Data Model: Equation accessibility metadata, custom label overrides

### Personas Benefited
- **Blind-Batsheva**: Primary — full independent access to all mathematical content
- **Low-Vision-Leah**: Secondary — MathML + screen magnification = readable equations
- **Dyslexic-David**: Secondary — hearing equations read aloud aids comprehension

### Implementation Sketch
```
Backend: Node.js service
  - MathML generation: MathJax (server-side) converts LaTeX/SymPy output to MathML
  - Speech Rule Engine (SRE) generates spoken labels in multiple languages
  - Custom label API: teachers can override auto-generated labels for complex equations
  - GET /equation/:id/accessibility → {mathml, aria_label, speech_text_he, speech_text_ar}
Frontend: MathJax browser rendering + ARIA
  - renderMathInElement() with assistive MathML enabled
  - aria-live="polite" regions for dynamic equation updates
  - Keyboard navigation: Tab to focus equation, arrow keys to navigate sub-expressions
  - Skip-to-content link: bypass equation and read plain-text summary
Data Model: {
  equation_id, mathml_markup,
  aria_label: {en, he, ar},
  sre_confidence_score,
  custom_label_override: string|null,
  keyboard_navigable: boolean,
  last_accessibility_audit: timestamp
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| No ML training on student data | **SAFE** — Uses deterministic Speech Rule Engine, not ML |
| No silent data collection from under-13 | **COMPLIANT** — Accessibility features don't require data collection |
| All other guardrails | **SAFE** |

### Verdict: **SHORTLIST**
Critical for blind students and legally required for WCAG 2.1 AA compliance. However, the implementation is complex and requires deep MathJax/SRE expertise. Hebrew and Arabic speech rule coverage may be incomplete. Recommend phased approach: (1) English MathML + ARIA labels first, (2) Hebrew labels with manual curation, (3) Arabic RTL MathML after #7 is complete. This is a must-have for compliance but can be staged.

---

<a name="feature-7"></a>
## Feature 7: Arabic RTL Math Renderer with Direction Isolation

### What It Is
A specialized math rendering system that correctly handles mixed-directional mathematical text in Arabic. Key capabilities: (1) **Direction Isolation** — all math expressions render LTR regardless of surrounding RTL Arabic text, preventing the "inherited directionality" bug where equations like F = m·a appear misaligned or backwards; (2) **Arabic Numeral Support** — option to display Eastern Arabic numerals (٠١٢٣٤٥٦٧٨٩) alongside or instead of Western Arabic numerals; (3) **Arabic Function Names** — localized identifiers (جا for sin, تا for cos, ظا for tan, لو for log); (4) **Bidirectional Text in mtext** — proper handling of mixed Arabic-Latin text within mathematical expressions using Unicode Bidirectional Algorithm; (5) **Mirroring** — integral signs, summation notation, and root symbols correctly mirrored for RTL layout per MathML 4.0 specification. The system uses CSS `direction: ltr` isolation on all math elements plus the MathJax Arabic extension for full RTL equation support.

### Why It Moves the Needle
Arabic-speaking students are severely underserved by math platforms. The KaTeX rendering issue documented in OpenAI's community forum (2025) shows that math expressions "inherit the directionality of surrounding text," resulting in misaligned, hard-to-read equations. Alsheri's 2014 research at University of Waterloo documented how MathML historically rendered Arabic notation backwards. For Cena serving Israeli Arabic-speaking students, this is not an edge case — it's a core requirement. The MathML 4.0 specification (2026) now formally defines RTL behavior for mathematics, making this the right time to implement. The Edraak Arabic MathJax extension proves this is technically feasible at scale.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | W3C. "MathML Version 4.0 — RTL directionality specification." https://www.w3.org/TR/mathml4/ | STANDARD | 2026 |
| 2 | Alsheri, M. (2014). "Issues of Rendering Arabic Mathematical Notation in MathML." University of Waterloo. https://cs.uwaterloo.ca/~smwatt/home/students/theses/MAlsheri2014-msc-project.pdf | PEER-REVIEWED | 2014 |
| 3 | OpenAI Community. "Request to Fix KaTeX Rendering Issues for RTL Languages." https://community.openai.com/t/request-to-fix-katex-rendering-issues-for-rtl-languages/1117054 | COMMUNITY | 2025 |
| 4 | Edraak. "arabic-mathjax: An extension for Arabic math support in MathJax." https://github.com/Edraak/arabic-mathjax | COMPETITIVE | 2015 |
| 5 | GitHub: "Math-Direction-Fixer" browser extension for RTL math. https://github.com/sma-abyar/Math-Direction-Fixer | COMPETITIVE | 2024 |

### Evidence Class: STANDARD + PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **M** (Medium — 2 sprints)
- Backend: Arabic localization strings, numeral conversion utilities (Western ↔ Eastern Arabic)
- Frontend: MathJax configuration with Arabic extension, CSS direction isolation, RTL layout testing
- Data Model: Localization strings, student language preference, numeral display preference

### Personas Benefited
- **Arabic-Ahmad**: Primary — can read math in native language with correct layout
- **Bilingual-Bisan**: Secondary — seamless switching between Hebrew and Arabic math notation
- **Color-Blind-Chen**: Tertiary — pattern-based differentiation works regardless of text direction

### Implementation Sketch
```
Backend: Localization service
  - JSON localization files: {math_functions, symbols, instructions}
  - Numeral converter: Western Arabic (0-9) ↔ Eastern Arabic (٠-٩)
  - GET /math/localize?lang=ar&numeral=eastern → localized equation template
Frontend: MathJax + CSS
  /* Direction isolation for all math elements */
  .katex, .katex-display, .MathJax, .MathJax_Display {
    direction: ltr !important;
    unicode-bidi: isolate;
  }
  /* RTL page layout but LTR math */
  html[dir="rtl"] .math-container {
    direction: ltr;
    text-align: right;  /* equation aligns to right on RTL page */
  }
  - MathJax config: load Arabic extension when lang=ar
  - Font: Amiri or Noto Naskh Arabic for Arabic math identifiers
Data Model: {
  student_id, ui_language: [he|ar], 
  math_numeral_system: [western|eastern|both],
  math_function_names: [localized|latin],  // show "جا" vs "sin"
  rtl_math_enabled: boolean
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| All guardrails | **SAFE** — Internationalization feature, no data collection or gamification concerns |

### Verdict: **SHIP**
Essential for Cena's Arabic-speaking student population. The MathML 4.0 standard (2026) provides clear guidance, the Edraak extension proves technical feasibility, and the implementation effort is moderate. This is both an accessibility requirement and a market differentiator — few math platforms handle Arabic RTL correctly. Must ship before onboarding Arabic-speaking schools.

---

<a name="feature-8"></a>
## Feature 8: Anxiety-Reducing Predictable UI with Progressive Disclosure

### What It Is
A comprehensive UI philosophy and component system designed to minimize math anxiety. Core elements: (1) **Progressive Disclosure** — advanced settings, detailed explanations, and optional help are hidden behind "Learn more" links; only the essential problem statement and input area are visible by default; (2) **Predictable Layout** — every exercise screen follows an identical structure: problem at top, workspace in middle, hint/help at bottom; navigation elements never move; (3) **Calm Color System** — soft blue-gray backgrounds, no red for errors (use amber with constructive messaging: "Not quite — let's look at this step together"), success indicated with subtle green + checkmark (no celebration animations); (4) **Forgiving Interactions** — unlimited undo, ability to edit answers before final submission, clear "Nothing is final until you confirm" messaging; (5) **No Surprise Difficulty** — students see a session preview showing exactly how many problems and what types before starting; difficulty never jumps more than one level within a single session; (6) **Warm Microcopy** — all error messages explain, never scold; hints are phrased as questions, not directives.

### Why It Moves the Needle
Math anxiety is not just discomfort — it physically hinders brain activity. Young et al. (2012) showed that high math anxiety reduces activity in brain regions responsible for mathematical reasoning. Ersozlu's 2024 systematic review found that while poorly designed online learning increases math anxiety, carefully designed digital tools with predictable patterns and low-stakes environments can reduce it. Calm UX principles (progressive disclosure, forgiving interactions, predictable feedback) directly address the uncertainty and loss of control that drive anxiety. For Bagrut preparation — inherently high-stakes — an anxiety-reducing UI is not a "nice to have" but a performance requirement.

### Sources
| # | Source | Type | Year |
|---|--------|------|------|
| 1 | Ersozlu, Z. (2024). "The role of technology in reducing mathematics anxiety in primary school students." Contemporary Educational Technology, 16(3), ep517. https://doi.org/10.30935/cedtech/14717 | PEER-REVIEWED | 2024 |
| 2 | Young, C.B., et al. (2012). "The Neurodevelopmental Basis of Math Anxiety." Psychological Science. | PEER-REVIEWED | 2012 |
| 3 | UX Matters. "Designing Calm: UX Principles for Reducing Users' Anxiety." https://www.uxmatters.com/mt/archives/2025/05/designing-calm-ux-principles-for-reducing-users-anxiety.php | COMPETITIVE | 2025 |
| 4 | Archedu. "Designing Calm in an Overstimulated Digital World." https://www.archedu.org/blog/designing-calm-in-an-overstimulated-digital-world/ | COMPETITIVE | 2026 |

### Evidence Class: PEER-REVIEWED + COMPETITIVE

### Effort Estimate: **S** (Small — 1-2 sprints)
- Backend: Session preview generator, difficulty progression enforcement
- Frontend: Component library with calm styling, progressive disclosure wrappers, microcopy system
- Data Model: UI preference profiles, anxiety-reduction feature flags

### Personas Benefited
- **Anxious-Avi**: Primary — every element designed to reduce uncertainty and fear
- **ADHD-Daniel**: Secondary — predictable layout reduces cognitive load of reorientation
- **Dyscalculia-Dana**: Secondary — forgiving interactions reduce penalty for mistakes

### Implementation Sketch
```
Backend: Difficulty progression guardrails
  - Enforce: max difficulty jump = 1 level per session
  - Session preview: generate human-readable summary of upcoming problems
  - Microcopy database: localized error messages, hint templates
Frontend: React component library
  - <ProgressiveDisclosure> wrapper component
  - <CalmAlert> component: amber (not red) background, helpful message, action suggestion
  - Consistent layout grid: problem (25%) | workspace (50%) | help (25%)
  - CSS: --bg-calm: #f0f4f8; --error-amber: #f59e0b; --success-subtle: #10b981;
  - No animations > 200ms duration; no auto-playing elements
  - All buttons maintain position across screens
Data Model: {
  student_id, ui_profile: [calm|standard|high-contrast],
  progressive_disclosure_enabled: boolean,
  session_preview_enabled: boolean,
  microcopy_language: [he|ar|en]
}
```

### Guardrail Tension
| Guardrail | Assessment |
|-----------|------------|
| No streak/loss-aversion mechanics | **SAFE** — Explicitly avoids these patterns |
| No variable-ratio rewards | **SAFE** — No reward schedule; focus is on reducing negative emotions, not adding positive ones |
| No leaderboards | **SAFE** — No comparative features |
| No misconception data retention | **SAFE** — UI-level feature, no assessment data |
| All other guardrails | **SAFE** |

### Verdict: **DEFER**
Important but primarily a design system concern rather than a distinct feature. The principles should be incorporated into ALL Cena UI components from day one, not built as a separate module. Recommend establishing "Calm UI" as a design system principle with progressive disclosure, forgiving interactions, and warm microcopy as standard patterns. Implement as part of the core design system, not as a standalone feature. Individual elements (progressive disclosure, session preview) can ship incrementally.

---

<a name="guardrail-analysis"></a>
## Cross-Cutting Guardrail Analysis

### Guardrail Compliance Summary

| # | Guardrail | Overall Risk | Mitigation |
|---|-----------|-------------|------------|
| 1 | No streak/loss-aversion mechanics | **GREEN** — No violations across any feature | None needed |
| 2 | No variable-ratio reward schedules | **GREEN** — One BORDERLINE in #4 (energy tracker); mitigated by self-regulation-only design | Remove any gamification from energy feature |
| 3 | No leaderboards with shame | **GREEN** — No comparative percentile features | None needed |
| 4 | No misconception data retention across sessions | **YELLOW** — #1 requires accuracy data for adaptation; #4 stores session timing | Anonymize after 90 days; allow parental deletion; COPPA consent |
| 5 | No ML training on student data | **GREEN** — All features use deterministic algorithms | None needed |
| 6 | No silent data collection from under-13 | **GREEN** — All data collection is explicit with parental consent per COPPA 2024/2025 updates | Ensure verifiable parental consent flow |

### COPPA 2024/2025 Compliance Notes
The FTC finalized changes to COPPA in January 2025 that:
- Require parental opt-in for third-party advertising (not applicable to Cena's non-ad model)
- Expand definition of personal information to include biometric data
- Introduce stricter data minimization requirements
- Schools can consent on behalf of parents ONLY for educational purposes (no commercial use)

**For Cena:** Since the platform is contracted through schools for educational purposes, school-provided consent is valid for educational data collection. However, any data used for product improvement or analytics requires separate parental consent. All features in this document stay within educational-purpose boundaries.

---

<a name="priority-matrix"></a>
## Implementation Priority Matrix

| Priority | Feature | Effort | Impact | Timeline | Verdict |
|----------|---------|--------|--------|----------|---------|
| P0 | #5 Color-Blind Safe Palette | S | High | Sprint 1 | SHIP |
| P0 | #7 Arabic RTL Math Renderer | M | High | Sprint 2-3 | SHIP |
| P1 | #2 Number Line with Audio-Haptic | M | Very High | Sprint 3-4 | SHIP |
| P1 | #6 MathML Screen Reader Pipeline | L | Very High | Sprint 4-6 (phased) | SHORTLIST |
| P2 | #1 Subitizing/Magnitude Trainer | L | High | Sprint 5-7 (pilot) | SHORTLIST |
| P2 | #4 ADHD Focus Session Builder | M | Medium | Sprint 6-7 | SHORTLIST |
| P3 | #8 Anxiety-Reducing UI (design system) | S | Medium | Ongoing | DEFER* |
| P4 | #3 Digital Graph Paper | M | Medium | Future | DEFER |

*"DEFER" for #8 means implement as design system principles across all features, not as a standalone module.

---

## Research Methodology Notes

**Sources Consulted:** 45+ sources across academic databases (MDPI, Frontiers, PLoS ONE), competitive product documentation (Desmos, Khan Academy, ModMath, Texthelp, Smartick, Calcularis), W3C standards, and community forums.

**Date of Research:** 2026-04-20

**Limitations:** 
- Limited Hebrew/Arabic TTS voice availability data found; may require integration with platform-specific speech APIs (iOS AVSpeechSynthesis, Android TextToSpeech)
- Voice Dream Reader provides variable-speed TTS but math equation support is limited to document reading, not interactive manipulation
- Ghotit focuses on dyslexia writing assistance, not math-specific support
- No competitive products found that combine ALL 8 features; this represents a differentiated integrated approach

---

*Document generated for Cena Product Team. Questions: contact accessibility research lead.*
