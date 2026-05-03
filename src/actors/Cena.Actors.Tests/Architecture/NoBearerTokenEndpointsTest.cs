// =============================================================================
// Cena Platform — No Bearer-Token Endpoints Architecture Gate (prr-011)
//
// After the cookie cut-over (prr-011), the ONLY endpoint allowed to read
// Authorization header directly is the session-exchange endpoint (it trades
// the Firebase ID token for the cookie). Any other endpoint that inspects
// `Authorization` or `Request.Headers["Authorization"]` is a regression —
// either (a) a new endpoint that expects a bearer token, which defeats the
// httpOnly cookie hardening, or (b) a leftover from the pre-cutover world
// that should have been deleted.
//
// The scan is intentionally coarse: any .cs file in src/api/**/Endpoints/ or
// src/api/**/Auth/ that references the literal string "Authorization" is
// flagged, then matched against the small explicit allowlist. False positives
// (e.g. a comment that says "the Authorization header") are addressed by
// renaming or, if the comment is load-bearing, adding the file to the
// allowlist with a justification.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoBearerTokenEndpointsTest
{
    /// <summary>
    /// Files allowed to read the Authorization header directly. Any new
    /// entry here must be justified in a task body + reviewed — do not
    /// add entries casually.
    /// </summary>
    private static readonly IReadOnlyCollection<string> Allowlist = new[]
    {
        // prr-011: the session-exchange endpoint is the one place that
        // reads Authorization: Bearer <Firebase ID token> — its entire job
        // is to trade that bearer for the httpOnly cookie. Every other
        // endpoint must rely on the cookie middleware.
        "SessionExchangeEndpoint.cs",
    };

    // Patterns that definitively prove the code is reading the Authorization
    // header. Comment-only references (that might mention Authorization in
    // passing) would not match because neither pattern appears in plain
    // English commentary.
    private static readonly Regex AuthHeaderReadPattern = new(
        @"(Request\.Headers\s*\[\s*[""']Authorization[""']\s*\]|"
        + @"Request\.Headers\.Authorization|"
        + @"HttpContext\.Request\.Headers\[\s*[""']Authorization[""']\s*\]|"
        + @"GetBearerToken\s*\()",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> EnumerateApiEndpointSourceFiles(string repoRoot)
    {
        var apiRoot = Path.Combine(repoRoot, "src", "api");
        if (!Directory.Exists(apiRoot))
            yield break;

        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.Ordinal)
                || normalized.Contains("/bin/", StringComparison.Ordinal)
                || normalized.Contains(".Tests/", StringComparison.Ordinal)
                || normalized.Contains(".Tests.", StringComparison.Ordinal))
            {
                continue;
            }

            // Only consider endpoint-style files (Endpoints/*.cs and Auth/*.cs)
            // to keep the surface focused. Middleware and DI registration
            // files are scanned by default because they live in Endpoints/
            // sibling folders; widen the filter if other folders grow
            // auth-sensitive code in the future.
            if (!(normalized.Contains("/Endpoints/", StringComparison.Ordinal)
                || normalized.Contains("/Auth/", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return file;
        }
    }

    [Fact]
    public void ApiEndpoints_DoNotReadAuthorizationHeader_ExceptAllowlisted()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateApiEndpointSourceFiles(repoRoot))
        {
            var fileName = Path.GetFileName(file);
            if (Allowlist.Contains(fileName))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var match = AuthHeaderReadPattern.Match(content);
            if (match.Success)
            {
                var lineNumber = content.Take(match.Index).Count(c => c == '\n') + 1;
                var relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                offenders.Add($"{relPath}:{lineNumber} — {match.Value}");
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                $"Found {offenders.Count} endpoint file(s) reading the Authorization "
                + "header directly outside the prr-011 allowlist. After the cookie "
                + "cut-over, only SessionExchangeEndpoint.cs is allowed to read "
                + "Authorization — every other endpoint must authenticate via the "
                + "httpOnly cookie populated by CookieAuthMiddleware.\n\n"
                + "Offenders:\n  " + string.Join("\n  ", offenders)
                + "\n\nIf a new endpoint legitimately needs to accept a bearer "
                + "token (e.g. an external-facing webhook), add the filename to "
                + "the Allowlist in this test with a justifying comment and link "
                + "the decision in a follow-up task.");
        }
    }
}
