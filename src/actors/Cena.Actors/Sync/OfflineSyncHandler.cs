// =============================================================================
// Cena Platform -- OfflineSyncHandler (Idempotent Offline Event Sync)
// Layer: Actor Model | Runtime: .NET 9
//
// Handles SyncOfflineEvents command with full idempotency via Redis SET NX.
// Three-tier classification: Unconditional, Conditional, ServerAuthoritative.
// Conditional weighting: 1.0 (context match), 0.75 (methodology changed),
// 0.0 (concept removed). All events committed atomically via FlushEvents.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Events;
using Cena.Actors.Students;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Sync;

// =============================================================================
// CLASSIFICATION
// =============================================================================

/// <summary>
/// Offline event acceptance classification.
/// </summary>
public enum OfflineEventTier
{
    /// <summary>Always accepted (session-start, annotations).</summary>
    Unconditional,

    /// <summary>Accepted with weight based on context match (attempts).</summary>
    Conditional,

    /// <summary>Server state takes precedence (methodology switches, level changes).</summary>
    ServerAuthoritative
}

/// <summary>
/// Result of processing a single offline event.
/// </summary>
public sealed record OfflineEventResult(
    string IdempotencyKey,
    OfflineEventTier Tier,
    double Weight,
    bool WasDuplicate,
    bool Accepted,
    string Reason);

/// <summary>
/// Result of the entire sync operation.
/// </summary>
public sealed record SyncResult(
    int TotalEvents,
    int Accepted,
    int Rejected,
    int Duplicates,
    IReadOnlyList<OfflineEventResult> Details);

// =============================================================================
// HANDLER
// =============================================================================

