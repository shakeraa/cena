# TASK-SAI-08: Conversational Tutoring — TutorActor

**Priority**: LOW (high value, high cost, high risk — build last)
**Effort**: 7-10 days
**Depends on**: ALL previous tasks (TASK-SAI-00 through TASK-SAI-07)
**Track**: F (final track)

---

## Context

Students currently cannot ask "why?" or "I don't understand this part" during a session. The interaction is strictly question -> answer -> feedback. This task adds a conversational interface where students ask follow-up questions about the current concept/question, grounded in curriculum content via RAG.

### Why This Is Last

- Bad RAG is **worse** than no RAG — confidently explaining wrong things destroys student trust
- Requires good retrieval corpus (TASK-SAI-06 + TASK-SAI-07)
- Requires real LLM calls (TASK-SAI-00)
- Requires methodology enforcement (validated in TASK-SAI-05 experiments)
- Value compounds only after Tier 1 (explanations) and Tier 2 (hints) are in place and validated
- Highest cost per interaction (~$0.01/turn)
- Requires safety guardrails (content filtering, topic confinement, methodology enforcement)

### Existing Entry Points

The SignalR contract (`contracts/frontend/signalr-messages.ts`) already has:
- `AddAnnotation(kind: 'confusion' | 'question')` — student explicitly flags they need help
- This is the natural entry point for conversational tutoring

---

## Architecture

```text
Student sends message (SignalR)
  |
  v
TutorActor (per-session, short-lived)
  |
  ├── Retrieves student context from StudentActor
  │     - ConceptMastery, BloomsLevel, ActiveMethodology
  │     - RecentErrors, HintHistory, ConfusionState
  |
  ├── Retrieves relevant content via ContentRetriever (RAG)
  │     - Top 3 passages from pgvector search
  │     - Filtered by concept + subject + language
  |
  ├── Constructs prompt with methodology enforcement
  |
  ├── Calls LLM via ILlmClient (Claude Sonnet 4.6)
  |
  ├── Validates response (safety + topic confinement)
  |
  └── Sends response back (SignalR)
```

### Actor Lifecycle

- **Created**: when student sends first tutoring message or `AddAnnotation(kind: 'confusion')`
- **Destroyed**: when session ends OR after 5 minutes of inactivity
- **State**: last 5 conversation turns (sliding window), current question context
- **NOT persistent**: conversation history stored in Redis for session duration, then archived to Marten

---

## Implementation

### Create: `src/actors/Cena.Actors/Tutoring/TutorActor.cs`

```csharp
public sealed class TutorActor : IActor
{
    private readonly ILlmClient _llmClient;
    private readonly IContentRetriever _contentRetriever;
    private readonly ITutorPromptBuilder _promptBuilder;
    private readonly ITutorSafetyGuard _safetyGuard;

    private readonly List<ConversationTurn> _history = new(capacity: 5);
    private TutoringSessionContext? _sessionContext;
}

public sealed record TutoringSessionContext(
    string StudentId,
    string SessionId,
    string CurrentQuestionId,
    string CurrentConceptId,
    string Subject,
    string Language,
    string ActiveMethodology,
    double ConceptMastery,
    int BloomsLevel,
    ScaffoldingLevel ScaffoldLevel);

public sealed record ConversationTurn(
    string Role,       // "student" or "tutor"
    string Content,
    DateTimeOffset Timestamp);

public sealed record TutorMessage(string SessionId, string StudentMessage);
public sealed record TutorResponse(string ResponseText, bool SuggestEndConversation);
```

### Create: `src/actors/Cena.Actors/Tutoring/TutorPromptBuilder.cs`

**Context window per turn** (ordered for LLM):

1. **System prompt**: Methodology enforcement + safety rules + persona
2. **Curriculum context**: Top 3 retrieved passages from RAG
3. **Question context**: Current question stem + student answer + error type
4. **Student state**: Mastery level, Bloom's level, scaffold level (anonymized)
5. **Conversation history**: Last 3-5 turns

**System prompt template**:

```
You are an expert {subject} tutor helping an Israeli student prepare for the Bagrut exam.

METHODOLOGY: {methodology}
{methodology_rules}

RULES:
1. Stay on topic: only discuss {subject} concepts related to {conceptName}.
2. Never reveal exam answers directly. Guide the student to discover them.
3. Use {language} for all responses.
4. Use LaTeX for math: $inline$ or $$block$$.
5. Keep responses concise (2-4 sentences). Students are in a learning session, not reading an essay.
6. If the student asks about something outside the curriculum, say: "That's an interesting question, but let's focus on {conceptName} for now."
7. If the student seems frustrated, acknowledge it briefly and offer a simpler approach.

STUDENT CONTEXT:
- Mastery on this concept: {mastery_description} (novice/developing/proficient/mastered)
- Bloom's level: {bloomsLevel} — calibrate your language accordingly
- Scaffold level: {scaffoldLevel}

CURRICULUM CONTEXT:
{retrieved_passages}
```

