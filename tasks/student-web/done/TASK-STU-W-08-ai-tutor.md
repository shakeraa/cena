# TASK-STU-W-08: AI Tutor

**Priority**: HIGH — primary "ask anything" surface and a major differentiator
**Effort**: 5-7 days
**Phase**: 3
**Depends on**: [STU-W-05](TASK-STU-W-05-home-dashboard.md)
**Backend tasks**: [STB-04](../student-backend/TASK-STB-04-tutor.md)
**Status**: Not Started

---

## Goal

Deliver a streaming, context-aware conversational tutor with text, voice, and image input, tool-call visualization, inline citations, and a revokable context panel — reachable from every feature entry point in the app.

## Spec

Full specification in [docs/student/07-ai-tutor.md](../../docs/student/07-ai-tutor.md). All 22 `STU-TUT-*` acceptance criteria form this task's checklist. This task also builds on the existing backend work tracked in [docs/tasks/student-ai-interaction/README.md](../../docs/tasks/student-ai-interaction/README.md).

## Scope

In scope:

- `/tutor` page with 3-column layout: threads list (left), conversation (middle), context panel (right), each collapsible
- `/tutor/:threadId` for deep linking
- Components:
  - `<TutorThreadList>` — grouped by date, pinned threads at top, search
  - `<TutorConversation>` — streaming message renderer
  - `<TutorMessage>` — markdown + KaTeX + code blocks + citations
  - `<TutorComposer>` — text input with voice, image, math, send
  - `<TutorContextPanel>` — list of shared items with per-item toggle
  - `<TutorToolCallCard>` — collapsible card showing tool name, input, result
  - `<TutorSuggestionChips>` — starter prompts seeded by student context
  - `<TutorCitation>` — inline tap-to-expand citation
- Streaming via SignalR events: `TutorTokenStreamed`, `TutorToolCalled`, `TutorToolCompleted`, `TutorMessageComplete`
- `useTutorThread(threadId)` composable — loads messages, subscribes to stream, exposes `send()` + `cancel()`
- Voice input via Web Speech API (`SpeechRecognition`) — continuous mode
- Voice output via server TTS endpoint (STB-04); toggleable; plays inline with the message
- Image upload → `POST /api/tutor/ocr` → insert extracted text into the composer; support drag-drop onto the composer area
- Math keyboard (shared with STU-W-15)
- Whiteboard component `<TutorWhiteboard>` for shared sketching; strokes save to the thread
- Inline graphing via a `<GraphInline>` tool-call renderer (Desmos-like)
- Code blocks with syntax highlighting (Shiki or Prism, lazy loaded)
- Thread rename / pin / delete / export / share actions
- Auto-generated thread title after first exchange (LLM-side; client just displays)
- Entry points:
  - Sidebar → `/tutor`
  - Session feedback overlay → "Ask the tutor" opens side-panel scope
  - Knowledge graph concept → "Ask about this concept" prefills
  - Progress mastery → "Why am I stuck?"
  - Wrong-answer toast → "Get help"
- Side-panel mode (400 px drawer) during a session, opened with `T` shortcut (shortcut itself in STU-W-15; panel container implemented here)
- PII pre-check in composer: if the user types obvious PII (email, phone, SSN pattern), show a soft warning before send
- IndexedDB cache of loaded threads for offline read
- Age-appropriate tone reflection in the context panel (shows "Tutor will use simple language" for under-13)

Out of scope:

- The actual LLM routing and tool implementation — all backend (STB-04 + existing SAI work)
- Voice conversation mode with full duplex — implement the pieces but wire it as a stretch goal in STU-W-15
- LMS citation linking — future

## Definition of Done

- [ ] All 22 `STU-TUT-*` acceptance criteria in [07-ai-tutor.md](../../docs/student/07-ai-tutor.md) pass
- [ ] Streaming response renders tokens within 50 ms of arrival with no layout shift
- [ ] Tool-call cards collapse/expand with smooth animation
- [ ] Image drag-drop onto composer works across Chrome, Firefox, Safari
- [ ] Voice input works in Chrome + Safari; degrades gracefully elsewhere
- [ ] Thread export to markdown and PDF produces valid files
- [ ] Context panel toggle revokes a context item and the tutor's next response reflects the revocation (visible in a system message)
- [ ] IndexedDB cache hydrates threads on offline load within 200 ms
- [ ] Entry points from session / graph / progress / toast all prefill the initial message correctly
- [ ] Side-panel mode opens on `T` and closes on `Esc`
- [ ] Playwright covers: new thread → send → stream → tool call → citation, voice input, image upload, thread rename/delete/export, side-panel mode during a session, PII pre-check
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Streaming token jank** — naive `v-for` over tokens causes quadratic re-renders. Use a buffered append strategy that batches updates at 60 fps.
- **Voice API inconsistency** — Safari's `SpeechRecognition` is different enough from Chrome's that a simple abstraction leaks. Pick one canonical behavior and document the Safari limitation.
- **OCR latency** — large image uploads can take 10+ seconds. Show a progress indicator and allow cancel.
- **Whiteboard strokes** — can grow unbounded in a long thread. Cap stored strokes per thread and prompt the student to save/export when nearing the cap.
- **IndexedDB quota** — image attachments blow through quota fast. Store image metadata + S3 URLs, not raw image data.
- **Prompt-injection safety** — backend handles this, but the client must not auto-execute any instruction-like content inside a tutor response. Treat responses as data, never as HTML or JavaScript.
