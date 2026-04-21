// =============================================================================
// Cena Platform — RubricSignOffMetadataRequiredTest (prr-033, ADR-0052 §3)
//
// Architecture gate: every *.yml in contracts/rubric/ has a non-empty
// sign-off triple (approved_by_user_id + approved_at_utc +
// ministry_circular_ref). The service loader enforces this at construction;
// this test is the CI pre-merge gate that catches a regression before the
// rubric ever loads at boot.
//
// A failing test here means a rubric was authored without a sign-off —
// which means it cannot be served. Don't waive the test; add the sign-off
// triple (or leave the file out of the directory until it's signed).
// =============================================================================

using Cena.Actors.Assessment.Rubric;

namespace Cena.Actors.Tests.Architecture;

public sealed class RubricSignOffMetadataRequiredTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string RubricDir() =>
        Path.Combine(FindRepoRoot(), "contracts", "rubric");

    [Fact]
    public void RubricDirectory_Exists_With_At_Least_One_File()
    {
        var dir = RubricDir();
        Assert.True(Directory.Exists(dir),
            $"prr-033: contracts/rubric/ must exist at {dir}");
        var ymls = Directory.EnumerateFiles(dir, "*.yml").ToArray();
        Assert.True(ymls.Length > 0,
            $"prr-033: contracts/rubric/ must contain at least one rubric yml");
    }

    [Fact]
    public void Every_Rubric_Has_SignOff_Triple()
    {
        // The loader throws RubricLoadException on any missing sign-off
        // field. Successful Load == every file in the directory carries
        // the full triple.
        var snapshot = RubricVersionPinningService_TestAccessor.Load(RubricDir());
        Assert.NotEmpty(snapshot.All);
        foreach (var r in snapshot.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.SignOff.ApprovedByUserId),
                $"prr-033: rubric {r.RubricId} has empty approved_by_user_id");
            Assert.False(string.IsNullOrWhiteSpace(r.SignOff.MinistryCircularRef),
                $"prr-033: rubric {r.RubricId} has empty ministry_circular_ref");
            Assert.True(r.SignOff.ApprovedAtUtc > DateTimeOffset.MinValue,
                $"prr-033: rubric {r.RubricId} has empty approved_at_utc");
        }
    }

    [Fact]
    public void Required_Bagrut_Math_Tracks_Are_Loaded()
    {
        var snapshot = RubricVersionPinningService_TestAccessor.Load(RubricDir());
        var codes = snapshot.ByExamCode.Keys.ToHashSet(StringComparer.Ordinal);
        string[] required = { "BAGRUT_MATH_3U", "BAGRUT_MATH_4U", "BAGRUT_MATH_5U" };
        var missing = required.Where(c => !codes.Contains(c)).ToArray();
        Assert.True(missing.Length == 0,
            "prr-033: required Bagrut math tracks missing from rubric dir: " +
            string.Join(", ", missing));
    }
}

/// <summary>
/// Test-only accessor — builds a fresh RubricVersionPinningService
/// against the contracts/rubric/ dir so the test exercises the real
/// load path (no reflection into internals).
/// </summary>
internal static class RubricVersionPinningService_TestAccessor
{
    public static RubricSnapshot Load(string dir)
    {
        var service = new RubricVersionPinningService(
            dir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RubricVersionPinningService>.Instance);
        return service.Current;
    }
}
