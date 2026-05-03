// =============================================================================
// Cena Platform — Curator Taxonomy Lookup Endpoint
//
// Exposes the parsed scripts/bagrut-taxonomy.json (TaxonomyCache) to the
// admin SPA so the Curator Metadata Panel can render a typeahead picker
// instead of accepting free-text taxonomy node strings. Without this the
// taxonomy field is a plain VTextField and curators can save typos that
// the cascade later can't resolve to a concept ID.
//
//   GET /api/admin/ingestion/taxonomy/leaves            → all leaves
//   GET /api/admin/ingestion/taxonomy/leaves?track=5u   → filtered to one track
//
// Tracks accepted: "3u" | "4u" | "5u" (the curator-facing wire values
// that mirror CuratorMetadata.Track). Internally the cache uses
// "math_3u"/"math_4u"/"math_5u" track ids, so we adapt at the boundary.
//
// Auth: ModeratorOrAbove — same gate as the rest of the curator surface.
// =============================================================================

using Cena.Admin.Api.Content;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Ingestion;

public static class CuratorTaxonomyEndpoints
{
    public static IEndpointRouteBuilder MapCuratorTaxonomyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/taxonomy")
            .WithTags("Ingestion CuratorMetadata")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/leaves", (string? track, TaxonomyCache cache) =>
        {
            var trackFilter = NormalizeTrack(track);
            if (track is not null && trackFilter is null)
            {
                return Results.BadRequest(new CenaError(
                    "invalid_track",
                    $"Unknown track '{track}'. Valid: 3u, 4u, 5u (or omit for all).",
                    ErrorCategory.Validation,
                    null,
                    null));
            }

            var leaves = trackFilter is null
                ? cache.AllLeaves
                : cache.AllLeaves.Where(l => string.Equals(l.TrackId, trackFilter, StringComparison.Ordinal));

            // Sort for deterministic UI rendering — Vuetify's autocomplete
            // doesn't sort by default and the kanban surfaces side-by-side
            // cards with adjacent track lookups; alphabetical is the
            // simplest order that doesn't surprise the curator.
            var dtos = leaves
                .OrderBy(l => l.TopicKey, StringComparer.Ordinal)
                .ThenBy(l => l.SubtopicKey, StringComparer.Ordinal)
                .Select(l => new TaxonomyLeafDto(
                    LeafId:      l.LeafId,
                    TrackId:     l.TrackId,
                    Topic:       l.TopicKey,
                    Subtopic:    l.SubtopicKey,
                    ConceptId:   l.ConceptId,
                    BloomMin:    l.BloomMin,
                    BloomMax:    l.BloomMax))
                .ToList();

            return Results.Ok(new TaxonomyLeavesResponse(
                Version: cache.Version,
                Track:   trackFilter,
                Leaves:  dtos));
        })
        .WithName("GetCuratorTaxonomyLeaves")
        .Produces<TaxonomyLeavesResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    // CuratorMetadata.Track wire values are "3u"/"4u"/"5u"; TaxonomyCache
    // track ids are "math_3u"/"math_4u"/"math_5u". Translate at the
    // boundary so callers send the same wire value they use everywhere
    // else in the curator handshake.
    private static string? NormalizeTrack(string? track)
    {
        if (string.IsNullOrWhiteSpace(track)) return null;
        return track.Trim().ToLowerInvariant() switch
        {
            "3u" or "math_3u" => "math_3u",
            "4u" or "math_4u" => "math_4u",
            "5u" or "math_5u" => "math_5u",
            _ => null,
        };
    }
}

public sealed record TaxonomyLeavesResponse(
    string Version,
    string? Track,
    IReadOnlyList<TaxonomyLeafDto> Leaves);

public sealed record TaxonomyLeafDto(
    string LeafId,        // "math_5u.calculus.derivative_rules"
    string TrackId,       // "math_5u"
    string Topic,         // "calculus"
    string Subtopic,      // "derivative_rules"
    string ConceptId,     // "CAL-003"
    int    BloomMin,
    int    BloomMax);
