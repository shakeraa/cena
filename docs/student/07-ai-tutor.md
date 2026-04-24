# 07 — AI Tutor

## Overview

The AI tutor is a conversational assistant the student can consult about a current question, a past mistake, a concept, or an open-ended topic. Unlike a chatbot, it is **context-aware**: it can see the active session, recent attempts, and the student's mastery profile.

This feature is strategically important — it is the primary "ask me anything" surface and the place the student most often talks to the platform.

## Mobile Parity

Source of truth:
- [tutor_chat_screen.dart](../../src/mobile/lib/features/tutor/tutor_chat_screen.dart)
- [tutor_state.dart](../../src/mobile/lib/features/tutor/tutor_state.dart)
- [student-ai-interaction-complete.md](../student-ai-interaction-complete.md)
- [student-ai-interaction-tasks.md](../student-ai-interaction-tasks.md)
- [autoresearch-student-ai-interaction.md](../autoresearch-student-ai-interaction.md)

## Pages

### `/tutor`

Full-screen chat UI with thread list on the left (collapsible), active conversation in the middle, context panel on the right (collapsible).

```
┌──────────────┬───────────────────────────────────┬──────────────┐
│              │                                   │              │
│  Threads     │  Conversation                     │  Context     │
│  ────────    │  ──────────                       │  ────────    │
│  Today       │  [tutor] Hi! What are we          │  Question:   │
│  ─ Quadratic │    working on today?              │  Quadratic   │
│    formula   │                                   │  formula     │
│  ─ Mitochon… │  [you] I don't get step 3         │              │
│  Yesterday   │                                   │  Concept:    │
│  ─ Verbs in… │  [tutor] Let's break it down…     │  Algebra →   │
│              │                                   │  Polynomials │
│  [+ New]     │  [input]________________          │              │
│              │  [voice] [image] [send]           │  Mastery:    │
│              │                                   │  64%         │
└──────────────┴───────────────────────────────────┴──────────────┘
```

### `/tutor/:threadId`

Direct link to a specific thread. Same UI with the thread preselected.

## Components

| Component | Purpose |
|-----------|---------|
| `<TutorThreadList>` | List of conversation threads grouped by date |
| `<TutorConversation>` | Message stream with markdown + KaTeX rendering |
| `<TutorMessage>` | Individual message bubble (user / tutor / system) |
| `<TutorComposer>` | Input box with rich features (text, voice, image, math) |
| `<TutorContextPanel>` | Right panel showing the context the tutor can see |
| `<TutorToolCallCard>` | Card shown when tutor invokes a tool (e.g. "Looking up concept…") |
| `<TutorSuggestionChips>` | Suggested follow-up prompts below the latest message |
| `<TutorCitation>` | Inline citation with tap-to-expand source |

## Conversation Capabilities

- **Streaming responses** via SignalR (`TutorTokenStreamed` events).
- **Tool use** — the tutor can invoke backend tools (look up concept, render diagram, open SRS card, suggest practice question). Each tool call is shown as a collapsible card.
- **Citations** — every factual claim includes a citation to a concept page or source material.
- **Safety rails** — off-topic, unsafe, or policy-violating prompts get a canned safe response and are logged.
- **Persona** — single friendly persona by default; admins can add alternates (configured in admin panel).

## Context Sharing

