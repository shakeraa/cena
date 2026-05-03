// =============================================================================
// Ship-gate positive-test fixture: ADR-0026 LLM-routing scanner
//
// Purpose: This file deliberately declares an LLM call site (imports Anthropic,
// names a class *LlmService, instantiates AnthropicClient) without a
// [TaskRouting] attribute. The scanner MUST fire on this fixture.
//
// DO NOT tag this file. DO NOT add it to llm-routing-allowlist.yml. The test
// at tests/shipgate/llm-routing-scanner.spec.mjs depends on it producing a
// violation.
//
// Sibling precedent: shipgate/fixtures/banned-citation-sample.md,
// shipgate/fixtures/banned-mechanics-sample.md — same pattern for positive-test
// fixtures across ship-gate scanners.
// =============================================================================

using Anthropic;

namespace Cena.ShipgateFixtures.LlmRouting;

public sealed class FixtureUntaggedLlmService
{
    private readonly AnthropicClient _client;

    public FixtureUntaggedLlmService()
    {
        _client = new AnthropicClient { ApiKey = "fixture-not-a-real-key" };
    }
}
