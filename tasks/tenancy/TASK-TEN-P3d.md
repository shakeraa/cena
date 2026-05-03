# TASK-TEN-P3d: Chat Capability (SignalR Mentor-Student Channel)

**Phase**: 3
**Priority**: normal
**Effort**: 3--5d
**Depends on**: Phase 2 (TEN-P2f), TEN-P3a
**Blocks**: nothing
**Queue ID**: `t_41f24e92beb5`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Wire the `MentorCapability.Chat` flag to a SignalR-based text chat channel between mentor and student within an enrollment. Chat is per-enrollment, privacy-scoped, and uses the existing `CenaHub` infrastructure.

## Background

ADR-0001 defines `Chat` as one of the `MentorCapability` flags. By default, `InstructorLed` classrooms have chat enabled; `PersonalMentorship` has it disabled (can be flipped per-relationship). Chat piggybacks the existing SignalR infrastructure used for session events, adding a new group pattern for mentor-student conversations.

## Specification

### Chat message document

Create `src/shared/Cena.Infrastructure/Documents/ChatMessageDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public class ChatMessageDocument
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string EnrollmentId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderRole { get; set; } = "";     // "mentor" | "student"
    public string Content { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTimeOffset? ReadAt { get; set; }
}
```

### Events

```csharp
public record ChatMessageSent_V1(
    string MessageId,
    string EnrollmentId,
    string SenderId,
    string SenderRole,
    string Content,
    DateTimeOffset SentAt
) : IDelegatedEvent;

public record ChatMessageRead_V1(
    string MessageId,
    string EnrollmentId,
    string ReadById,
    DateTimeOffset ReadAt
) : IDelegatedEvent;
```

### SignalR hub extension

Add to `CenaHub`:

**Server -> Client events**:
- `ChatMessageReceived(BusEnvelope<ChatMessagePayload>)` -- new message notification
- `ChatMessageMarkedRead(string messageId, string enrollmentId)` -- read receipt

**Client -> Server commands**:
- `SendChatMessage(string enrollmentId, string content)` -- send message
- `MarkChatRead(string enrollmentId, string lastMessageId)` -- mark messages read

**Group pattern**: `chat:{enrollmentId}` -- both mentor and student join this group when the chat panel is open.

### REST endpoints

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `GET` | `/api/chat/{enrollmentId}/messages?page=&pageSize=` | Message history | Mentor or Student (enrollment member) |
| `POST` | `/api/chat/{enrollmentId}/messages` | Send message (REST fallback) | Mentor or Student |
| `POST` | `/api/chat/{enrollmentId}/read` | Mark as read | Mentor or Student |
| `GET` | `/api/chat/unread-count` | Unread count across all enrollments | Authenticated user |

### Content constraints

- `Content` max length: 2000 characters.
- No file attachments in v1 (text only).
- No HTML -- plain text only, rendered with line breaks preserved.
- Rate limit: 10 messages per minute per user per enrollment.

### Privacy enforcement

- Chat is ONLY accessible to the two parties (mentor + student) of the enrollment.
- `MentorCapability.Chat` must be set on the enrollment. If not, return 403.
- Chat messages are not visible to other mentors or instructors in the same institute.
- Chat messages are not visible in other enrollments, even if the same mentor-student pair has multiple enrollments.

## Implementation notes

- Reuse the existing `CenaHub` and `BusEnvelope<T>` pattern from `Cena.Api.Contracts/Hub/`.
- The `chat:{enrollmentId}` group is joined when the user opens the chat panel and left when they close it.
- Follow FIND-sec-005: verify enrollment membership before joining the chat group.
- Follow FIND-data-007 CQRS pattern: `SendChatMessage` emits `ChatMessageSent_V1`, projection writes `ChatMessageDocument`.
- Rate limiting follows the FIND-ux-006b pattern: per-user, per-enrollment throttle.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Chat is privacy-sensitive -- messages must never leak across enrollments. Follow FIND-sec-005 tenant scoping. Follow FIND-data-005 event naming convention.

## Tests required

**Test class**: `ChatEndpointTests` in `src/api/Cena.Student.Api.Tests/ChatEndpointTests.cs`

| Test method | Assertion |
|---|---|
| `SendMessage_ValidEnrollment_Returns201` | Student sends message in enrollment with `Chat` capability, assert 201. |
| `SendMessage_NoChatCapability_Returns403` | Enrollment without `Chat` flag, assert 403. |
| `SendMessage_NotEnrolled_Returns403` | Student not in enrollment, assert 403. |
| `SendMessage_ExceedsMaxLength_Returns400` | Content > 2000 chars, assert 400. |
| `GetMessages_ReturnsPaginated` | 50 messages, request page 1 size 20, assert 20 items + correct total. |
| `GetMessages_OnlyEnrollmentMembers` | Student A cannot read Student B's chat with a mentor. |
| `MarkRead_UpdatesReadStatus` | Mark messages read, reload, assert `IsRead == true`. |
| `UnreadCount_ReturnsCorrectCount` | 3 unread across 2 enrollments, assert total = 3. |
| `RateLimit_ExceedsThrottle_Returns429` | Send 11 messages in 1 minute, assert 429 on the 11th. |

**Test class**: `ChatHubTests` in `src/api/Cena.Student.Api.Tests/ChatHubTests.cs`

| Test method | Assertion |
|---|---|
| `SendChatMessage_BroadcastsToGroup` | Send via hub, assert other group member receives `ChatMessageReceived`. |
| `JoinChatGroup_RequiresEnrollmentMembership` | Non-member attempts to join `chat:{enrollmentId}`, assert rejected. |

## Definition of Done

- [ ] `ChatMessageDocument` created
- [ ] 2 new events registered in MartenConfiguration
- [ ] SignalR hub extended with chat commands/events
- [ ] 4 REST endpoints implemented
- [ ] `MentorCapability.Chat` gate enforced
- [ ] Privacy enforcement (per-enrollment isolation)
- [ ] Rate limiting (10/min/user/enrollment)
- [ ] Content length validation (2000 char max)
- [ ] All 9 endpoint tests pass
- [ ] All 2 hub tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- chat capability spec
2. `src/api/Cena.Api.Contracts/Hub/HubContracts.cs` -- existing hub event pattern
3. `src/shared/Cena.Infrastructure/Documents/MentorCapabilityFlags.cs` -- `Chat` flag
4. `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` -- enrollment reference

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/ChatMessageDocument.cs` | create | Chat message document |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 2 chat events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register document + events |
| `src/api/Cena.Api.Contracts/Hub/HubContracts.cs` | modify | Add chat event types |
| `src/api/Cena.Student.Api.Host/Endpoints/ChatEndpoints.cs` | create | 4 REST endpoints |
| `src/api/Cena.Student.Api.Host/Hub/CenaHub.cs` | modify | Add chat commands |
| `src/api/Cena.Student.Api.Tests/ChatEndpointTests.cs` | create | 9 endpoint tests |
| `src/api/Cena.Student.Api.Tests/ChatHubTests.cs` | create | 2 hub tests |