The tutor can see (with the student's session cookie):
- Current session ID, question ID, and last answer.
- Student profile: age band, grade level, locale.
- Mastery for the concept in question.
- Last 10 mistakes in this subject.

Can **not** see:
- Other students' data.
- Raw identity (name, email, phone).
- Any admin configuration.

The context panel makes all of this **visible and revokable** — the student can toggle individual context items off, and the tutor will explicitly know what it cannot see.

## Multi-Modal Input

- **Text** — primary, markdown supported for student inputs (auto-preview).
- **Voice** — Web Speech API for input; server-side TTS for the tutor's reply (toggleable).
- **Image** — upload or drag-drop a photo of a problem; tutor OCRs and answers.
- **Math** — inline math keyboard (same as session input).

## Entry Points

The tutor is reachable from multiple surfaces:
- Sidebar → AI Tutor
- Session feedback overlay → "Ask the tutor"
- Side panel (web-only, `T` shortcut) during a session
- Knowledge graph concept page → "Ask about this concept"
- Progress mastery item → "Why am I stuck?"
- Wrong-answer toast → "Get help"

Each entry point seeds the initial message with the relevant context ("I was asked: ... my answer was: ... what went wrong?").

## Prompt Templates & Starters

Empty-state thread shows prompt starters matched to the student's context:
- "Explain quadratic formula in a different way"
- "Give me a harder practice question on X"
- "Quiz me on what I learned today"
- "Why was my answer to Q12 wrong?"

## Thread Management

- Rename thread (auto-generated title via LLM, editable).
- Pin important threads.
- Delete thread (soft-delete with 30-day recovery).
- Export thread to markdown / PDF.
- Share thread (view-only link, optional anonymization).

## Safety & Guardrails

- Every prompt + response passes through the content moderation pipeline (PII scrubbing, policy filters).
- Age-appropriate tone: under-13 uses simpler language, friendlier emojis.
- "I don't know" is allowed and preferred over hallucinated content.
- Tutor never asks for personal info; if the student volunteers PII the tutor immediately pauses and suggests removing it.

## Web-Specific Enhancements

- **Split-view during session** — a 400 px side panel rather than a full-screen context switch.
- **Drag-drop image upload** — drop an image of a handwritten problem directly into the composer.
- **Whiteboard** — a shared sketching canvas the tutor can annotate. Saves as part of the thread.
- **Code blocks with syntax highlighting** — for programming subjects.
- **Graphing** — tutor can render a Desmos-style graph inline.
- **Voice conversation mode** — hands-free back-and-forth with automatic turn detection.
- **Multi-tab continuity** — switching tabs preserves the live stream via a shared worker.
- **Offline transcript** — already-loaded threads are readable without connectivity (IndexedDB cache).

## Acceptance Criteria

- [ ] `STU-TUT-001` — `/tutor` page with thread list, conversation, and context panel.
- [ ] `STU-TUT-002` — Threads are paginated and grouped by date.
- [ ] `STU-TUT-003` — New-thread button creates a thread with an LLM-generated title after the first exchange.
- [ ] `STU-TUT-004` — Composer supports text, voice, image, and math input.
- [ ] `STU-TUT-005` — Messages render markdown + KaTeX + code blocks + tool-call cards + citations.
- [ ] `STU-TUT-006` — Tutor responses stream token-by-token via SignalR `TutorTokenStreamed` events.
- [ ] `STU-TUT-007` — Tool calls show collapsible cards with tool name, input summary, and result preview.
- [ ] `STU-TUT-008` — Citations are inline and tap-to-expand.
- [ ] `STU-TUT-009` — Context panel lists shared items and allows toggling each off.
- [ ] `STU-TUT-010` — Tutor is explicitly told when a context item has been revoked.
- [ ] `STU-TUT-011` — Voice input uses Web Speech API; output uses server TTS, both toggleable.
- [ ] `STU-TUT-012` — Image upload OCRs the image and includes the result in the prompt.
- [ ] `STU-TUT-013` — Suggested starters appear on empty threads, seeded by student context.
- [ ] `STU-TUT-014` — Rename / pin / delete / export / share thread actions work.
- [ ] `STU-TUT-015` — Entry points from session feedback, knowledge graph, progress, and wrong-answer toast prefill the initial message.
- [ ] `STU-TUT-016` — Side panel mode opens with `T` during a session.
- [ ] `STU-TUT-017` — Whiteboard component saves strokes to the thread.
- [ ] `STU-TUT-018` — Graphing tool renders an inline graph on math concepts.
- [ ] `STU-TUT-019` — Voice conversation mode supports continuous turn detection.
- [ ] `STU-TUT-020` — IndexedDB cache makes loaded threads readable offline.
- [ ] `STU-TUT-021` — PII detection in composer prompts the student before sending.
- [ ] `STU-TUT-022` — Age-appropriate tone enforced server-side; client reflects age band in context panel.

## Backend Dependencies

- `GET /api/tutor/threads` — new (may exist in some form)
- `POST /api/tutor/threads` — new
- `GET /api/tutor/threads/{id}/messages` — new
- `POST /api/tutor/threads/{id}/messages` — new
- Hub events: `TutorTokenStreamed`, `TutorToolCalled`, `TutorToolCompleted`, `TutorMessageComplete`
- `POST /api/tutor/ocr` — new (image → text)
- `POST /api/tutor/tts` — new (text → audio stream)
- `GET /api/tutor/threads/{id}/export?format=md|pdf` — new
