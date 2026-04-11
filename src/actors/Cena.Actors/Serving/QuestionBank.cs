// =============================================================================
// Cena Platform — Question Bank Service (HARDEN SessionEndpoints)
// Marten-backed question retrieval for learning sessions
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Serving;

/// <summary>
/// Service interface for querying questions from the question bank.
/// </summary>
public interface IQuestionBank
{
    /// <summary>
    /// Get a question by its ID.
    /// </summary>
    Task<QuestionDocument?> GetQuestionAsync(string questionId, CancellationToken ct = default);
    
    /// <summary>
    /// Get questions by subject.
    /// </summary>
    Task<IReadOnlyList<QuestionDocument>> GetQuestionsBySubjectAsync(string subject, CancellationToken ct = default);
    
    /// <summary>
    /// Get questions by concept ID.
    /// </summary>
    Task<IReadOnlyList<QuestionDocument>> GetQuestionsByConceptAsync(string conceptId, CancellationToken ct = default);
    
    /// <summary>
    /// Get a random question for a subject and difficulty level.
    /// </summary>
    Task<QuestionDocument?> GetRandomQuestionAsync(string subject, string difficulty, CancellationToken ct = default);
}

/// <summary>
/// Marten-backed implementation of IQuestionBank.
/// </summary>
public class QuestionBank : IQuestionBank
{
    private readonly IDocumentStore _store;

    public QuestionBank(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<QuestionDocument?> GetQuestionAsync(string questionId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<QuestionDocument>()
            .FirstOrDefaultAsync(q => q.QuestionId == questionId && q.IsActive, ct);
    }

    public async Task<IReadOnlyList<QuestionDocument>> GetQuestionsBySubjectAsync(string subject, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<QuestionDocument>()
            .Where(q => q.Subject == subject && q.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QuestionDocument>> GetQuestionsByConceptAsync(string conceptId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<QuestionDocument>()
            .Where(q => q.ConceptId == conceptId && q.IsActive)
            .ToListAsync(ct);
    }

    public async Task<QuestionDocument?> GetRandomQuestionAsync(string subject, string difficulty, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        
        // Use random ordering at database level if possible, otherwise fetch and randomize
        var questions = await session.Query<QuestionDocument>()
            .Where(q => q.Subject == subject && q.Difficulty == difficulty && q.IsActive)
            .ToListAsync(ct);
        
        if (questions.Count == 0)
            return null;
        
        var random = new Random();
        return questions[random.Next(questions.Count)];
    }
}
