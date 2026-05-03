# ADM-025: Wire Admin Chat UI to Real Messaging Backend

**Priority:** P1 — admin chat is currently template scaffolding with mock data
**Blocked by:** MSG-001–MSG-005 (messaging context, done), BKD-005 (admin API)
**Estimated effort:** 3 days

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin dashboard has a chat UI at `src/admin/full-version/src/views/apps/chat/` (Vuexy template) that hits fake API routes (`/apps/chat/chats-and-contacts`, `/apps/chat/chats/{userId}`). The backend now has a full `ConversationThreadActor` with Redis Streams storage, NATS event publishing, and content moderation (MSG-001–005). This task replaces the fake API calls with real Admin API endpoints backed by the messaging actors.

## Subtasks

### ADM-025.1: Admin Messaging API Endpoints

**Files:**
- `src/api/Cena.Admin.Api/MessagingAdminEndpoints.cs`
- `src/api/Cena.Admin.Api/MessagingAdminDtos.cs`
- `src/api/Cena.Admin.Api/MessagingAdminService.cs`

**Endpoints:**
```
GET    /api/admin/messaging/threads              — list threads (filterable by type, participant)
GET    /api/admin/messaging/threads/{threadId}    — thread detail with message history (paginated)
POST   /api/admin/messaging/threads              — create new thread (direct, broadcast, parent)
POST   /api/admin/messaging/threads/{threadId}/messages — send message in thread
PUT    /api/admin/messaging/threads/{threadId}/mute     — mute/unmute thread
GET    /api/admin/messaging/contacts              — list contactable users (students, parents, teachers)
```

**Acceptance:**
- [ ] Thread list returns `ThreadSummaryDto[]` from Marten `ThreadSummaryProjection`
- [ ] Thread detail returns paginated messages from Redis Streams (cursor-based)
- [ ] Create thread routes to `ConversationThreadActor` via NATS request/reply
- [ ] Send message routes to actor, returns `MessageSentResult`
- [ ] Content moderation results included in response (blocked messages show reason)
- [ ] Contacts endpoint returns users from Firebase Auth with roles
- [ ] All endpoints require admin/teacher role

### ADM-025.2: Update Chat Pinia Store

**Files:**
- `src/admin/full-version/src/views/apps/chat/useChatStore.ts` — replace mock API calls

**Acceptance:**
- [ ] `fetchChatsAndContacts(q)` calls `GET /api/admin/messaging/threads` + `GET /api/admin/messaging/contacts`
- [ ] `getChat(threadId)` calls `GET /api/admin/messaging/threads/{threadId}`
- [ ] `sendMsg(threadId, message)` calls `POST /api/admin/messaging/threads/{threadId}/messages`
- [ ] Remove all references to fake API routes (`/apps/chat/*`)
- [ ] Add `createThread(type, participantIds)` action
- [ ] Add `muteThread(threadId, muted)` action
- [ ] Error handling: show snackbar on failure

### ADM-025.3: Update Chat Components

**Files:**
- `src/admin/full-version/src/views/apps/chat/ChatLog.vue` — display real message history
- `src/admin/full-version/src/views/apps/chat/ChatContact.vue` — show real thread data
- `src/admin/full-version/src/views/apps/chat/ChatLeftSidebarContent.vue` — thread list from API

**Acceptance:**
- [ ] `ChatLog.vue` renders messages from `ThreadSummary` with correct role badges (Teacher, Parent, Student, System)
- [ ] Message timestamps use relative time (e.g., "2 min ago")
- [ ] Blocked messages shown with moderation reason (admin-only visibility)
- [ ] Thread type indicator (DM icon, broadcast icon, parent icon)
- [ ] Contact list grouped by role
- [ ] Search filters threads and contacts

### ADM-025.4: Real-Time Message Updates (SSE or Polling)

**Files:**
- `src/api/Cena.Admin.Api/MessagingAdminEndpoints.cs` — add SSE endpoint or polling
- `src/admin/full-version/src/views/apps/chat/useChatStore.ts` — subscribe to updates

**Acceptance:**
- [ ] New messages appear without page refresh (either SSE stream or 5s polling)
- [ ] Thread list re-sorts when new message arrives (most recent first)
- [ ] Unread count badge updates on sidebar
- [ ] If SSE: `GET /api/admin/messaging/events` streams `text/event-stream`
- [ ] If polling: `GET /api/admin/messaging/threads?since={lastTimestamp}` returns only changed threads

## Definition of Done
- [ ] Admin chat loads real threads from backend
- [ ] Sending messages creates real entries in Redis Streams
- [ ] Content moderation blocks unsafe messages
- [ ] New messages appear in near-real-time
- [ ] `vue-tsc --noEmit` passes
- [ ] `dotnet build` + `dotnet test` pass
