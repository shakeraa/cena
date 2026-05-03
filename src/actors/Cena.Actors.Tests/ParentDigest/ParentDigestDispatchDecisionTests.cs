// =============================================================================
// Cena Platform — ParentDigestDispatchPolicy.Decide unit tests (prr-051).
//
// Covers:
//   - Null preferences + default-on purpose → send (SafetyAlerts).
//   - Null preferences + default-off purpose → skip (reason = NoPreferencesNoDefault).
//   - Explicit OptedIn → send; Explicit OptedOut → skip (reason = OptedOut).
//   - UnsubscribedAtUtc set → skip with reason = Unsubscribed (takes priority
//     over per-purpose statuses).
//   - NotSet status → falls through to default-table.
//   - Unknown purpose → skip with reason = UnknownPurpose (defensive).
//   - Metric labels: stable wire-format strings.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.ParentDigest;

namespace Cena.Actors.Tests.ParentDigest;

public sealed class ParentDigestDispatchDecisionTests
{
    private const string ParentA = "parent-A";
    private const string ChildA = "child-A";
    private const string InstX = "institute-X";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 21, 12, 0, 0, TimeSpan.Zero);

    private static ParentDigestPreferences NewPrefs(
        params (DigestPurpose purpose, OptInStatus status)[] explicitStatuses)
    {
        var builder = ImmutableDictionary<DigestPurpose, OptInStatus>.Empty.ToBuilder();
        foreach (var (p, s) in explicitStatuses) builder[p] = s;
        return new ParentDigestPreferences(
            ParentA, ChildA, InstX,
            builder.ToImmutable(),
            Now);
    }

    // ── Null-preferences path (ship-wide default table) ─────────────────

    [Fact]
    public void NullPrefs_SafetyAlerts_DispatchesWithNoSkipReason()
    {
        var decision = ParentDigestDispatchPolicy.Decide(null, DigestPurpose.SafetyAlerts);
        Assert.True(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.None, decision.SkipReason);
    }

    [Fact]
    public void NullPrefs_WeeklySummary_SkipsWithNoPrefsReason()
    {
        var decision = ParentDigestDispatchPolicy.Decide(null, DigestPurpose.WeeklySummary);
        Assert.False(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.NoPreferencesNoDefault, decision.SkipReason);
    }

    // ── Explicit statuses ───────────────────────────────────────────────

    [Fact]
    public void ExplicitOptedIn_Dispatches()
    {
        var prefs = NewPrefs((DigestPurpose.WeeklySummary, OptInStatus.OptedIn));
        var decision = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.WeeklySummary);
        Assert.True(decision.ShouldDispatch);
    }

    [Fact]
    public void ExplicitOptedOut_SkipsWithOptedOutReason()
    {
        var prefs = NewPrefs((DigestPurpose.SafetyAlerts, OptInStatus.OptedOut));
        var decision = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.SafetyAlerts);
        Assert.False(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.OptedOut, decision.SkipReason);
    }

    [Fact]
    public void NotSet_FallsThroughToDefaultTable()
    {
        // NotSet explicit entry behaves identically to "no entry".
        var prefs = NewPrefs((DigestPurpose.WeeklySummary, OptInStatus.NotSet));
        var decision = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.WeeklySummary);
        Assert.False(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.DefaultOptedOut, decision.SkipReason);
    }

    // ── Unsubscribe takes priority ──────────────────────────────────────

    [Fact]
    public void Unsubscribed_BeatsExplicitOptedIn()
    {
        var prefs = NewPrefs((DigestPurpose.WeeklySummary, OptInStatus.OptedIn));
        prefs = prefs.AsFullyUnsubscribed(Now.AddHours(1));

        var decision = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.WeeklySummary);
        Assert.False(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.Unsubscribed, decision.SkipReason);
    }

    // ── Defensive unknowns ──────────────────────────────────────────────

    [Fact]
    public void UnknownPurpose_SkipsDefensively()
    {
        var prefs = NewPrefs();
        var decision = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.Unknown);
        Assert.False(decision.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.UnknownPurpose, decision.SkipReason);
    }

    // ── Metric labels are stable wire strings ───────────────────────────

    [Theory]
    [InlineData(ParentDigestSkipReason.None, "none")]
    [InlineData(ParentDigestSkipReason.OptedOut, "opted_out")]
    [InlineData(ParentDigestSkipReason.DefaultOptedOut, "default_opted_out")]
    [InlineData(ParentDigestSkipReason.Unsubscribed, "unsubscribed")]
    [InlineData(ParentDigestSkipReason.UnknownPurpose, "unknown_purpose")]
    [InlineData(ParentDigestSkipReason.NoPreferencesNoDefault, "no_preferences_no_default")]
    public void MetricLabels_Stable(ParentDigestSkipReason reason, string expected)
    {
        Assert.Equal(expected, ParentDigestDispatchPolicy.ToMetricLabel(reason));
    }

    // ── End-to-end dispatcher scenario ──────────────────────────────────

    [Fact]
    public void FullFlow_DispatcherHonorsExplicitEdits()
    {
        // Parent opts in to weekly summary, opts out of safety alerts.
        // Dispatcher decisions must reflect both.
        var prefs = NewPrefs(
            (DigestPurpose.WeeklySummary, OptInStatus.OptedIn),
            (DigestPurpose.SafetyAlerts, OptInStatus.OptedOut));

        var weekly = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.WeeklySummary);
        var safety = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.SafetyAlerts);
        var homework = ParentDigestDispatchPolicy.Decide(prefs, DigestPurpose.HomeworkReminders);

        Assert.True(weekly.ShouldDispatch);
        Assert.False(safety.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.OptedOut, safety.SkipReason);
        Assert.False(homework.ShouldDispatch);
        Assert.Equal(ParentDigestSkipReason.DefaultOptedOut, homework.SkipReason);
    }
}
