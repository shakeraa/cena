# CENA Feature Verification Report

> **Date:** 2026-03-31
> **Purpose:** Map every claimed competitive advantage to actual code in `src/mobile/lib/`
> **Source:** extracted-features.md (129 features) cross-referenced against codebase
> **Result:** 35 fully implemented, 16 partial, 78 gaps

---

## Summary

| Status | Count | Action |
|--------|-------|--------|
| **FULLY IMPLEMENTED** | 35 | Defend, polish, market |
| **PARTIAL** (data exists, UI/logic incomplete) | 16 | Complete the last mile — mostly UI screens |
| **GAP** (missing entirely) | 78 | Prioritized in tasks-p0/p1/p2 docs |

---

## Fully Implemented (35 features)

These are real, production-grade, code-verified competitive advantages.

### Learning Engine (5)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| A2 | AI tutoring sessions | `features/tutor/tutor_chat_screen.dart`, `features/tutor/tutor_state.dart` | WebSocket streaming, message buffering, real-time chat |
| A3 | Step-by-step solutions | `features/session/widgets/hint_chip.dart` | 3-level hint progression (clue -> bigger clue -> full solution) with XP decay per hint |
| A4 | Adaptive difficulty | `core/services/adaptive_difficulty_service.dart` | ZPD (Zone of Proximal Development) targeting, rolling accuracy window |
| A5 | Multiple question types | `core/models/domain_models.dart` | QuestionType enum: MCQ, free text, numeric, proof, diagram |
| A10 | Offline content & sync | `core/services/offline_sync_service.dart` | Event queue, conflict resolution, durable storage, connectivity monitoring |

### Deep Study & Sessions (1)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| A13 | Deep study mode | `features/session/widgets/deep_dive_sheet.dart`, `features/session/models/deep_study_session.dart` | Extended focus sessions, deep dive sheets |

### Spaced Repetition & Memory Science (3)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| B1 | FSRS-4.5 algorithm | `core/services/fsrs_scheduler.dart` | Full FSRS-4.5 with 15 learnable parameters, card state machine. Only Anki matches this |
| B3 | Adaptive interleaving | `core/services/adaptive_interleaving.dart` | Stochastic P(interleave) formula based on mastery, dynamic blocking/mixing |
| B4 | Confidence-based scheduling | `features/session/widgets/confidence_rating.dart` | Post-answer confidence ratings feed into FSRS scheduling |

### Gamification (10)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| C1 | Quality-gated streaks | `core/services/streak_quality_gate.dart` | Qualifying session threshold required, vacation mode, freeze mechanics |
| C9 | XP system | `core/state/gamification_state.dart` | Cumulative XP, daily XP, first-session bonus, level formula: `100*n*(1+0.1*n)` |
| C10 | Badges / achievements | `features/gamification/badge_3d_widget.dart`, `core/state/gamification_state.dart` | 3D badges with 4 rarity tiers (common, rare, epic, legendary), unlock animations |
| C11 | Boss battles | `features/session/models/boss_battle.dart`, `features/session/widgets/boss_battle_screen.dart` | Triggered at 80%+ module mastery, 3 lives, power-ups, victory/defeat outcomes |
| C12 | Quest system | `features/gamification/models/quest_models.dart`, `features/gamification/services/quest_generator.dart` | Daily/weekly/monthly/side quests with sealed QuestCriteria hierarchy |
| C13 | Class leaderboards | `features/gamification/leaderboard_widget.dart` | Weekly reset, aggregate stats, lateral peer comparison |
| C14 | Daily challenges | `features/social/daily_challenge_card.dart` | Challenge of the Day MCQ with anonymous class voting, aggregate percentages |
| C15 | Celebration animations | `features/gamification/celebration_service.dart`, `features/gamification/celebration_overlay.dart` | 5-tier system: micro, minor, medium, major, epic — proportional to achievement |
| C24 | Confidence mode | `features/session/widgets/confidence_rating.dart` | Self-reported confidence ratings influencing subsequent difficulty |

