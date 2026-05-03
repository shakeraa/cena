// =============================================================================
// Cena Platform — Learning Objective Service
// FIND-pedagogy-008: read API for the admin question editor's LO picker.
//
// Pedagogical rationale:
//   - Wiggins & McTighe (2005), "Understanding by Design" 2nd ed. ASCD.
//     ISBN 978-1416600350 — backward design requires every assessment item
//     to trace to an explicit learning goal.
//   - Anderson & Krathwohl (2001), "A Taxonomy for Learning, Teaching, and
//     Assessing." Pearson. ISBN 978-0321084057 — 2-axis revised Bloom's
//     matrix (cognitive-process × knowledge dimension).
//   - Biggs (2003), "Aligning Teaching for Constructing Learning." Higher
//     Education Academy — constructive alignment between learning
//     objectives, teaching activities, and assessment tasks.
//
// Scope: read-only (list + filter + lookup). Full CRUD is explicitly deferred.
// =============================================================================

using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Admin.Api;

public interface ILearningObjectiveService
{
    /// <summary>
    /// List active learning objectives, optionally filtered by subject.
    /// </summary>
    Task<LearningObjectiveListResponse> ListAsync(string? subject = null);

    /// <summary>
    /// Look up a single LO by id. Returns null when not found or inactive.
    /// </summary>
    Task<LearningObjectiveListItem?> GetByIdAsync(string id);
}

/// <summary>
/// Marten-backed implementation. Reads from <see cref="LearningObjectiveDocument"/>.
/// </summary>
public sealed class LearningObjectiveService : ILearningObjectiveService
{
    private readonly IDocumentStore _store;

    public LearningObjectiveService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<LearningObjectiveListResponse> ListAsync(string? subject = null)
    {
        await using var session = _store.QuerySession();

        IQueryable<LearningObjectiveDocument> query =
            session.Query<LearningObjectiveDocument>().Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(subject))
            query = query.Where(x => x.Subject == subject);

        var docs = await query
            .OrderBy(x => x.Subject)
            .ThenBy(x => x.Code)
            .ToListAsync();

        var items = docs.Select(Map).ToList();
        return new LearningObjectiveListResponse(items, items.Count);
    }

    public async Task<LearningObjectiveListItem?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<LearningObjectiveDocument>(id);
        if (doc == null || !doc.IsActive) return null;
        return Map(doc);
    }

    private static LearningObjectiveListItem Map(LearningObjectiveDocument doc) =>
        new(
            Id: doc.Id,
            Code: doc.Code,
            Title: doc.Title,
            Description: doc.Description,
            Subject: doc.Subject,
            Grade: doc.Grade,
            CognitiveProcess: doc.CognitiveProcess.ToString(),
            KnowledgeType: doc.KnowledgeType.ToString(),
            BloomsLevel: (int)doc.CognitiveProcess,
            ConceptIds: doc.ConceptIds.ToList(),
            StandardsAlignment:
                new Dictionary<string, string>(doc.StandardsAlignment));
}
