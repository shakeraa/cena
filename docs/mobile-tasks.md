# CENA Mobile App — Task Registry & Integration Status

> **Date:** 2026-03-31
> **Status:** Active
> **Scope:** Flutter mobile app (`src/mobile/`) alignment with backend + research implementation

---

## Architecture Overview

```
Flutter Client (Riverpod + GoRouter)
    │ SignalR JSON protocol (web_socket_channel)
    ▼
CenaHub.cs (/hub/cena) — JWT auth, rate-limiting, BusEnvelope wrapping
    │ NATS publish (cena.session.*, cena.mastery.*)
    ▼
NatsBusRouter (hosted service) — subscribes, routes to Proto.Actor virtual actors
    │ Proto.Actor cluster grain activation
    ▼
StudentActor → spawns → LearningSessionActor
    │ Evaluates, updates mastery (BKT), publishes domain events
    ▼
NATS Events (cena.events.student.{studentId}.*)
    │ NatsSignalRBridge subscribes to per-student wildcard
    ▼
SignalR Group Push → Flutter Client (MessageEnvelope stream)
```

**Key files:**
- Hub: `src/api/Cena.Api.Host/Hubs/CenaHub.cs`
- Contracts: `src/api/Cena.Api.Host/Hubs/HubContracts.cs`
- Bridge: `src/api/Cena.Api.Host/Hubs/NatsSignalRBridge.cs`
- NATS subjects: `src/actors/Cena.Actors/Bus/NatsSubjects.cs`
- NATS router: `src/actors/Cena.Actors/Bus/NatsBusRouter.cs`
- Student actor: `src/actors/Cena.Actors/Students/StudentActor.Commands.cs`
- Session actor: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`
- Mobile WS impl: `src/mobile/lib/core/services/websocket_service_impl.dart`
- Mobile session: `src/mobile/lib/core/state/session_notifier.dart`
- Mobile config: `src/mobile/lib/core/config/app_config.dart`

---

## P0 — Integration Fixes (Mobile ↔ Backend)

These must be fixed before any live session can work.

### MOB-INT-001: Fix Hub URL Mismatch

| Field | Value |
|-------|-------|
| **Mobile** | `wss://dev-api.cena.education/hub/learning` (app_config.dart:43) |
| **Backend** | Mapped at `/hub/cena` (SignalRConfiguration.cs:83) |
| **Fix** | Change mobile URLs from `/hub/learning` to `/hub/cena` |
| **Files** | `src/mobile/lib/core/config/app_config.dart` |

### MOB-INT-002: Wire Firebase Token Provider

| Field | Value |
|-------|-------|
| **Current** | `signalr_provider.dart:19` returns empty string `''` |
| **Required** | `FirebaseAuth.instance.currentUser?.getIdToken()` |
| **Files** | `src/mobile/lib/core/state/signalr_provider.dart` |

### MOB-INT-003: Override Providers in main.dart

| Field | Value |
|-------|-------|
| **Current** | `webSocketServiceProvider` throws `UnimplementedError` |
| **Current** | `syncManagerProvider` throws `UnimplementedError` |
| **Required** | Override both in ProviderScope using `signalRProvider` and concrete sync impls |
| **Files** | `src/mobile/lib/main.dart`, `src/mobile/lib/core/state/derived_providers.dart` |

### MOB-INT-004: Align Event Names

| Mobile Expects | Backend Sends | Action |
|---|---|---|
| `SessionStarted` | `SessionStarted` | OK |
| `QuestionPresented` | (not sent) | Backend must push after session start + each answer eval, OR mobile fetches via REST |
| `AnswerEvaluated` | `AnswerEvaluated` | OK |
| `MethodologySwitched` | `MethodologySwitched` | OK |
| `CognitiveLoadWarning` | `StagnationDetected` / `MicrobreakSuggested` (not wired to SignalR) | Map `StagnationDetected` → `CognitiveLoadWarning` in mobile, or wire `MicrobreakSuggested` through NatsSignalRBridge |
| `SessionSummary` | `SessionEnded` | Rename in mobile handler |
| (not handled) | `XpAwarded` | Add handler → gamification_state |
| (not handled) | `StreakUpdated` | Add handler → gamification_state |
| (not handled) | `HintDelivered` | Add handler → session_notifier |

### MOB-INT-005: Remove GraphQL Endpoint

