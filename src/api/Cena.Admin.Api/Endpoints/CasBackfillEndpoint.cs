// =============================================================================
// Cena Platform — CAS Backfill Endpoint (RDY-036)
//
// POST /api/admin/questions/cas-backfill
//
// Re-verifies questions that have no CAS binding or whose binding is in
// Failed/Unverifiable state. Uses the same idempotency cache as the main
// gate (binding doc keyed by (QuestionId, CorrectAnswerHash)) so repeated
// runs are cheap.
//
// Limits:
//   - Default batch = 50 questions; max 500.
//   - Admin-only (not Moderator). Backfill is a maintenance operation.
//   - Per-question failures never abort the batch; results are reported in
//     the response body.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Questions;
using Cena.Admin.Api.QualityGate;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Endpoints;

public sealed record CasBackfillRequest(
    int? BatchSize,
    bool RetryFailed,
    bool RetryUnverifiable);

public sealed record CasBackfillItemResult(
    string QuestionId,
    string Outcome,
    string Engine,
    double LatencyMs,
    string? Reason);

public sealed record CasBackfillResponse(
    int Considered,
    int Verified,
    int Failed,
    int Unverifiable,
    int Skipped,
    IReadOnlyList<CasBackfillItemResult> Items);

public static class CasBackfillEndpoint
{
    public const int DefaultBatchSize = 50;
    public const int MaxBatchSize = 500;

    public static IEndpointRouteBuilder MapCasBackfillEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/questions/cas-backfill", HandleAsync)
            .WithName("BackfillCasBindings")
            .WithTags("Question Bank", "CAS")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api")
            .Produces<CasBackfillResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        CasBackfillRequest request,
        IDocumentStore store,
        ICasVerificationGate casGate,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("CasBackfill");
        var batchSize = Math.Clamp(request.BatchSize ?? DefaultBatchSize, 1, MaxBatchSize);

        var items = new List<CasBackfillItemResult>();
        int verified = 0, failed = 0, unverifiable = 0, skipped = 0;

        await using var session = store.LightweightSession();

        // Strategy: pull QuestionState read models (or ids), then for each
        // load the binding (if any) and re-verify when it's missing/Failed/
        // Unverifiable per request flags.
        var questions = await session.Query<QuestionState>()
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var q in questions)
        {
            ct.ThrowIfCancellationRequested();

            var binding = await session.LoadAsync<QuestionCasBinding>(q.Id, ct);
            bool needs = binding is null
                         || (request.RetryFailed && binding.Status == CasBindingStatus.Failed)
                         || (request.RetryUnverifiable && binding.Status == CasBindingStatus.Unverifiable);

            if (!needs)
            {
                skipped++;
                continue;
            }

            // Locate authored correct answer from the projected QuestionState.
            var correct = q.Options.FirstOrDefault(o => o.IsCorrect)?.Text ?? "";

            try
            {
                var result = await casGate.VerifyForCreateAsync(
                    q.Id, q.Subject, q.Stem, correct, variable: null, ct);

                session.Store(result.Binding);

                items.Add(new CasBackfillItemResult(
                    q.Id, result.Outcome.ToString(), result.Engine,
                    result.LatencyMs, result.FailureReason));

                switch (result.Outcome)
                {
                    case CasGateOutcome.Verified: verified++; break;
                    case CasGateOutcome.Failed: failed++; break;
                    case CasGateOutcome.Unverifiable: unverifiable++; break;
                    case CasGateOutcome.CircuitOpen: unverifiable++; break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[CAS_BACKFILL_ERROR] questionId={Qid} — leaving binding untouched", q.Id);
                skipped++;
                items.Add(new CasBackfillItemResult(q.Id, "Error", "none", 0, ex.Message));
            }
        }

        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[CAS_BACKFILL_DONE] considered={N} verified={V} failed={F} unverifiable={U} skipped={S}",
            questions.Count, verified, failed, unverifiable, skipped);

        return Results.Ok(new CasBackfillResponse(
            questions.Count, verified, failed, unverifiable, skipped, items));
    }
}
