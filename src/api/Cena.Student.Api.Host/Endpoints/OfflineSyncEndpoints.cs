// =============================================================================
// Cena Platform — Offline Sync Endpoints (RDY-075 Phase 1B)
//
// Receives a batch of offline answer events from a reconnecting PWA
// client, dedupes via IOfflineSyncLedger, grades each accepted event
// against its ItemVersionFreeze, and returns a per-event outcome map
// so the client can clear matched queue entries.
//
// The endpoint is intentionally THIN: all the non-trivial logic
// (dedup decision, grading) already lives in Cena.Actors/Sessions/.
// This file wires it to the HTTP surface.
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Sessions;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public sealed record OfflineSyncBatchRequest(
    IReadOnlyList<OfflineAnswerEvent> Events);

public sealed record OfflineSyncEventOutcome(
    string IdempotencyKey,
    OfflineIngestDecision Decision,
    bool? IsCorrect);

public sealed record OfflineSyncBatchResponse(
    IReadOnlyList<OfflineSyncEventOutcome> Outcomes,
    int AcceptedCount,
    int DuplicateCount,
    int RejectedCount);

public static class OfflineSyncEndpoints
{
    public const int MaxBatchSize = 200;

    public static IEndpointRouteBuilder MapOfflineSyncEndpoints(
        this IEndpointRouteBuilder app)
    {
        // POST /api/sessions/sync-on-reconnect
        // Body: OfflineSyncBatchRequest
        // Response: OfflineSyncBatchResponse with per-event outcome
        //
        // Auth: bearer token, student-role; studentAnonId is taken
        // from the token NOT the request body so a client cannot spoof
        // another student's idempotency keys.
        app.MapPost("/api/sessions/sync-on-reconnect", async (
                HttpContext http,
                OfflineSyncBatchRequest request,
                IOfflineSyncLedger ledger,
                IDocumentStore store,
                ILogger<OfflineSyncBatchEndpointMarker> logger,
                CancellationToken ct) =>
            {
                if (request?.Events is null || request.Events.Count == 0)
                    return Results.BadRequest(new { error = "empty-batch" });

                if (request.Events.Count > MaxBatchSize)
                    return Results.BadRequest(new
                    {
                        error = "batch-too-large",
                        maxBatchSize = MaxBatchSize,
                    });

                var authenticatedStudentAnonId =
                    http.User.FindFirst("studentAnonId")?.Value
                    ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(authenticatedStudentAnonId))
                    return Results.Unauthorized();

                // Item-existence check: phase 1B treats every item as
                // existing; a real check would scan the item bank. For
                // now, reject events whose ItemId is empty/whitespace.
                bool ItemExists(string id) => !string.IsNullOrWhiteSpace(id);

                var outcomes = new List<OfflineSyncEventOutcome>(request.Events.Count);
                var accepted = 0;
                var duplicate = 0;
                var rejected = 0;

                foreach (var ev in request.Events)
                {
                    // Defence-in-depth: ignore events whose studentAnonId
                    // does not match the authenticated caller. A malicious
                    // client cannot pollute another student's stream via
                    // this endpoint.
                    if (!string.Equals(
                            ev.StudentAnonId,
                            authenticatedStudentAnonId,
                            StringComparison.Ordinal))
                    {
                        outcomes.Add(new OfflineSyncEventOutcome(
                            IdempotencyKey: ev.IdempotencyKey,
                            Decision: OfflineIngestDecision.Reject,
                            IsCorrect: null));
                        rejected++;
                        logger.LogWarning(
                            "[OFFLINE_SYNC] mismatched studentAnonId on key={Key} "
                            + "(claim={Claim}, event={Event}); rejected",
                            ev.IdempotencyKey,
                            authenticatedStudentAnonId,
                            ev.StudentAnonId);
                        continue;
                    }

                    var decision = OfflineSyncIngest.Decide(ev, ledger, ItemExists);
                    bool? isCorrect = null;

                    if (decision == OfflineIngestDecision.Accept)
                    {
                        isCorrect = ev.Freeze.IsAnswerCorrect(ev.SubmittedAnswer);
                        ledger.MarkSeen(ev.IdempotencyKey, DateTimeOffset.UtcNow);
                        accepted++;
                    }
                    else if (decision == OfflineIngestDecision.Duplicate)
                    {
                        duplicate++;
                    }
                    else
                    {
                        rejected++;
                    }

                    outcomes.Add(new OfflineSyncEventOutcome(
                        IdempotencyKey: ev.IdempotencyKey,
                        Decision: decision,
                        IsCorrect: isCorrect));
                }

                logger.LogInformation(
                    "[OFFLINE_SYNC] reconnect batch: student={Student} accepted={A} "
                    + "duplicate={D} rejected={R}",
                    authenticatedStudentAnonId,
                    accepted,
                    duplicate,
                    rejected);

                return Results.Ok(new OfflineSyncBatchResponse(
                    Outcomes: outcomes,
                    AcceptedCount: accepted,
                    DuplicateCount: duplicate,
                    RejectedCount: rejected));
            })
            .RequireAuthorization()
            .WithName("SyncOnReconnect")
            .WithTags("Sessions")
            .WithSummary("RDY-075 F4: accept a batch of offline answer events on reconnect.");

        return app;
    }

    // Placeholder type so ILogger<T> has a stable type argument tied to
    // this endpoint family (keeps log scopes readable).
    private sealed class OfflineSyncBatchEndpointMarker { }
}