### Onboarding (3)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| D2 | Goal setting | `features/onboarding/widgets/goal_setting_card.dart` | Goal types: Bagrut prep, homework help, get ahead, review |
| D3 | Learning pace selection | `features/onboarding/onboarding_screen.dart` | Time commitment picker: 5/10/15/20 min per session |
| D4 | Progressive disclosure | `core/services/disclosure_level_service.dart` | Feature revelation based on usage level |

### Social (2)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| E1 | Peer solution sharing | `features/social/widgets/peer_solutions_sheet.dart`, `core/state/peer_solutions_state.dart` | Methodology tracking (spaced repetition, socratic, etc.) |
| E2 | Class social feed | `features/social/class_activity_feed.dart`, `core/state/social_feed_state.dart` | k-anonymity, teacher endorsement cards, optional reactions |

### AI & Personalization (3)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| F1 | Socratic AI tutor | `features/tutor/tutor_chat_screen.dart` | `Methodology.socratic` enumerated, conversational flow management |
| F3 | Adaptive content sequencing | `core/services/adaptive_difficulty_service.dart` + `core/services/adaptive_interleaving.dart` | Mastery-driven, rolling accuracy, interleaving probability |
| F4 | Knowledge graph + prerequisites | `features/knowledge_graph/knowledge_graph_screen.dart`, `core/state/knowledge_graph_notifier.dart`, `features/knowledge_graph/knowledge_graph_renderer.dart` | prerequisiteIds per concept, graph visualization, mastery overlays |

### Visualization (2)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| G1 | Interactive diagrams | `features/diagrams/diagram_viewer.dart`, `features/diagrams/models/diagram_models.dart` | SVG-based, pinch-zoom, pan, hotspots, formula chips, drag-label challenges |
| G2 | Knowledge graph visible | `features/knowledge_graph/knowledge_graph_screen.dart` | Subject filters, mastery overlays, dependency visualization |

### Wellbeing (4)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| H1 | Session time limits | `core/services/quiet_hours_service.dart` | 90/120/180 min daily limits with break enforcement |
| H2 | Bedtime mode | `core/services/bedtime_mode_service.dart`, `core/services/quiet_hours_service.dart` | 9PM-7AM quiet hours, zero notifications, review-only restriction |
| H3 | Flow state monitoring | `core/services/flow_monitor_service.dart` | 5-signal weighted formula: focus, challenge/skill balance, consistency, inverse fatigue, voluntary engagement |
| H4 | Age-tiered safety | `core/services/age_safety_service.dart` | COPPA compliance, 4 age tiers (6-9, 10-12, 13-15, 16+), feature gating |

### Retention (2)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| I1 | Intelligent notifications | `core/services/notification_intelligence_service.dart` | Daily budget of 2, priority ranking, quiet hours enforcement, streak risk detection |
| I2 | Habit stacking | `core/services/routine_profile_service.dart` | Learns from 14+ days of session data, preferred study windows, weekday patterns |

### Platform (3)

| ID | Feature | File(s) | Implementation Quality |
|----|---------|---------|----------------------|
| J3 | Arabic + Hebrew RTL | `l10n/app_localizations_ar.dart`, `l10n/app_localizations_he.dart`, `core/theme/cena_theme.dart` | Full RTL support, locale-aware typography, Directionality widget |
| J4 | Dark mode | `core/theme/cena_theme.dart` | Material 3 dark theme, OLED-friendly Slate 900 background |
| J5 | Accessibility | `core/services/accessibility_service.dart` | Dyslexia font, reduced motion, enlarged touch targets, high contrast, color-blind mode |

---

## Partial Implementation (16 features)

These have backend services or data models but are missing UI, visualization, or orchestration layers.

### Originally Claimed as HAVE — Reclassified to PARTIAL (7)

