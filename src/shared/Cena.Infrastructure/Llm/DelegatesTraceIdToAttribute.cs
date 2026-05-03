// =============================================================================
// Cena Platform — DelegatesTraceIdTo attribute (prr-143)
//
// Companion to [DelegatesLlmCost]. Used to mark a [TaskRouting]-tagged class
// that does not itself stamp `trace_id` on the LLM call, because a
// collaborator it invokes is the stamping site.
//
// Typical case: a service that constructs an LlmRequest and calls
// ILlmClient.CompleteAsync. ILlmClient's canonical implementation
// (AnthropicLlmClient) stamps the trace_id; the outer service does not need
// its own IActivityPropagator injection.
//
// The arch test EveryLlmServiceEmitsTraceIdTest treats this attribute as an
// explicit opt-out: classes carrying it are accepted without an
// IActivityPropagator / GetTraceId reference in the file, so long as the
// named collaborator is itself observed to carry one.
//
// Usage:
//
//   [TaskRouting("tier2", "ideation_l2_hint")]
//   [DelegatesTraceIdTo("ILlmClient")]
//   public sealed class L2HaikuHintGenerator : IL2HaikuHintGenerator { ... }
//
// The attribute value is free-form (the arch test only reads the attribute
// presence) but convention is to name the collaborator interface or class.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Declares that the annotated [TaskRouting] class does not itself call
/// <see cref="IActivityPropagator.GetTraceId"/> because a collaborator it
/// invokes is the trace-id stamping site. The argument names the
/// collaborator for reviewer context.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DelegatesTraceIdToAttribute : Attribute
{
    public string Collaborator { get; }

    public DelegatesTraceIdToAttribute(string collaborator)
    {
        if (string.IsNullOrWhiteSpace(collaborator))
            throw new ArgumentException("collaborator must not be empty", nameof(collaborator));
        Collaborator = collaborator;
    }
}
