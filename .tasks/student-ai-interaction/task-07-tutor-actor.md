# Task 07: Conversational Tutoring (TutorActor)

**Track**: F
**Effort**: 7-10 days
**Depends on**: All previous tasks (00, 01a, 01b, 02, 03, 04, 05, 06)
**Blocks**: Nothing (this is the capstone)

---

## System Context

Cena is an event-sourced .NET educational platform using Proto.Actor virtual actors, Marten/PostgreSQL, Redis, NATS JetStream, and SignalR WebSocket for student sessions.

After Tasks 00-06, the platform has:
- Real Anthropic SDK calls with circuit breaker resilience (Task 00)
- Persisted L1 explanations per question (Task 01a)
- Hint system with BKT credit adjustment and confusion gating (Task 01b)
- L2 error-type explanation cache in Redis (Task 02)
- L3 personalized explanations using full student context (Task 03)
- A/B experiment framework configured for impact measurement (Task 04)
- Content extraction from source documents into semantic blocks (Task 05)
- pgvector embeddings with HNSW search for content retrieval (Task 06)

This task builds the `TutorActor` — a multi-turn conversational tutoring actor that manages guided dialogue sessions. The student can ask follow-up questions, request clarification, or express confusion, and the tutor responds with methodology-aware, context-informed answers grounded in retrieved content.

The existing SignalR contract already provides the entry points:
- `AddAnnotation(kind: 'confusion')` — student flags confusion (lines 211-220 of `signalr-messages.ts`)
- `AddAnnotation(kind: 'question')` — student asks a question
- `ConfusionDetector` reaching `ConfusionStuck` state — automatic trigger

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `contracts/frontend/signalr-messages.ts` | 211-220 | `AddAnnotationPayload` — `kind: 'note' | 'question' | 'confusion' | 'insight'` |
| `contracts/llm/routing-config.yaml` | Find `socratic_question` | Task config: Sonnet 4.6, temp 0.4 — this is the tutoring task type |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Full | Parent actor that creates and manages child actors. `TutorActor` will be a child like `StagnationDetectorActor` |
| `src/actors/Cena.Actors/Students/StudentActor.cs` | Full | Grain per student — understand the actor hierarchy and event delegation pattern |
| `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` | Full | From Task 03 — student context assembly. TutorActor reuses this. |
| `src/actors/Cena.Actors/Services/EmbeddingService.cs` | Full | From Task 06 — `SearchSimilarAsync()` is the RAG retrieval interface |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 79-85 | `ConfusionState` — `ConfusionStuck` triggers tutoring |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | 34-59 | All LLM calls through circuit breaker |
| All Task 01b-03 services | Full | Understand the affect/attention system: ConfusionDetector, DisengagementClassifier, CognitiveLoadService, ScaffoldingService, MethodologyResolver |

---

## Implementation Requirements

### 1. Create `TutorActor`

**Location**: `src/actors/Cena.Actors/Sessions/TutorActor.cs`

Proto.Actor virtual actor, managed as a child of `LearningSessionActor`. One `TutorActor` per tutoring episode (not per session — a session may have 0-N tutoring episodes).

```csharp
public sealed class TutorActor : IActor
{
    // Conversation state (ephemeral, not event-sourced)
    private readonly List<TutorTurn> _turns = new();
    private readonly string _studentId;
    private readonly string _sessionId;
    private readonly string _conceptId;
    private int _turnCount;

    // Injected services
    private readonly IEmbeddingService _embeddings;
    private readonly PersonalizedExplanationService _personalization;
    private readonly IConceptGraphCache _conceptGraph;
    // ... circuit breaker, methodology resolver, etc.
}
```

### 2. Lifecycle

```
Creation:     LearningSessionActor spawns TutorActor on tutoring trigger
Active:       Handles student messages, generates responses
Termination:  After max turns (10), student ends conversation, or session ends
```

- `ReceiveTimeout`: 5 minutes — if student doesn't respond, gracefully end episode
- Do NOT persist conversation to event store — tutoring conversations are ephemeral
- DO emit a summary event: `TutoringEpisodeCompleted_V1(studentId, sessionId, conceptId, turnCount, duration, triggerType)`

### 3. Entry Points (3 triggers)

**Trigger 1: Student annotation** — `AddAnnotation(kind: 'confusion' | 'question')`
- `LearningSessionActor` receives `AddAnnotation` → spawns `TutorActor` with the annotation text as the initial message
- The annotation text is the student's question/confusion description

**Trigger 2: ConfusionStuck** — automatic
- When `ConfusionDetector` transitions to `ConfusionStuck`, `LearningSessionActor` spawns `TutorActor` with a generated opening: "I noticed you might be finding [concept] challenging. Let me help."
- This is a proactive intervention — the student didn't ask for help

**Trigger 3: Post-explanation follow-up**
- After L2/L3 explanation is delivered (Tasks 02-03), offer "Want to explore this further?" via SignalR
- If student responds, spawn `TutorActor` with the explanation + student's follow-up as context

### 4. Conversation Flow

Each turn follows this pipeline:

```
1. Receive student message
2. Assemble context:
   a. Student mastery state (from parent actor)
   b. Active methodology (from MethodologyResolver)
   c. Conversation history (last 3 turns — token budget)
   d. RAG retrieval (EmbeddingService.SearchSimilarAsync)
   e. Current question context (if active)
3. Build prompt with methodology enforcement
4. Call LLM via circuit breaker
5. Send response via SignalR
6. Increment turn counter → check limits
```

