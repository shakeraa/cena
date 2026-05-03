// =============================================================================
// Cena Platform — WhatsApp opt-out enforcement policy (prr-108).
//
// Decorates any IWhatsAppSender with a preferences-check performed BEFORE the
// vendor call. Every WhatsApp send consults ParentDigestPreferences (prr-051)
// for the template's purpose; a parent opt-out short-circuits with
// WhatsAppDeliveryOutcome.OptedOut and NO Twilio/Meta HTTP call is ever made.
//
// Why a decorator, not a policy-chain (like OutboundSmsPolicyChain):
//
//   - The WhatsApp dispatch flow is a single vendor hop (no sanitiser, no
//     rate-limit composition today). A chain here would be over-engineered.
//   - An architecture ratchet (NoParentDigestBypassesPreferencesTest) already
//     requires every IWhatsAppSender consumer to consult preferences — the
//     decorator pattern KEEPS the consult inside the sender pipeline so
//     future consumers automatically inherit it just by asking DI for an
//     IWhatsAppSender.
//   - ADR-0026 §Consequences "every LLM-consuming feature" pattern maps
//     cleanly to "every vendor-messaging feature" here: one decorator, one
//     metric, one reason-code.
//
// WHY consult BEFORE vendor call instead of letting vendor's own opt-out list
// handle it:
//
//   - Vendors' opt-out lists are not authoritative for our DSR-compliant
//     record of consent. GDPR Art 7(3) "withdrawal as easy as giving" +
//     AXIS-9 ethics review require our system to honour the opt-out as soon
//     as the parent flips it; we cannot wait 1-10min for Twilio's opt-out
//     index to propagate.
//   - A vendor billed send on an opted-out parent is a finops regression +
//     a support-ticket magnet ("we unsubscribed but you kept messaging us").
//
// Template → purpose mapping:
//
//   Every WhatsApp template ships with a DigestPurpose that maps it to one
//   of the prr-051 opt-in categories. The default table below covers the
//   Phase 1B live templates; a template we haven't mapped is treated as
//   "unmapped" and fail-CLOSED (blocked) rather than fail-open. Adding a
//   new template to the vendor-side catalog without updating this table
//   trips the architecture ratchet test.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Maps a WhatsApp template id → digest purpose. A template that is not in
/// the map is blocked by <see cref="WhatsAppOptOutPolicy"/> (fail-closed).
/// </summary>
public interface IWhatsAppTemplatePurposeCatalog
{
    /// <summary>
    /// Return the digest purpose for a template, or null if unmapped
    /// (unmapped → fail-closed in the policy).
    /// </summary>
    DigestPurpose? PurposeFor(string templateId);
}

/// <summary>
/// Default catalog — covers the Phase 1B live parent-digest templates.
/// Adding a new template requires both: a row here AND a ship-gate review.
/// </summary>
public sealed class DefaultWhatsAppTemplatePurposeCatalog : IWhatsAppTemplatePurposeCatalog
{
    private readonly IReadOnlyDictionary<string, DigestPurpose> _map;

    public DefaultWhatsAppTemplatePurposeCatalog()
    {
        _map = new Dictionary<string, DigestPurpose>(StringComparer.OrdinalIgnoreCase)
        {
            // Weekly Monday-morning digest templates.
            ["weekly-digest-v1"] = DigestPurpose.WeeklySummary,
            ["weekly-digest-en-v1"] = DigestPurpose.WeeklySummary,
            ["weekly-digest-he-v1"] = DigestPurpose.WeeklySummary,

            // Per-assignment nudge templates.
            ["homework-reminder-v1"] = DigestPurpose.HomeworkReminders,

            // Exam-readiness ramp-up templates.
            ["exam-readiness-v1"] = DigestPurpose.ExamReadiness,

            // Accommodation change notifications.
            ["accommodation-changed-v1"] = DigestPurpose.AccommodationsChanges,

            // Welfare / safety alerts.
            ["safety-alert-v1"] = DigestPurpose.SafetyAlerts,
        };
    }

    public DigestPurpose? PurposeFor(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return null;
        return _map.TryGetValue(templateId, out var purpose) ? purpose : null;
    }
}

