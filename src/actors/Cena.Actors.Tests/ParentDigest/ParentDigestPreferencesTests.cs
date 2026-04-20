// =============================================================================
// Cena Platform — ParentDigestPreferences unit tests (prr-051).
//
// Covers the preferences aggregate:
//   - Defaults: SafetyAlerts opted-in, every other purpose opted-out.
//   - Explicit overrides beat defaults.
//   - Unsubscribe-all flips every purpose to opted-out AND stamps
//     UnsubscribedAtUtc.
//   - Partial updates only touch the mentioned purposes.
//   - Round-trip of wire tokens (purpose + status).
//   - Store: idempotent, tenant-keyed (cross-tenant probes miss).
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.ParentDigest;

namespace Cena.Actors.Tests.ParentDigest;

public sealed class ParentDigestPreferencesTests
{
    private const string ParentA = "parent-A";
    private const string ChildA = "child-A";
    private const string InstX = "institute-X";
    private const string InstY = "institute-Y";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 21, 12, 0, 0, TimeSpan.Zero);

    // ── Aggregate defaults ──────────────────────────────────────────────

    [Fact]
    public void Empty_ResolvesDefaults_SafetyAlertsOnOthersOff()
    {
        var prefs = ParentDigestPreferences.Empty(ParentA, ChildA, InstX, Now);

        Assert.Equal(OptInStatus.OptedIn, prefs.EffectiveStatus(DigestPurpose.SafetyAlerts));
        Assert.True(prefs.ShouldSend(DigestPurpose.SafetyAlerts));

        Assert.Equal(OptInStatus.OptedOut, prefs.EffectiveStatus(DigestPurpose.WeeklySummary));
        Assert.Equal(OptInStatus.OptedOut, prefs.EffectiveStatus(DigestPurpose.HomeworkReminders));
        Assert.Equal(OptInStatus.OptedOut, prefs.EffectiveStatus(DigestPurpose.ExamReadiness));
        Assert.Equal(OptInStatus.OptedOut, prefs.EffectiveStatus(DigestPurpose.AccommodationsChanges));

        Assert.False(prefs.ShouldSend(DigestPurpose.WeeklySummary));
        Assert.False(prefs.ShouldSend(DigestPurpose.HomeworkReminders));
        Assert.False(prefs.ShouldSend(DigestPurpose.ExamReadiness));
        Assert.False(prefs.ShouldSend(DigestPurpose.AccommodationsChanges));

        Assert.Null(prefs.UnsubscribedAtUtc);
    }

    [Fact]
    public void WithUpdates_ExplicitStatusBeatsDefault()
    {
        var prefs = ParentDigestPreferences
            .Empty(ParentA, ChildA, InstX, Now)
            .WithUpdates(
                ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                    .Add(DigestPurpose.WeeklySummary, OptInStatus.OptedIn)
                    .Add(DigestPurpose.SafetyAlerts, OptInStatus.OptedOut),
                Now);

        Assert.True(prefs.ShouldSend(DigestPurpose.WeeklySummary));
        Assert.False(prefs.ShouldSend(DigestPurpose.SafetyAlerts));
        // Purposes not mentioned still default:
        Assert.False(prefs.ShouldSend(DigestPurpose.HomeworkReminders));
    }

    [Fact]
    public void WithUpdates_IgnoresUnknownPurpose()
    {
        var prefs = ParentDigestPreferences
            .Empty(ParentA, ChildA, InstX, Now)
            .WithUpdates(
                ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                    .Add(DigestPurpose.Unknown, OptInStatus.OptedIn),
                Now);
        Assert.Empty(prefs.PurposeStatuses);
    }

    [Fact]
    public void AsFullyUnsubscribed_OptsOutEveryPurposeIncludingSafety()
    {
        var prefs = ParentDigestPreferences
            .Empty(ParentA, ChildA, InstX, Now)
            .AsFullyUnsubscribed(Now);

        foreach (var purpose in DigestPurposes.KnownPurposes)
        {
            Assert.Equal(OptInStatus.OptedOut, prefs.EffectiveStatus(purpose));
            Assert.False(prefs.ShouldSend(purpose));
        }
        Assert.NotNull(prefs.UnsubscribedAtUtc);
        Assert.Equal(Now, prefs.UnsubscribedAtUtc);
    }

    [Fact]
    public void WithUpdates_DoesNotClearUnsubscribeStamp()
    {
        // A parent who unsubscribes then opts back in one purpose must
        // still carry the audit stamp of the prior unsubscribe.
        var prefs = ParentDigestPreferences
            .Empty(ParentA, ChildA, InstX, Now)
            .AsFullyUnsubscribed(Now)
            .WithUpdates(
                ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                    .Add(DigestPurpose.SafetyAlerts, OptInStatus.OptedIn),
                Now.AddHours(1));

        Assert.Equal(Now, prefs.UnsubscribedAtUtc);
        Assert.True(prefs.ShouldSend(DigestPurpose.SafetyAlerts));
    }

    // ── Wire format round-trip ──────────────────────────────────────────

    [Theory]
    [InlineData(DigestPurpose.WeeklySummary, "weekly_summary")]
    [InlineData(DigestPurpose.HomeworkReminders, "homework_reminders")]
    [InlineData(DigestPurpose.ExamReadiness, "exam_readiness")]
    [InlineData(DigestPurpose.AccommodationsChanges, "accommodations_changes")]
    [InlineData(DigestPurpose.SafetyAlerts, "safety_alerts")]
    public void Purposes_RoundTripThroughWireFormat(DigestPurpose purpose, string expected)
    {
        var wire = DigestPurposes.ToWire(purpose);
        Assert.Equal(expected, wire);
        Assert.True(DigestPurposes.TryParseWire(wire, out var parsed));
        Assert.Equal(purpose, parsed);
    }

    [Fact]
    public void TryParseWire_RejectsUnknownToken()
    {
        Assert.False(DigestPurposes.TryParseWire("not_a_purpose", out var p));
        Assert.Equal(DigestPurpose.Unknown, p);
    }

    [Fact]
    public void KnownPurposes_PinnedSet()
    {
        // Domain-rule ratchet — adding a purpose without updating the
        // default table is a ship-blocker because the parent's consent
        // posture for the new purpose is undefined.
        Assert.Equal(5, DigestPurposes.KnownPurposes.Length);
        Assert.Contains(DigestPurpose.WeeklySummary, DigestPurposes.KnownPurposes);
        Assert.Contains(DigestPurpose.HomeworkReminders, DigestPurposes.KnownPurposes);
        Assert.Contains(DigestPurpose.ExamReadiness, DigestPurposes.KnownPurposes);
        Assert.Contains(DigestPurpose.AccommodationsChanges, DigestPurposes.KnownPurposes);
        Assert.Contains(DigestPurpose.SafetyAlerts, DigestPurposes.KnownPurposes);
        Assert.DoesNotContain(DigestPurpose.Unknown, DigestPurposes.KnownPurposes);
    }

    // ── Store contract ──────────────────────────────────────────────────

    [Fact]
    public async Task Store_ReturnsNullWhenNoRow()
    {
        var store = new InMemoryParentDigestPreferencesStore();
        var row = await store.FindAsync(ParentA, ChildA, InstX);
        Assert.Null(row);
    }

    [Fact]
    public async Task Store_UpsertThenRead_ReturnsLatest()
    {
        var store = new InMemoryParentDigestPreferencesStore();
        var updates = ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
            .Add(DigestPurpose.WeeklySummary, OptInStatus.OptedIn);

        var written = await store.ApplyUpdateAsync(ParentA, ChildA, InstX, updates, Now);
        Assert.True(written.ShouldSend(DigestPurpose.WeeklySummary));

        var read = await store.FindAsync(ParentA, ChildA, InstX);
        Assert.NotNull(read);
        Assert.True(read!.ShouldSend(DigestPurpose.WeeklySummary));
    }

    [Fact]
    public async Task Store_CrossTenant_Miss()
    {
        var store = new InMemoryParentDigestPreferencesStore();
        await store.ApplyUpdateAsync(
            ParentA, ChildA, InstX,
            ImmutableDictionary<DigestPurpose, OptInStatus>.Empty
                .Add(DigestPurpose.WeeklySummary, OptInStatus.OptedIn),
            Now);

        var crossTenant = await store.FindAsync(ParentA, ChildA, InstY);
        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task Store_UnsubscribeAll_IsIdempotent()
    {
        var store = new InMemoryParentDigestPreferencesStore();
        var first = await store.ApplyUnsubscribeAllAsync(ParentA, ChildA, InstX, Now);
        var second = await store.ApplyUnsubscribeAllAsync(ParentA, ChildA, InstX, Now.AddMinutes(5));

        Assert.NotNull(first.UnsubscribedAtUtc);
        Assert.NotNull(second.UnsubscribedAtUtc);
        foreach (var purpose in DigestPurposes.KnownPurposes)
        {
            Assert.False(second.ShouldSend(purpose));
        }
        // Stamp advances on the second call.
        Assert.True(second.UnsubscribedAtUtc > first.UnsubscribedAtUtc);
    }
}