| Field | Value |
|-------|-------|
| **Decision** | CENA uses REST + SignalR, no GraphQL (per feedback_no_graphql.md) |
| **Files** | `src/mobile/lib/core/config/app_config.dart` — remove `graphqlEndpoint` field |

### MOB-INT-006: Wire Cognitive Load to Backend Events

| Field | Value |
|-------|-------|
| **Backend** | Publishes `StagnationDetected` via SignalR bridge. `MicrobreakSuggested` defined in NATS subjects but NOT wired to bridge. |
| **Mobile** | Listens for `CognitiveLoadWarning` with `fatigueScore` payload |
| **Fix** | Listen for `StagnationDetected` in session_notifier, extract fatigue data, trigger break suggestion |

### MOB-INT-007: Wire studentId Into Commands

| Field | Value |
|-------|-------|
| **Current** | `session_notifier.dart:142` sends `studentId: ''` |
| **Required** | Read from `userProvider` / `authNotifierProvider` |
| **Files** | `src/mobile/lib/core/state/session_notifier.dart` |

---

## P1 — Core Features

### MOB-CORE-001: Wire Language Switching
- Connect settings locale ListTiles to `currentLocaleProvider`
- Persist selection to SharedPreferences
- Re-render entire widget tree on locale change
- **Files:** `home_screen.dart` (settings tab), `app.dart`

### MOB-CORE-002: Session History (Learn Tab)
- Fetch past sessions from `GET /api/sessions`
- Display list with subject, accuracy, duration, date
- "Continue where you left off" card using `GET /api/sessions/active`
- **Files:** `home_screen.dart` (_SessionsTabContent)

### MOB-CORE-003: Knowledge Graph Visualization
- Force-directed graph layout with concept nodes + prerequisite edges
- Mastery color interpolation (red → yellow → green)
- Zoom/pan gestures, node tap for detail
- **Files:** `knowledge_graph_screen.dart`, new `knowledge_graph_renderer.dart`
- **Backend:** `GET /api/knowledge-graph/{studentId}`

### MOB-CORE-005: Adaptive Difficulty (ZPD Targeting)
- Track P(correct) in rolling window, target 0.55-0.75
- Send difficulty preference signals to backend
- Display difficulty indicator in session UI
- **Backend:** FlowMonitorActor already adjusts difficulty server-side

### MOB-CORE-006: Try-Question Before Signup
- Show 1 seed question anonymously before auth gate
- On correct/incorrect, show value prop + auth screen
- Convert to full account, preserve answer data
- **Files:** `router.dart`, new `try_question_screen.dart`

### MOB-CORE-007: Notification Center Screen
- List FCM notifications with read/unread states
- Tap-to-navigate via deep links
- Badge count on app bar bell icon
- **Files:** new `notification_center_screen.dart`

### MOB-CORE-008: Profile Screen
- Avatar, display name, email, school, grade, Bagrut track
- Account actions (edit profile, delete account)
- **Files:** new `profile_screen.dart`

---

## P1 — Visual Design

### MOB-VIS-001: Glassmorphism / Liquid Glass Design System
- Add `glassmorphic_ui_kit` package
- Create `GlassCard`, `GlassContainer` reusable widgets
- Apply to dashboard cards, modal overlays, navigation
- **Research ref:** CENA_UI_UX_Design_Strategy_2026.md §1.1-1.3

### MOB-VIS-002: Bento Grid Dashboard
- Replace simple home tab with 6-module bento layout
- Modules: progress ring, daily challenge, badges shelf, continue learning hero, skill tree preview, leaderboard preview
- **Research ref:** CENA_UI_UX_Design_Strategy_2026.md §3.1

---

## P1 — AI & Learning

### MOB-AI-001: AI Tutor Chat Interface
- Conversation UI with message bubbles (left=tutor, right=student)
- Typing indicator animation
- Quick-reply chips for common actions
- Math symbol keyboard overlay
- Voice input button (speech-to-text)
- **Backend:** `TutoringStarted`, `TutorMessage`, `TutoringEnded` events already in HubContracts
- **Files:** new `features/tutor/` feature directory

---

## P1 — Testing

