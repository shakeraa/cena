# Cena Documentation Index

> **Last updated**: 2026-04-13
> **Total documents**: 182 markdown files across `docs/` and `tasks/`
> **Scope**: Arabic+Hebrew Bagrut math (806/807) + physics (036), Israeli market

---

## How to Read This Index

Start with the **two master documents** — they consolidate everything else:

| Document | Lines | What it covers |
|----------|-------|----------------|
| [Question Engine Architecture](research/cena-question-engine-architecture-2026-04-12.md) | 2,500+ | 44 sections, 50 improvements: question types, ingestion, variants, figures, physics, step-solver, CAS engine, IRT, CAT, curriculum, RTL, motivation, security, privacy, accessibility, cost model, adversarial review, solution design |
| [Game-Design Research Synthesis](research/cena-sexy-game-research-2026-04-11.md) | ~1,200 | 10-track research synthesis: Duolingo, Khan, Brilliant, competitors, gamification meta-analyses, SDT/flow, Israeli market, dark patterns, AI tutoring, game-design primitives |
| [Mobile: PWA Approach](research/cena-mobile-pwa-approach.md) | ~250 | PWA architecture, capabilities, offline strategy, camera access, TWA wrapper, cost analysis, risks |
| [Mobile: Flutter Approach](research/cena-mobile-flutter-approach.md) | ~250 | Flutter architecture, rendering parity challenge (Improvement #48), cost comparison, timeline impact |

Everything below feeds into or is referenced by these two.

---

## 1. Architecture & Design

| Document | Topic |
|----------|-------|
| [architecture-design.md](architecture-design.md) | System architecture (DDD, event sourcing, actors) |
| [architecture-audit.md](architecture-audit.md) | Architecture review findings |
| [api-contracts.md](api-contracts.md) | REST + SignalR API contracts |
| [event-schemas.md](event-schemas.md) | Marten event store schema definitions |
| [actor-system-review.md](actor-system-review.md) | Proto.Actor system review |
| [resilience-architecture.md](resilience-architecture.md) | Circuit breakers, retries, fallbacks |
| [failure-modes.md](failure-modes.md) | Failure mode analysis |
| [offline-sync-protocol.md](offline-sync-protocol.md) | Mobile offline sync protocol |
| [intelligence-layer.md](intelligence-layer.md) | AI/LLM integration layer |
| [llm-routing-strategy.md](llm-routing-strategy.md) | Model routing (Haiku/Sonnet/Opus) |
| [messaging-context-design.md](messaging-context-design.md) | NATS messaging patterns |
| [operations.md](operations.md) | Operational runbook |
| [operations/deploy-runbook.md](operations/deploy-runbook.md) | Deployment procedures |
| [adr/0001-multi-institute-enrollment.md](adr/0001-multi-institute-enrollment.md) | ADR: multi-tenant enrollment |

## 2. Learning Engine & Pedagogy

| Document | Topic |
|----------|-------|
| [mastery-engine-architecture.md](mastery-engine-architecture.md) | BKT mastery model architecture |
| [mastery-engine-implementation.md](mastery-engine-implementation.md) | Mastery engine implementation details |
| [mastery-measurement-research.md](mastery-measurement-research.md) | Research: measuring mastery |
| [adaptive-learning-architecture-research.md](adaptive-learning-architecture-research.md) | Adaptive learning systems research |
| [learning-methodology-strategy.md](learning-methodology-strategy.md) | Methodology switching strategy |
| [learning-science-srs-research.md](learning-science-srs-research.md) | Spaced repetition research |
| [assessment-specification.md](assessment-specification.md) | Assessment item specification |
| [cognitive-load-progressive-disclosure-research.md](cognitive-load-progressive-disclosure-research.md) | Cognitive load theory + progressive disclosure |
| [flow-state-design-research.md](flow-state-design-research.md) | Flow state in learning design |
| [focus-degradation-research.md](focus-degradation-research.md) | Attention/focus degradation patterns |
| [engagement-signals-research.md](engagement-signals-research.md) | Student engagement signal detection |

## 3. Gamification & UX Psychology

| Document | Topic |
|----------|-------|
| [gamification-motivation-research.md](gamification-motivation-research.md) | Gamification + motivation research |
| [ethical-persuasion-research.md](ethical-persuasion-research.md) | Ethical persuasion in EdTech |
| [ethical-persuasion-digital-wellbeing-research.md](ethical-persuasion-digital-wellbeing-research.md) | Digital wellbeing + persuasion |
| [habit-loops-hook-model-research.md](habit-loops-hook-model-research.md) | Habit loop / hook model analysis |
| [onboarding-first-time-ux-research.md](onboarding-first-time-ux-research.md) | First-time UX / onboarding research |
| [design/microinteractions-emotional-design.md](design/microinteractions-emotional-design.md) | Micro-interactions + emotional design |
| [design/difficulty-aware-responses.md](design/difficulty-aware-responses.md) | Difficulty-aware response design |
| [design/interactive-questions-design.md](design/interactive-questions-design.md) | Interactive question UX design |
| [design/micro-lessons-design.md](design/micro-lessons-design.md) | Micro-lesson format design |

## 4. Question Engine & Content Pipeline

| Document | Topic |
|----------|-------|
| [**research/cena-question-engine-architecture-2026-04-12.md**](research/cena-question-engine-architecture-2026-04-12.md) | **Master doc** — 44 sections, 50 improvements |
| [question-ingestion-specification.md](question-ingestion-specification.md) | Ingestion pipeline spec |
| [question-quality-gate-research.md](question-quality-gate-research.md) | Question quality gate research |
| [quality-gate-implementation.md](quality-gate-implementation.md) | Quality gate implementation |
| [content-authoring.md](content-authoring.md) | Content authoring workflow |

## 5. Auto-Research (AI-Generated Deep Dives)

### 5.1 Core Research

| Document | Topic |
|----------|-------|
| [autoresearch/math-ocr-research.md](autoresearch/math-ocr-research.md) | Math OCR tool evaluation (Mathpix, Nougat, GOT-OCR) |
| [autoresearch/arabic-math-education-research.md](autoresearch/arabic-math-education-research.md) | Arabic math education landscape |
| [autoresearch/assessment-item-schema-research.md](autoresearch/assessment-item-schema-research.md) | Assessment item schema design |
| [autoresearch/question-ingestion-pipeline-research.md](autoresearch/question-ingestion-pipeline-research.md) | Question ingestion pipeline research |
| [autoresearch/question-ingestion-research.md](autoresearch/question-ingestion-research.md) | Question ingestion approaches |
| [autoresearch/syllabus-corpus-research.md](autoresearch/syllabus-corpus-research.md) | Bagrut syllabus corpus analysis |
| [autoresearch-student-ai-interaction.md](autoresearch-student-ai-interaction.md) | Student-AI interaction patterns |
| [discussion-student-ai-interaction.md](discussion-student-ai-interaction.md) | Student-AI interaction discussion |

### 5.2 Screenshot Analyzer Security (10 iterations, 87/100 score)

Each iteration has a technical article + controversy companion:

| Iter | Technical | Controversy | Topic |
|------|-----------|-------------|-------|
| 01 | [Vision model safety](autoresearch/screenshot-analyzer/iteration-01-vision-model-safety.md) | [Automation bias](autoresearch/screenshot-analyzer/iteration-01-controversy.md) | Adversarial image attacks |
| 02 | [Prompt injection OCR](autoresearch/screenshot-analyzer/iteration-02-prompt-injection-ocr.md) | [Deploy to minors?](autoresearch/screenshot-analyzer/iteration-02-controversy.md) | Multimodal prompt injection |
| 03 | [LaTeX sanitization](autoresearch/screenshot-analyzer/iteration-03-latex-sanitization.md) | [LaTeX constraints](autoresearch/screenshot-analyzer/iteration-03-controversy.md) | Code execution prevention |
| 04 | [Content moderation](autoresearch/screenshot-analyzer/iteration-04-content-moderation-minors.md) | [Over-filtering](autoresearch/screenshot-analyzer/iteration-04-controversy.md) | CSAM, SafeSearch, minors |
| 05 | [Rate limiting](autoresearch/screenshot-analyzer/iteration-05-rate-limiting.md) | [Throttling ethics](autoresearch/screenshot-analyzer/iteration-05-controversy.md) | Token bucket, cost protection |
| 06 | [Privacy-preserving images](autoresearch/screenshot-analyzer/iteration-06-privacy-preserving-images.md) | [Privacy-utility tradeoff](autoresearch/screenshot-analyzer/iteration-06-controversy.md) | Ephemeral processing |
| 07 | [Academic integrity](autoresearch/screenshot-analyzer/iteration-07-academic-integrity.md) | [AI + assessment](autoresearch/screenshot-analyzer/iteration-07-controversy.md) | Exam cheating detection |
| 08 | [Accessibility photo input](autoresearch/screenshot-analyzer/iteration-08-accessibility-photo-input.md) | [Digital divide](autoresearch/screenshot-analyzer/iteration-08-controversy.md) | WCAG 2.2 AA compliance |
| 09 | [Error handling](autoresearch/screenshot-analyzer/iteration-09-error-handling-degradation.md) | [UX framing](autoresearch/screenshot-analyzer/iteration-09-controversy.md) | 17 failure modes |
| 10 | [E2E attack simulation](autoresearch/screenshot-analyzer/iteration-10-e2e-attack-simulation.md) | [Does photo input help?](autoresearch/screenshot-analyzer/iteration-10-controversy.md) | 30 attacks, 0% ASR |

## 6. Game-Design Research (10 tracks)

| Track | Document | Topic |
|-------|----------|-------|
| Synthesis | [**cena-sexy-game-research-2026-04-11.md**](research/cena-sexy-game-research-2026-04-11.md) | **Master synthesis** — 10-track findings + revised proposal |
| 1 | [track-1-duolingo.md](research/tracks/track-1-duolingo.md) | Duolingo mechanics analysis |
| 2 | [track-2-khan-khanmigo.md](research/tracks/track-2-khan-khanmigo.md) | Khan Academy + Khanmigo |
| 3 | [track-3-brilliant.md](research/tracks/track-3-brilliant.md) | Brilliant.org analysis |
| 4 | [track-4-prodigy-dragonbox-kahoot.md](research/tracks/track-4-prodigy-dragonbox-kahoot.md) | Anti-patterns: Prodigy, DragonBox, Kahoot |
| 5 | [track-5-gamification-metaanalyses.md](research/tracks/track-5-gamification-metaanalyses.md) | Gamification meta-analyses |
| 6 | [track-6-sdt-flow.md](research/tracks/track-6-sdt-flow.md) | SDT + flow theory |
| 7 | [track-7-israeli-bagrut-market.md](research/tracks/track-7-israeli-bagrut-market.md) | Israeli Bagrut market analysis |
| 8 | [track-8-dark-patterns-compliance.md](research/tracks/track-8-dark-patterns-compliance.md) | Dark patterns + compliance |
| 9 | [track-9-socratic-ai-tutoring.md](research/tracks/track-9-socratic-ai-tutoring.md) | Socratic AI tutoring |
| 10 | [track-10-game-design-primitives.md](research/tracks/track-10-game-design-primitives.md) | Game-design primitives |

## 7. Competition Research

| Document | Topic |
|----------|-------|
| [competition-research/CENA_Master_Competitive_Analysis.md](competition-research/CENA_Master_Competitive_Analysis.md) | Master competitive analysis |
| [competition-research/competitive_intelligence_ai_tutors.md](competition-research/competitive_intelligence_ai_tutors.md) | AI tutor competitive intelligence |
| [competition-research/STEM_Visualization_Competitor_Analysis.md](competition-research/STEM_Visualization_Competitor_Analysis.md) | STEM visualization competitors |
| [competition-research/regional_emerging_edtech_competitors_report.md](competition-research/regional_emerging_edtech_competitors_report.md) | Regional emerging EdTech |
| [competition-research/srs_competitor_analysis.md](competition-research/srs_competitor_analysis.md) | SRS competitor analysis |
| [competitor-eself-deep-dive.md](competitor-eself-deep-dive.md) | eSelf deep dive |
| [eself-web-scrape.md](eself-web-scrape.md) | eSelf web scrape data |
| [cet-partnership-research.md](cet-partnership-research.md) | CET partnership research |

## 8. Mobile App

| Document | Topic |
|----------|-------|
| [mobile-research/CENA_UI_UX_Design_Strategy_2026.md](mobile-research/CENA_UI_UX_Design_Strategy_2026.md) | UI/UX design strategy |
| [mobile-research/CENA_Mobile_UX_Psychology_Blueprint.md](mobile-research/CENA_Mobile_UX_Psychology_Blueprint.md) | Mobile UX psychology |
| [mobile-research/CENA_Flutter_Template_Research_Report.md](mobile-research/CENA_Flutter_Template_Research_Report.md) | Flutter template research |
| [mobile-research/CENA_Figma_Flutter_Design_Resources_14-18.md](mobile-research/CENA_Figma_Flutter_Design_Resources_14-18.md) | Figma + Flutter design resources |
| [mobile-research/CENA_Competitive_UX_Analysis.md](mobile-research/CENA_Competitive_UX_Analysis.md) | Competitive UX analysis |
| [mobile-research/mobile-ux-patterns-research.md](mobile-research/mobile-ux-patterns-research.md) | Mobile UX patterns |
| [mobile-tasks.md](mobile-tasks.md) | Mobile task backlog |

## 9. Legal, Privacy & Compliance

| Document | Topic |
|----------|-------|
| [legal/privacy-policy-children.md](legal/privacy-policy-children.md) | Privacy policy (children) |
| [legal/terms-of-service.md](legal/terms-of-service.md) | Terms of service |
| [compliance/dpia-2026-04.md](compliance/dpia-2026-04.md) | Data Protection Impact Assessment |
| [compliance/dpia-template.md](compliance/dpia-template.md) | DPIA template |
| [accessibility/WCAG-AA-Primary-Color-Analysis.md](accessibility/WCAG-AA-Primary-Color-Analysis.md) | WCAG AA color analysis |

## 10. Reviews & Audits

| Document | Topic |
|----------|-------|
| [reviews/cena-review-2026-04-11.md](reviews/cena-review-2026-04-11.md) | Full platform review |
| [reviews/cena-review-2026-04-11-reverify.md](reviews/cena-review-2026-04-11-reverify.md) | Re-verification report |
| [reviews/agent-1-arch-findings.md](reviews/agent-1-arch-findings.md) | Architecture findings |
| [reviews/agent-2-security-findings.md](reviews/agent-2-security-findings.md) | Security findings |
| [reviews/agent-3-data-findings.md](reviews/agent-3-data-findings.md) | Data layer findings |
| [reviews/agent-4-pedagogy-findings.md](reviews/agent-4-pedagogy-findings.md) | Pedagogy findings |
| [reviews/agent-5-ux-findings.md](reviews/agent-5-ux-findings.md) | UX findings |
| [cross-doc-audit.md](cross-doc-audit.md) | Cross-document consistency audit |
| [fact-check-report.md](fact-check-report.md) | Fact-check report |

## 11. Business & Strategy

| Document | Topic |
|----------|-------|
| [business-viability-assessment.md](business-viability-assessment.md) | Business viability assessment |
| [product-research.md](product-research.md) | Product research |
| [fundraising-playbook.md](fundraising-playbook.md) | Fundraising playbook |

---

## Task Queue

### Active Task Sets

| Set | Count | Path | Status |
|-----|-------|------|--------|
| Figure rendering | 8 | [tasks/figures/FIGURE-001..008](../tasks/figures/) | Enqueued, unassigned |
| Game design | 10 | [tasks/game-design/GD-001..010](../tasks/game-design/) | Enqueued, unassigned |

### Master Plans

| Document | Topic |
|----------|-------|
| [tasks/00-master-plan.md](../tasks/00-master-plan.md) | Master implementation plan |
| [tasks/EXECUTION-PLAN.md](../tasks/EXECUTION-PLAN.md) | Execution plan |
| [tasks/PRIORITY-BACKLOG.md](../tasks/PRIORITY-BACKLOG.md) | Priority backlog |

### Completed Task Sets

| Set | Count | Path |
|-----|-------|------|
| Actors | 32 | `tasks/actors/done/ACT-001..032` |
| Admin | 26 | `tasks/admin/done/ADM-001..026` |
| Backend | 10 | `tasks/backend/done/BKD-001..005, ERR-001, MSG-001..005, SES-001..002` |
| Content | 8 | `tasks/content/done/CNT-001..010` |
| Data | 4+ | `tasks/data/done/DATA-001..006` |

---

## Interactive Samples

| File | Description |
|------|-------------|
| [examples/figure-sample/index.html](../examples/figure-sample/index.html) | 5 question cards: quadratic, trig, physics inclined plane, Arabic RTL, derivative |
| [examples/figure-sample/step-solver.html](../examples/figure-sample/step-solver.html) | Step-by-step solver with 3 scaffolding levels |
| [examples/figure-sample/figure-sample.js](../examples/figure-sample/figure-sample.js) | Rendering script: function-plot.js + programmatic SVG |

---

## Quick Stats

- **Total docs**: 182 markdown files
- **Architecture improvements**: 42 (from 8 expert review sessions)
- **Security research iterations**: 10 (87/100 robustness score)
- **Game-design research tracks**: 10
- **Competition analyses**: 8
- **Completed task sets**: 80+ tasks across 5 domains
- **Active task queue**: 18 tasks (8 figure + 10 game-design)
