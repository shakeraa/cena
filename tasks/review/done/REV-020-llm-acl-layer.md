# REV-020: Establish LLM Anti-Corruption Layer in src/llm-acl/

**Priority:** P2 -- MEDIUM (LLM client code deeply embedded in actor domain; provider change cascades through actors)
**Blocked by:** None
**Blocks:** Multi-provider support, global rate limiting, cost accounting
**Estimated effort:** 3 days
**Source:** System Review 2026-03-28 -- Lead Architect (LLM ACL section, rated 0/5), Pedagogy Professor 2 (LLM section)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

`src/llm-acl/` is an empty directory. LLM integration code is scattered across the actor system:

| File | Location | Responsibility |
|------|----------|---------------|
| `AnthropicLlmClient.cs` | `Cena.Actors/Gateway/` | Direct Anthropic SDK client |
| `LlmClientRouter.cs` | `Cena.Actors/Gateway/` | Multi-model routing |
| `LlmCircuitBreakerActor.cs` | `Cena.Actors/Gateway/` | Per-model circuit breaker |
| `LlmUsageTracker.cs` | `Cena.Actors/Gateway/` | Token usage tracking |
| `ExplanationGenerator.cs` | `Cena.Actors/Services/` | Prompt construction + LLM calls |
| `L3ExplanationGenerator.cs` | `Cena.Actors/Services/` | Deep explanation generation |
| `TutorPromptBuilder.cs` | `Cena.Actors/Tutoring/` | Tutoring prompt construction |
| `AiGenerationService.cs` | `Cena.Admin.Api/` | AI question generation |

This creates tight coupling: changing the LLM provider, adding a global rate limiter, or implementing prompt versioning requires touching the actor domain.

## Architect's Decision

Create `src/llm-acl/Cena.LlmAcl/` as a proper .NET class library project implementing the Anti-Corruption Layer pattern:

1. **`ILlmGateway`** -- the single interface both actors and admin services call. Replaces direct `ILlmClient` usage.
2. **Provider implementations** -- Anthropic, OpenAI, Google behind the gateway. Currently only Anthropic is real; others throw `NotSupportedException` with clear message (not `NotImplementedException` -- the difference matters).
3. **Global rate limiter** -- per-minute and per-day token budget across all callers (not just per-student).
4. **Prompt registry** -- versioned prompt templates as embedded resources, not hardcoded strings.

Do NOT move `TutorPromptBuilder` or `ExplanationGenerator` -- those contain domain logic (pedagogical decisions). Only the LLM client, routing, circuit breaking, and usage tracking move.

## Subtasks

### REV-020.1: Create the LLM ACL Project

**Files to create:**
```
src/llm-acl/Cena.LlmAcl/
  Cena.LlmAcl.csproj
  ILlmGateway.cs                    -- the anti-corruption interface
  LlmGateway.cs                     -- orchestrates routing + circuit breaking + tracking
  LlmGatewayOptions.cs              -- configuration
  Providers/
    ILlmProvider.cs                 -- provider-specific interface
    AnthropicProvider.cs            -- moved from Cena.Actors/Gateway/AnthropicLlmClient.cs
  Routing/
    ModelRouter.cs                  -- moved from Cena.Actors/Gateway/LlmClientRouter.cs
  Resilience/
    CircuitBreakerConfig.cs         -- moved from Cena.Actors/Gateway/
  Tracking/
    UsageTracker.cs                 -- moved from Cena.Actors/Gateway/LlmUsageTracker.cs
    GlobalRateLimiter.cs            -- NEW: cross-caller token budget
  Registration/
    LlmAclServiceRegistration.cs   -- DI extension method
```

**ILlmGateway interface:**
```csharp
public interface ILlmGateway
{
    /// <summary>
    /// Send a completion request through the ACL layer.
    /// Handles routing, circuit breaking, rate limiting, and usage tracking.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if the global rate limit allows another request.
    /// </summary>
    bool CanMakeRequest(string modelTier);
}

public record LlmRequest(
    string ModelPreference,       // "haiku", "sonnet", "opus"
    string SystemPrompt,
    string UserMessage,
    float Temperature = 0.3f,
    int MaxTokens = 2048,
    string? CallerContext = null   // "tutor", "explanation", "classification"
);

public record LlmResponse(
    string Content,
    string ModelUsed,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency
);
```

