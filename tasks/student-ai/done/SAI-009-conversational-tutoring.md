# SAI-009: Conversational Tutoring — TutorActor + RAG Pipeline

**Priority:** P3 — highest-value Tier 3 feature, most complex
**Blocked by:** SAI-004 (L3 explanations), SAI-008 (pgvector + embeddings)
**Estimated effort:** 7-10 days
**Stack:** .NET 9, Proto.Actor, pgvector, LLM ACL (gRPC), SignalR, Marten

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Students can flag confusion via `AddAnnotation(kind: 'confusion' | 'question')` but get no response. The `ConversationThreadActor` handles student-teacher messaging but is not wired for concept-level tutoring. This task creates a `TutorActor` that provides methodology-aware conversational tutoring backed by a RAG pipeline retrieving from the content corpus (SAI-007/008).

Entry point: extend `AddAnnotation(kind: 'question')` to trigger a `TutoringResponse` SignalR event. Alternatively, add a new `RequestTutoring` SignalR command.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Explanations/ExplanationResolver.cs` | L2/L3 pipeline — TutorActor extends this for multi-turn |
| `src/actors/Cena.Actors/Explanations/L3ExplanationRequest.cs` | Student context model — reuse for tutoring context |
| `src/actors/Cena.Actors/Embeddings/IContentVectorStore.cs` | From SAI-008 — RAG retrieval |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | Confusion state — gates tutoring timing |
| `src/actors/Cena.Actors/Methodology/MethodologyResolver.cs` | Active methodology — constrains tutoring style |
| `src/actors/Cena.Actors/Messaging/ConversationThreadActor.cs` | Messaging pattern — model TutorActor similarly |
| `contracts/frontend/signalr-messages.ts` | Add new tutoring commands/events |

## Subtasks

### SAI-009.1: TutorActor — Session-Scoped Conversational Actor

**Files to create:**
- `src/actors/Cena.Actors/Tutoring/TutorActor.cs`
- `src/actors/Cena.Actors/Tutoring/TutoringMessages.cs`
- `src/actors/Cena.Actors/Tutoring/TutoringState.cs`

**Implementation:**

Classic (non-virtual) child actor of `LearningSessionActor`, spawned on first tutoring request within a session. Destroyed on session end.

State:
- Current concept being tutored
- Conversation history (last 5 turns)
- Methodology constraint
- Student mastery snapshot at tutoring start

Messages:
- `StartTutoring(conceptId, questionId, studentQuestion)` → spawns actor if not exists
- `ContinueTutoring(studentMessage)` → multi-turn
- `EndTutoring` → destroy actor

**Acceptance:**
- [ ] Child of `LearningSessionActor` — delegates events to `StudentActor` via same `DelegateEvent` pattern
- [ ] Max 1 `TutorActor` per session (single concept focus)
- [ ] Auto-destroys after 10 minutes of inactivity
- [ ] Conversation history capped at 5 turns (beyond that, summarize)
- [ ] Emits `TutoringSessionStarted_V1`, `TutoringMessageSent_V1`, `TutoringSessionEnded_V1` events

---

### SAI-009.2: RAG Retrieval Pipeline

**Files to create:**
- `src/actors/Cena.Actors/Tutoring/RagPipeline.cs`

**Implementation:**

For each student question:
1. Generate query embedding from student message via `IEmbeddingService`
2. Search `IContentVectorStore` for top 3 passages (filtered by concept, subject, language)
3. Combine retrieved passages with student context (from `L3ExplanationRequest` builder)
4. Build LLM prompt with: system instruction (methodology), retrieved passages, conversation history, student question
5. Call LLM ACL for response
6. Return response with source citations

**Context window budget** (per turn):
- System prompt + methodology constraint: ~200 tokens
- Student mastery context: ~100 tokens
- Retrieved passages (top 3): ~600 tokens
- Conversation history (last 5 turns): ~500 tokens
- Student question: ~100 tokens
- Response budget: ~500 tokens
- **Total: ~2000 tokens** (fits Haiku/Sonnet comfortably)

**Acceptance:**
- [ ] Top-3 retrieval from pgvector with concept and language filters
- [ ] Methodology enforced as system instruction (Socratic NEVER gives answer, etc.)
- [ ] Conversation history included (last 5 turns)
- [ ] Source citations: each retrieved passage has `contentId` traceable to source document
- [ ] Circuit breaker check before LLM call
- [ ] Latency budget: < 3 seconds total (embedding: ~100ms, search: ~50ms, LLM: ~2s)

---

### SAI-009.3: SignalR Contract for Tutoring

**Files to modify:**
- `contracts/frontend/signalr-messages.ts`

Add new commands and events:

```typescript
// Client → Server
interface RequestTutoringPayload {
    conceptId: string;
    questionId: string;
    question: string;       // Student's question text
}

