// =============================================================================
// Cena Platform — "Redis session store has a health emitter" ratchet (prr-020)
//
// Enforces that RedisSessionStoreMetricsService is wired as a hosted
// service somewhere in the service registrations. If someone removes it
// by accident, Prometheus loses the alert signal for ADR-0003 session
// correctness and eviction spikes go silent.
//
// Shape: light-touch text scan. We don't want to instantiate the DI
// graph here (that would require a real Redis) — the positive assertion
// is simply that (a) the service source exists and (b) it is referenced
// in at least one composition root.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class RedisSessionHealthEmitterExistsTest
{
    private static readonly Regex HostedServiceRegistration = new(
        @"AddCenaRedisSessionStoreMetrics\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex HostedServiceFallback = new(
        @"AddHostedService\s*<\s*RedisSessionStoreMetricsService\s*>",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    [Fact]
    public void RedisSessionMetricsService_SourceExists()
    {
        var repoRoot = FindRepoRoot();
        var servicePath = Path.Combine(
            repoRoot, "src", "shared", "Cena.Infrastructure",
            "Observability", "RedisSessionStoreMetricsService.cs");
        Assert.True(
            File.Exists(servicePath),
            $"prr-020 regression: RedisSessionStoreMetricsService.cs is missing at {servicePath}. " +
            "The misconception session store (ADR-0003) has no eviction-rate metric; " +
            "the Prometheus alert rule in ops/prometheus/alerts-redis-sessions.yml " +
            "will fire on zero data.");
    }

    [Fact]
    public void RedisSessionMetricsService_IsRegisteredInAtLeastOneHost()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var hostsWithRegistration = 0;
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            if (rel.EndsWith($"{sep}RedisSessionStoreMetricsService.cs")) continue;
            if (rel.EndsWith($"{sep}RedisSessionStoreMetricsRegistration.cs")) continue;
            if (rel.EndsWith($"{sep}RedisSessionHealthEmitterExistsTest.cs")) continue;

            var text = File.ReadAllText(file);
            if (HostedServiceRegistration.IsMatch(text) || HostedServiceFallback.IsMatch(text))
                hostsWithRegistration++;
        }

        Assert.True(
            hostsWithRegistration > 0,
            "prr-020 regression: no host registers AddCenaRedisSessionStoreMetrics " +
            "(or AddHostedService<RedisSessionStoreMetricsService>). Without it, " +
            "the misconception session-store eviction metric is not emitted and " +
            "the Prometheus alert rule fires on missing data. Wire it in the " +
            "Actor Host and/or the Admin API composition root.");
    }

    [Fact]
    public void AlertRuleFile_Exists()
    {
        var repoRoot = FindRepoRoot();
        var alertPath = Path.Combine(repoRoot, "ops", "prometheus", "alerts-redis-sessions.yml");
        Assert.True(
            File.Exists(alertPath),
            $"prr-020 regression: ops/prometheus/alerts-redis-sessions.yml is missing. " +
            "The emitter will publish metrics with no consumer.");
    }
}
