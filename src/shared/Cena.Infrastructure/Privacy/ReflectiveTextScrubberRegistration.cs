// =============================================================================
// Cena Platform — DI registration for the Reflective Text Scrubber (prr-036)
//
// Hosts that persist or LLM-egress reflective text MUST call
// AddReflectiveTextScrubber(). The scrubber depends on the ADR-0047
// IPiiPromptScrubber — hosts must also call AddPiiPromptScrubber() (or the
// arch test will fail at startup because the DI resolve will throw).
// =============================================================================

using Cena.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Privacy;

/// <summary>
/// DI extensions for the prr-036 reflective-text scrubber.
/// </summary>
public static class ReflectiveTextScrubberRegistration
{
    /// <summary>
    /// Register <see cref="IReflectiveTextScrubber"/> → <see cref="ReflectiveTextScrubber"/>
    /// as a singleton. Also ensures the underlying ADR-0047 baseline scrubber
    /// is registered so a host can call only this method and get both.
    /// </summary>
    public static IServiceCollection AddReflectiveTextScrubber(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPiiPromptScrubber();
        services.TryAddSingleton<IReflectiveTextScrubber, ReflectiveTextScrubber>();
        return services;
    }
}
