// =============================================================================
// Cena Platform — Corpus Expander Endpoint (RDY-059)
//
// POST /api/admin/questions/expand-corpus  (SuperAdminOnly + ai rate-limit)
//
// Defaults body.DryRun = true. Operators must explicitly set false to spend.
// All validation + orchestration lives in CorpusExpanderHandler.RunAsync.
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Content;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Questions;

public static class CorpusExpanderEndpoints
{
    public static IEndpointRouteBuilder MapCorpusExpanderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/questions")
            .WithTags("Corpus Expander")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("ai");

        group.MapPost("/expand-corpus", async (
            CorpusExpansionRequest request,
            ClaimsPrincipal user,
            ICorpusSourceProvider sourceProvider,
            IContentCoverageService coverage,
            IDocumentStore store,
            IAiGenerationService ai,
            IQualityGateService qualityGate,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.Json(new CenaError(
                    "invalid_body", "CorpusExpansionRequest body required.",
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);

            var startedBy = user.FindFirst("user_id")?.Value
                            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? user.FindFirst("sub")?.Value
                            ?? "unknown-super-admin";

            try
            {
                var response = await CorpusExpanderHandler.RunAsync(
                    request,
                    sourceProvider,
                    coverage,
                    store,
                    ai,
                    qualityGate,
                    startedBy,
                    loggerFactory.CreateLogger("Cena.Admin.Api.Questions.CorpusExpander"),
                    ct);

                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new CenaError(
                    "invalid_request", ex.Message,
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ExpandCorpus")
        .Produces<CorpusExpansionResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
