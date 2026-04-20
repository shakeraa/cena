// =============================================================================
// Cena Platform — PiiPreScrubbed attribute (ADR-0046, prr-022)
//
// Declarative opt-out from the runtime PII scrubber check in
// NoPiiFieldInLlmPromptTest. A [TaskRouting]-tagged service that does NOT
// inject IPiiPromptScrubber must carry this attribute, with a Reason string
// documenting which upstream collaborator already scrubbed the payload.
//
// The Reason is surfaced in the architecture-ratchet failure message, so
// opt-outs are visible at code review. A missing or empty Reason fails
// construction — silent "I'll fix it later" opt-outs are not allowed.
//
// Canonical example: TutorMessageService and ClaudeTutorLlmService both
// process the student's free-text turn; TutorMessageService runs
// TutorPromptScrubber upstream with the per-student PII context
// (FIND-privacy-008). ClaudeTutorLlmService is therefore [PiiPreScrubbed]
// with the reason pointing at that upstream seam.
//
// See docs/adr/0046-no-pii-in-llm-prompts.md §Decision 3.
// =============================================================================

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Marks a [TaskRouting]-tagged service whose prompt payload has already been
/// scrubbed by an upstream collaborator. Exempts the class from the
/// IPiiPromptScrubber injection requirement in NoPiiFieldInLlmPromptTest.
///
/// The <see cref="Reason"/> MUST name the upstream seam that actually runs
/// the scrub (e.g. "TutorPromptScrubber.Scrub() runs upstream in
/// TutorMessageService") — a bare "already scrubbed" is not acceptable.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PiiPreScrubbedAttribute : Attribute
{
    /// <summary>
    /// Human-readable pointer to the upstream scrub seam. Surfaced in code
    /// review and in the architecture-ratchet failure message.
    /// </summary>
    public string Reason { get; }

    public PiiPreScrubbedAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Reason must not be empty — name the upstream seam that runs the scrub (ADR-0046).",
                nameof(reason));
        }
        Reason = reason;
    }
}