**Acceptance:**
- [ ] `Cena.LlmAcl.csproj` builds as a standalone class library
- [ ] `ILlmGateway` is the single interface for all LLM calls
- [ ] No Anthropic SDK references exist outside `Cena.LlmAcl` project

### REV-020.2: Move Gateway Code from Actors to LLM ACL

**Files to move:**
- `Cena.Actors/Gateway/AnthropicLlmClient.cs` -> `Cena.LlmAcl/Providers/AnthropicProvider.cs`
- `Cena.Actors/Gateway/LlmClientRouter.cs` -> `Cena.LlmAcl/Routing/ModelRouter.cs`
- `Cena.Actors/Gateway/LlmUsageTracker.cs` -> `Cena.LlmAcl/Tracking/UsageTracker.cs`
- `Cena.Actors/Gateway/ILlmClient.cs` -> keep as deprecated adapter, forwarding to `ILlmGateway`

**Files to modify (update references):**
- `Cena.Actors/Services/ExplanationGenerator.cs` -- inject `ILlmGateway` instead of `ILlmClient`
- `Cena.Actors/Services/L3ExplanationGenerator.cs` -- same
- `Cena.Actors/Services/ErrorClassificationService.cs` -- same
- `Cena.Actors/Tutoring/TutorActor.cs` -- same
- `Cena.Admin.Api/AiGenerationService.cs` -- same
- `Cena.Actors.Host/Program.cs` -- register `ILlmGateway` via `builder.Services.AddCenaLlmAcl()`
- `Cena.Api.Host/Program.cs` -- same

**Acceptance:**
- [ ] `Cena.Actors` project does not reference Anthropic SDK directly
- [ ] All LLM calls go through `ILlmGateway`
- [ ] `LlmCircuitBreakerActor` remains in `Cena.Actors` (it's a Proto.Actor concern) but delegates to the ACL for actual calls
- [ ] All 938 existing tests still pass

### REV-020.3: Add Global Rate Limiter

**File to create:** `src/llm-acl/Cena.LlmAcl/Tracking/GlobalRateLimiter.cs`

```csharp
public class GlobalRateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public bool TryAcquire(string modelTier, int estimatedTokens)
    {
        var window = _windows.GetOrAdd(modelTier, _ => new SlidingWindow(
            modelTier switch
            {
                "haiku" => 500_000,   // 500K tokens/min
                "sonnet" => 100_000,  // 100K tokens/min
                "opus" => 50_000,     // 50K tokens/min
                _ => 100_000
            },
            TimeSpan.FromMinutes(1)));

        return window.TryConsume(estimatedTokens);
    }
}
```

**Acceptance:**
- [ ] Global rate limiter enforces per-minute token budgets across all callers
- [ ] 300 concurrent students cannot exceed the per-model token rate
- [ ] Rate limit rejection returns a clear error message (not a silent failure)
- [ ] `TokenBudgetAdminService` reads from the same limiter (not disconnected)

### REV-020.4: Add Prompt Versioning (Embedded Resources)

**Directory to create:** `src/llm-acl/Cena.LlmAcl/Prompts/`

Store prompt templates as `.txt` embedded resources with version suffixes:
```
Prompts/
  tutor-socratic-v1.txt
  tutor-worked-example-v1.txt
  tutor-feynman-v1.txt
  tutor-direct-v1.txt
  explanation-personalized-v1.txt
  classification-error-v1.txt
```

**File to create:** `src/llm-acl/Cena.LlmAcl/Prompts/PromptRegistry.cs`

```csharp
public class PromptRegistry
{
    private readonly Dictionary<string, string> _templates = new();

    public PromptRegistry()
    {
        var assembly = typeof(PromptRegistry).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Prompts.")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var key = Path.GetFileNameWithoutExtension(name.Split('.').Last());
            _templates[key] = reader.ReadToEnd();
        }
    }

    public string GetTemplate(string name)
        => _templates.TryGetValue(name, out var template)
            ? template
            : throw new KeyNotFoundException($"Prompt template '{name}' not found");
}
```

**Acceptance:**
- [ ] All prompt templates are embedded resources (visible in git diff when changed)
- [ ] `PromptRegistry` loads templates at startup
- [ ] `TutorPromptBuilder` can optionally use registry templates (not forced -- domain logic stays in actors)
- [ ] Adding a new prompt version = adding a new `.txt` file (no code change needed)
