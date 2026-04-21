// =============================================================================
// Cena Platform — Legal document endpoints (prr-123)
//
// GET /api/v1/legal/privacy-policy?audience=parent|student
//
// Returns the canonical Markdown + parsed version metadata for the
// audience-specific privacy policy. Two canonical docs are tracked:
//
//   docs/legal/privacy-policy-parent.md  (audience=parent)
//   docs/legal/privacy-policy-student.md (audience=student, 13+ reading level)
//
// The endpoint is PUBLIC (no RequireAuthorization) so the app shell can
// render the current policy during the pre-consent onboarding step without
// an authenticated session. Rate limiting is applied via the "api" bucket
// to prevent cheap content enumeration from being used as a DoS vector.
//
// Versioning contract:
//   - Each policy doc carries a YAML front-matter `version:` field in the
//     shape "v<MAJOR>.<MINOR>.<PATCH> YYYY-MM-DD".
//   - A ConsentGranted_V2 event captures the exact version string the
//     grantor accepted (prr-123); the admin audit export surfaces it.
//   - Legacy V1 events upcast to V2 with the sentinel
//     "v0.0.0-pre-versioning" so the export always has a value.
//
// Senior-architect note: legal copy is NOT inlined in source. The MD files
// are the single source of truth, reviewed by counsel as plain text.
// =============================================================================

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

// ---- Wire DTO ----------------------------------------------------------------

