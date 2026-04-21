// =============================================================================
// Cena Platform — Peer-Confused Signal Service (prr-159 / F5)
//
// Business logic for the anonymous "I'm confused too" signal. Lives in the
// Cena.Actors layer so tests can drive it without spinning up the API host.
//
// CONTRACT:
//   - Emit(studentId, sessionId, questionId) — idempotent per emitter; does
//     not expose the emitting student's identity on the returned count.
//   - GetVisibleCount(sessionId, questionId) — returns null if count is
//     below k-anonymity floor, otherwise returns the count.
//
// DESIGN INVARIANTS:
//   - Student ID NEVER stored on the aggregate document. We store a salted
//     hash of (studentId, sessionId, questionId) only — this lets us
//     enforce idempotency without persisting identity.
//   - Session-scoped: the service does not look up history across sessions.
//   - k-anonymity floor defaults to 3 (configurable per institute later).
//   - Tenant-scoped: the service accepts the caller's schoolId and records
//     it on first emission; subsequent emissions cross-check it and refuse
//     on mismatch.
//
// RELATED:
//   - ADR-0003: misconception / affective signal session-scoping.
//   - ADR-0052 (prr-023): this is a fan-in aggregate under the
//     collaboration-saga pattern. A later task migrates the persistence to
//     the full saga actor; this service is the simpler Marten-backed slice
//     that ships for MVP.
// =============================================================================

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Sessions;

/// <summary>
/// Outcome of an emission attempt. Deliberately narrow — callers should not
/// branch on implementation details.
/// </summary>
public enum PeerConfusedEmitOutcome
{
    /// <summary>New emission recorded; count was incremented.</summary>
    Recorded,

    /// <summary>Student already emitted for this question; count unchanged.</summary>
    AlreadyEmitted,

    /// <summary>Cross-tenant / cross-school mismatch. Emission rejected.</summary>
    TenantMismatch,
}

/// <summary>
/// Visible count for a (session, question) pair, honouring the k-anonymity
/// floor. Below the floor, <see cref="Count"/> is null and
/// <see cref="BelowAnonymityFloor"/> is true.
/// </summary>
public sealed record PeerConfusedVisibleCount(
    string SessionId,
    string QuestionId,
    int? Count,
    bool BelowAnonymityFloor,
    int AnonymityFloor);

public interface IPeerConfusedSignalService
{
    /// <summary>
    /// Record a "confused too" emission from <paramref name="studentId"/> for
    /// the given session + question. Idempotent per emitter.
    /// </summary>
    Task<PeerConfusedEmitOutcome> EmitAsync(
        string studentId,
        string sessionId,
        string questionId,
        string? schoolId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the visible count honouring the k-anonymity floor. Below
    /// the floor, returns null <see cref="PeerConfusedVisibleCount.Count"/>.
    /// </summary>
    Task<PeerConfusedVisibleCount> GetVisibleCountAsync(
        string sessionId,
        string questionId,
        CancellationToken ct = default);
}

public sealed class PeerConfusedSignalService : IPeerConfusedSignalService
{
    /// <summary>
    /// k-anonymity floor. Below this count, the visible-count endpoint
    /// reports null. Default 3 — a participant seeing "1 other is confused"
    /// can identify the emitter in a small session. 3 is the floor used by
    /// cultural-equity rollups (CulturalContextService) and by the aggregate
    /// reporting elsewhere in the platform.
    /// </summary>
    public const int DefaultAnonymityFloor = 3;

    private readonly IDocumentStore _store;
    private readonly ILogger<PeerConfusedSignalService> _logger;
    private readonly int _anonymityFloor;

    public PeerConfusedSignalService(
        IDocumentStore store,
        ILogger<PeerConfusedSignalService> logger,
        int anonymityFloor = DefaultAnonymityFloor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (anonymityFloor < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(anonymityFloor),
                anonymityFloor,
                "k-anonymity floor must be at least 2 to preserve anonymity.");
        }
        _anonymityFloor = anonymityFloor;
    }

