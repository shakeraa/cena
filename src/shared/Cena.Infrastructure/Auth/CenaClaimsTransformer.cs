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

        return Task.FromResult(principal);
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