interface ContinueTutoringPayload {
    message: string;
}

interface EndTutoringPayload {
    reason: 'done' | 'moved_on' | 'session_end';
}

// Server → Client
interface TutoringResponsePayload {
    sessionId: string;
    conceptId: string;
    response: string;       // Markdown + LaTeX
    methodology: string;    // Active methodology used
    sources: ReadonlyArray<{
        contentId: string;
        contentType: string;
        excerpt: string;
    }>;
    turnCount: number;
    hasMoreTurns: boolean;  // false if approaching limit
}
```

**Acceptance:**
- [ ] New SignalR commands added to contract
- [ ] `TutoringResponse` includes source citations
- [ ] `hasMoreTurns` warns client when approaching conversation limit
- [ ] All payloads include `correlationId` for client-server matching

---

### SAI-009.4: Safety and Guardrails

**Files to create:**
- `src/actors/Cena.Actors/Tutoring/TutoringGuardrails.cs`

**Implementation:**

Student-facing LLM responses need safety checks:

1. **Topic guardrails**: Validate student question relates to current concept or its prerequisites (use `IConceptGraphCache` scope)
2. **Content filtering**: LLM ACL handles content safety (LLM-003 sanitizer)
3. **Methodology enforcement**: Post-check response — if Socratic mode and response contains direct answer, regenerate with stronger constraint
4. **Rate limiting**: Max 10 tutoring turns per session, max 3 tutoring sessions per day per student
5. **Language consistency**: Response must match question language (Hebrew or Arabic)

**Acceptance:**
- [ ] Off-topic questions return: "Let's stay focused on {conceptName}. What about it is confusing?"
- [ ] Rate limits enforced (10 turns/session, 3 sessions/day)
- [ ] Methodology enforcement checked post-generation
- [ ] Response language matches question language
- [ ] Counter: `cena.tutoring.guardrail_triggered_total` with `type` tag

---

### SAI-009.5: Marten Events for Tutoring Analytics

**Files to create:**
- `src/actors/Cena.Actors/Events/TutoringEvents.cs`

Register events:
- `TutoringSessionStarted_V1(StudentId, SessionId, ConceptId, QuestionId, Methodology, Timestamp)`
- `TutoringMessageSent_V1(StudentId, TutoringSessionId, Direction: student|tutor, MessageLength, SourceCount, Latency, Timestamp)`
- `TutoringSessionEnded_V1(StudentId, TutoringSessionId, TurnCount, Duration, Reason, Timestamp)`

Register in `MartenConfiguration.cs`. Publish to NATS `cena.events.tutoring.*`.

**Acceptance:**
- [ ] Events persisted to Marten
- [ ] Published to NATS for analytics
- [ ] Direction tracked (student message vs tutor response)
- [ ] Latency tracked per response (for performance monitoring)

---

## Testing

```csharp
[Fact]
public async Task TutorActor_EnforcesSocraticMethodology()
{
    // Start tutoring with Socratic methodology
    // Student asks: "What's the answer?"
    // Tutor response should ask a guiding question, NOT give the answer
}

[Fact]
public async Task TutorActor_RejectsOffTopicQuestion()
{
    // Tutoring on "quadratic-formula" concept
    // Student asks about "history of Rome"
    // Response redirects to concept
}

[Fact]
public async Task TutorActor_IncludesRagSources()
{
    // Content corpus has theorem about Pythagorean theorem
    // Student asks about right triangles
    // Response includes source citation with contentId
}

[Fact]
public async Task TutorActor_RateLimits10Turns()
{
    // Send 10 tutoring messages
    // 11th should return hasMoreTurns: false and end session
}
```