    public async Task<PeerConfusedEmitOutcome> EmitAsync(
        string studentId,
        string sessionId,
        string questionId,
        string? schoolId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("studentId must be non-empty", nameof(studentId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId must be non-empty", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId must be non-empty", nameof(questionId));

        var docId = BuildDocId(sessionId, questionId);
        var emitterHash = HashEmitter(studentId, sessionId, questionId);

        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<PeerConfusedSignalAggregate>(docId, ct);

        if (doc is null)
        {
            doc = new PeerConfusedSignalAggregate
            {
                Id = docId,
                SessionId = sessionId,
                QuestionId = questionId,
                SchoolId = schoolId,
                ConfusedCount = 1,
                EmitterHashes = new System.Collections.Generic.HashSet<string> { emitterHash },
                FirstEmittedAt = DateTimeOffset.UtcNow,
                LastEmittedAt = DateTimeOffset.UtcNow,
                MlExcluded = true,
            };
            session.Store(doc);
            await session.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[PEER_CONFUSED_SIGNAL] first emission session={SessionId} question={QuestionId} school={SchoolId}",
                sessionId, questionId, schoolId ?? "(none)");
            return PeerConfusedEmitOutcome.Recorded;
        }

        // Tenant cross-check: first writer wins. Later emissions must match
        // the recorded school, otherwise we have a tenancy leak in progress.
        if (doc.SchoolId is not null && schoolId is not null && doc.SchoolId != schoolId)
        {
            _logger.LogWarning(
                "[PEER_CONFUSED_SIGNAL] tenant mismatch session={SessionId} question={QuestionId} recorded={Recorded} caller={Caller}",
                sessionId, questionId, doc.SchoolId, schoolId);
            return PeerConfusedEmitOutcome.TenantMismatch;
        }

        if (doc.EmitterHashes.Contains(emitterHash))
        {
            return PeerConfusedEmitOutcome.AlreadyEmitted;
        }

        doc.EmitterHashes.Add(emitterHash);
        doc.ConfusedCount = doc.EmitterHashes.Count;
        doc.LastEmittedAt = DateTimeOffset.UtcNow;
        if (doc.SchoolId is null && schoolId is not null) doc.SchoolId = schoolId;

        session.Store(doc);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[PEER_CONFUSED_SIGNAL] emission session={SessionId} question={QuestionId} count={Count}",
            sessionId, questionId, doc.ConfusedCount);

        return PeerConfusedEmitOutcome.Recorded;
    }

    public async Task<PeerConfusedVisibleCount> GetVisibleCountAsync(
        string sessionId,
        string questionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId must be non-empty", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId must be non-empty", nameof(questionId));

        var docId = BuildDocId(sessionId, questionId);

        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<PeerConfusedSignalAggregate>(docId, ct);

        if (doc is null || doc.ConfusedCount < _anonymityFloor)
        {
            return new PeerConfusedVisibleCount(
                SessionId: sessionId,
                QuestionId: questionId,
                Count: null,
                BelowAnonymityFloor: true,
                AnonymityFloor: _anonymityFloor);
        }

        return new PeerConfusedVisibleCount(
            SessionId: sessionId,
            QuestionId: questionId,
            Count: doc.ConfusedCount,
            BelowAnonymityFloor: false,
            AnonymityFloor: _anonymityFloor);
    }

    /// <summary>
    /// Salt the hash with both the session id and the question id. Including
    /// both means the same (studentId, sessionId) across questions, or the
    /// same (studentId, questionId) across sessions, produces a different
    /// hash — so a leaked hash reveals membership in one row only, not the
    /// student's full activity graph.
    /// </summary>
    internal static string HashEmitter(string studentId, string sessionId, string questionId)
    {
        // Canonical separator byte keeps the three components unambiguous.
        // Using a non-printable separator prevents crafted ids that would
        // collide after naive concatenation.
        var material = new StringBuilder()
            .Append(studentId).Append('\x01')
            .Append(sessionId).Append('\x01')
            .Append(questionId).ToString();
        var bytes = Encoding.UTF8.GetBytes(material);
        var hash = SHA256.HashData(bytes);
        // Hex-encode — stored as-is on the document, no reversal path.
        return Convert.ToHexString(hash);
    }

    internal static string BuildDocId(string sessionId, string questionId)
        => $"{sessionId}:{questionId}";
}
