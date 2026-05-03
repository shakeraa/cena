# WEB-009: Student Session UI — Learning Interface, Question Flow, Tutoring Chat

**Priority:** P0 — core student-facing experience
**Blocked by:** WEB-001 (scaffold), WEB-002 (SignalR client), WEB-004 (state)
**Estimated effort:** 5 days
**Contract:** `contracts/frontend/signalr-messages.ts`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The student web app (React PWA) needs the core learning session interface. Students start a session, receive questions via SignalR, submit answers, see mastery feedback, receive hints, and engage in multi-turn tutoring conversations. All real-time interaction goes through the SignalR `CenaHub` (SES-001). Session history and analytics use REST (SES-002).

## Subtasks

### WEB-009.1: Session Start & Subject Selection

**Files:**
- `src/web/src/features/session/SessionStartPage.tsx` — subject picker + start button
- `src/web/src/features/session/hooks/useSession.ts` — session state management

**Acceptance:**
- [ ] Route: `/session/start`
- [ ] Shows available subjects from `GET /api/analytics/mastery` (grouped by subject)
- [ ] Each subject card shows: name, mastery %, last practiced date, recommended badge
- [ ] "Start Session" sends `StartSession` command via SignalR
- [ ] On `SessionStarted` event: navigate to `/session/active`
- [ ] If active session exists (from `GET /api/sessions/active`): show "Resume" option
- [ ] Resume sends `POST /api/sessions/{sessionId}/resume`
- [ ] RTL layout support for Hebrew/Arabic

### WEB-009.2: Active Session — Question Display & Answer

**Files:**
- `src/web/src/features/session/ActiveSessionPage.tsx` — main session container
- `src/web/src/features/session/components/QuestionCard.tsx` — question display
- `src/web/src/features/session/components/AnswerInput.tsx` — answer input (multi-format)
- `src/web/src/features/session/components/SessionHeader.tsx` — progress bar, timer, streak

**Acceptance:**
- [ ] Route: `/session/active`
- [ ] Listens for `QuestionPresented` events via SignalR
- [ ] `QuestionCard` renders: question text, methodology badge, difficulty indicator, concept name
- [ ] `AnswerInput` handles 5 formats: `free-text` (textarea), `multiple-choice` (radio group), `numeric` (number input), `proof` (multi-step textarea), `graph-sketch` (placeholder for v2)
- [ ] Submit sends `SubmitAnswer` command with `responseTimeMs` (auto-tracked from question shown → submit)
- [ ] `SessionHeader` shows: question index / total, accuracy %, current streak, session timer
- [ ] On `AnswerEvaluated`: show correct/incorrect feedback with explanation
- [ ] Correct: green flash + XP animation (from `XpAwarded` event)
- [ ] Incorrect: show explanation, offer "Need help?" → triggers `AddAnnotation(kind: 'confusion')`
- [ ] "Request Hint" button sends `RequestHint`, shows hint from `HintDelivered` event
- [ ] "Skip" button sends `SkipQuestion`
- [ ] `CognitiveLoadWarning` event: show break suggestion overlay

### WEB-009.3: Tutoring Chat Overlay

**Files:**
- `src/web/src/features/session/components/TutoringChat.tsx` — chat overlay during session
- `src/web/src/features/session/components/TutoringMessage.tsx` — individual message bubble

**Acceptance:**
- [ ] Appears as slide-up overlay when `TutoringStarted` event received
- [ ] Shows tutor messages as they arrive (`TutorMessage` events)
- [ ] Student can type replies → sends `SessionTutor` command via SignalR
- [ ] Message bubbles: tutor (left, blue), student (right, gray)
- [ ] Shows turn count (e.g., "Turn 3 of 10")
- [ ] "End Chat" button sends annotation to close tutoring
- [ ] On `TutoringEnded`: show summary, close overlay, resume questions
- [ ] RTL text direction per message language

### WEB-009.4: Session Summary

**Files:**
- `src/web/src/features/session/SessionSummaryPage.tsx` — post-session summary
- `src/web/src/features/session/components/MasteryDelta.tsx` — before/after mastery visualization

**Acceptance:**
- [ ] Route: `/session/summary/{sessionId}`
- [ ] Displayed when `SessionSummary` event received or session ends
- [ ] Shows: total questions, correct count, accuracy %, time spent, XP earned
- [ ] `MasteryDelta` per concept: before vs after mastery bar
- [ ] Methodology used during session
- [ ] "Practice Again" button → back to `/session/start`
- [ ] "View History" → `/session/history`
- [ ] Data from `GET /api/sessions/{sessionId}`

### WEB-009.5: Session History Page

**Files:**
- `src/web/src/features/session/SessionHistoryPage.tsx` — past sessions list
- `src/web/src/features/session/components/SessionCard.tsx` — session summary card

**Acceptance:**
- [ ] Route: `/session/history`
- [ ] Paginated list from `GET /api/sessions`
- [ ] `SessionCard`: date, subject, accuracy %, question count, duration, methodology
- [ ] Click → `/session/summary/{sessionId}` (detail view)
- [ ] Filter by subject, date range
- [ ] Sort by date (newest first)

## Definition of Done
- [ ] Full session flow works: start → question → answer → feedback → hint → tutoring → summary
- [ ] All SignalR events handled (no unhandled event warnings)
- [ ] RTL layout verified for Hebrew
- [ ] `npm test` passes
- [ ] `npm run build` succeeds with no TypeScript errors
- [ ] Offline: if disconnected mid-session, show reconnection banner (from WEB-002.3)