### MOB-TEST-001: Core Unit Tests
- `auth_service_test.dart` — sign-in flows, error mapping, claims extraction
- `session_notifier_test.dart` — event handling, state transitions, offline fallback
- `gamification_state_test.dart` — XP calculation, level progression, badge award
- `momentum_state_test.dart` — rolling window, anxiety detection, switch suggestion
- `offline_sync_test.dart` — queue/dequeue, retry logic, conflict resolution
- `websocket_service_test.dart` — handshake, reconnection, offline queue flush

### MOB-TEST-002: Widget Tests
- `auth_screen_test.dart`, `home_screen_test.dart`, `session_screen_test.dart`
- `gamification_screen_test.dart`, `onboarding_screen_test.dart`

### MOB-TEST-003: Integration Tests
- Full session flow: start → answer → feedback → end
- Offline queue → sync cycle
- Auth → onboarding → home redirect

---

## P2 — Enhancement Features

| ID | Task | Category |
|----|------|----------|
| MOB-VIS-003 | Dark Mode OLED polish | Visual |
| MOB-VIS-004 | Micro-interactions & animations | Visual |
| MOB-VIS-005 | 3D Achievement Badges with rarity | Visual |
| MOB-CORE-004 | Skill Tree / Mastery Map | Core |
| MOB-AI-002 | FSRS Spaced Repetition client | Learning |
| MOB-GAM-001 | Leaderboard widget | Gamification |
| MOB-GAM-002 | Variable Reward System | Gamification |
| MOB-SOC-001 | Class activity feed | Social |
| MOB-DIAG-001 | Diagram rendering widget | Content |
| MOB-A11Y-001 | Accessibility pass | Accessibility |
| MOB-ETHICS-001 | Quiet hours + wellbeing limits | Ethics |

---

## P3 — Future

| ID | Task | Category |
|----|------|----------|
| MOB-GAM-003 | Age-stratified gamification intensity | Gamification |
| MOB-SOC-002 | Study groups | Social |
| MOB-A11Y-002 | Tablet & adaptive layout | Accessibility |

---

## Command ↔ Event Mapping (Mobile ↔ Backend)

### Client → Server Commands

| Mobile Method | SignalR Target | NATS Subject | Actor Handler |
|---|---|---|---|
| `startSession()` | `StartSession` | `cena.session.start` | `StudentActor.HandleStartSession()` |
| `attemptConcept()` | `SubmitAnswer` | `cena.mastery.attempt` | `StudentActor.HandleAttemptConcept()` |
| `requestHint()` | `RequestHint` | `cena.session.annotate` | `LearningSessionActor.HandleHint()` |
| `skipQuestion()` | `SkipQuestion` | (via session actor) | `LearningSessionActor.HandleSkip()` |
| `switchApproach()` | `SwitchApproach` | `cena.mastery.switch` | `StudentActor.HandleSwitchMethodology()` |
| `endSession()` | `EndSession` | `cena.session.end` | `StudentActor.HandleEndSession()` |

### Server → Client Events

| Backend Event | NATS Pattern | Bridge Routes? | Mobile Handler |
|---|---|---|---|
| `SessionStarted` | `*.session_started` | Yes | `_onSessionStarted` |
| `SessionEnded` | `*.session_ended` | Yes | Needs rename from `SessionSummary` |
| `AnswerEvaluated` | `*.answer_evaluated` | Yes | `_onAnswerEvaluated` |
| `MasteryUpdated` | `*.mastery_updated` | Yes | Not handled (add to gamification) |
| `HintDelivered` | `*.hint_delivered` | Yes | Not handled (add to session) |
| `MethodologySwitched` | `*.methodology_switched` | Yes | `_onMethodologySwitched` |
| `StagnationDetected` | `*.stagnation_detected` | Yes | Map to cognitive load warning |
| `XpAwarded` | `*.xp_awarded` | Yes | Not handled (add to gamification) |
| `StreakUpdated` | `*.streak_updated` | Yes | Not handled (add to gamification) |
| `TutoringStarted` | `*.tutoring_started` | Yes | Not handled (future AI tutor) |
| `TutorMessage` | `*.tutor_message` | Yes | Not handled (future AI tutor) |
| `TutoringEnded` | `*.tutoring_ended` | Yes | Not handled (future AI tutor) |
| `Error` | (hub-level) | N/A | Not handled (add error snackbar) |
| `CommandAck` | (hub-level) | N/A | Not handled (optional) |

---

*Generated: 2026-03-31*
