# MSG-005: Inbound Webhooks & SignalR Hub — WhatsApp, Telegram, Real-Time Delivery

**Priority:** P1 — enables bidirectional communication
**Blocked by:** MSG-002 (Redis Streams), MSG-003 (NATS), WEB-002 (SignalR), SEC-001 (auth)
**Estimated effort:** 3 days
**Design:** `docs/messaging-context-design.md` Sections 4, 7, 8

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

This task implements the two entry points for messages into the Messaging context:
1. **Inbound webhooks** — WhatsApp (Twilio) and Telegram (Bot API) callbacks when students reply to outreach messages
2. **SignalR hub** — in-app real-time messaging for teachers and parents using the web/mobile client

Both entry points validate authentication, apply content moderation, write to Redis Streams (via MSG-002), publish to NATS (via MSG-003), and push real-time updates to connected clients.

## Subtasks

### MSG-005.1: WhatsApp Webhook (Twilio)

**Files:**
- `src/api/Cena.Api/Endpoints/WhatsAppWebhookEndpoint.cs` — ASP.NET minimal API
- `src/infrastructure/Cena.Infrastructure/Webhooks/TwilioSignatureValidator.cs` — HMAC-SHA1 validation
- `src/infrastructure/Cena.Infrastructure/Webhooks/IStudentPhoneLookup.cs` — phone → studentId mapping

**Acceptance:**
- [ ] `POST /api/webhooks/whatsapp` receives Twilio webhook callbacks
- [ ] Validates `X-Twilio-Signature` header:
  - Reconstructs the URL + sorted POST params
  - Computes HMAC-SHA1 with Twilio auth token (from `IConfiguration["Twilio:AuthToken"]`)
  - Compares Base64-encoded result to header value
  - Returns `403 Forbidden` if validation fails
- [ ] Extracts: `From` (phone), `Body` (text), `MessageSid` (idempotency key)
- [ ] Idempotency check: `SET cena:webhook:dedup:whatsapp:{MessageSid} 1 NX EX 300`
  - If key already exists → return `200 OK` without processing (duplicate)
- [ ] Phone lookup: `IStudentPhoneLookup.LookupAsync(phoneNumber)` returns `studentId?`
  - Unknown phone → log warning (SHA-256 hashed phone, not plaintext), return `200 OK`
- [ ] Known phone → publish `InboundWebhookMessage` to `cena.messaging.commands.RouteInboundReply`
- [ ] Returns `200 OK` with TwiML `<Response/>` (empty — no auto-reply from webhook)
- [ ] Rate limiting: 100 requests/minute per source phone number (sliding window via Redis)
  - Exceeded → `429 Too Many Requests`
- [ ] Status callbacks (`MessageStatus` field present) → acknowledge and ignore (not a message)
- [ ] PII logging: phone numbers logged as `SHA256(phone)[0:8]` — never plaintext

**Test:**
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Security.Cryptography;
using Xunit;

