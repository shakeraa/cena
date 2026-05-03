// =============================================================================
// Cena Platform — Anthropic connection probe
//
// Real "is the API key + model reachable" check. Used by
// AiGenerationService.TestConnectionAsync to replace the previous "is a key
// set?" stub with an actual round-trip to Anthropic.
//
// Probe must NEVER throw — wrap every error path into a structured
// ConnectionTestResult so the SPA can render the reason (auth failed,
// model not found, rate limited, network).
// =============================================================================

namespace Cena.Admin.Api.AiSettings;

public interface IAnthropicConnectionProbe
{
    Task<ConnectionTestResult> ProbeAsync(
        string apiKey,
        string modelId,
        string? baseUrl,
        CancellationToken ct = default);
}
