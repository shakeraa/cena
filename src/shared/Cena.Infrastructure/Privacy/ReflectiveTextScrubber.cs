// =============================================================================
// Cena Platform — Reflective Text PII Scrubber (prr-036, privacy + redteam lens)
//
// Student "reflective text" — free-form writing captured by the self-regulation
// axis (axis 2) and the motivation axis (axis 1) — is persisted into a Marten
// stream AND routed through an LLM for tutor hinting. Both paths carry the
// same risk: a student may write "call my mum Rachel on 050-123-4567 if I'm
// stuck" into a reflective prompt. That string must never reach persistent
// storage OR the LLM.
//
// Design: this scrubber is a thin specialisation over the ADR-0047 baseline
// (IPiiPromptScrubber). It reuses the battle-tested pattern library (email,
// phone, israeli-id, address, postal-code) but:
//
//   1. Emits a DIFFERENT metric (cena_reflective_text_pii_scrubbed_total) so
//      on-call can distinguish a reflective-text leak from an LLM-prompt leak.
//   2. Exposes a ScrubForPersistence entrypoint that is the REQUIRED seam for
//      every reflective-text repository write (enforced by arch test
//      ReflectiveTextPiiScrubbedTest).
//   3. Fails SOFT (never throws) so a leak never blocks the student's flow —
//      but the scrubbed-count is non-zero and the on-call alert fires. The
//      student's original text is NEVER persisted if the scrubber saw PII;
//      the scrubbed version is what lands in Marten.
//
// Why a thin wrapper instead of duplicating patterns:
//   Consistency — if we add a new pattern (e.g. EU IBANs) to the baseline,
//   reflective text gets it for free. Duplication would drift.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Privacy;

/// <summary>
/// prr-036: Surface where the reflective text was captured, for metric
/// labelling and runbook routing.
/// </summary>
public enum ReflectiveTextSurface
{
    /// <summary>Self-regulation axis reflection prompt (axis 2).</summary>
    SelfRegulation,

    /// <summary>Motivation axis reflection prompt (axis 1).</summary>
    Motivation,

    /// <summary>Post-session free-text feedback.</summary>
    SessionFeedback,

    /// <summary>Tutor chat reflective turn (before LLM send).</summary>
    TutorReflection
}

/// <summary>
/// prr-036: Result of a single scrub pass, with provenance of where the pass
/// happened (persistence vs LLM egress) for the audit trail.
/// </summary>
/// <param name="ScrubbedText">Text safe to persist / send to LLM.</param>
/// <param name="RedactionCount">Number of PII patterns that fired. Zero is
/// the happy path.</param>
/// <param name="Categories">Categories that fired ("email", "phone", ...).
/// Never contains the raw matched content.</param>
/// <param name="Surface">Where the reflective text came from.</param>
public sealed record ReflectiveScrubResult(
    string ScrubbedText,
    int RedactionCount,
    IReadOnlyList<string> Categories,
    ReflectiveTextSurface Surface);

/// <summary>
/// prr-036: Scrubs PII from student reflective text. Every repository that
/// persists reflective text AND every service that routes reflective text to
/// an LLM MUST pass the text through this seam first.
/// </summary>
public interface IReflectiveTextScrubber
{
    /// <summary>
    /// Scrub reflective text BEFORE persistence. Emits the persistence metric.
    /// Returns the safe-to-store text; caller writes the returned string, not
    /// the original.
    /// </summary>
    ReflectiveScrubResult ScrubForPersistence(string text, ReflectiveTextSurface surface);

    /// <summary>
    /// Scrub reflective text BEFORE LLM send. Emits the LLM-egress metric.
    /// Returns the safe-to-send text; caller passes the returned string to the
    /// LLM, not the original. On non-zero RedactionCount, callers should follow
    /// ADR-0047 Decision 4 (fail-closed on the LLM call).
    /// </summary>
    ReflectiveScrubResult ScrubForLlm(string text, ReflectiveTextSurface surface);
}

/// <summary>
/// prr-036: Default implementation — delegates pattern matching to the
/// ADR-0047 <see cref="IPiiPromptScrubber"/> baseline, emits reflective-text
/// metrics on top.
/// </summary>
public sealed class ReflectiveTextScrubber : IReflectiveTextScrubber
{
    /// <summary>Meter name scraped by the OTLP collector.</summary>
    public const string MeterName = "Cena.Privacy.ReflectiveText";

    /// <summary>
    /// Counter fired on every persistence-path redaction. Non-zero ⇒ the
    /// student's free text contained PII that would otherwise have been
    /// written to Marten. Severity-2 (below LLM prompt leaks which are sev-1).
    /// </summary>
    public const string PersistenceCounterName = "cena_reflective_text_pii_scrubbed_total";

