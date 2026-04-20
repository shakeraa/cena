// =============================================================================
// Cena Platform -- Firebase Custom Claims Transformer
// BKD-001.2: Extracts Firebase custom claims into .NET ClaimsPrincipal
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// Transforms Firebase JWT claims into standard .NET claims.
/// Extracts: role, school_id, student_ids, locale, plan from Firebase custom claims.
/// </summary>
public sealed class CenaClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        // Firebase custom claims are embedded in the JWT payload as top-level claims
        ExtractClaim(identity, "role", ClaimTypes.Role);
        ExtractClaim(identity, "school_id", "school_id");
        ExtractClaim(identity, "locale", "locale", defaultValue: "en");
        ExtractClaim(identity, "plan", "plan");

        // student_ids may be a JSON array
        var studentIdsClaim = identity.FindFirst("student_ids");
        if (studentIdsClaim != null)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<string[]>(studentIdsClaim.Value);
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        if (!identity.HasClaim("student_id", id))
                            identity.AddClaim(new Claim("student_id", id));
                    }
                }
            }
            catch (JsonException)
            {
                // Single value, not array
                if (!identity.HasClaim("student_id", studentIdsClaim.Value))
                    identity.AddClaim(new Claim("student_id", studentIdsClaim.Value));
            }
        }

        // prr-009 / ADR-0041: parent_of carries per-(student, institute)
        // binding entries. Wire shape from Firebase is a JSON array of
        // objects; explode into individual per-entry claims so
        // ParentAuthorizationGuard can FindAll("parent_of") and parse
        // one object per call.
        ExtractParentOfClaims(identity);

        return Task.FromResult(principal);
    }

    /// <summary>
    /// ADR-0041: parent_of is a per-(student, institute) binding cache.
    /// Input shapes accepted from Firebase custom claims:
    ///   - JSON array of objects: [{"studentId":"s1","instituteId":"i1"}, ...]
    ///   - Single JSON object: {"studentId":"s1","instituteId":"i1"}
    /// Array input is exploded into one-per-entry claims. Single-object
    /// and pre-exploded inputs are left untouched. Malformed input is
    /// dropped so downstream cannot mistake garbage for a valid binding.
    /// </summary>
    private static void ExtractParentOfClaims(ClaimsIdentity identity)
    {
        var claims = identity.FindAll("parent_of").ToList();
        if (claims.Count == 0) return;

        // Already exploded (>1 entry) — assume claims were added directly
        // by a test harness or an earlier transformer pass.
        if (claims.Count > 1) return;

        var raw = claims[0];
        try
        {
            using var doc = JsonDocument.Parse(raw.Value);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return; // Single-object / scalar — leave as-is.

            identity.RemoveClaim(raw);
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                identity.AddClaim(new Claim("parent_of", el.GetRawText()));
            }
        }
        catch (JsonException)
        {
            // Malformed parent_of — drop so downstream cannot mistake it
            // for a valid binding. No logging (non-PII structural error,
            // log spam gives no operator value).
            identity.RemoveClaim(raw);
        }
    }

    private static void ExtractClaim(
        ClaimsIdentity identity, string sourceClaim, string targetClaim, string? defaultValue = null)
    {
        if (identity.HasClaim(c => c.Type == targetClaim))
            return;

        // Try direct claim first
        var existing = identity.FindFirst(sourceClaim);

        // Firebase may also put custom claims under a JSON claim type path
        // e.g., the JWT may have "role" as a direct claim, or nested under another claim
        if (existing == null)
        {
            // Try common Firebase JWT claim patterns
            foreach (var claim in identity.Claims)
            {
                // Some Firebase SDKs serialize custom claims with full URI type names
                if (claim.Type.EndsWith($"/{sourceClaim}", StringComparison.OrdinalIgnoreCase))
                {
                    existing = claim;
                    break;
                }
            }
        }

        if (existing != null)
        {
            if (sourceClaim != targetClaim)
                identity.AddClaim(new Claim(targetClaim, existing.Value));
        }
        else if (defaultValue != null)
        {
            identity.AddClaim(new Claim(targetClaim, defaultValue));
        }
    }
}
