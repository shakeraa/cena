# Task 07: Conversational Tutoring (TutorActor)

**Effort**: 7-10 days | **Track**: F | **Depends on**: ALL previous tasks (00-06)

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor virtual actors, Marten, NATS JetStream, SignalR, Redis.

After Tasks 00-06, the system has:
- Real Anthropic SDK calls with circuit breaker (Task 00)
- Persisted L1 explanations on questions (Task 01a)
- Hint generation with BKT credit + confusion gating (Task 01b)
- L2 Redis-cached explanations by error type (Task 02)
- L3 personalized explanations with methodology awareness (Task 03)
- A/B experiment framework for measuring impact (Task 04)
- Content extraction from textbooks/materials (Task 05)
- pgvector semantic search for RAG retrieval (Task 06)

This task is the **culmination**: a `TutorActor` that manages multi-turn conversational tutoring, using all prior work.

**Research basis**: VanLehn (2011) — conversational tutoring has ~0.4 sigma effect size. D'Mello & Graesser (2012) — confusion resolution in dialogue leads to deeper learning. But bad RAG is WORSE than no RAG — the retrieval corpus and methodology enforcement must be solid.

---

## Objective

Build a `TutorActor` (Proto.Actor virtual actor, per-student, per-session) that provides multi-turn conversational tutoring with RAG retrieval, methodology enforcement, and budget controls.

---

## Files to Read First (MANDATORY — ALL of them)

| File | Path | Why |
|------|------|-----|
| signalr-messages.ts | `contracts/frontend/signalr-messages.ts` | `AddAnnotation(kind: 'confusion'|'question')` — entry points. `TutoringResponse` will be new. |
| LearningSessionActor | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Parent actor that delegates to TutorActor |
| routing-config.yaml | `contracts/llm/routing-config.yaml` | `socratic_question` → Sonnet 4.6, temp 0.4. `tutoring_response` task type. |
| ConfusionDetector | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | ConfusionStuck triggers automatic tutoring |
| ScaffoldingService | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Determines explanation depth |
| ExplanationCacheService | (Task 02) | L2 fallback when budget exhausted |
| EmbeddingService | (Task 06) | RAG retrieval |
| LlmCircuitBreakerActor | `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | All LLM calls through this |
| All methodology services | MethodologySwitchService, etc. | MethodologyResolver determines tutoring style |

---

## Architecture

```
┌────────────────────┐     ┌───────────────────┐     ┌──────────────────┐
│  Student Client    │────▶│ LearningSession   │────▶│   TutorActor     │
│  (SignalR)         │◀────│ Actor (parent)     │◀────│ (per session)    │
└────────────────────┘     └───────────────────┘     └──────┬───────────┘
                                                            │
                           ┌───────────────────┐            │
                           │ StudentActor      │◄───────────┤ (mastery context)
                           │ (per student)     │            │
                           └───────────────────┘            │
                                                            │
                           ┌───────────────────┐            │
                           │ EmbeddingService  │◄───────────┤ (RAG retrieval)
                           │ (pgvector)        │            │
                           └───────────────────┘            │
                                                            │
                           ┌───────────────────┐            │
                           │ LlmCircuitBreaker │◄───────────┘ (LLM calls)
                           │ Actor             │
                           └───────────────────┘
```

---

## Implementation

### 1. TutorActor

Proto.Actor virtual actor. Lifecycle: created by `LearningSessionActor` when tutoring is triggered, destroyed at session end.

```csharp
public sealed class TutorActor : IActor
{
    private readonly List<ConversationTurn> _turns = new();
    private string _currentConceptId;
    private string _activeMethodology;
    private int _turnCount;

    // Max 10 turns per tutoring episode
    private const int MaxTurns = 10;
}

public record ConversationTurn(
    string Role,        // "student" or "tutor"
    string Content,
    DateTimeOffset Timestamp);
