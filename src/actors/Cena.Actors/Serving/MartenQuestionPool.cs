// =============================================================================
// Cena Platform — Marten-backed Question Pool (FIND-pedagogy-016)
// On-demand IQuestionPool for the REST host. Loads published questions from
// Marten/PostgreSQL by subject(s) without requiring NATS hot-reload.
// Used by AdaptiveQuestionPool in the Student API Host context.
// =============================================================================

using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Serving;

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
    /// Load published questions for the given subjects from Marten.
    /// </summary>
    public static async Task<MartenQuestionPool> LoadAsync(
        IDocumentStore store,
        string[] subjects,
        ILogger logger,
        CancellationToken ct = default)
    {
        var pool = new MartenQuestionPool();
        await using var session = store.QuerySession();

        foreach (var subject in subjects)
        {
            var questions = await session.Query<QuestionReadModel>()
                .Where(q => q.Subject == subject && q.Status == "Published")
                .ToListAsync(ct);

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
            "MartenQuestionPool loaded {Count} questions across {Concepts} concepts for subjects [{Subjects}]",
            pool._totalItems, pool._conceptIndex.Count, string.Join(", ", subjects));

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