### 5. RAG Retrieval

For each student turn, retrieve relevant content blocks:

```csharp
var results = await _embeddings.SearchSimilarAsync(
    query: studentMessage,
    filter: new SearchFilter(
        ConceptIds: new[] { _conceptId },  // stay on-topic
        Language: student.Language
    ),
    limit: 3,           // top 3 blocks
    minSimilarity: 0.7f
);
```

Inject retrieved content into the LLM prompt as reference material. The prompt must instruct the LLM to cite content from the retrieved blocks, not hallucinate.

### 6. Methodology Enforcement

The `MethodologyResolver` determines the tutoring style. This is a hard constraint, not a suggestion.

| Methodology | Tutoring Behavior | LLM Prompt Constraint |
|-------------|------------------|----------------------|
| Socratic | Ask questions, guide discovery | "You MUST ask questions. You MUST NOT reveal the answer. If the student asks for the answer directly, redirect with a leading question." |
| WorkedExample | Step-by-step demonstration | "Walk through a similar problem step by step. Then ask the student to apply the same approach." |
| Feynman | Teach-back | "Ask the student to explain the concept. Identify gaps in their explanation. Then fill those gaps." |
| RetrievalPractice | Recall prompts | "Before explaining, ask what the student remembers. Only fill gaps they can't recall." |
| DirectInstruction | Clear explanation | "Explain clearly and directly. Use concrete examples." |

If methodology is `DrillAndPractice` or `SpacedRepetition`: do NOT start a tutoring conversation — these methodologies don't benefit from dialogue. Instead, present the next practice question.

### 7. Guardrails

**Turn limit**: Max 10 turns per episode. After turn 8, signal "We have 2 more exchanges. Let me summarize what we've covered."

**Off-topic detection**: If the student's message has no semantic overlap with the current concept (cosine similarity < 0.3 with concept description), respond: "Let's stay focused on [concept]. What specifically about [concept] is unclear?"

**Safety**: No personal advice, no opinions, only educational content. If the student's message contains non-educational content, redirect to the concept.

**Token budget**: Enforce per-student daily budget from routing config (`daily_output_token_limit: 25000`). Shared with L3 explanations. If exhausted: "I've used my explanation budget for today. Let's continue tomorrow. In the meantime, review [concept] using these hints: [L1 hint]."

### 8. SignalR Communication

Use the existing SignalR hub — do NOT create a new hub. Define new message types:

**Server → Client**:
- `TutoringStarted` — `{ sessionId, conceptId, openingMessage, methodology }`
- `TutorResponse` — `{ sessionId, messageText (Markdown+LaTeX), suggestedActions: string[], turnNumber, turnsRemaining }`
- `TutoringEnded` — `{ sessionId, summary, conceptId, nextAction }`

**Client → Server**:
- `TutorMessage` — `{ sessionId, text }` — student's follow-up message

Add these to `contracts/frontend/signalr-messages.ts` following the existing pattern.

### 9. Summary Event

When the episode ends, emit a lightweight domain event for analytics:

```csharp
public sealed record TutoringEpisodeCompleted_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string TriggerType,         // confusion_annotation, question_annotation, confusion_stuck, post_explanation
    string Methodology,
    int TurnCount,
    TimeSpan Duration,
    string? ResolutionStatus,   // resolved, unresolved, student_ended, turn_limit, timeout
    DateTimeOffset Timestamp);
```

This event is delegated to the parent `StudentActor` for persistence. The conversation text is NOT persisted — only the metadata.

---

## What NOT to Do

- Do NOT build WhatsApp/Telegram integration — in-app only for now
- Do NOT persist conversation text to the event store — only metadata
- Do NOT create a new SignalR hub — use the existing student session hub
- Do NOT start tutoring for DrillAndPractice or SpacedRepetition methodologies
- Do NOT let the tutor reveal answers in Socratic mode — this is a hard constraint
- Do NOT make the TutorActor a singleton or cluster-wide actor — it's a child of LearningSessionActor
- Do NOT bypass circuit breaker for LLM calls
- Do NOT hallucinate content — ground responses in RAG-retrieved blocks

---

## Verification Checklist

- [ ] Trigger tutoring via `AddAnnotation(kind: 'confusion')` → multi-turn conversation starts
- [ ] Trigger tutoring via `ConfusionStuck` state → proactive opening message
- [ ] Trigger tutoring via post-explanation follow-up → context preserved from explanation
- [ ] Socratic mode: tutor asks questions, NEVER reveals the answer
- [ ] WorkedExample mode: tutor walks through analogous problem
- [ ] DrillAndPractice methodology: tutoring NOT started, next question presented instead
- [ ] RAG retrieval returns relevant content blocks for the concept
- [ ] Off-topic student message → redirect to concept
- [ ] Turn limit (10) reached → graceful summary and end
- [ ] 5-minute timeout → graceful end
- [ ] Token budget exhausted → degraded mode with L1 hints
- [ ] `TutoringEpisodeCompleted_V1` event emitted with correct metadata
- [ ] Conversation text NOT in event store
- [ ] SignalR messages follow existing contract patterns
- [ ] Hebrew/Arabic tutoring with RTL
- [ ] Circuit breaker failure → graceful degradation
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