```

### 2. Entry Points (4 triggers)

| Trigger | Source | Behavior |
|---------|--------|----------|
| `AddAnnotation(kind: 'confusion')` | Student flags confusion | Start tutoring dialogue about current concept |
| `AddAnnotation(kind: 'question')` | Student asks question | Start tutoring with student's question as first turn |
| `ConfusionStuck` | ConfusionDetector | Auto-start tutoring (proactive, not student-initiated) |
| Post-wrong-answer follow-up | After L2/L3 explanation | Offer: "Do you want to discuss this further?" |

### 3. RAG Context Assembly

Per turn, retrieve relevant content:
```csharp
var retrieved = await _embeddingService.SearchSimilar(
    queryText: studentMessage,
    conceptFilter: new[] { _currentConceptId },
    subjectFilter: _subject,
    limit: 3,
    minSimilarity: 0.7f);
```

Inject top 3 passages into LLM context alongside:
- Current question + student's answer
- ConceptMasteryState (BKT probability, Bloom level)
- Last 3 conversation turns
- Active methodology constraint

### 4. Methodology Enforcement

The LLM system prompt MUST enforce the active methodology:

| Methodology | System Prompt Instruction |
|-------------|--------------------------|
| **Socratic** | "You are a Socratic tutor. Ask questions to guide the student to discover the answer. NEVER give the answer directly, even if asked. If the student says 'just tell me', redirect with another question." |
| **WorkedExample** | "Show a similar solved problem step-by-step. After showing, ask the student to apply the same pattern to their original problem." |
| **Feynman** | "Ask the student to explain the concept in their own words. When they explain, identify gaps in their reasoning and ask about those gaps." |
| **Direct** | "Explain the concept clearly and directly. Break into numbered steps. Use the student's language level." |

### 5. Guardrails

- **Max 10 turns** per episode. After 10: "Let's try a new question to practice what we discussed."
- **Off-topic detection**: If student goes off-curriculum, redirect: "That's interesting, but let's focus on [concept]. Can you tell me..."
- **Safety**: No personal advice, no opinions, only educational content. If detected, deflect gracefully.
- **Budget**: Per-student daily token budget (`daily_output_token_limit: 25000`). When exhausted, serve L2 cached explanations only.

### 6. Conversation State

Ephemeral — lives in actor memory. Do NOT persist to Marten event store. Conversation history dies with the session. Only aggregate metrics are persisted (turn count, topics covered, resolution outcome).

### 7. SignalR Integration

New event (add to signalr-messages.ts):
```typescript
interface TutoringResponse {
    turnNumber: number;
    response: string;        // Markdown+LaTeX
    isComplete: boolean;     // true when episode ends
    remainingTurns: number;
}
```

Use existing SignalR hub — do NOT create a new hub.

---

## What NOT to Do

- Do NOT build WhatsApp/Telegram integration — in-app only
- Do NOT persist conversation history to event store — ephemeral
- Do NOT create a new SignalR hub — use existing student session hub
- Do NOT bypass circuit breaker — all LLM calls through `LlmCircuitBreakerActor`
- Do NOT allow unlimited turns — hard cap at 10
- Do NOT skip RAG retrieval — every response must be grounded in content
- Do NOT use streaming for tutoring responses — send complete responses (simpler, more reliable, methodology enforcement works on complete responses)

---

## Verification Checklist

- [ ] `AddAnnotation(kind: 'confusion')` → tutoring dialogue starts
- [ ] `AddAnnotation(kind: 'question')` → tutoring dialogue starts with student question
- [ ] `ConfusionStuck` → auto-triggered tutoring
- [ ] Socratic mode: tutor asks questions, never reveals answer directly
- [ ] WorkedExample mode: tutor shows step-by-step similar problem
- [ ] RAG retrieval returns relevant content (verify via logs)
- [ ] Turn limit: after 10 turns, episode ends gracefully
- [ ] Off-topic detection: redirect to current concept
- [ ] Budget exhaustion: fallback to L2 cached explanations
- [ ] Circuit breaker open: graceful degradation
- [ ] Hebrew tutoring works (RTL, correct terminology)
- [ ] Arabic tutoring works (RTL, correct terminology)
- [ ] Session end: TutorActor destroyed, no memory leak
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
