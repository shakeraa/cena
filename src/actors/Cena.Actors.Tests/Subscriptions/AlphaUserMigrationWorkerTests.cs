// =============================================================================
// Cena Platform — AlphaUserMigrationWorker tests (EPIC-PRR-I PRR-344)
//
// Locks in the CandidatesForGrace filter kernel. This is a pure
// function on the worker; running the full RunMigrationOnceAsync path
// against Marten is covered by the Postgres-backed integration tests
// elsewhere. What matters for the PRR-344 DoD is:
//   1. Populated seed → grace granted to every seed parent (delta).
//   2. Idempotent on re-run: already-granted parents are filtered out.
//   3. Active-subscriber parents are filtered out (no double-entitlement).
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class AlphaUserMigrationWorkerTests
{
    private static HashSet<string> EmptySet() =>
        new(StringComparer.Ordinal);

    [Fact]
    public void Populated_seed_grants_markers_to_every_seed_parent()
    {
        var seed = new[]
        {
            "enc::parent::a",
            "enc::parent::b",
            "enc::parent::c",
        };

        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(seed, EmptySet(), EmptySet())
            .ToList();

        Assert.Equal(3, candidates.Count);
        Assert.Contains("enc::parent::a", candidates);
        Assert.Contains("enc::parent::b", candidates);
        Assert.Contains("enc::parent::c", candidates);
    }

    [Fact]
    public void Worker_is_idempotent_on_re_run_with_same_seed()
    {
        var seed = new[]
        {
            "enc::parent::a",
            "enc::parent::b",
            "enc::parent::c",
        };
        // Simulate a prior run: all three already granted.
        var alreadyGranted = new HashSet<string>(seed, StringComparer.Ordinal);

        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(seed, alreadyGranted, EmptySet())
            .ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void Worker_skips_parents_with_existing_subscription_streams()
    {
        var seed = new[]
        {
            "enc::parent::alpha-only",
            "enc::parent::paid-sub",
            "enc::parent::still-free",
        };
        // paid-sub already has an active subscription stream — granting
        // grace to them would double-entitle and confuse analytics.
        var parentsWithSubs = new HashSet<string>(
            new[] { "enc::parent::paid-sub" }, StringComparer.Ordinal);

        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(seed, EmptySet(), parentsWithSubs)
            .ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Contains("enc::parent::alpha-only", candidates);
        Assert.Contains("enc::parent::still-free", candidates);
        Assert.DoesNotContain("enc::parent::paid-sub", candidates);
    }

    [Fact]
    public void Worker_partial_overlap_only_grants_the_delta()
    {
        var seed = new[]
        {
            "enc::parent::a",
            "enc::parent::b",
            "enc::parent::c",
            "enc::parent::d",
        };
        // a is already granted from a prior run. b has a paid sub now.
        // Only c and d should be new grants.
        var alreadyGranted = new HashSet<string>(
            new[] { "enc::parent::a" }, StringComparer.Ordinal);
        var parentsWithSubs = new HashSet<string>(
            new[] { "enc::parent::b" }, StringComparer.Ordinal);

        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(seed, alreadyGranted, parentsWithSubs)
            .ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Contains("enc::parent::c", candidates);
        Assert.Contains("enc::parent::d", candidates);
    }

    [Fact]
    public void Worker_empty_seed_is_a_noop()
    {
        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(Array.Empty<string>(), EmptySet(), EmptySet())
            .ToList();

        Assert.Empty(candidates);
    }

    [Fact]
    public void Worker_filters_whitespace_seed_entries()
    {
        // A dirty seed from an InMemory source that didn't clean (belt-
        // and-suspenders: the helper must be safe against whitespace
        // even if its caller doesn't pre-clean).
        var seed = new[] { "enc::parent::a", "", "  ", null! };

        var candidates = AlphaUserMigrationWorker
            .CandidatesForGrace(seed!, EmptySet(), EmptySet())
            .ToList();

        Assert.Single(candidates);
        Assert.Equal("enc::parent::a", candidates[0]);
    }
}