| ID | Feature | What EXISTS | What's MISSING | Fix Effort |
|----|---------|-------------|----------------|------------|
| A12 | Micro-lesson format | Session architecture supports configurable lengths | No dedicated "micro-lesson" mode or label | S (1-2 weeks) |
| B2 | Forgetting curve viz | SRS state in `core/state/srs_state.dart` tracks all data | No chart/graph rendering the curve to students | S (2-3 weeks) |
| F2 | Personalized learning path | MasteryState + Concept models with prerequisites | No "recommended next lesson" UI or path visualization | M (3-4 weeks) |
| F5 | Weakness detection | Error types tracked: conceptual, procedural, careless, notation, incomplete | No "Your Weak Areas" screen surfacing this data | S (2-3 weeks) |
| M1 | Progress dashboard | Profile screen + gamification screen exist | Not a consolidated single-view dashboard | M (3-4 weeks) |
| M2 | Mastery % per topic | `knowledge_graph_notifier.dart` tracks P(Known) per concept | Mastery % not in a prominent list/grid view | S (1-2 weeks) |
| M3 | Weak area identification | Adaptive difficulty detects struggling concepts | No explicit "weak areas" UI surfacing to student | S (2-3 weeks) |

### Originally Claimed as PARTIAL (9)

| ID | Feature | What EXISTS | What's MISSING | Fix Effort |
|----|---------|-------------|----------------|------------|
| D1 | Placement test | Onboarding has goal setting + pace selection | No adaptive diagnostic assessment on first use | M (4-5 weeks) |
| F6 | AI tutor personality | AI tutor functional | Warm named personality, growth mindset prompts, effort celebration | S (2-3 weeks) |
| F9 | AI practice problem gen | Session question flow exists | Auto-generate custom practice from topic/difficulty on demand | M (3-4 weeks) |
| G10 | Animation explanations | Basic UI animations exist | Animated concept explainers (Brilliant-style) | L (6-8 weeks) |
| H5 | Frustration detection | Flow monitor tracks signals | No explicit frustration → intervention trigger | S (2-3 weeks) |
| H9 | Growth mindset messaging | Some encouragement in UI | Not systematic in AI tutor responses | S (1-2 weeks) |
| M6 | Learning time tracking | Session timestamps captured | No prominent time-spent display in profile/dashboard | S (1-2 weeks) |
| M7 | Historical trends | Data exists in session history | No charts showing progress over time | S (2-3 weeks) |
| N5 | Neurodiverse design | Accessibility service has dyslexia font, reduced motion | Not branded as neurodiverse-first, no ADHD-specific mode | S (2-3 weeks) |

---

## Feature Architecture Map

