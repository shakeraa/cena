// =============================================================================
// Cena Platform — NoParentEndpointBypassesBindingTest (prr-009)
//
// ADR-0041 + prr-009 regression gate. Every endpoint handler under
// src/api/Cena.Admin.Api/Features/ParentConsole/ MUST either:
//
//   1. Call ParentAuthorizationGuard.AssertCanAccessAsync (directly OR via
//      a declared wrapper such as RequireParentBindingAsync /
//      AuthorizeParentOrAdminAsync / AuthorizeReadOrForbidAsync) before
//      touching Marten or emitting a domain event, OR
//
//   2. Be explicitly annotated with [AllowsUnboundParent("<justification>")].
//
// Strategy (matches the TeacherOverrideNoCrossTenantTest precedent):
//   - Scan every .cs file under Features/ParentConsole/.
//   - For each public static handler whose name is the Handle*Async shape
//     the endpoints use, walk the method body to the closing brace.
//   - A handler body that contains "ParentAuthorizationGuard." OR any of
//     the approved wrapper names passes.
//   - Otherwise the method name must carry [AllowsUnboundParent(...)] and
//     that method name must also appear in the _allowlist_ below
//     (empty on first ship — every addition needs a PR comment from the
//     security reviewer).
//
// Static scan is intentional: a new parent endpoint that forgets the guard
// must fail CI without anyone having to remember to update a runtime
// checklist. See /docs/security/parent-idor-threat-model.md for the
// threat model this ratchet enforces.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoParentEndpointBypassesBindingTest
{
    // ── Approved call-sites (whitelist of "this is the guard") ─────────
    //
    // Each string is either (a) the guard itself, or (b) a declared wrapper
    // that inlines the guard call. Adding a new wrapper requires adding it
    // here AND showing the wrapper reaches the guard.
    private static readonly string[] ApprovedGuardCalls = new[]
    {
        "ParentAuthorizationGuard.AssertCanAccessAsync",
        "RequireParentBindingAsync",
        "AuthorizeParentOrAdminAsync",
        "AuthorizeReadOrForbidAsync",
    };

    // ── Allowlist for legitimate [AllowsUnboundParent] callers ─────────
    //
    // Every entry here MUST have a corresponding [AllowsUnboundParent]
    // annotation in source with a one-line justification. Start empty;
    // the security reviewer signs off on each addition.
    private static readonly HashSet<string> UnboundAllowlist = new(StringComparer.Ordinal)
    {
    };

    // Handler signature — public static async Task<IResult> Handle*Async(...)
    private static readonly Regex HandlerSignature = new(
        @"private\s+static\s+async\s+Task<IResult>\s+(?<method>Handle[A-Z][A-Za-z0-9]*Async)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AllowsUnboundAnnotation = new(
        @"\[AllowsUnboundParent\(",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ParentConsoleDir(string repoRoot) => Path.Combine(
        repoRoot, "src", "api", "Cena.Admin.Api", "Features", "ParentConsole");

    [Fact]
    public void Every_parent_console_handler_routes_through_the_binding_guard()
    {
        var repoRoot = FindRepoRoot();
        var dir = ParentConsoleDir(repoRoot);
        Assert.True(Directory.Exists(dir), $"ParentConsole feature dir missing at {dir}");

        var endpointFiles = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpointFiles);

        var failures = new StringBuilder();
        var scannedHandlers = 0;

        foreach (var path in endpointFiles)
        {
            var source = File.ReadAllText(path);
            var matches = HandlerSignature.Matches(source);
            if (matches.Count == 0) continue;

            foreach (Match m in matches)
            {
                scannedHandlers++;
                var method = m.Groups["method"].Value;
                var (body, annotationBlock) = ExtractMethodBodyAndPrelude(source, m.Index);

                // Explicitly-waived handlers: annotation present + name
                // is on the allowlist.
                var isAnnotated = AllowsUnboundAnnotation.IsMatch(annotationBlock);
                if (isAnnotated)
                {
                    if (!UnboundAllowlist.Contains(method))
                    {
                        failures.AppendLine(
                            $"  {Path.GetFileName(path)}::{method}: [AllowsUnboundParent] present but "
                            + "method name missing from NoParentEndpointBypassesBindingTest.UnboundAllowlist. "
                            + "A security reviewer must sign off on the addition.");
                    }
                    continue;
                }

                // Plain guard call — at least one approved call-site MUST
                // appear in the body.
                var guarded = ApprovedGuardCalls.Any(call =>
                    body.IndexOf(call, StringComparison.Ordinal) >= 0);
                if (!guarded)
                {
                    failures.AppendLine(
                        $"  {Path.GetFileName(path)}::{method}: no call to ParentAuthorizationGuard "
                        + "(or an approved wrapper). Every parent-facing handler MUST route through "
                        + "the single IDOR seam before touching student data.");
                }
            }
        }

        Assert.True(scannedHandlers > 0,
            "No parent-console handlers detected — regex drifted, or the feature moved. Update this test.");

        Assert.True(failures.Length == 0,
            "Parent-console binding-guard ratchet failed (prr-009, ADR-0041):\n" + failures);
    }

    [Fact]
    public void Guard_is_declared_where_the_task_requires()
    {
        // The TASK-PRR-009 body names the exact file the helper must
        // live in. If the helper moves, the task owner has to update both
        // this test AND the task record — the friction is deliberate.
        var repoRoot = FindRepoRoot();
        var helper = Path.Combine(
            repoRoot, "src", "shared", "Cena.Infrastructure", "Security",
            "ParentChildAuthorization.cs");
        Assert.True(File.Exists(helper),
            $"prr-009 helper missing at {helper} — task body names this exact path.");

        var source = File.ReadAllText(helper);
        Assert.Contains("public static class ParentAuthorizationGuard", source);
        Assert.Contains("AssertCanAccessAsync", source);
        Assert.Contains("IParentChildBindingService", source);
    }

    /// <summary>
    /// Returns (body, prelude) where prelude is the 500 characters
    /// immediately preceding the method signature (enough to catch any
    /// attribute block) and body is the substring from the opening `{`
    /// through the matching closing `}`. Quote handling matches the
    /// TeacherOverrideNoCrossTenantTest brace scanner so #-quotes and
    /// escape sequences inside strings do not confuse the depth counter.
    /// </summary>
    private static (string Body, string Prelude) ExtractMethodBodyAndPrelude(string source, int sigIndex)
    {
        var preludeStart = Math.Max(0, sigIndex - 500);
        var prelude = source.Substring(preludeStart, sigIndex - preludeStart);

        var openIdx = source.IndexOf('{', sigIndex);
        if (openIdx < 0) return (string.Empty, prelude);

        var depth = 0;
        for (var i = openIdx; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < source.Length && source[i] != quote)
                {
                    if (source[i] == '\\') i++;
                    i++;
                }
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return (source.Substring(openIdx, i - openIdx + 1), prelude);
            }
        }
        return (source[openIdx..], prelude);
    }
}
