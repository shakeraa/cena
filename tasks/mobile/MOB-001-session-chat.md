# MOB-001: Mobile Session & Tutoring Chat — React Native

**Priority:** P0 — students primarily use mobile app
**Blocked by:** SES-001 (SignalR hub), mobile app scaffold (if not started)
**Estimated effort:** 5 days
**Contract:** `contracts/frontend/signalr-messages.ts`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The student mobile app (React Native) is the primary learning interface. It needs the same session flow as the web app (WEB-009) adapted for mobile: session start, question flow, answer input, real-time tutoring chat, and session summary. All real-time communication uses `@microsoft/signalr` over WebSocket to the `CenaHub` (SES-001). REST endpoints (SES-002) handle history and analytics.

## Subtasks

### MOB-001.1: SignalR Client for React Native

**Files:**
- `src/mobile/src/services/signalr/hub-client.ts` — SignalR connection manager
- `src/mobile/src/services/signalr/types.ts` — shared message types (from contract)

**Acceptance:**
- [ ] Uses `@microsoft/signalr` with WebSocket transport (skip SSE/long-polling)
- [ ] `accessTokenFactory` returns Firebase ID token from auth context
- [ ] Reconnection: exponential backoff (1s–30s), 8 max retries
- [ ] Background handling: disconnect on app background, reconnect on foreground
- [ ] Network state listener: pause reconnection when offline, resume when online
- [ ] `invoke<T>(command, payload)` → typed response promise (correlationId matching)
- [ ] `on<T>(event, handler)` → unsubscribe function

### MOB-001.2: Session Flow Screens

**Files:**
- `src/mobile/src/screens/session/StartSessionScreen.tsx` — subject picker
- `src/mobile/src/screens/session/ActiveSessionScreen.tsx` — question + answer
- `src/mobile/src/screens/session/SessionSummaryScreen.tsx` — results

**Acceptance:**
- [ ] Subject selection: scrollable cards with mastery ring indicator
- [ ] Question display: large text, concept badge, methodology indicator
- [ ] Answer input adapts to format: text input, radio buttons (MC), numeric keypad
- [ ] Timer visible but not distracting (top bar)
- [ ] Correct answer: haptic feedback + XP animation
- [ ] Wrong answer: explanation card + "Need help?" button
- [ ] Session summary: score card, mastery deltas, XP earned
- [ ] RTL layout for Hebrew/Arabic (react-native-rtl)

### MOB-001.3: Tutoring Chat Screen

**Files:**
- `src/mobile/src/screens/session/TutoringChatScreen.tsx` — in-session tutoring
- `src/mobile/src/components/ChatBubble.tsx` — message bubble

**Acceptance:**
- [ ] Bottom sheet or full screen overlay when tutoring starts
- [ ] `FlatList` with inverted scroll (newest at bottom)
- [ ] Tutor messages: left-aligned, colored bubble
- [ ] Student messages: right-aligned, gray bubble
- [ ] Text input with send button at bottom
- [ ] "Turn X of 10" indicator
- [ ] Auto-scroll to new messages
- [ ] Keyboard-aware layout (input stays above keyboard)
- [ ] RTL text per message

### MOB-001.4: Session History & Analytics

**Files:**
- `src/mobile/src/screens/session/SessionHistoryScreen.tsx`
- `src/mobile/src/screens/session/AnalyticsScreen.tsx`

**Acceptance:**
- [ ] Session history: `FlatList` with session cards (subject, date, score)
- [ ] Pull-to-refresh + infinite scroll pagination
- [ ] Analytics: mastery chart (per concept), streak calendar, XP progress bar
- [ ] Data from REST endpoints (SES-002)

## Definition of Done
- [ ] Full mobile session flow works end-to-end with SignalR hub
- [ ] Tutoring chat works with multi-turn conversation
- [ ] Background/foreground reconnection tested
- [ ] RTL verified for Hebrew
- [ ] Builds on iOS and Android
