// =============================================================================
// Cena Platform — Generate-Similar Endpoint (RDY-058)
//
// Thin minimal-API wrapper around GenerateSimilarHandler.HandleAsync.
// Auth + rate-limit enforced here; all validation + orchestration lives in
// the handler so unit tests drive the real control flow.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Variants;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
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
            IConfiguration configuration,
            IVariantGenerationGate variantGate,
            CancellationToken ct) =>
        {
            var generatedBy = user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? user.FindFirst("sub")?.Value
                              ?? "unknown-curator";

            // PRR-265 / ADR-0059 §15.5 R1: legal-flag gate is read here
            // (per-request) so a config refresh flips behaviour without a
            // process restart. ADR-0059 §14.2 R8 (takedown runbook) requires
            // a 30-min global kill-switch — keeping the read at request
            // time honours that.
            var legalFlagEnabled = configuration
                .GetValue<bool>("Cena:Variants:BagrutSeedToLlmEnabled");
            // Curator institute id (school_id claim, set at sign-in for
            // tenant-bound admins). SUPER_ADMIN runs without one and we
            // fall back to null (gate skips per-institute scopes).
            var instituteId = user.FindFirst("school_id")?.Value;

            return await GenerateSimilarHandler.HandleAsync(
                questionId:                  id,
                body:                        body ?? new GenerateSimilarRequest(),
                store:                       store,
                ai:                          ai,
                qualityGate:                 qualityGate,
                generatedBy:                 generatedBy,
                logger:                      loggerFactory.CreateLogger("Cena.Admin.Api.Questions.GenerateSimilar"),
                ct:                          ct,
                variantGate:                 variantGate,
                variantGateLegalFlagEnabled: legalFlagEnabled,
                variantGateInstituteId:      string.IsNullOrWhiteSpace(instituteId) ? null : instituteId);
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