**Methodology rules** (injected per methodology):

| Methodology | Rules |
|-------------|-------|
| Socratic | "Ask guiding questions. Never give direct answers. Lead the student to discover the solution through questioning." |
| WorkedExample | "Walk through a similar problem step-by-step. Then ask the student to apply the same approach to their question." |
| Feynman | "Ask the student to explain their understanding. Identify gaps. Correct specific misconceptions." |
| DrillAndPractice | "Keep responses very brief. Correct errors directly. Encourage speed." |
| Default | "Provide clear, pedagogically appropriate explanations at the student's level." |

### Create: `src/actors/Cena.Actors/Tutoring/TutorSafetyGuard.cs`

```csharp
public interface ITutorSafetyGuard
{
    SafetyResult Validate(string studentMessage, string tutorResponse, TutoringSessionContext context);
}

public sealed record SafetyResult(
    bool IsAllowed,
    string? BlockReason,
    string? SanitizedResponse);
```

**Checks**:
1. **Topic confinement**: If tutor response mentions concepts outside the current subject, truncate and redirect.
2. **Answer leaking**: If tutor response contains the exact correct answer to the current question, redact it.
3. **Inappropriate content**: Basic content filter for student messages (profanity, off-topic, PII disclosure).
4. **Response length**: Enforce max 500 tokens per response.
5. **Turn limit**: Max 10 turns per conversation. After 10, suggest returning to practice.

### Wire SignalR

**Modify**: The SignalR hub that handles student messages.

When `AddAnnotation(kind: 'confusion')` or a new `TutorMessage` arrives:
1. Spawn `TutorActor` (if not already active for this session)
2. Forward message to `TutorActor`
3. Return `TutorResponse` via SignalR

### Conversation Persistence

**Redis** (session duration):
```
tutor:{sessionId}:history -> List<ConversationTurn> (max 10)
tutor:{sessionId}:context -> TutoringSessionContext
TTL: session duration + 1 hour
```

**Marten** (permanent archive, after session ends):

```csharp
public sealed class TutoringSessionDocument
{
    public string Id { get; init; }
    public string StudentId { get; init; }
    public string SessionId { get; init; }
    public string ConceptId { get; init; }
    public string Subject { get; init; }
    public IReadOnlyList<ConversationTurn> Turns { get; init; }
    public string Methodology { get; init; }
    public int TotalTurns { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}
```

### Rate Limiting & Cost Control

- Max 3 tutor turns per student per minute
- Max 20 tutor turns per student per day
- Per-month budget alert: if student exceeds $2/month in tutoring LLM costs, downgrade to Haiku fallback
- Circuit breaker: `LlmCircuitBreakerActor` (Sonnet config: 3 failures/90s)

---

## Coding Standards

- `TutorActor` is a short-lived Proto.Actor grain — no heavy state. Conversation history in Redis, not in-memory.
- `TutorPromptBuilder` is a pure function — takes context in, returns prompt string out. No I/O. Trivially testable.
- `TutorSafetyGuard` is a synchronous validator — no LLM calls for safety (too slow). Use rule-based checks.
- Never include `StudentId` in LLM prompts. All context is anonymized.
- Conversation history is capped at 5 turns in the prompt (context window management).
- Archive to Marten only on session end (batch write, not per-turn).
- Test: write a test that simulates a 5-turn conversation and verifies methodology enforcement, topic confinement, and turn limits.
- Integration test: end-to-end SignalR message -> TutorActor -> LLM -> response -> SignalR.

---

## What NOT To Build (Yet)

- Multi-concept conversations (student asks about a different concept mid-conversation) — keep topic confined for V1
- Proactive tutoring (system initiates conversation) — only student-initiated for V1
- WhatsApp/Telegram integration — in-app only for V1
- Voice input — text only for V1
- Image input (student photographs their work) — text only for V1

---

## Acceptance Criteria

1. `TutorActor` spawns on `AddAnnotation(kind: 'confusion')` or explicit tutor message
2. Responses are grounded in RAG-retrieved curriculum content (top 3 passages)
3. Responses enforce active methodology (Socratic asks questions, WorkedExample shows steps)
4. Topic confinement: tutor stays on current subject/concept
5. Safety guard: no answer leaking, no inappropriate content, response length capped
6. Turn limit: max 10 turns per conversation, then suggests returning to practice
7. Rate limit: 3 turns/min, 20 turns/day per student
8. Conversation archived to Marten after session ends
9. Cost tracking: per-student LLM cost logged via `LlmUsageTracker`
10. Graceful degradation: if LLM unavailable, respond "I'm having trouble right now. Try using the hint button instead."