/// <summary>
/// Handles offline event synchronization with idempotency and three-tier classification.
/// Not an actor itself -- invoked by StudentActor when processing SyncOfflineEvents.
/// </summary>
public sealed class OfflineSyncHandler
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OfflineSyncHandler> _logger;

    // ── Redis key prefix and TTL ──
    private const string IdempotencyKeyPrefix = "cena:offline:idem:";
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(72);

    public OfflineSyncHandler(IConnectionMultiplexer redis, ILogger<OfflineSyncHandler> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process a batch of offline events with idempotency checking and three-tier classification.
    /// Returns the domain events to be appended atomically by the caller.
    /// </summary>
    public async Task<(SyncResult Result, IReadOnlyList<object> DomainEvents)> ProcessAsync(
        SyncOfflineEvents command,
        StudentState currentState)
    {
        var results = new List<OfflineEventResult>(command.Events.Count);
        var domainEvents = new List<object>();
        int accepted = 0;
        int rejected = 0;
        int duplicates = 0;

        var db = _redis.GetDatabase();

        foreach (var offlineEvent in command.Events)
        {
            // Step 1: Idempotency check via Redis SET NX
            string redisKey = $"{IdempotencyKeyPrefix}{offlineEvent.IdempotencyKey}";
            bool isNew = await db.StringSetAsync(
                redisKey,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                IdempotencyTtl,
                When.NotExists);

            if (!isNew)
            {
                duplicates++;
                results.Add(new OfflineEventResult(
                    offlineEvent.IdempotencyKey,
                    ClassifyTier(offlineEvent),
                    0.0,
                    WasDuplicate: true,
                    Accepted: false,
                    Reason: "Duplicate event (idempotency key exists in Redis)"));
                continue;
            }

            // Step 2: Classify tier
            var tier = ClassifyTier(offlineEvent);

            // Step 3: Compute weight and decide acceptance
            var (weight, reason) = ComputeWeight(offlineEvent, tier, currentState);

            bool accept = weight > 0.0 && tier != OfflineEventTier.ServerAuthoritative;

            if (accept)
            {
                // Step 4: Convert to domain event
                var domainEvent = ConvertToDomainEvent(offlineEvent, command.StudentId, weight);
                if (domainEvent != null)
                {
                    domainEvents.Add(domainEvent);
                    accepted++;
                }
                else
                {
                    accept = false;
                    reason = "Could not convert offline event to domain event";
                    rejected++;
                }
            }
            else
            {
                rejected++;
            }

            results.Add(new OfflineEventResult(
                offlineEvent.IdempotencyKey,
                tier,
                weight,
                WasDuplicate: false,
                Accepted: accept,
                Reason: reason));
        }

        _logger.LogInformation(
            "Offline sync for student {StudentId}: Total={Total}, Accepted={Accepted}, " +
            "Rejected={Rejected}, Duplicates={Duplicates}",
            command.StudentId, command.Events.Count, accepted, rejected, duplicates);

        var syncResult = new SyncResult(
            command.Events.Count, accepted, rejected, duplicates, results);

        return (syncResult, domainEvents);
    }

    // ── Tier Classification ──

    private static OfflineEventTier ClassifyTier(OfflineEvent evt)
    {
        return evt switch
        {
            OfflineAttemptEvent => OfflineEventTier.Conditional,
            _ => OfflineEventTier.Unconditional
        };
    }

    // ── Weight Computation ──

    /// <summary>
    /// Compute the acceptance weight for a conditional offline event.
    /// Weight rules:
    ///   1.0  - Context matches (same methodology, concept still active)
    ///   0.75 - Methodology has changed since the offline event was created
    ///   0.0  - Concept has been removed from the student's curriculum
    /// </summary>
    private static (double Weight, string Reason) ComputeWeight(
        OfflineEvent evt, OfflineEventTier tier, StudentState state)
    {
        if (tier == OfflineEventTier.Unconditional)
            return (1.0, "Unconditional acceptance");

        if (tier == OfflineEventTier.ServerAuthoritative)
            return (0.0, "Server-authoritative event: server state takes precedence");

        // Conditional tier: check context match
        if (evt is OfflineAttemptEvent attempt)
        {
            // Check if concept still exists in the mastery map (not removed)
            bool conceptActive = state.MasteryMap.ContainsKey(attempt.ConceptId)
                || state.MasteryMap.Count == 0; // Accept if mastery map is empty (new student)

            if (!conceptActive)
                return (0.0, $"Concept {attempt.ConceptId} removed from curriculum");

            // Check if methodology has changed since the offline event
            if (state.MethodologyMap.TryGetValue(attempt.ConceptId, out var currentMethodology))
            {
                // We don't know the offline methodology exactly, but if the server has
                // switched methodology since the event timestamp, reduce weight
                if (state.LastActivityDate > evt.ClientTimestamp)
                {
                    // Server had activity after this offline event was created
                    // Check if there was a methodology switch in the attempt history
                    if (state.MethodAttemptHistory.TryGetValue(attempt.ConceptId, out var history)
                        && history.Count > 0
                        && history[^1].SwitchedAt > evt.ClientTimestamp)
                    {
                        return (0.75, $"Methodology changed since offline event. Current: {currentMethodology}");
                    }
                }
            }

            return (1.0, "Context match: concept active, methodology unchanged");
        }

        return (1.0, "Default unconditional acceptance");
    }

    // ── Domain Event Conversion ──

    private static object? ConvertToDomainEvent(OfflineEvent evt, string studentId, double weight)
    {
        if (evt is OfflineAttemptEvent attempt)
        {
            // Hash the answer for privacy
            string answerHash = ComputeHash(attempt.Answer);

            return new ConceptAttempted_V1(
                StudentId: studentId,
                ConceptId: attempt.ConceptId,
                SessionId: attempt.SessionId,
                IsCorrect: false, // Cannot determine correctness offline -- will be re-evaluated
                ResponseTimeMs: attempt.ResponseTimeMs,
                QuestionId: attempt.QuestionId,
                QuestionType: attempt.QuestionType.ToString(),
                MethodologyActive: "offline",
                ErrorType: "unknown",
                PriorMastery: 0.0,
                PosteriorMastery: 0.0, // BKT will recalculate on replay
                HintCountUsed: attempt.HintCountUsed,
                WasSkipped: attempt.WasSkipped,
                AnswerHash: answerHash,
                BackspaceCount: attempt.BackspaceCount,
                AnswerChangeCount: attempt.AnswerChangeCount,
                WasOffline: true,
                Timestamp: attempt.ClientTimestamp);
        }

        return null;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..16];
    }
}
