// =============================================================================
// Ship-gate positive-test fixture: ADR-0026 LLM-routing scanner (TAGGED)
//
// Purpose: Counterpart to llm-routing-violation-sample.cs. This file carries a
// [TaskRouting] attribute and the scanner MUST NOT fire on it.
//
// Demonstrates the canonical annotation pattern for an LLM-consuming class.
// =============================================================================

using Anthropic;
using Cena.Infrastructure.Llm;

namespace Cena.ShipgateFixtures.LlmRouting;

[TaskRouting("tier3", "socratic_question")]
public sealed class FixtureTaggedLlmService
{
    private readonly AnthropicClient _client;

    public FixtureTaggedLlmService()
    {
        _client = new AnthropicClient { ApiKey = "fixture-not-a-real-key" };
    }
}