/// <summary>
/// Response shape for <c>GET /api/v1/legal/privacy-policy</c>.
/// </summary>
/// <param name="Audience">Audience of the returned doc (parent or student).</param>
/// <param name="Version">Exact version string from the doc front matter.</param>
/// <param name="EffectiveFrom">ISO-8601 date from the doc front matter.</param>
/// <param name="DocumentId">Stable identifier of the doc (e.g. cena-privacy-policy-parent-v1.0.0).</param>
/// <param name="Markdown">The full Markdown body (front matter stripped).</param>
public sealed record PrivacyPolicyDocumentDto(
    string Audience,
    string Version,
    string EffectiveFrom,
    string DocumentId,
    string Markdown);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Registers <c>/api/v1/legal/*</c> routes. The handler is small, pure,
/// and depends only on <see cref="IHostEnvironment"/> for the content
/// root + filesystem access (tests inject a temp content root).
/// </summary>
public static class LegalEndpoints
{
    /// <summary>Canonical route for the privacy-policy GET endpoint.</summary>
    public const string PrivacyPolicyRoute = "/api/v1/legal/privacy-policy";

    /// <summary>Valid audience values (guarded case-sensitively).</summary>
    public static readonly IReadOnlyList<string> ValidAudiences =
        new[] { "parent", "student" };

    /// <summary>Repo-relative directory that contains the canonical docs.</summary>
    public const string LegalDocsDirectory = "docs/legal";

    /// <summary>Marker for <see cref="ILogger{T}"/> stability.</summary>
    public sealed class LegalEndpointMarker { }

    /// <summary>Map the legal routes.</summary>
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(PrivacyPolicyRoute, HandleGetPrivacyPolicyAsync)
            .WithName("GetPrivacyPolicy")
            .WithTags("Legal", "Privacy");
        return app;
    }

    /// <summary>
    /// Test seam — the production endpoint uses <c>MapGet</c> which calls
    /// this via the private <c>HandleGetPrivacyPolicyAsync</c> adapter.
    /// Exposed as <c>internal</c> for test projects that have
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    internal static Task<IResult> HandleGetPrivacyPolicyAsyncForTests(
        HttpContext http,
        IHostEnvironment env,
        ILogger<LegalEndpointMarker> logger,
        string? audience,
        CancellationToken ct)
        => HandleGetPrivacyPolicyAsync(http, env, logger, audience, ct);

    private static Task<IResult> HandleGetPrivacyPolicyAsync(
        HttpContext http,
        IHostEnvironment env,
        ILogger<LegalEndpointMarker> logger,
        string? audience,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            return Task.FromResult(Results.BadRequest(new
            {
                error = "missing-audience",
                validAudiences = ValidAudiences,
            }));
        }

        if (!ValidAudiences.Contains(audience, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "[prr-123] privacy-policy: unknown audience={Audience}", audience);
            return Task.FromResult(Results.BadRequest(new
            {
                error = "unknown-audience",
                audience,
                validAudiences = ValidAudiences,
            }));
        }

        var docPath = ResolveDocPath(env.ContentRootPath, audience);
        if (docPath is null || !File.Exists(docPath))
        {
            logger.LogError(
                "[prr-123] privacy-policy doc missing for audience={Audience} path={Path}",
                audience, docPath ?? "(unresolved)");
            return Task.FromResult(Results.Problem(
                title: "legal-doc-unavailable",
                detail: "The requested privacy-policy document is not "
                    + "available on this deployment.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var raw = File.ReadAllText(docPath);
        var parsed = PrivacyPolicyParser.Parse(raw, audience);
        if (parsed is null)
        {
            logger.LogError(
                "[prr-123] privacy-policy doc malformed audience={Audience} path={Path}",
                audience, docPath);
            return Task.FromResult(Results.Problem(
                title: "legal-doc-malformed",
                detail: "The privacy-policy document on disk is missing "
                    + "required front-matter fields.",
                statusCode: StatusCodes.Status500InternalServerError));
        }

        return Task.FromResult(Results.Ok(parsed));
    }

    /// <summary>
    /// Resolves the absolute path to the canonical privacy-policy doc for
    /// the given audience, walking upward from the content root so tests
    /// that host inside a per-test temp directory still find the repo docs.
    /// Returns null when no candidate exists.
    /// </summary>
    internal static string? ResolveDocPath(string contentRoot, string audience)
    {
        var fileName = audience switch
        {
            "parent"  => "privacy-policy-parent.md",
            "student" => "privacy-policy-student.md",
            _         => null,
        };
        if (fileName is null) return null;

        // Walk up until we find docs/legal/<file> — tolerates content-root
        // being set to the Host project dir vs. the repo root.
        var cursor = new DirectoryInfo(contentRoot);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, LegalDocsDirectory, fileName);
            if (File.Exists(candidate)) return candidate;
            cursor = cursor.Parent;
        }

        // Fallback: treat env var CENA_LEGAL_DOCS_DIR as an override used
        // in container deployments where docs/ is copied into a known place.
        var overrideDir = Environment.GetEnvironmentVariable("CENA_LEGAL_DOCS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var overridePath = Path.Combine(overrideDir, fileName);
            if (File.Exists(overridePath)) return overridePath;
        }

        return null;
    }
}

// ---- Front-matter parser -----------------------------------------------------

/// <summary>
/// Parses the `---`-delimited YAML front matter of the privacy-policy docs
/// into a <see cref="PrivacyPolicyDocumentDto"/>. The parser is deliberately
/// minimal: we do NOT pull in a YAML dependency for two key–value lookups.
/// </summary>
internal static class PrivacyPolicyParser
{
    private static readonly Regex FrontMatterRegex = new(
        @"^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n(?<rest>.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parse a Markdown document into the public DTO. Returns null when the
    /// front matter is missing or the required fields are absent.
    /// </summary>
    public static PrivacyPolicyDocumentDto? Parse(string raw, string audience)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = FrontMatterRegex.Match(raw);
        if (!match.Success) return null;

        var frontMatter = match.Groups["body"].Value;
        var markdown = match.Groups["rest"].Value.TrimStart();

        var parsedAudience = ExtractScalar(frontMatter, "audience");
        var version = ExtractScalar(frontMatter, "version");
        var effectiveFrom = ExtractScalar(frontMatter, "effective_from");
        var docId = ExtractScalar(frontMatter, "doc_id");

        if (string.IsNullOrWhiteSpace(parsedAudience)
            || string.IsNullOrWhiteSpace(version)
            || string.IsNullOrWhiteSpace(effectiveFrom)
            || string.IsNullOrWhiteSpace(docId))
        {
            return null;
        }

        // Audience-mismatch is a deployment bug (wrong file wired to wrong
        // route). Surface as null so the caller returns 500, not 200 with
        // the wrong content.
        if (!string.Equals(parsedAudience, audience, StringComparison.Ordinal))
        {
            return null;
        }

        return new PrivacyPolicyDocumentDto(
            Audience: parsedAudience,
            Version: version,
            EffectiveFrom: effectiveFrom,
            DocumentId: docId,
            Markdown: markdown);
    }

    /// <summary>
    /// Extract a top-level scalar YAML value (quoted or unquoted). Nested
    /// lists and mappings are ignored — we only care about the four
    /// well-known keys listed in <see cref="Parse"/>.
    /// </summary>
    private static string? ExtractScalar(string frontMatter, string key)
    {
        // Match `key: value` at start of a line. Capture the rest of the
        // line up to the newline; trim quotes.
        var pattern = new Regex(
            "^" + Regex.Escape(key) + @":\s*(?<v>[^\r\n]+?)\s*$",
            RegexOptions.Multiline);
        var m = pattern.Match(frontMatter);
        if (!m.Success) return null;
        var v = m.Groups["v"].Value.Trim();
        if (v.StartsWith('"') && v.EndsWith('"') && v.Length >= 2)
        {
            v = v[1..^1];
        }
        else if (v.StartsWith('\'') && v.EndsWith('\'') && v.Length >= 2)
        {
            v = v[1..^1];
        }
        return v;
    }
}