```
src/mobile/lib/
├── core/
│   ├── models/
│   │   └── domain_models.dart          ← QuestionType, MasteryState, Concept, Methodology
│   ├── services/
│   │   ├── adaptive_difficulty_service.dart    ← A4: ZPD targeting
│   │   ├── adaptive_interleaving.dart         ← B3: stochastic interleaving
│   │   ├── age_safety_service.dart            ← H4: COPPA, 4 age tiers
│   │   ├── accessibility_service.dart         ← J5: dyslexia, motion, contrast
│   │   ├── bedtime_mode_service.dart          ← H2: quiet hours
│   │   ├── disclosure_level_service.dart      ← D4: progressive disclosure
│   │   ├── flow_monitor_service.dart          ← H3: 5-signal flow state
│   │   ├── fsrs_scheduler.dart                ← B1: FSRS-4.5 (CROWN JEWEL)
│   │   ├── notification_intelligence_service.dart ← I1: daily budget of 2
│   │   ├── offline_sync_service.dart          ← A10, J2: offline event queue
│   │   ├── quiet_hours_service.dart           ← H1, H2: time limits
│   │   ├── routine_profile_service.dart       ← I2: habit learning (14+ days)
│   │   └── streak_quality_gate.dart           ← C1: quality-gated streaks
│   ├── state/
│   │   ├── gamification_state.dart            ← C9: XP, levels
│   │   ├── knowledge_graph_notifier.dart      ← F4, M2: mastery per concept
│   │   ├── offline_notifier.dart              ← J2: connectivity state
│   │   ├── peer_solutions_state.dart          ← E1: peer solutions
│   │   ├── social_feed_state.dart             ← E2: class feed
│   │   └── srs_state.dart                     ← B2: SRS data (needs viz)
│   └── theme/
│       └── cena_theme.dart                    ← J4: dark mode, J3: RTL
│
├── features/
│   ├── diagrams/
│   │   ├── diagram_viewer.dart                ← G1: SVG, pinch-zoom, hotspots
│   │   └── models/diagram_models.dart
│   ├── gamification/
│   │   ├── badge_3d_widget.dart               ← C10: 4 rarity tiers
│   │   ├── celebration_service.dart           ← C15: 5-tier celebrations
│   │   ├── celebration_overlay.dart
│   │   ├── leaderboard_widget.dart            ← C13: weekly class leaderboard
│   │   ├── models/quest_models.dart           ← C12: sealed QuestCriteria
│   │   └── services/quest_generator.dart      ← C12: daily/weekly/monthly
│   ├── knowledge_graph/
│   │   ├── knowledge_graph_screen.dart        ← F4, G2: graph + mastery overlays
│   │   ├── knowledge_graph_renderer.dart
│   │   └── (knowledge_graph_notifier in core/state/)
│   ├── onboarding/
│   │   ├── onboarding_screen.dart             ← D3: pace selection
│   │   └── widgets/goal_setting_card.dart     ← D2: goal types
│   ├── session/
│   │   ├── session_screen.dart                ← A12: session architecture
│   │   ├── models/
│   │   │   ├── boss_battle.dart               ← C11: boss battle model
│   │   │   └── deep_study_session.dart        ← A13: deep study
│   │   └── widgets/
│   │       ├── boss_battle_screen.dart        ← C11: 3 lives, power-ups
│   │       ├── confidence_rating.dart         ← B4, C24: confidence input
│   │       ├── deep_dive_sheet.dart           ← A13: extended focus
│   │       └── hint_chip.dart                 ← A3: 3-level hints
│   ├── social/
│   │   ├── class_activity_feed.dart           ← E2: k-anonymity feed
│   │   ├── daily_challenge_card.dart          ← C14: challenge of the day
│   │   └── widgets/peer_solutions_sheet.dart  ← E1: peer solutions
│   ├── tutor/
│   │   ├── tutor_chat_screen.dart             ← A2, F1: Socratic AI tutor
│   │   └── tutor_state.dart
│   └── profile/
│       └── profile_screen.dart                ← M1: progress (partial)
│
└── l10n/
    ├── app_localizations_ar.dart              ← J3: Arabic RTL
    └── app_localizations_he.dart              ← J3: Hebrew RTL
```

---

## Key Takeaways

### Crown Jewels (defend at all costs)
1. **FSRS-4.5** (`fsrs_scheduler.dart`) — Only Anki matches this. No commercial competitor has it.
2. **Knowledge Graph** (`knowledge_graph_screen.dart` + `knowledge_graph_notifier.dart`) — Unique in market.
3. **Quality-Gated Streaks** (`streak_quality_gate.dart`) — Superior to Duolingo's login-based streaks.
4. **Arabic + Hebrew RTL** (`l10n/`) — Only dual-RTL ed-tech platform.
5. **Wellbeing Suite** (`bedtime_mode_service.dart`, `flow_monitor_service.dart`, `age_safety_service.dart`) — Largely unique.

### Quick Wins (partial -> fully implemented)
The 7 reclassified features need UI screens, not backend work. Total effort: ~15-20 weeks for all 7.

### Biggest Vulnerability
The 78 GAP features include table-stakes engagement mechanics (leagues, hearts, gems, camera/OCR) that competitors have and CENA lacks. The P0 tasks address the most critical ones.
