// =============================================================================
// Cena Platform — Question Pool Actor
// In-memory per-subject question pool. Hot-reloads on NATS publish events.
// Provides sub-10ms question selection for adaptive serving.
// =============================================================================

using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Serving;

/// <summary>Read-only view of the question pool for the selector.</summary>
public interface IQuestionPool
{
    IReadOnlyList<PublishedQuestion> GetForConcept(string conceptId);
    IReadOnlyList<PublishedQuestion> GetFiltered(string conceptId, int minBloom, int maxBloom, float minDifficulty, float maxDifficulty);
    IReadOnlyList<string> GetAvailableConcepts();
    int ItemCount { get; }
}

/// <summary>
/// Manages the in-memory pool of published questions for a single subject.
/// Loads from PostgreSQL on startup, subscribes to NATS for hot-reload.
/// </summary>
public sealed class QuestionPoolActor : IQuestionPool, IAsyncDisposable
{
    private readonly IDocumentStore _store;
    private readonly INatsConnection _nats;
    private readonly ILogger<QuestionPoolActor> _logger;
    private readonly string _subject;

    // In-memory index: conceptId → questions sorted by (bloom, difficulty)
    private Dictionary<string, List<PublishedQuestion>> _conceptIndex = new();
    private int _totalItems;
    private DateTimeOffset _lastReloadAt;

    public int ItemCount => _totalItems;
    public DateTimeOffset LastReloadAt => _lastReloadAt;

    public QuestionPoolActor(
        IDocumentStore store,
        INatsConnection nats,
        ILogger<QuestionPoolActor> logger,
        string subject)
    {
        _store = store;
        _nats = nats;
        _logger = logger;
        _subject = subject;
    }

    /// <summary>Load all published questions from PostgreSQL into memory.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        var questions = await session.Query<QuestionReadModel>()
            .Where(q => q.Subject == _subject && q.Status == "Published")
            .ToListAsync(ct);

        var index = new Dictionary<string, List<PublishedQuestion>>();

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
                if (!index.TryGetValue(conceptId, out var list))
                {
                    list = new List<PublishedQuestion>();
                    index[conceptId] = list;
                }
                list.Add(published);
            }
        }

        // Sort each concept's questions by bloom level then difficulty
        foreach (var list in index.Values)
        {
            list.Sort((a, b) =>
            {
                var bloomCmp = a.BloomLevel.CompareTo(b.BloomLevel);
                return bloomCmp != 0 ? bloomCmp : a.Difficulty.CompareTo(b.Difficulty);
            });
        }

        _conceptIndex = index;
        _totalItems = questions.Count;
        _lastReloadAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "QuestionPool [{Subject}] loaded {Count} questions across {Concepts} concepts",
            _subject, _totalItems, index.Count);

        // Subscribe to NATS for hot-reload in background
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _nats.SubscribeAsync<byte[]>("cena.serve.item.published", cancellationToken: ct))
                {
                    _logger.LogInformation("QuestionPool [{Subject}] received publish event, reloading...", _subject);
                    await InitializeAsync(CancellationToken.None);
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QuestionPool [{Subject}] NATS subscription error", _subject);
            }
        }, ct);
    }

    /// <summary>Get all questions for a concept, optionally filtered.</summary>
    public IReadOnlyList<PublishedQuestion> GetForConcept(string conceptId)
    {
        return _conceptIndex.TryGetValue(conceptId, out var list)
            ? list
            : Array.Empty<PublishedQuestion>();
    }

    /// <summary>Get questions for a concept filtered by bloom level range and difficulty range.</summary>
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

    /// <summary>Get all concept IDs that have at least one published question.</summary>
    public IReadOnlyList<string> GetAvailableConcepts() => _conceptIndex.Keys.ToList();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Denormalized published question for fast in-memory selection.</summary>
public sealed record PublishedQuestion(
    string ItemId,
    string Subject,
    IReadOnlyList<string> ConceptIds,
    int BloomLevel,
    float Difficulty,
    int QualityScore,
    string Language,
    string StemPreview,
    string SourceType,
    DateTimeOffset PublishedAt,
    string? Explanation);
