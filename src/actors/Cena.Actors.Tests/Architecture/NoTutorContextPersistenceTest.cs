// =============================================================================
// Cena Platform — NoTutorContextPersistenceTest (prr-204 / ADR-0003)
//
// Architecture ratchet that locks the session-scope boundary of the
// SessionTutorContextService in place:
//
//   1. The service source file (Cena.Actors/Tutoring/SessionTutorContextService.cs)
//      MUST NOT reference any student-scoped persistent store —
//      StudentState, StudentProfile, StudentProfileSnapshot, StudentDataExporter,
//      StudentLifetimeStatsProjection — either by type name or by using-import.
//   2. The service MUST cache via a Redis key prefixed with "cena:tutor-ctx:"
//      so the retention-worker scan can find and TTL these keys.
//   3. The endpoint file (Cena.Student.Api.Host/Endpoints/TutorContextEndpoint.cs)
//      MUST route under /api/v1/sessions/{sessionId}/tutor-context and
//      MUST reject cross-tenant reads (the endpoint file must mention
//      institute_id or tenant_id to prove tenant scoping is wired).
//
// Breaking any of these ratchets means a refactor has widened the ADR-0003
// scope boundary — a ship-blocker. The test fails at .NET test time so the
// bad edit surfaces before CI's ship-gate scan runs.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoTutorContextPersistenceTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Repo root (CLAUDE.md) not found");
    }

    private static string ReadFile(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read file with comment lines stripped. Rationale comments legitimately
    /// name the types we are forbidding as imports (e.g. "never touches
    /// StudentState") and those mentions must not trip the ratchet.
    /// </summary>
    private static string ReadFileCodeOnly(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file at {path}");

        var lines = File.ReadAllLines(path);
        var buffer = new System.Text.StringBuilder(capacity: 4096);
        var inBlockComment = false;
        foreach (var raw in lines)
        {
            var line = raw;

            // Strip /* ... */ block comments (may span lines).
            if (inBlockComment)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) continue;
                line = line[(end + 2)..];
                inBlockComment = false;
            }
            while (true)
            {
                var start = line.IndexOf("/*", StringComparison.Ordinal);
                if (start < 0) break;
                var end = line.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    line = line[..start];
                    inBlockComment = true;
                    break;
                }
                line = line[..start] + line[(end + 2)..];
            }

            var slashIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (slashIdx >= 0) line = line[..slashIdx];

            if (line.Trim().Length == 0) continue;
            buffer.AppendLine(line);
        }
        return buffer.ToString();
    }

    private const string ServicePath =
        "src/actors/Cena.Actors/Tutoring/SessionTutorContextService.cs";

    private const string EndpointPath =
        "src/api/Cena.Student.Api.Host/Endpoints/TutorContextEndpoint.cs";

    // -------------------------------------------------------------------------
    // Service-side scope boundary
    // -------------------------------------------------------------------------

    [Fact]
    public void Service_DoesNotImportStudentProfilePersistence()
    {
        var code = ReadFileCodeOnly(ServicePath);

        // No direct type references to long-lived student stores.
        Assert.DoesNotMatch(new Regex(@"\bStudentState\b"), code);
        Assert.DoesNotMatch(new Regex(@"\bStudentProfileSnapshot\b"), code);
        Assert.DoesNotMatch(new Regex(@"\bStudentDataExporter\b"), code);
        Assert.DoesNotMatch(new Regex(@"\bStudentLifetimeStatsProjection\b"), code);

        // No using-import of the student-profile namespaces.
        Assert.DoesNotMatch(new Regex(@"\busing\s+Cena\.Actors\.Students\b"), code);
    }

    [Fact]
    public void Service_DoesNotPersistThroughStudentStreamApi()
    {
        // Session-scope means the service must not Events.Append onto a
        // student stream. If a refactor ever adds "Events.Append(studentId,
        // ...)" here, it's a scope-boundary violation.
        var code = ReadFileCodeOnly(ServicePath);
        Assert.DoesNotMatch(
            new Regex(@"Events\s*\.\s*Append\s*\(\s*studentId"),
            code);
        Assert.DoesNotMatch(
            new Regex(@"Events\s*\.\s*StartStream\b"),
            code);
    }

    [Fact]
    public void Service_UsesSessionScopedCacheKeyPrefix()
    {
        // The retention-worker scan is keyed on the "cena:tutor-ctx:" prefix;
        // renaming is a breaking change. Assert the verbatim literal so the
        // scan can locate the key namespace.
        var src = ReadFile(ServicePath);
        Assert.Contains("\"cena:tutor-ctx\"", src);
    }

    [Fact]
    public void Service_WritesCacheWithTtl_NotPersistentSet()
    {
        // Every StringSetAsync call in this file must pass a TTL. A set
        // without TTL would mean the misconception tag outlives the session
        // indefinitely — a direct ADR-0003 violation.
        var code = ReadFileCodeOnly(ServicePath);

        // A StringSetAsync WITHOUT a third comma argument is forbidden:
        // StringSetAsync(key, value, ttl, ...).
        var bareSet = Regex.Match(code,
            @"StringSetAsync\s*\(\s*[^,]+,\s*[^,]+\s*\)",
            RegexOptions.Singleline);
        Assert.False(bareSet.Success,
            "StringSetAsync must be called with a TTL — session-scoped keys cannot persist indefinitely (ADR-0003).");
    }

    // -------------------------------------------------------------------------
    // Endpoint-side route + tenant scoping
    // -------------------------------------------------------------------------

    [Fact]
    public void Endpoint_RoutesUnderV1SessionsTutorContext()
    {
        var src = ReadFile(EndpointPath);
        Assert.Matches(
            new Regex(@"/api/v1/sessions"),
            src);
        Assert.Matches(
            new Regex(@"""/\{sessionId\}/tutor-context"""),
            src);
    }

    [Fact]
    public void Endpoint_RequiresAuthorization()
    {
        var src = ReadFile(EndpointPath);
        Assert.Matches(new Regex(@"\.RequireAuthorization\(\)"), src);
    }

    [Fact]
    public void Endpoint_EnforcesTenantScope()
    {
        // Tenant scoping must be wired — the endpoint must read the
        // institute_id / tenant_id claim from the caller.
        var src = ReadFile(EndpointPath);
        Assert.Matches(new Regex(@"""institute_id"""), src);
        Assert.Matches(new Regex(@"""tenant_id"""), src);
    }

    [Fact]
    public void Endpoint_DoesNotImportLLMSeams()
    {
        // The tutor-context read endpoint must not know about ILlmClient or
        // any direct LLM seam — it is a pure projection read.
        var code = ReadFileCodeOnly(EndpointPath);
        Assert.DoesNotMatch(new Regex(@"\bILlmClient\b"), code);
        Assert.DoesNotMatch(new Regex(@"\bITutorLlmService\b"), code);
        Assert.DoesNotMatch(new Regex(@"\busing\s+Anthropic"), code);
    }

    [Fact]
    public void Endpoint_DoesNotImportStudentProfilePersistence()
    {
        var code = ReadFileCodeOnly(EndpointPath);
        Assert.DoesNotMatch(new Regex(@"\bStudentState\b"), code);
        Assert.DoesNotMatch(new Regex(@"\bStudentProfileSnapshot\b"), code);
        Assert.DoesNotMatch(new Regex(@"\busing\s+Cena\.Actors\.Students\b"), code);
    }
}
