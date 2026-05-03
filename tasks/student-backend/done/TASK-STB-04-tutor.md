# TASK-STB-04: Tutor REST + Streaming

**Priority**: HIGH — required for STU-W-08 and the web tutor is a flagship feature
**Effort**: 6-8 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md), existing SAI work in [docs/tasks/student-ai-interaction/](../../docs/tasks/student-ai-interaction/README.md)
**UI consumers**: [STU-W-08](../student-web/TASK-STU-W-08-ai-tutor.md)
**Status**: Not Started

---

## Goal

Expose the tutor as a first-class REST + streaming API, reusing the existing `TutorActor`, LLM routing, and quality gates from SAI. The web (and eventually mobile) client sends messages, the tutor streams tokens and tool calls back over SignalR, and threads persist so students can resume conversations across devices.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/tutor/threads` | List threads with pagination | `api` | JWT |
| `POST` | `/api/tutor/threads` | Create a new thread | `tutor` (30/hour) | JWT |
| `GET` | `/api/tutor/threads/{id}` | Get thread metadata | `api` | JWT |
| `PATCH` | `/api/tutor/threads/{id}` | Rename, pin, unpin | `api` | JWT |
| `DELETE` | `/api/tutor/threads/{id}` | Soft-delete (30-day recovery) | `api` | JWT |
| `GET` | `/api/tutor/threads/{id}/messages` | Paginated message history | `api` | JWT |
| `POST` | `/api/tutor/threads/{id}/messages` | Send a message; response streams over SignalR | `tutor` (30/hour) | JWT |
| `POST` | `/api/tutor/ocr` | Image → text for composer input | `tutor` (20/day) | JWT |
| `POST` | `/api/tutor/tts` | Text → audio stream for tutor voice output | `tutor` (100/day) | JWT |
| `GET` | `/api/tutor/threads/{id}/export?format=md\|pdf` | Export thread | `api` (10/day) | JWT |

## Data Access

- **Reads**: `TutorThreadDocument`, `TutorMessageDocument` (both new or extended from existing SAI work)
- **Writes**: append to thread stream on each user message; tutor responses append after streaming completes
- **Async projections**: thread list view (title, last message preview, unread count)

## Hub Events (additive, land in STB-10)

- `TutorTokenStreamed` — { threadId, messageId, token, tokenIndex }
- `TutorToolCalled` — { threadId, messageId, toolName, toolInput }
- `TutorToolCompleted` — { threadId, messageId, toolName, toolResult }
- `TutorMessageComplete` — { threadId, messageId, completeText, citations }

Server MUST stream tokens in-order and the client MUST drop out-of-order tokens defensively.

## Contracts

Add to `Cena.Api.Contracts/Dtos/Tutor/`:

- `TutorThreadDto`, `TutorThreadPageDto`
- `TutorMessageDto`, `TutorMessageRoleDto` (`user` | `tutor` | `system` | `tool`)
- `TutorMessageSendRequest` — `{ threadId, text, attachments?, contextOverrides? }`
- `TutorMessageSendResponse` — `{ messageId, streamingVia: "hub" }`
- `TutorCitationDto`
- `TutorToolCallDto`
- `TutorOcrRequest`, `TutorOcrResponse`
- `TutorTtsRequest`, `TutorTtsResponse`

## Context Sharing

The tutor can see:

- Current session ID + question ID + last answer (if the student is in a session)
- Student profile: age band, grade level, locale
- Mastery for the concept in question
- Last 10 mistakes in this subject

The tutor **cannot** see:

- Other students
- Raw identity (name, email, phone)
- Admin configuration

The server must honor client-provided `contextOverrides.revoked` entries — if the client says "don't use the mastery context this turn", the server must inject a system message telling the tutor it cannot see that.

## Safety Rails

- All inputs pass through the existing content moderation pipeline
- All outputs pass through the existing quality gate
- PII scrubbing on any text before logging
- Age-appropriate tone enforced via the LLM routing config (`age-band` parameter)
- Tutor never asks for PII; if the student volunteers PII, the tutor pauses and suggests removal (prompt engineering responsibility, not a separate endpoint)
- Failed safety checks surface to the client as a canned safe response and are logged

## Auth & Authorization

- Firebase JWT
- `ResourceOwnershipGuard` on thread operations — students can only see their own threads
- Rate limits enforced per student

## Cross-Cutting

- Streaming uses existing SignalR `/hub/cena` — no new hub
- Every tutor request flows through the `LlmCircuitBreakerActor` (existing)
- Token caching uses the existing prompt cache strategy (system prompt 1 h TTL, student context 5 min TTL)
- Export streams (md/pdf) use existing background job patterns
- Handler logs with `correlationId` and `threadId` tags

## Definition of Done

- [ ] All 10 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Tutor/`
- [ ] Streaming tokens deliver in-order with no gaps under load test
- [ ] Tool-call events wrap correctly in `BusEnvelope`
- [ ] Context overrides (revoked items) reflected in the tutor's response
- [ ] OCR endpoint accepts JPEG / PNG / HEIC, returns extracted text
- [ ] TTS endpoint streams audio chunks
- [ ] Export to markdown and PDF produces valid files
- [ ] Thread soft-delete with 30-day recovery implemented
- [ ] Rate limits enforced (tested with rapid POSTs)
- [ ] PII scrubbing verified on logs
- [ ] Integration tests: new thread → send message → receive stream → complete → export → delete
- [ ] Mobile lead review: confirm the tutor contract works for Flutter, which currently has a simpler tutor UX
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated

## Out of Scope

- New LLM providers — existing routing stays
- Long-term tutor memory across threads — existing SAI work covers this
- Voice conversation mode with full duplex — client-side stitch in STU-W-15
- Whiteboard persistence across threads — client stores per-thread; server stores as a single blob attachment
- Classroom tutor (shared across students) — future
