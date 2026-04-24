// =============================================================================
// Cena Platform — WhatsAppOptOutPolicy tests (prr-108).
//
// Covers the DoD cases:
//   (a) Parent opted-out of weekly_summary → vendor call short-circuited with
//       OptedOut; no Twilio HTTP hit.
//   (b) Parent opted-in → passthrough to inner sender preserves outcome.
//   (c) Unknown template (not in catalog) → fail-closed (OptedOut), no send.
//   (d) Fully-unsubscribed parent → every template short-circuits.
//   (e) No stored row + template maps to SafetyAlerts → default-on, send passes.
//   (f) No stored row + template maps to WeeklySummary → default-off, refused.
// =============================================================================

using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using Cena.Actors.ParentDigest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

[Trait("Category", "WhatsAppOptOutPolicy")]
public sealed class WhatsAppOptOutPolicyTests
{
    private const string ParentA = "parent-A";
    private const string ChildA = "child-A";
    private const string InstX = "institute-X";

    private static WhatsAppOptOutPolicy Build(
        RecordingWhatsAppSender inner,
        InMemoryParentDigestPreferencesStore prefs)
    {
        return new WhatsAppOptOutPolicy(
            inner,
            prefs,
            new DefaultWhatsAppTemplatePurposeCatalog(),
            new DummyMeterFactory(),
            NullLogger<WhatsAppOptOutPolicy>.Instance);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private static WhatsAppDeliveryAttempt NewAttempt(string templateId) => new(
        CorrelationId: Guid.NewGuid().ToString(),
        ParentAnonId: ParentA,
        MinorAnonId: ChildA,
        TemplateId: templateId,
        Locale: "en",
        AttemptNumber: 1,
        AttemptedAtUtc: DateTimeOffset.UtcNow,
        InstituteId: InstX);

    // (a) Parent opted-out of weekly_summary → short-circuit.
    [Fact]
    public async Task OptedOut_WeeklySummary_ShortCircuits_NoVendorCall()
    {
        var prefs = new InMemoryParentDigestPreferencesStore();
        await prefs.ApplyUpdateAsync(
            ParentA, ChildA, InstX,
            ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                .SetItem(DigestPurpose.WeeklySummary, OptInStatus.OptedOut),
            DateTimeOffset.UtcNow);

        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        var outcome = await policy.SendAsync(NewAttempt("weekly-digest-v1"));

        Assert.Equal(WhatsAppDeliveryOutcome.OptedOut, outcome);
        Assert.Equal(0, inner.CallCount);
    }

    // (b) Parent opted-in → passthrough.
    [Fact]
    public async Task OptedIn_Passthrough_PreservesInnerOutcome()
    {
        var prefs = new InMemoryParentDigestPreferencesStore();
        await prefs.ApplyUpdateAsync(
            ParentA, ChildA, InstX,
            ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                .SetItem(DigestPurpose.WeeklySummary, OptInStatus.OptedIn),
            DateTimeOffset.UtcNow);

        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        var outcome = await policy.SendAsync(NewAttempt("weekly-digest-v1"));

        Assert.Equal(WhatsAppDeliveryOutcome.Accepted, outcome);
        Assert.Equal(1, inner.CallCount);
    }

    // (c) Unknown template → fail-closed.
    [Fact]
    public async Task UnknownTemplate_FailsClosed_NoVendorCall()
    {
        var prefs = new InMemoryParentDigestPreferencesStore();
        // Explicitly opt-in to everything, to prove the unmapped template
        // is refused REGARDLESS of preferences (not because of opt-out).
        var all = ImmutableDictionary<DigestPurpose, OptInStatus>.Empty;
        foreach (var p in DigestPurposes.KnownPurposes)
            all = all.SetItem(p, OptInStatus.OptedIn);
        await prefs.ApplyUpdateAsync(ParentA, ChildA, InstX, all, DateTimeOffset.UtcNow);

        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        var outcome = await policy.SendAsync(NewAttempt("template-not-in-catalog"));

        Assert.Equal(WhatsAppDeliveryOutcome.OptedOut, outcome);
        Assert.Equal(0, inner.CallCount);
    }

    // (d) Fully-unsubscribed parent → every template short-circuits.
    [Fact]
    public async Task FullyUnsubscribed_EveryTemplateRefused()
    {
        var prefs = new InMemoryParentDigestPreferencesStore();
        await prefs.ApplyUnsubscribeAllAsync(
            ParentA, ChildA, InstX, DateTimeOffset.UtcNow);

        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        foreach (var template in new[]
                 {
                     "weekly-digest-v1",
                     "homework-reminder-v1",
                     "exam-readiness-v1",
                     "accommodation-changed-v1",
                     "safety-alert-v1",
                 })
        {
            var outcome = await policy.SendAsync(NewAttempt(template));
            Assert.Equal(WhatsAppDeliveryOutcome.OptedOut, outcome);
        }
        Assert.Equal(0, inner.CallCount);
    }

    // (e) No stored row + SafetyAlerts → default-on → passthrough.
    [Fact]
    public async Task NoRow_SafetyAlert_DefaultOn_Passes()
    {
        var prefs = new InMemoryParentDigestPreferencesStore(); // empty
        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        var outcome = await policy.SendAsync(NewAttempt("safety-alert-v1"));

        Assert.Equal(WhatsAppDeliveryOutcome.Accepted, outcome);
        Assert.Equal(1, inner.CallCount);
    }

    // (f) No stored row + WeeklySummary → default-off → refused.
    [Fact]
    public async Task NoRow_WeeklySummary_DefaultOff_Refused()
    {
        var prefs = new InMemoryParentDigestPreferencesStore(); // empty
        var inner = new RecordingWhatsAppSender(WhatsAppDeliveryOutcome.Accepted);
        var policy = Build(inner, prefs);

        var outcome = await policy.SendAsync(NewAttempt("weekly-digest-v1"));

        Assert.Equal(WhatsAppDeliveryOutcome.OptedOut, outcome);
        Assert.Equal(0, inner.CallCount);
    }

    // Default catalog maps the known templates.
    [Theory]
    [InlineData("weekly-digest-v1", DigestPurpose.WeeklySummary)]
    [InlineData("homework-reminder-v1", DigestPurpose.HomeworkReminders)]
    [InlineData("exam-readiness-v1", DigestPurpose.ExamReadiness)]
    [InlineData("accommodation-changed-v1", DigestPurpose.AccommodationsChanges)]
    [InlineData("safety-alert-v1", DigestPurpose.SafetyAlerts)]
    public void DefaultCatalog_MapsKnownTemplates(string templateId, DigestPurpose expected)
    {
        var catalog = new DefaultWhatsAppTemplatePurposeCatalog();
        Assert.Equal(expected, catalog.PurposeFor(templateId));
    }

    [Fact]
    public void DefaultCatalog_ReturnsNullForUnknownTemplate()
    {
        var catalog = new DefaultWhatsAppTemplatePurposeCatalog();
        Assert.Null(catalog.PurposeFor("does-not-exist"));
        Assert.Null(catalog.PurposeFor(""));
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class RecordingWhatsAppSender : IWhatsAppSender
    {
        private readonly WhatsAppDeliveryOutcome _outcome;
        public int CallCount { get; private set; }
        public RecordingWhatsAppSender(WhatsAppDeliveryOutcome outcome) => _outcome = outcome;
        public string VendorId => "recording";
        public bool IsConfigured => true;
        public Task<WhatsAppDeliveryOutcome> SendAsync(
            WhatsAppDeliveryAttempt attempt, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_outcome);
        }
    }
}
