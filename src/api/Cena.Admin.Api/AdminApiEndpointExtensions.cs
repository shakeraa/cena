// =============================================================================
// Cena Platform — admin-API endpoint-builder extension helpers.
//
// Extracted from AdminApiEndpoints.cs so the 500-LOC ratchet (ADR-0012,
// baseline 1500) stays satisfied and the boilerplate `.Produces<CenaError>`
// chain repeated 100+ times in the admin surface has a single canonical
// definition. Behavior unchanged — the chain produced is identical to the
// inline form, just hoisted into one place so reviewers can change it once
// (e.g. when a new common error code is introduced).
// =============================================================================

using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cena.Admin.Api;

internal static class AdminApiEndpointExtensions
{
    /// <summary>
    /// Annotates a minimal-API endpoint with the canonical error envelopes
    /// the admin surface emits for every authenticated/rate-limited route:
    /// 401 (Unauthorized), 429 (TooManyRequests), 500 (InternalServerError).
    /// </summary>
    public static RouteHandlerBuilder WithStandardErrorResponses(this RouteHandlerBuilder builder)
        => builder
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
            .Produces<CenaError>(StatusCodes.Status500InternalServerError);
}
