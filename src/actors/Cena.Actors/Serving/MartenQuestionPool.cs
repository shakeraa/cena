// =============================================================================
// Cena Platform — Marten-backed Question Pool (FIND-pedagogy-016 + PRR-246)
//
// On-demand IQuestionPool for the REST host. Loads published questions from
// Marten/PostgreSQL by subject(s) without requiring NATS hot-reload.
// Used by AdaptiveQuestionPool in the Student API Host context.
//
// PRR-246 + ADR-0043 — Pool policy. The original LoadAsync filtered only on
// (Subject, Status="Published"), which leaks raw BagrutReference items into
// the pool — a P0 ship-gate violation. The new LoadAsync takes a
// QuestionPoolPolicy that enforces:
//
//   * ADR-0043 default — SourceType="BagrutReference" items are EXCLUDED
//     unless the caller opts in via AllowReferenceItems=true (admin-only
//     surfaces; never set true for student-facing pools).
//
//   * PRR-246 — when QuestionPaperCodes is non-empty (set by the SessionMode=
//     ExamPrep callers from the active ExamTarget), only items whose
//     QuestionPaperCodes intersects the policy's set surface in the pool.
//     Empty/null = no exam-target restriction (Freestyle mode).
// =============================================================================

using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Serving;

/// <summary>
/// PRR-246 — selection policy for <see cref="MartenQuestionPool.LoadAsync"/>.
///
/// Defaults are STRICT (ADR-0043 enforcement on; no exam-target binding).
/// Callers must opt OUT of the strict path explicitly — closes the
/// silent-leak failure mode of the prior signature.
/// </summary>
public sealed record QuestionPoolPolicy(
    /// <summary>
    /// ADR-0043 — when false (default), items with
    /// <c>SourceType="BagrutReference"</c> are filtered out. Only set true
    /// for admin / curator surfaces that need to inspect the reference
    /// corpus directly. NEVER set true for any student-facing pool.
    /// </summary>
    bool AllowReferenceItems = false,

    /// <summary>
    /// PRR-246 — when non-empty, only questions whose
    /// <c>QuestionPaperCodes</c> intersects this set surface. Sourced from
    /// the active ExamTarget's QuestionPaperCodes (e.g.,
    /// {"035581","035582"}) by the SessionEndpoints caller in
    /// <c>SessionMode=ExamPrep</c>. Empty/null = Freestyle (no
    /// exam-target restriction).
    /// </summary>
    IReadOnlyList<string>? QuestionPaperCodes = null)
{
    /// <summary>Convenience: the strict default policy.</summary>
    public static QuestionPoolPolicy Strict { get; } = new();
}

/// <summary>
/// Marten-backed question pool that loads published questions from PostgreSQL.
/// Unlike <see cref="QuestionPoolActor"/> (which requires NATS for hot-reload),
/// this implementation loads on-demand and is suitable for the REST API host
/// where the actor infrastructure is not available.
/// </summary>
public sealed class MartenQuestionPool : IQuestionPool
{
    private readonly Dictionary<string, List<PublishedQuestion>> _conceptIndex = new();
    private int _totalItems;

    public int ItemCount => _totalItems;

    /// <summary>
    /// Load published questions for the given subjects from Marten with the
    /// provided <paramref name="policy"/> applied. Defaults to
    /// <see cref="QuestionPoolPolicy.Strict"/> when omitted (closes ADR-0043).
    /// </summary>
    public static async Task<MartenQuestionPool> LoadAsync(
        IDocumentStore store,
        string[] subjects,
        ILogger logger,
        CancellationToken ct = default)
        => await LoadAsync(store, subjects, QuestionPoolPolicy.Strict, logger, ct);

