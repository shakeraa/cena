// =============================================================================
// Cena Platform — prr-026 architecture ratchet
//
// Invariant: every HTTP endpoint whose route matches
// `/classrooms/{*}/analytics/*` must reference IKAnonymityEnforcer in the
// same file (either as a constructor parameter, field, or local reference).
//
// Motivation: prr-026 raises the k-anonymity floor to k=10 for aggregate
// teacher/classroom analytics surfaces. The platform primitive is
// IKAnonymityEnforcer. The ratchet fails if a new analytics route ships
// without wiring through the enforcer — which would silently let a
// future endpoint produce statistical claims below the floor.
//
// Scope:
//   - Scans every .cs file under src/ (non-test, non-bin, non-obj).
//   - Finds files whose source text contains a route literal matching
//     the regex `/classrooms/{[^}]+}/analytics/`.
//   - Fails if any such file does NOT also contain `IKAnonymityEnforcer`.
//
// Allowlist is empty by design — adding a new analytics endpoint without
// the enforcer requires editing this test, which is visible in code review.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class ClassroomAnalyticsUsesKAnonymityEnforcerTest
{
    private static readonly Regex AnalyticsRouteLiteral = new(
        @"""[^""]*/classrooms/\{[^}]+\}/analytics/",
        RegexOptions.Compiled);

    private static readonly string[] RequiredTokens =
    {
        "IKAnonymityEnforcer",
        "KAnonymityEnforcer ",
    };

    private static readonly string[] AllowlistedRelativePaths =
        Array.Empty<string>();

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
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
    }

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcRoot)) yield break;

        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}Tests{sep}")) continue;
            if (rel.Contains($".Tests{sep}")) continue;
            // Exempt the enforcer's own source, the ratchet, and any test
            // fixtures that reference the route pattern as documentation.
            if (rel.EndsWith($"{sep}KAnonymityEnforcer.cs")) continue;
            if (rel.EndsWith($"{sep}KAnonymityEnforcerRegistration.cs")) continue;
            yield return f;
        }
    }

    [Fact]
    public void Every_Classroom_Analytics_Route_References_IKAnonymityEnforcer()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = new HashSet<string>(
            AllowlistedRelativePaths, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        var routesFound = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            var raw = File.ReadAllText(file);
            if (!AnalyticsRouteLiteral.IsMatch(raw)) continue;
            routesFound++;

            var rel = Path.GetRelativePath(repoRoot, file)
                          .Replace(Path.DirectorySeparatorChar, '/');
            if (allowlist.Contains(rel)) continue;

            if (RequiredTokens.Any(t => raw.Contains(t, StringComparison.Ordinal)))
                continue;

            violations.Add(
                $"{rel} — carries a `/classrooms/{{...}}/analytics/*` route but does " +
                "not reference IKAnonymityEnforcer. Inject the enforcer and call " +
                "AssertMinimumGroupSize (k=10) before producing the aggregate " +
                "payload, OR document the exemption via the allowlist in this " +
                "arch test with an ADR link (prr-026).");
        }

        Assert.True(
            routesFound > 0,
            "ClassroomAnalyticsUsesKAnonymityEnforcerTest found zero analytics " +
            "routes under src/. Either the scan is broken or the prr-026 " +
            "endpoint landed without a route literal matching the pattern.");

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine(
            $"prr-026 violation: {violations.Count} analytics endpoint(s) miss " +
            "IKAnonymityEnforcer wiring.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }

    [Fact]
    public void ClassMasteryService_References_IKAnonymityEnforcer()
    {
        // Narrow check: the specific service that predates prr-026 (it had a
        // hard-coded MinStudentsForAnonymity = 10) must now route through
        // the shared primitive. Without this assertion the generic scan
        // above could false-pass if someone moved ClassMasteryService's
        // ctor wiring out of the file.
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(
            repoRoot, "src", "api", "Cena.Admin.Api", "ClassMasteryService.cs");
        Assert.True(File.Exists(path), $"Expected ClassMasteryService at {path}");

        var raw = File.ReadAllText(path);
        Assert.Contains("IKAnonymityEnforcer", raw);
    }
}