    /// <summary>
    /// Counter fired on LLM-egress redactions. Severity-1 (same bar as
    /// ADR-0047's generic LLM scrub counter — callers MUST refuse the LLM call).
    /// </summary>
    public const string LlmEgressCounterName = "cena_reflective_text_llm_scrubbed_total";

    private readonly IPiiPromptScrubber _baseline;
    private readonly Counter<long> _persistenceCounter;
    private readonly Counter<long> _llmEgressCounter;
    private readonly ILogger<ReflectiveTextScrubber> _logger;

    public ReflectiveTextScrubber(
        IPiiPromptScrubber baseline,
        IMeterFactory meterFactory,
        ILogger<ReflectiveTextScrubber> logger)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _baseline = baseline;
        _logger = logger;

        var meter = meterFactory.Create(MeterName, "1.0.0");
        _persistenceCounter = meter.CreateCounter<long>(
            PersistenceCounterName,
            unit: "events",
            description:
                "Reflective-text persistence-path PII redaction events. " +
                "Non-zero ⇒ student PII was scrubbed before Marten write (prr-036).");
        _llmEgressCounter = meter.CreateCounter<long>(
            LlmEgressCounterName,
            unit: "events",
            description:
                "Reflective-text LLM-egress PII redaction events. " +
                "Non-zero is a severity-1 alert (prr-036 + ADR-0047).");
    }

    public ReflectiveScrubResult ScrubForPersistence(string text, ReflectiveTextSurface surface)
    {
        // Label the underlying scrubber's metric with a reflective-text
        // feature so the ADR-0047 dashboard can still see the event, but
        // also emit our own counter with the persistence-path label.
        var feature = "reflective_persist_" + surface.ToString().ToLowerInvariant();
        var baselineResult = _baseline.Scrub(text ?? string.Empty, feature);

        if (baselineResult.RedactionCount > 0)
        {
            _persistenceCounter.Add(
                baselineResult.RedactionCount,
                new KeyValuePair<string, object?>("surface", surface.ToString().ToLowerInvariant()),
                new KeyValuePair<string, object?>("categories",
                    string.Join(",", baselineResult.Categories)));
            _logger.LogWarning(
                "[REFLECTIVE_PII_PERSIST] surface={Surface} redactions={Count} " +
                "categories=[{Categories}] — scrubbed text persisted; raw text discarded.",
                surface,
                baselineResult.RedactionCount,
                string.Join(", ", baselineResult.Categories));
        }

        return new ReflectiveScrubResult(
            baselineResult.ScrubbedText,
            baselineResult.RedactionCount,
            baselineResult.Categories,
            surface);
    }

    public ReflectiveScrubResult ScrubForLlm(string text, ReflectiveTextSurface surface)
    {
        var feature = "reflective_llm_" + surface.ToString().ToLowerInvariant();
        var baselineResult = _baseline.Scrub(text ?? string.Empty, feature);

        if (baselineResult.RedactionCount > 0)
        {
            _llmEgressCounter.Add(
                baselineResult.RedactionCount,
                new KeyValuePair<string, object?>("surface", surface.ToString().ToLowerInvariant()),
                new KeyValuePair<string, object?>("categories",
                    string.Join(",", baselineResult.Categories)));
            _logger.LogWarning(
                "[REFLECTIVE_PII_LLM] surface={Surface} redactions={Count} " +
                "categories=[{Categories}] — caller MUST refuse the LLM call (ADR-0047).",
                surface,
                baselineResult.RedactionCount,
                string.Join(", ", baselineResult.Categories));
        }

        return new ReflectiveScrubResult(
            baselineResult.ScrubbedText,
            baselineResult.RedactionCount,
            baselineResult.Categories,
            surface);
    }
}

/// <summary>
/// prr-036: Attribute applied to any class (repository / LLM adapter / endpoint
/// handler) whose responsibility includes persisting OR transmitting student
/// reflective text. The <see cref="ReflectiveTextPiiScrubbedTest"/> arch test
/// enforces that every class carrying this attribute either injects
/// <see cref="IReflectiveTextScrubber"/> directly OR declares the upstream
/// scrub seam via <see cref="ReflectiveTextPreScrubbedAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlesReflectiveTextAttribute : Attribute
{
    /// <summary>
    /// Short human-readable tag for the seam (e.g. "motivation-reflection-write").
    /// Used for metric labelling and the runbook.
    /// </summary>
    public string Seam { get; }

    public HandlesReflectiveTextAttribute(string seam)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seam);
        Seam = seam;
    }
}

/// <summary>
/// prr-036: Opt-out attribute for classes that operate downstream of an already-
/// scrubbed reflective text (mirrors ADR-0047's <c>PiiPreScrubbed</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ReflectiveTextPreScrubbedAttribute : Attribute
{
    public string Reason { get; }

    public ReflectiveTextPreScrubbedAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }
}