    /// <summary>
    /// PRR-246 — explicit-policy overload. New code should call this; the
    /// 4-arg overload above stays for back-compat with admin-side callers
    /// that have already proven the strict path is correct for their use.
    /// </summary>
    public static async Task<MartenQuestionPool> LoadAsync(
        IDocumentStore store,
        string[] subjects,
        QuestionPoolPolicy policy,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var pool = new MartenQuestionPool();
        await using var session = store.QuerySession();

        var paperCodes = policy.QuestionPaperCodes;
        var hasPaperCodeFilter = paperCodes is { Count: > 0 };

        foreach (var subject in subjects)
        {
            // Base filter: subject + Published. ADR-0043 SourceType filter
            // applies UNLESS the caller opted into reference items. Marten's
            // LINQ provider compiles all of these into a single SQL WHERE.
            var query = session.Query<QuestionReadModel>()
                .Where(q => q.Subject == subject && q.Status == "Published");

            if (!policy.AllowReferenceItems)
            {
                query = query.Where(q => q.SourceType != "BagrutReference");
            }

            if (hasPaperCodeFilter)
            {
                // PRR-246 — only items aligned to one of the active
                // ExamTarget's paper codes. List<string>.Any in Marten LINQ
                // emits a JSONB ?| (any-of) operator on the indexed column.
                query = query.Where(q =>
                    q.QuestionPaperCodes.Any(c => paperCodes!.Contains(c)));
            }

            var questions = await query.ToListAsync(ct);

            foreach (var q in questions)
            {
                var published = new PublishedQuestion(
                    ItemId: q.Id,
                    Subject: q.Subject,
                    ConceptIds: q.Concepts,
                    BloomLevel: q.BloomsLevel,
                    Difficulty: q.Difficulty,
                    QualityScore: q.QualityScore,
                    Language: q.Language,
                    StemPreview: q.StemPreview,
                    SourceType: q.SourceType,
                    PublishedAt: q.UpdatedAt ?? q.CreatedAt,
                    Explanation: q.Explanation);

                foreach (var conceptId in q.Concepts)
                {
                    if (!pool._conceptIndex.TryGetValue(conceptId, out var list))
                    {
                        list = new List<PublishedQuestion>();
                        pool._conceptIndex[conceptId] = list;
                    }
                    list.Add(published);
                }
            }
        }

        // Sort each concept's questions by bloom level then difficulty
        foreach (var list in pool._conceptIndex.Values)
        {
            list.Sort((a, b) =>
            {
                var bloomCmp = a.BloomLevel.CompareTo(b.BloomLevel);
                return bloomCmp != 0 ? bloomCmp : a.Difficulty.CompareTo(b.Difficulty);
            });
        }

        pool._totalItems = pool._conceptIndex.Values.Sum(l => l.Count);

        logger.LogInformation(
            "MartenQuestionPool loaded {Count} questions across {Concepts} concepts for subjects [{Subjects}] " +
            "(policy: AllowReferenceItems={AllowRef} PaperCodes={PaperCodes})",
            pool._totalItems, pool._conceptIndex.Count, string.Join(", ", subjects),
            policy.AllowReferenceItems,
            hasPaperCodeFilter ? string.Join(",", paperCodes!) : "(none)");

        return pool;
    }

    public IReadOnlyList<PublishedQuestion> GetForConcept(string conceptId)
    {
        return _conceptIndex.TryGetValue(conceptId, out var list)
            ? list
            : Array.Empty<PublishedQuestion>();
    }

    public IReadOnlyList<PublishedQuestion> GetFiltered(
        string conceptId,
        int minBloom, int maxBloom,
        float minDifficulty, float maxDifficulty)
    {
        if (!_conceptIndex.TryGetValue(conceptId, out var list))
            return Array.Empty<PublishedQuestion>();

        return list.Where(q =>
            q.BloomLevel >= minBloom && q.BloomLevel <= maxBloom &&
            q.Difficulty >= minDifficulty && q.Difficulty <= maxDifficulty
        ).ToList();
    }

    public IReadOnlyList<string> GetAvailableConcepts() => _conceptIndex.Keys.ToList();
}