public class WhatsAppWebhookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private const string TestAuthToken = "test-auth-token-32chars-exactly!";

    [Fact]
    public async Task ValidSignature_Returns200_PublishesEvent()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+972501234567",
            ["Body"] = "42",
            ["MessageSid"] = "SM001"
        };
        var request = CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<Response/>", body);
        Assert.Single(CapturedNatsMessages, m =>
            m.Subject == "cena.messaging.commands.RouteInboundReply");
    }

    [Fact]
    public async Task InvalidSignature_Returns403()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+972501234567",
            ["Body"] = "hello",
            ["MessageSid"] = "SM002"
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("X-Twilio-Signature", "invalid-garbage");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateMessageSid_Idempotent()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+972501234567",
            ["Body"] = "42",
            ["MessageSid"] = "SM003"
        };

        await _client.SendAsync(CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken));
        await _client.SendAsync(CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken));

        Assert.Single(CapturedNatsMessages); // Only one despite two calls
    }

    [Fact]
    public async Task UnknownPhone_Returns200_NoEvent()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+19999999999",
            ["Body"] = "hello",
            ["MessageSid"] = "SM004"
        };
        var request = CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // Don't reveal existence
        Assert.Empty(CapturedNatsMessages);
    }

    [Fact]
    public async Task StatusCallback_AcknowledgedButIgnored()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+972501234567",
            ["MessageSid"] = "SM005",
            ["MessageStatus"] = "delivered" // This is a status callback, not a message
        };
        var request = CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(CapturedNatsMessages); // Status callbacks don't generate events
    }

    [Fact]
    public async Task RateLimitExceeded_Returns429()
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = "+972501234567",
            ["Body"] = "spam"
        };

        HttpResponseMessage? lastResponse = null;
        for (int i = 0; i < 110; i++)
        {
            form["MessageSid"] = $"SM{i:D5}";
            lastResponse = await _client.SendAsync(
                CreateSignedRequest("/api/webhooks/whatsapp", form, TestAuthToken));
        }

        // At least one should be rate limited
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }

    private HttpRequestMessage CreateSignedRequest(string url, Dictionary<string, string> form, string authToken)
    {
        var fullUrl = $"https://localhost{url}";
        var sortedParams = string.Concat(form.OrderBy(kv => kv.Key).Select(kv => kv.Key + kv.Value));
        var dataToSign = fullUrl + sortedParams;

        using var hmac = new HMACSHA1(System.Text.Encoding.UTF8.GetBytes(authToken));
        var signature = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dataToSign)));

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("X-Twilio-Signature", signature);
        return request;
    }
}
```

---

### MSG-005.2: Telegram Webhook (Bot API)

**Files:**
- `src/api/Cena.Api/Endpoints/TelegramWebhookEndpoint.cs` — ASP.NET minimal API
- `src/infrastructure/Cena.Infrastructure/Webhooks/TelegramTokenValidator.cs` — secret token validation
- `src/infrastructure/Cena.Infrastructure/Webhooks/IStudentTelegramLookup.cs` — telegram user ID → studentId

**Acceptance:**
- [ ] `POST /api/webhooks/telegram` receives Telegram Bot API updates
- [ ] Validates `X-Telegram-Bot-Api-Secret-Token` header against configured secret
  - Returns `403 Forbidden` if missing or invalid
- [ ] Extracts: `message.from.id` (Telegram user ID), `message.text`, `update_id`
- [ ] Idempotency: `SET cena:webhook:dedup:telegram:{update_id} 1 NX EX 300`
- [ ] Telegram user ID lookup → `studentId` mapping
- [ ] `edited_message` updates → log but don't process (no edit support in MVP)
- [ ] Non-text messages (photos, stickers, voice) → ignore, return `200 OK`
- [ ] Group messages (`message.chat.type != "private"`) → ignore, return `200 OK`
- [ ] Returns `200 OK` with empty body (Telegram requirement)

**Test:**
```csharp
public class TelegramWebhookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestTelegramToken = "test-telegram-secret-token";

    [Fact]
    public async Task ValidToken_TextMessage_PublishesEvent()
    {
        var update = new { update_id = 100, message = new {
            from = new { id = 12345L }, text = "תודה רבה!",
            chat = new { type = "private" }
        }};
        var request = CreateTelegramRequest(update, TestTelegramToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(CapturedNatsMessages, m =>
            m.Subject == "cena.messaging.commands.RouteInboundReply");
    }

    [Fact]
    public async Task InvalidToken_Returns403()
    {
        var update = new { update_id = 101, message = new {
            from = new { id = 12345L }, text = "hello",
            chat = new { type = "private" }
        }};
        var request = CreateTelegramRequest(update, "wrong-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditedMessage_IgnoredGracefully()
    {
        var update = new { update_id = 102, edited_message = new {
            from = new { id = 12345L }, text = "edited text",
            chat = new { type = "private" }
        }};
        var request = CreateTelegramRequest(update, TestTelegramToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(CapturedNatsMessages);
    }

    [Fact]
    public async Task GroupMessage_IgnoredGracefully()
    {
        var update = new { update_id = 103, message = new {
            from = new { id = 12345L }, text = "group message",
            chat = new { type = "group" }
        }};
        var request = CreateTelegramRequest(update, TestTelegramToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(CapturedNatsMessages);
    }

    [Fact]
    public async Task PhotoMessage_IgnoredGracefully()
    {
        var update = new { update_id = 104, message = new {
            from = new { id = 12345L },
            photo = new[] { new { file_id = "abc", width = 100, height = 100 } },
            chat = new { type = "private" }
            // Note: no "text" field
        }};
        var request = CreateTelegramRequest(update, TestTelegramToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(CapturedNatsMessages);
    }
}
```

---

### MSG-005.3: MessagingHub (SignalR) & REST Endpoints

**Files:**
- `src/api/Cena.Api/Hubs/MessagingHub.cs` — SignalR hub for in-app messaging
- `src/api/Cena.Api/Endpoints/MessagingEndpoints.cs` — REST API for thread browsing
- `contracts/frontend/signalr-messages.ts` — append new message types to existing unions

**Acceptance:**
- [ ] **SignalR Hub** (`/hubs/messaging`):
  - `SendDirectMessage(recipientId, content, replyToMessageId?)` — teacher/parent sends message
    - Validates sender role from JWT claims
    - Teacher: checks `recipientId` is a student in sender's `class_ids`
    - Parent: checks `recipientId` is in sender's `child_ids`
    - Student: returns `FORBIDDEN` error (receive-only MVP)
    - Creates thread if first message between these participants
    - Calls `RedisMessageWriter.WriteMessageAsync()` (MSG-002)
    - Calls `MessagingNatsPublisher.PublishMessageSentAsync()` (MSG-003)
    - Pushes `MessageReceived` event to recipient's SignalR connections
  - `MarkMessageRead(threadId, messageId)` — mark as read
    - Calls `RedisMessageReader.MarkReadAsync()` (MSG-002)
    - Pushes `MessageReadReceipt` event to sender's SignalR connections
  - `BroadcastToClass(classRoomId, content)` — teacher broadcasts
    - Validates sender is teacher of that class
    - Writes single message to class thread
    - Pushes `ClassAnnouncement` event to all students in the class

- [ ] **SignalR events** added to `contracts/frontend/signalr-messages.ts`:
  ```typescript
  // Server → Client
  export interface MessageReceivedPayload {
    readonly threadId: string;
    readonly messageId: string;
    readonly senderId: string;
    readonly senderName: string;
    readonly senderRole: 'Teacher' | 'Parent' | 'System';
    readonly content: MessageContent;
    readonly sentAt: string;
    readonly replyToMessageId: string | null;
    readonly threadType: 'DirectMessage' | 'ClassBroadcast' | 'ParentThread';
  }

  export interface MessageReadReceiptPayload {
    readonly threadId: string;
    readonly messageId: string;
    readonly readById: string;
    readonly readByName: string;
    readonly readAt: string;
  }

  export interface ClassAnnouncementPayload {
    readonly threadId: string;
    readonly messageId: string;
    readonly teacherId: string;
    readonly teacherName: string;
    readonly content: MessageContent;
    readonly classRoomId: string;
    readonly sentAt: string;
  }

  export interface MessageContent {
    readonly text: string;
    readonly contentType: 'text' | 'resource-link' | 'encouragement';
    readonly resourceUrl?: string;
  }
  ```
  Added to `ServerEvent` union: `MessageReceivedEvent`, `MessageReadReceiptEvent`, `ClassAnnouncementEvent`
  Added to `ClientCommand` union: `SendDirectMessage`, `MarkMessageRead`, `BroadcastToClass`

- [ ] **REST endpoints**:
  - `GET /api/messaging/threads` — paginated thread list for authenticated user
    - Queries `ThreadSummary` Marten projection by `participantIds`
    - Sorted by `LastMessageAt DESC`
    - Relay cursor pagination (cursor = `lastMessageAt` of last item)
    - Max 20 per page
  - `GET /api/messaging/threads/:threadId/messages?before={cursor}&limit={n}` — paginated history
    - Delegates to `CompositeMessageReader` (MSG-002 + MSG-004)
    - Returns `MessagePage { messages, nextCursor, hasMore }`
    - Max 50 per request
  - `GET /api/messaging/threads/:threadId/unread-count` — unread count
    - Reads from Redis: `GET cena:thread:{threadId}:unread:{userId}`
  - `POST /api/messaging/threads/:threadId/mute` — mute/unmute
    - Body: `{ muted: bool, mutedUntil?: string }`
  - All endpoints require JWT auth; user must be participant in the thread (checked against `ThreadSummary.ParticipantIds`)

**Test:**
```csharp
public class MessagingHubTests
{
    [Fact]
    public async Task Teacher_SendsDirectMessage_StudentReceivesEvent()
    {
        var teacherHub = await CreateHub("teacher-1", "Teacher", classIds: new[] { "class-1" });
        var studentHub = await CreateHub("student-1", "Student");

        await teacherHub.InvokeAsync("SendDirectMessage", new {
            recipientId = "student-1",
            content = new { text = "Review fractions!", contentType = "text" },
        });

        var received = await studentHub.WaitForAsync<MessageReceivedPayload>(
            "MessageReceived", TimeSpan.FromSeconds(5));
        Assert.Equal("Review fractions!", received.Content.Text);
        Assert.Equal("Teacher", received.SenderRole);
        Assert.Equal("DirectMessage", received.ThreadType);
    }

    [Fact]
    public async Task Student_CannotSendMessage()
    {
        var studentHub = await CreateHub("student-1", "Student");

        var error = await Assert.ThrowsAsync<HubException>(() =>
            studentHub.InvokeAsync("SendDirectMessage", new {
                recipientId = "teacher-1",
                content = new { text = "Hello teacher!", contentType = "text" },
            }));

        Assert.Contains("FORBIDDEN", error.Message);
    }

    [Fact]
    public async Task Teacher_CannotMessageStudentOutsideClass()
    {
        var teacherHub = await CreateHub("teacher-1", "Teacher", classIds: new[] { "class-1" });
        // student-99 is NOT in class-1

        var error = await Assert.ThrowsAsync<HubException>(() =>
            teacherHub.InvokeAsync("SendDirectMessage", new {
                recipientId = "student-99",
                content = new { text = "Hello!", contentType = "text" },
            }));

        Assert.Contains("UNAUTHORIZED", error.Message);
    }

    [Fact]
    public async Task MarkRead_SenderReceivesReceipt()
    {
        var teacherHub = await CreateHub("teacher-1", "Teacher", classIds: new[] { "class-1" });
        var studentHub = await CreateHub("student-1", "Student");

        // Teacher sends message
        await teacherHub.InvokeAsync("SendDirectMessage", new {
            recipientId = "student-1",
            content = new { text = "Review tonight!", contentType = "text" },
        });
        var msg = await studentHub.WaitForAsync<MessageReceivedPayload>(
            "MessageReceived", TimeSpan.FromSeconds(5));

        // Student marks as read
        await studentHub.InvokeAsync("MarkMessageRead", new {
            threadId = msg.ThreadId, messageId = msg.MessageId,
        });

        // Teacher gets read receipt
        var receipt = await teacherHub.WaitForAsync<MessageReadReceiptPayload>(
            "MessageReadReceipt", TimeSpan.FromSeconds(5));
        Assert.Equal(msg.MessageId, receipt.MessageId);
    }

    [Fact]
    public async Task BroadcastToClass_AllStudentsReceive()
    {
        var teacherHub = await CreateHub("teacher-1", "Teacher", classIds: new[] { "class-1" });
        var student1 = await CreateHub("student-1", "Student");
        var student2 = await CreateHub("student-2", "Student");

        await teacherHub.InvokeAsync("BroadcastToClass", new {
            classRoomId = "class-1",
            content = new { text = "Quiz tomorrow!", contentType = "text" },
        });

        var a1 = await student1.WaitForAsync<ClassAnnouncementPayload>(
            "ClassAnnouncement", TimeSpan.FromSeconds(5));
        var a2 = await student2.WaitForAsync<ClassAnnouncementPayload>(
            "ClassAnnouncement", TimeSpan.FromSeconds(5));

        Assert.Equal("Quiz tomorrow!", a1.Content.Text);
        Assert.Equal("Quiz tomorrow!", a2.Content.Text);
        Assert.Equal("class-1", a1.ClassRoomId);
    }
}

public class MessagingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetThreads_ReturnsParticipantThreadsOnly()
    {
        var client = CreateAuthenticatedClient("teacher-1", "Teacher");

        var response = await client.GetAsync("/api/messaging/threads");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var threads = await DeserializeAsync<ThreadSummary[]>(response);
        Assert.All(threads, t => Assert.Contains("teacher-1", t.ParticipantIds));
    }

    [Fact]
    public async Task GetMessages_PaginatesCorrectly()
    {
        var client = CreateAuthenticatedClient("teacher-1", "Teacher");

        var page1 = await client.GetAsync("/api/messaging/threads/t-1/messages?limit=10");
        var messages = await DeserializeAsync<MessagePage>(page1);
        Assert.Equal(10, messages.Messages.Length);
        Assert.True(messages.HasMore);

        var page2 = await client.GetAsync(
            $"/api/messaging/threads/t-1/messages?limit=10&before={messages.NextCursor}");
        var messages2 = await DeserializeAsync<MessagePage>(page2);
        Assert.True(messages2.Messages[0].SentAt < messages.Messages[^1].SentAt);
    }

    [Fact]
    public async Task GetMessages_NonParticipant_Returns403()
    {
        var client = CreateAuthenticatedClient("teacher-2", "Teacher");

        var response = await client.GetAsync("/api/messaging/threads/t-owned-by-teacher-1/messages");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsFromRedis()
    {
        var client = CreateAuthenticatedClient("student-1", "Student");

        var response = await client.GetAsync("/api/messaging/threads/t-1/unread-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var count = await DeserializeAsync<UnreadCountDto>(response);
        Assert.True(count.Count >= 0);
    }
}
```

---

### MSG-005.4: Content Moderation & Throttling

**Files:**
- `src/domain/Cena.Domain/Messaging/ContentModerator.cs` — blocks PII and unsafe URLs
- `src/domain/Cena.Domain/Messaging/MessageThrottler.cs` — per-role rate limiting
- `src/domain/Cena.Domain/Messaging/ModerationResult.cs` — result type

**Acceptance:**
- [ ] `ContentModerator.Check(text)` returns `ModerationResult(safe, reason?)`:
  - Blocks phone numbers: `\+?\d{7,15}` → `reason: "phone_number_detected"`
  - Blocks email addresses: standard RFC 5322 regex → `reason: "email_detected"`
  - Blocks non-allowlisted URLs → `reason: "url_not_allowlisted"`
  - Allowlisted domains: `youtube.com`, `khanacademy.org`, `desmos.com`, `geogebra.org`, `wikipedia.org`
  - Flags excessive caps (>50% uppercase, >20 chars total) → `reason: "excessive_caps"` (logged, not blocked)
  - Normal Hebrew/Arabic text passes without issue
- [ ] `MessageThrottler` tracks per-user sends via Redis:
  - `INCR cena:throttle:{userId}:daily` with TTL = midnight UTC
  - `INCR cena:throttle:{userId}:hourly` with TTL = 3600s
  - Returns `ThrottleResult(allowed, retryAfterSeconds?)`
- [ ] Blocked messages → `MessagingNatsPublisher.PublishMessageBlockedAsync()` for audit
- [ ] Throttle exceeded → `ActorResult(Success: false, ErrorCode: "RATE_LIMIT_EXCEEDED", retryAfterSeconds: N)`
- [ ] Content moderation runs BEFORE Redis write (don't store blocked messages)
- [ ] Throttle check runs BEFORE content moderation (cheap check first)

**Test:**
```csharp
public class ContentModeratorTests
{
    private readonly ContentModerator _mod = new();

    [Theory]
    [InlineData("Call me at +972501234567", "phone_number_detected")]
    [InlineData("My number is 0501234567", "phone_number_detected")]
    [InlineData("Email me at user@gmail.com", "email_detected")]
    [InlineData("Visit https://random-site.com", "url_not_allowlisted")]
    public void BlocksDangerousContent(string text, string expectedReason)
    {
        var result = _mod.Check(text);
        Assert.False(result.Safe);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Theory]
    [InlineData("Great job on today's quiz!")]
    [InlineData("כל הכבוד! המשיכי כך")]
    [InlineData("أحسنت! واصل العمل الجيد")]
    [InlineData("Watch this: https://www.youtube.com/watch?v=abc")]
    [InlineData("Try https://www.desmos.com/calculator")]
    [InlineData("Read https://en.wikipedia.org/wiki/Fractions")]
    public void AllowsSafeContent(string text)
    {
        Assert.True(_mod.Check(text).Safe);
    }

    [Fact]
    public void FlagsExcessiveCaps_ButDoesNotBlock()
    {
        var result = _mod.Check("STOP DOING THAT RIGHT NOW!!!");
        Assert.True(result.Safe); // Not blocked
        Assert.Equal("excessive_caps", result.Flag); // Flagged for review
    }
}

public class MessageThrottlerTests
{
    [Fact]
    public async Task Teacher_Allowed100PerDay()
    {
        var throttler = CreateThrottler();
        for (int i = 0; i < 100; i++)
        {
            var result = await throttler.CheckAsync("teacher-1", MessageRole.Teacher);
            Assert.True(result.Allowed, $"Failed at message {i}");
        }
        var blocked = await throttler.CheckAsync("teacher-1", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task Parent_Allowed10PerDay()
    {
        var throttler = CreateThrottler();
        for (int i = 0; i < 10; i++)
            Assert.True((await throttler.CheckAsync("parent-1", MessageRole.Parent)).Allowed);
        Assert.False((await throttler.CheckAsync("parent-1", MessageRole.Parent)).Allowed);
    }

    [Fact]
    public async Task Student_AlwaysBlocked()
    {
        var throttler = CreateThrottler();
        Assert.False((await throttler.CheckAsync("student-1", MessageRole.Student)).Allowed);
    }

    [Fact]
    public async Task Teacher_HourlyLimit30()
    {
        var throttler = CreateThrottler();
        for (int i = 0; i < 30; i++)
            Assert.True((await throttler.CheckAsync("teacher-burst", MessageRole.Teacher)).Allowed);
        var blocked = await throttler.CheckAsync("teacher-burst", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
    }
}
```

---

## Integration Test

```csharp
[Fact]
public async Task FullFlow_TeacherSendsMessage_StudentReceives_ReadReceipt()
{
    // 1. Teacher sends via SignalR
    var teacherHub = await CreateHub("teacher-1", "Teacher", classIds: new[] { "class-1" });
    var studentHub = await CreateHub("student-1", "Student");

    await teacherHub.InvokeAsync("SendDirectMessage", new {
        recipientId = "student-1",
        content = new { text = "Review fractions before tomorrow!", contentType = "text" },
    });

    // 2. Student receives real-time event
    var received = await studentHub.WaitForAsync<MessageReceivedPayload>(
        "MessageReceived", TimeSpan.FromSeconds(5));
    Assert.Equal("Review fractions before tomorrow!", received.Content.Text);

    // 3. Message is in Redis
    var reader = CreateRedisReader();
    var page = await reader.GetMessagesAsync(received.ThreadId, before: null, limit: 10);
    Assert.Single(page.Messages);

    // 4. NATS audit event published
    Assert.Contains(CapturedNatsMessages, m =>
        m.Subject == "cena.messaging.events.MessageSent");

    // 5. Thread metadata in Marten
    var summary = await _session.LoadAsync<ThreadSummary>(received.ThreadId);
    Assert.Equal("DirectMessage", summary.ThreadType);
    Assert.Contains("teacher-1", summary.ParticipantIds);

    // 6. Student marks as read
    await studentHub.InvokeAsync("MarkMessageRead", new {
        threadId = received.ThreadId, messageId = received.MessageId,
    });

    // 7. Teacher gets receipt
    var receipt = await teacherHub.WaitForAsync<MessageReadReceiptPayload>(
        "MessageReadReceipt", TimeSpan.FromSeconds(5));
    Assert.Equal(received.MessageId, receipt.MessageId);

    // 8. Unread count is 0
    var unread = await reader.GetUnreadCountAsync(received.ThreadId, "student-1");
    Assert.Equal(0, unread);
}

[Fact]
public async Task FullFlow_WhatsAppReply_ClassifiedAndStored()
{
    // 1. Student replies "Thanks!" via WhatsApp
    var form = new Dictionary<string, string>
    {
        ["From"] = "+972501234567",
        ["Body"] = "Thanks for the reminder!",
        ["MessageSid"] = "SM500"
    };
    await _client.SendAsync(CreateSignedRequest("/api/webhooks/whatsapp", form, AuthToken));

    // 2. Classified as communication → stored in Redis
    await Task.Delay(1000); // Allow NATS consumer to process
    // Message should be in the thread between the student and whoever sent the outreach
    var reader = CreateRedisReader();
    var threads = await reader.GetUserThreadsAsync("student-1", 0, 10);
    Assert.NotEmpty(threads);

    // 3. NATS audit trail
    Assert.Contains(CapturedNatsMessages, m =>
        m.Subject == "cena.messaging.events.InboundReceived");
}
```

## Rollback Criteria

- If SignalR message delivery rate drops below 95%: add Redis pub/sub fallback for cross-instance fan-out
- If webhook latency exceeds 200ms: defer NATS publish to background (acknowledge webhook immediately)
- If content moderation false positives exceed 5%: expand URL allowlist, soften phone regex
- If class broadcasts with 200+ students cause memory spikes: batch SignalR sends in groups of 20

## Definition of Done

- [ ] All 4 subtasks pass their tests
- [ ] `dotnet test --filter Messaging` → 0 failures
- [ ] WhatsApp webhook validates Twilio signatures, deduplicates, and routes correctly
- [ ] Telegram webhook validates tokens, ignores non-text/non-private messages
- [ ] SignalR hub delivers real-time messages, read receipts, and class announcements
- [ ] REST endpoints paginate threads and messages with cursor-based pagination
- [ ] Access control enforced: users can only access threads they participate in
- [ ] Content moderation blocks phone numbers, emails, and non-allowlisted URLs
- [ ] Per-role throttling enforced (teacher 100/day, parent 10/day, student 0/day)
- [ ] PII never logged in plaintext (phone numbers SHA-256 hashed)
- [ ] All message events published to NATS for audit trail
- [ ] SignalR types added to `contracts/frontend/signalr-messages.ts`
