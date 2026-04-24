// =============================================================================
// Cena Platform — DI registration for PII prompt scrubber (ADR-0047, prr-022)
//
// Hosts call AddPiiPromptScrubber() during startup to register:
//   - IPiiPromptScrubber → PiiPromptScrubber (singleton; pre-compiled regex)
//
// The scrubber is a singleton because the regexes are compiled once and shared
// across all threads. The meter is ambient via IMeterFactory (registered by
// the standard AddMetrics()/AddOpenTelemetry() path — every host that calls
// AddLlmCostMetric() already has that wiring).
//
// See docs/adr/0047-no-pii-in-llm-prompts.md.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// DI extensions for registering the ADR-0047 PII prompt scrubber.
/// </summary>
public static class PiiPromptScrubberRegistration
{
    /// <summary>
    /// Register <see cref="IPiiPromptScrubber"/> → <see cref="PiiPromptScrubber"/>
    /// as a singleton. Idempotent (uses TryAddSingleton) so hosts can call it
    /// once per composition root without worrying about double-registration.
    /// </summary>
    public static IServiceCollection AddPiiPromptScrubber(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPiiPromptScrubber, PiiPromptScrubber>();
        return services;
    }
}
