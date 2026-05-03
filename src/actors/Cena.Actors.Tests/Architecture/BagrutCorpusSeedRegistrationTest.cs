// =============================================================================
// PRR-251: architecture test — BagrutCorpus seed must be wired
//
// Closes the regression class shaker called out as the "PRR-148 false-done
// anti-pattern": PRR-242 was marked Done but the corpus was never visible
// in any runnable environment. This test fails the build if either:
//   1. BagrutCorpusSeedData no longer exposes its public seed entry point, OR
//   2. DatabaseSeeder.SeedAllAsync stops calling BagrutCorpusSeedData.SeedAsync, OR
//   3. The Marten schema registration for BagrutCorpusItemDocument disappears.
//
// All three are static-source checks — no DB connection needed. Production
// "is the corpus actually populated" health is enforced at runtime by the
// admin-api startup seeder + per-environment monitoring (out of scope for
// this test).
// =============================================================================

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class BagrutCorpusSeedRegistrationTest
{
    /// <summary>
    /// Walk up from the test assembly's bin folder until we find the repo
    /// root (folder containing the Cena.sln file). Mirrors the pattern
    /// used by other architecture tests in this project.
    /// </summary>
    private static string FindRepoRoot()
    {
        // Walk up from this test assembly's bin folder. Repo root carries
        // both `src` and `docs` directories at top level — that's the
        // most reliable signature across worktrees, primary clones, and
        // CI checkouts. Earlier `.sln` lookup is unreliable: not all
        // worktrees check the sln out at root (sparse-checkout) and the
        // sln file isn't named consistently across the repo.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (d.GetDirectories("src").Length > 0
                && d.GetDirectories("docs").Length > 0
                && d.GetDirectories("tasks").Length > 0)
            {
                return d.FullName;
            }
            d = d.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate Cena repo root from {AppContext.BaseDirectory}. " +
            "Looked for any ancestor folder containing src/, docs/, and tasks/.");
    }

    [Fact]
    public void BagrutCorpusSeedData_SeedAsync_must_be_called_from_DatabaseSeeder()
    {
        var repoRoot = FindRepoRoot();
        var seederPath = Path.Combine(repoRoot, "src", "shared", "Cena.Infrastructure", "Seed", "DatabaseSeeder.cs");
        Assert.True(File.Exists(seederPath), $"DatabaseSeeder source must be present at {seederPath}");

        var content = File.ReadAllText(seederPath);
        Assert.Contains("BagrutCorpusSeedData.SeedAsync", content);
    }

    [Fact]
    public void BagrutCorpusSeedData_must_expose_SeedAsync_with_canonical_signature()
    {
        var asm = typeof(Cena.Infrastructure.Seed.BagrutCorpusSeedData).Assembly;
        var seedType = asm.GetType("Cena.Infrastructure.Seed.BagrutCorpusSeedData");
        Assert.NotNull(seedType);

        var method = seedType!.GetMethod(
            "SeedAsync",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var parms = method!.GetParameters();
        Assert.Equal(2, parms.Length);
        Assert.Equal("IDocumentStore", parms[0].ParameterType.Name);
        Assert.Equal("ILogger", parms[1].ParameterType.Name);
    }

    [Fact]
    public void BagrutCorpusItemDocument_must_be_registered_in_central_Marten_schema()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, "src", "actors", "Cena.Actors", "Configuration", "MartenConfiguration.cs");
        Assert.True(File.Exists(configPath));

        var content = File.ReadAllText(configPath);
        Assert.Contains("BagrutCorpusItemDocument", content);
    }

    [Fact]
    public void BagrutCorpusSeedData_BuildItems_must_produce_canonical_count()
    {
        var items = Cena.Infrastructure.Seed.BagrutCorpusSeedData.BuildItems(DateTimeOffset.UtcNow);
        Assert.Equal(Cena.Infrastructure.Seed.BagrutCorpusSeedData.SeededItemCount, items.Count);

        // Every item must carry the dev-seeder marker so a human reviewer
        // (or a downstream architecture test) can tell synthetic from real.
        Assert.All(items, item =>
        {
            Assert.Equal(Cena.Infrastructure.Seed.BagrutCorpusSeedData.DevSeederMarker, item.IngestedBy);
            Assert.StartsWith(Cena.Infrastructure.Seed.BagrutCorpusSeedData.DevFixturePrefix, item.RawText);
        });
    }
}
