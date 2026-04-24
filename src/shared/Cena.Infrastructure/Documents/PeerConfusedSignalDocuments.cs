// =============================================================================
// Cena Platform — Peer-Confused Signal Documents (prr-159 / F5)
//
// Session-scoped, per-question anonymous "I'm confused too" signal.
// DESIGN:
//   - One aggregate document per (sessionId, questionId).
//   - Count is bumped once per emitting student via an idempotency key that
//     is hashed from studentId + sessionId + questionId — we store the hash,
//     not the studentId, so the document cannot be de-anonymised even by
//     someone with DB access.
//   - Count is only visible to session participants once it crosses the
//     k-anonymity floor (default 3). Below the floor, the endpoint
//     reports "count available soon" without surfacing a small-N number.
//   - Session-scoped retention (ADR-0003 treatment). Documents are purged
//     30 days after the session ends by the retention job.
//   - NO ML training on these signals (MlExcluded marker).
// =============================================================================

using System;
using System.Collections.Generic;

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Aggregate count of anonymous "I'm confused too" emissions for a
/// (sessionId, questionId) pair. Never identifies the emitting students.
/// </summary>
public class PeerConfusedSignalAggregate
{
    /// <summary>
    /// Document key: "{sessionId}:{questionId}". Single row per question per
    /// session.
    /// </summary>
    public string Id { get; set; } = "";

    public string SessionId { get; set; } = "";
    public string QuestionId { get; set; } = "";

    /// <summary>
    /// Tenant capture. Stored so retention + RTBF jobs can scope cleanly
    /// and so the endpoint can block cross-tenant reads.
    /// </summary>
    public string? SchoolId { get; set; }

    /// <summary>
    /// Emission count. Never revealed to the UI below the k-anonymity floor.
    /// </summary>
    public int ConfusedCount { get; set; }

    /// <summary>
    /// Hashed idempotency keys of students who already emitted for this
    /// (session, question). Stored as an opaque string set — the hash is
    /// salted per-session so scraping one session gives zero information
    /// about any other session, and the salt is not persisted outside the
    /// document itself. Student id is NEVER stored in this field.
    /// </summary>
    public HashSet<string> EmitterHashes { get; set; } = new();

    public DateTimeOffset FirstEmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastEmittedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Marks this document as excluded from ML training / fine-tune corpora.
    /// Session-scoped signal per ADR-0003.
    /// </summary>
    public bool MlExcluded { get; set; } = true;
}
