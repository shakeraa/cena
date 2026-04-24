// =============================================================================
// Cena Platform — Generate-Similar Endpoint (RDY-058)
//
// Thin minimal-API wrapper around GenerateSimilarHandler.HandleAsync.
// Auth + rate-limit enforced here; all validation + orchestration lives in
// the handler so unit tests drive the real control flow.
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Questions;

public static class GenerateSimilarEndpoints
{
    public static IEndpointRouteBuilder MapGenerateSimilarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/questions")
            .WithTags("Question Recreation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("ai");

        group.MapPost("/{id}/generate-similar", async (
            string id,
            GenerateSimilarRequest body,
            ClaimsPrincipal user,
            IDocumentStore store,
            IAiGenerationService ai,
            IQualityGateService qualityGate,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var generatedBy = user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? user.FindFirst("sub")?.Value
                              ?? "unknown-curator";

            return await GenerateSimilarHandler.HandleAsync(
                questionId:   id,
                body:         body ?? new GenerateSimilarRequest(),
                store:        store,
                ai:           ai,
                qualityGate:  qualityGate,
                generatedBy:  generatedBy,
                logger:       loggerFactory.CreateLogger("Cena.Admin.Api.Questions.GenerateSimilar"),
                ct:           ct);
        })
        .WithName("GenerateSimilarQuestions")
        .Produces<BatchGenerateResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
