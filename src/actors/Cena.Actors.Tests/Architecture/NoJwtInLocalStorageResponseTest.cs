// =============================================================================
// Cena Platform — No JWT-in-Response-Body Architecture Gate (prr-011 Phase 1B)
//
// Companion to NoLocalStorageAuthTest (frontend) and NoBearerTokenEndpointsTest
// (backend). Closes the last seam: a backend regression that hands the session
// JWT back in a response body for a client to store (in localStorage,
// IndexedDB, or anywhere else JS-accessible).
//
// Mechanism: scan every auth-related C# response DTO in src/api/**/Auth/ and
// src/api/**/Endpoints/ for string-typed public members named in the set
// {access_token, id_token, jwt, session_token, sessionJwt, bearer}. Any match
// fails CI.
//
// Allowlist is empty — there is no legitimate reason for an auth DTO to
// carry a raw JWT as a string field. If one surfaces, the right answer is
// to rename it to something that is NOT a JWT or to drop it entirely, not
// to add it to an allowlist.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public class NoJwtInLocalStorageResponseTest
{
    /// <summary>
    /// Property / field names that imply the value carries a raw JWT. If a
    /// response DTO has a string member named any of these, it's a smell —
    /// either the DTO is leaking the httpOnly-cookie's JWT back to the
    /// client, or it's a misnamed token-like field that should be renamed
    /// to something honest.
    /// </summary>
    private static readonly Regex JwtShapedMemberName = new(
        @"\b(access_?token|id_?token|jwt|session_?token|session_?jwt|bearer|auth_?token|refresh_?token)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Matches a C# public property / field declaration whose type is string
    /// (or string?). Narrow on purpose — we don't care about byte[], Guid,
    /// DateTimeOffset, or nested records.
    /// </summary>
    private static readonly Regex PublicStringMember = new(
        @"public\s+string\??\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a C# record positional parameter whose type is string (or
    /// string?). Positional records are the idiomatic shape for response
    /// DTOs in this codebase.
    /// </summary>
    private static readonly Regex RecordParamString = new(
        @"string\??\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:,|\))",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static IEnumerable<string> EnumerateAuthDtoFiles(string repoRoot)
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

            // Scope: Auth/, Endpoints/ — everywhere a response DTO is defined.
            if (!(normalized.Contains("/Auth/", StringComparison.Ordinal)
                || normalized.Contains("/Endpoints/", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return file;
        }
    }

    [Fact]
    public void AuthResponseDtos_HaveNoJwtShapedStringMembers()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateAuthDtoFiles(repoRoot))
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            // Property / field form
            foreach (Match m in PublicStringMember.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (JwtShapedMemberName.IsMatch(name))
                {
                    var lineNumber = content.Take(m.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{relPath}:{lineNumber} — public string {name}");
                }
            }

            // Positional-record form: only consider lines that look like they
            // are inside a record/class declaration context — i.e. the file
            // contains "record" or "class". This keeps us from matching
            // random method signatures that happen to contain "string name".
            if (!content.Contains("record ", StringComparison.Ordinal)
                && !content.Contains("class ", StringComparison.Ordinal))
            {
                continue;
            }

            // Walk each line, only fire on lines whose text clearly reads as
            // a record-positional-parameter: we require the paren context be
            // open at the match position. Approximate by looking 120 chars
            // back for "record " or "class ".
            foreach (Match m in RecordParamString.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (!JwtShapedMemberName.IsMatch(name))
                    continue;

                var windowStart = Math.Max(0, m.Index - 200);
                var prefix = content.Substring(windowStart, m.Index - windowStart);
                if (!prefix.Contains("record ", StringComparison.Ordinal)
                    && !prefix.Contains("class ", StringComparison.Ordinal))
                {
                    continue;
                }

                var lineNumber = content.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{relPath}:{lineNumber} — record param string {name}");
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                $"Found {offenders.Count} auth DTO member(s) that look like JWTs "
                + "leaking out through a response body — prr-011 Phase 1B forbids "
                + "returning raw tokens to the client in any form other than the "
                + "httpOnly `__Host-cena_session` cookie.\n\n"
                + "Offenders:\n  " + string.Join("\n  ", offenders)
                + "\n\nIf a field legitimately carries a non-JWT token that happens "
                + "to share a name, rename it to something unambiguous (e.g. "
                + "'refresh_handle' instead of 'refresh_token'). If a field "
                + "legitimately carries a JWT that MUST be in the body, open a "
                + "new task with a written threat-model justification — do NOT "
                + "allowlist it casually.");
        }
    }
}