/// <summary>
/// Opt-out-aware decorator around an <see cref="IWhatsAppSender"/>. Consults
/// <see cref="IParentDigestPreferencesStore"/> for the template's purpose
/// before delegating to the inner sender. A parent opt-out (or full
/// unsubscribe) short-circuits with <see cref="WhatsAppDeliveryOutcome.OptedOut"/>.
///
/// Architectural contract (prr-108):
///
///   - The preferences check runs on EVERY call — there is no per-call
///     bypass flag. An attacker who forges a template id cannot use the
///     "unmapped → allow" loophole because unmapped templates fail-CLOSED.
///   - Metric cena_whatsapp_opted_out_total (label: purpose) fires once
///     per short-circuit so the ops dashboard can see opt-out volume.
///   - The logger emits [prr-108] with correlation-id so an audit trail
///     exists even when no vendor call was made.
/// </summary>
public sealed class WhatsAppOptOutPolicy : IWhatsAppSender
{
    private readonly IWhatsAppSender _inner;
    private readonly IParentDigestPreferencesStore _preferencesStore;
    private readonly IWhatsAppTemplatePurposeCatalog _templateCatalog;
    private readonly ILogger<WhatsAppOptOutPolicy> _logger;
    private readonly Counter<long> _optedOutCounter;
    private readonly Counter<long> _unmappedTemplateCounter;

    public WhatsAppOptOutPolicy(
        IWhatsAppSender inner,
        IParentDigestPreferencesStore preferencesStore,
        IWhatsAppTemplatePurposeCatalog templateCatalog,
        IMeterFactory meterFactory,
        ILogger<WhatsAppOptOutPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(preferencesStore);
        ArgumentNullException.ThrowIfNull(templateCatalog);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _preferencesStore = preferencesStore;
        _templateCatalog = templateCatalog;
        _logger = logger;

        var meter = meterFactory.Create("Cena.Actors.ParentDigest.WhatsAppOptOut", "1.0.0");
        _optedOutCounter = meter.CreateCounter<long>(
            "cena_whatsapp_opted_out_total",
            description:
                "WhatsApp sends short-circuited because parent opted out of the " +
                "template's purpose (prr-108).");
        _unmappedTemplateCounter = meter.CreateCounter<long>(
            "cena_whatsapp_template_unmapped_total",
            description:
                "WhatsApp sends refused because the template id has no purpose " +
                "in the catalog (fail-closed default, prr-108).");
    }

    public string VendorId => _inner.VendorId;
    public bool IsConfigured => _inner.IsConfigured;

    public async Task<WhatsAppDeliveryOutcome> SendAsync(
        WhatsAppDeliveryAttempt attempt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // Fail-closed: no template → no purpose → refuse. An unmapped
        // template is a programmer error (template shipped without a
        // catalog entry) — better to not-send than to send on the wrong
        // opt-out default.
        var purpose = _templateCatalog.PurposeFor(attempt.TemplateId);
        if (purpose is null || purpose.Value == DigestPurpose.Unknown)
        {
            _unmappedTemplateCounter.Add(1,
                new KeyValuePair<string, object?>("template_id", attempt.TemplateId));
            _logger.LogWarning(
                "[prr-108] WhatsApp refused: template '{TemplateId}' has no purpose in catalog. " +
                "correlation={CorrelationId}",
                attempt.TemplateId,
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.OptedOut;
        }

        // Look up preferences for (parent, student, institute). Missing
        // institute → conservative fallback to "unknown" so the store
        // returns null (no row); effective status falls back to the default
        // table (ShouldSend). Callers that care about the distinction
        // should thread InstituteId; the architecture test will catch new
        // dispatchers that omit it.
        var instituteId = attempt.InstituteId ?? string.Empty;
        var preferences = string.IsNullOrWhiteSpace(instituteId)
            ? null
            : await _preferencesStore.FindAsync(
                attempt.ParentAnonId,
                attempt.MinorAnonId,
                instituteId,
                ct).ConfigureAwait(false);

        // A pair that has never visited the preferences screen: fall through
        // to the default-table effective status. Safety alerts default-on;
        // every other purpose default-off.
        var shouldSend = preferences is null
            ? DigestPurposes.DefaultOptedIn(purpose.Value)
            : preferences.ShouldSend(purpose.Value);

        if (!shouldSend)
        {
            _optedOutCounter.Add(1,
                new KeyValuePair<string, object?>("purpose", DigestPurposes.ToWire(purpose.Value)));
            _logger.LogInformation(
                "[prr-108] WhatsApp opted-out short-circuit: purpose={Purpose} " +
                "parent={ParentAnonId} student={MinorAnonId} institute={InstituteId} " +
                "template={TemplateId} correlation={CorrelationId}",
                DigestPurposes.ToWire(purpose.Value),
                attempt.ParentAnonId,
                attempt.MinorAnonId,
                instituteId,
                attempt.TemplateId,
                attempt.CorrelationId);
            return WhatsAppDeliveryOutcome.OptedOut;
        }

        // Preferences allow → delegate to the inner vendor-facing sender.
        return await _inner.SendAsync(attempt, ct).ConfigureAwait(false);
    }
}
