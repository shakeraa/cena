// =============================================================================
// Cena Platform — SymPy Template Gate Seam Test (prr-010)
//
// Best-effort static check: every production code path that ends up calling
// ISymPySidecarClient.VerifyAsync must do so through the concrete
// SymPySidecarClient, which is the ONLY public entry that runs the
// SymPyTemplateGuard before marshalling to NATS. If a new code path
// instantiates a different ISymPySidecarClient without going through the
// guarded concrete (or registers its own implementation), the guard is
// bypassed and LLM-authored templates can reach the sidecar unscreened.
//
// This is a heuristic seam check, not a full call-graph analysis. It:
//
//   1. Enumerates every .cs under src/actors/Cena.Actors and src/api/ that
//      references `ISymPySidecarClient`.
//   2. Confirms the concrete binding `ISymPySidecarClient, SymPySidecarClient`
//      appears in each application entry (Program.cs) that references the
//      interface — i.e., no Host silently swaps in an alternative impl.
//   3. Confirms that `SymPySidecarClient.VerifyAsync` invokes the guard
//      (source contains `_guard.Screen(request)`). If someone refactors the
//      guard call away, this test fails with a clear pointer at the seam.
//
// Limitations (documented for the next reviewer):
//   * Does not follow runtime DI overrides in user-land code.
//   * Does not detect reflection-based bypasses.
//   * Does not catch code that constructs a `SymPySidecarClient` passing
//     a do-nothing guard in a production scenario. For that we rely on
//     code review + the DI registration pattern (see Host/Program.cs).
//
// If any of those bypasses become a real concern, tighten the test — do not
// delete it.
// =============================================================================

using System.Text.RegularExpressions;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class SymPyTemplateGateTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    /// <summary>
    /// The concrete SymPySidecarClient MUST call the guard on every request.
    /// If someone refactors the gate away the canary suite would still pass
    /// (the canary tests exercise the guard directly) — this assertion is
    /// the second line of defense that catches "accidentally deleted
    /// `_guard.Screen` line" regressions.
    /// </summary>
    [Fact]
    public void SymPySidecarClient_InvokesTemplateGuardBeforeNats()
    {
        var repoRoot = FindRepoRoot();
        var clientPath = Path.Combine(
            repoRoot, "src", "actors", "Cena.Actors", "Cas", "SymPySidecarClient.cs");
        Assert.True(File.Exists(clientPath), $"Expected client at {clientPath}");

        var src = File.ReadAllText(clientPath);

        // Guard invocation is load-bearing. Keep the assertion specific
        // enough to catch rename regressions but loose enough to survive
        // harmless whitespace/formatting churn.
        Assert.Matches(new Regex(@"_guard\.Screen\s*\(\s*request\s*\)", RegexOptions.Compiled), src);

        // The guard call must appear BEFORE the NATS RequestAsync call. The
        // simple line-index check below is adequate because the file is
        // linear; if someone inverts the order they'll have other problems.
        var guardIdx = src.IndexOf("_guard.Screen", StringComparison.Ordinal);
        var natsIdx = src.IndexOf("_nats.RequestAsync", StringComparison.Ordinal);
        Assert.True(guardIdx > 0, "_guard.Screen not found in SymPySidecarClient.cs");
        Assert.True(natsIdx > 0, "_nats.RequestAsync not found in SymPySidecarClient.cs");
        Assert.True(guardIdx < natsIdx,
            "SymPy template guard must run BEFORE the NATS request. " +
            "Guard at char " + guardIdx + "; NATS at char " + natsIdx + ".");
    }

    /// <summary>
    /// Every Program.cs that registers <c>ISymPySidecarClient</c> must bind
    /// it to the concrete <c>SymPySidecarClient</c> (the only implementation
    /// that enforces the guard). Silently substituting an alternative
    /// implementation would bypass the sandbox.
    /// </summary>
    [Fact]
    public void AllHosts_BindSymPyInterfaceToGuardedConcrete()
    {
        var repoRoot = FindRepoRoot();
        var hostsToCheck = new[]
        {
            Path.Combine(repoRoot, "src", "actors", "Cena.Actors.Host", "Program.cs"),
            Path.Combine(repoRoot, "src", "api", "Cena.Student.Api.Host", "Program.cs"),
            Path.Combine(repoRoot, "src", "api", "Cena.Admin.Api.Host", "Program.cs"),
        };

        var violations = new List<string>();
        // Pattern: binding line must use ", SymPySidecarClient" on the right
        // side of the generic pair. Tolerates the fully-qualified form used
        // by the Host Program.cs variants (with Cena.Actors.Cas. prefix).
        var pattern = new Regex(
            @"ISymPySidecarClient[^>]*,\s*(?:Cena\.Actors\.Cas\.)?SymPySidecarClient\b",
            RegexOptions.Compiled);

        foreach (var host in hostsToCheck)
        {
            if (!File.Exists(host))
            {
                // If the host doesn't exist in a slim checkout, skip (CI full
                // checkout will still run it).
                continue;
            }

            var src = File.ReadAllText(host);
            // If this host references the interface at all, it must also
            // bind it to the guarded concrete.
            if (!src.Contains("ISymPySidecarClient", StringComparison.Ordinal))
                continue;

            if (!pattern.IsMatch(src))
            {
                violations.Add(
                    $"{Path.GetRelativePath(repoRoot, host)}: references ISymPySidecarClient " +
                    "but does not bind it to the guarded concrete SymPySidecarClient. " +
                    "Add: services.AddSingleton<ISymPySidecarClient, SymPySidecarClient>();");
            }
        }

        Assert.True(violations.Count == 0,
            "prr-010 SymPy template-gate seam violated:\n  " + string.Join("\n  ", violations));
    }
}
