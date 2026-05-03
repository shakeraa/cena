// =============================================================================
// Cena Platform — Content Catalog Service (STB-08b)
// Loads and queries concept graph and learning paths from JSON seed
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Content;

/// <summary>
/// Service for managing content catalog and concept graph.
/// </summary>
public interface IContentCatalogService
{
    /// <summary>
    /// Seed the catalog from JSON file if empty.
    /// </summary>
    Task SeedFromJsonAsync(string jsonPath, CancellationToken ct = default);
    
    /// <summary>
    /// Get all concepts for a subject.
    /// </summary>
    Task<IReadOnlyList<ConceptDocument>> GetConceptsBySubjectAsync(
        string subject, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Get concept by ID.
    /// </summary>
    Task<ConceptDocument?> GetConceptAsync(string conceptId, CancellationToken ct = default);
    
    /// <summary>
    /// Get concept graph with prerequisites and successors.
    /// </summary>
    Task<ConceptGraph?> GetConceptGraphAsync(
        string conceptId, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Get all learning paths for a subject.
    /// </summary>
    Task<IReadOnlyList<LearningPathDocument>> GetLearningPathsAsync(
        string? subject = null,
        string? grade = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get learning path with full concept details.
    /// </summary>
    Task<LearningPathDetail?> GetLearningPathDetailAsync(
        string pathId, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Find concepts by topic/tag.
    /// </summary>
    Task<IReadOnlyList<ConceptDocument>> SearchConceptsAsync(
        string query,
        string? subject = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get Bagrut-relevant concepts sorted by frequency.
    /// </summary>
    Task<IReadOnlyList<ConceptDocument>> GetBagrutConceptsAsync(
        string? subject = null,
        int limit = 20,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation using Marten for persistence.
/// </summary>
public class ContentCatalogService : IContentCatalogService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<ContentCatalogService> _logger;

    public ContentCatalogService(
        IDocumentStore store,
        ILogger<ContentCatalogService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task SeedFromJsonAsync(string jsonPath, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        
        // Check if already seeded
        var existingCount = await session.Query<ConceptDocument>().CountAsync();
        if (existingCount > 0)
        {
            _logger.LogInformation("Content catalog already seeded with {Count} concepts", existingCount);
            return;
        }

        if (!File.Exists(jsonPath))
        {
            _logger.LogWarning("Seed file not found: {Path}", jsonPath);
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        var seedData = JsonSerializer.Deserialize<ContentSeedData>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (seedData == null)
        {
            _logger.LogError("Failed to deserialize seed data");
            return;
        }

        // Seed concepts
        foreach (var entry in seedData.Concepts)
        {
            var concept = new ConceptDocument
            {
                Id = $"concept/{entry.ConceptId}",
                ConceptId = entry.ConceptId,
                Name = entry.Name,
                Subject = entry.Subject,
                Description = entry.Description,
                ParentConceptIds = entry.Parents ?? new List<string>(),
                PrerequisiteIds = entry.Prerequisites ?? new List<string>(),
                SuccessorIds = new List<string>(), // Will be populated after all concepts loaded
                Difficulty = entry.Difficulty,
                Topics = entry.Topics ?? new List<string>(),
                IsInBagrut = entry.IsInBagrut,
                BagrutFrequencyScore = entry.BagrutFrequency,
                CreatedAt = DateTime.UtcNow
            };
            session.Store(concept);
        }

        await session.SaveChangesAsync(ct);

        // Build successor relationships (reverse of prerequisites)
        await BuildSuccessorRelationships(ct);

        // Seed learning paths
        foreach (var entry in seedData.LearningPaths)
        {
            var pathConcepts = new List<PathConcept>();
            for (int i = 0; i < entry.ConceptIds.Count; i++)
            {
                pathConcepts.Add(new PathConcept
                {
                    ConceptId = entry.ConceptIds[i],
                    SequenceOrder = i + 1,
                    IsRequired = true
                });
            }

            var path = new LearningPathDocument
            {
                Id = $"path/{entry.PathId}",
                PathId = entry.PathId,
                Name = entry.Name,
                Description = entry.Description,
                Subject = entry.Subject,
                TargetGrade = entry.TargetGrade,
                Concepts = pathConcepts,
                EstimatedHours = entry.EstimatedHours,
                Difficulty = entry.Difficulty,
                IsPublished = true,
                CreatedAt = DateTime.UtcNow
            };
            session.Store(path);
        }

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seeded content catalog: {ConceptCount} concepts, {PathCount} learning paths",
            seedData.Concepts.Count,
            seedData.LearningPaths.Count);
    }

    public async Task<IReadOnlyList<ConceptDocument>> GetConceptsBySubjectAsync(
        string subject, 
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var concepts = await session.Query<ConceptDocument>()
            .Where(c => c.Subject == subject)
            .OrderBy(c => c.Difficulty)
            .ToListAsync(ct);
        return concepts;
    }

    public async Task<ConceptDocument?> GetConceptAsync(
        string conceptId, 
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ConceptDocument>()
            .FirstOrDefaultAsync(c => c.ConceptId == conceptId, ct);
    }

    public async Task<ConceptGraph?> GetConceptGraphAsync(
        string conceptId, 
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        
        var concept = await GetConceptAsync(conceptId, ct);
        if (concept == null) return null;

        // Load prerequisites
        var prerequisites = new List<ConceptDocument>();
        foreach (var prereqId in concept.PrerequisiteIds)
        {
            var prereq = await GetConceptAsync(prereqId, ct);
            if (prereq != null) prerequisites.Add(prereq);
        }

        // Load successors
        var successors = new List<ConceptDocument>();
        foreach (var succId in concept.SuccessorIds)
        {
            var succ = await GetConceptAsync(succId, ct);
            if (succ != null) successors.Add(succ);
        }

        // Load parents
        var parents = new List<ConceptDocument>();
        foreach (var parentId in concept.ParentConceptIds)
        {
            var parent = await GetConceptAsync(parentId, ct);
            if (parent != null) parents.Add(parent);
        }

        return new ConceptGraph
        {
            Concept = concept,
            Prerequisites = prerequisites,
            Successors = successors,
            Parents = parents
        };
    }

    public async Task<IReadOnlyList<LearningPathDocument>> GetLearningPathsAsync(
        string? subject = null,
        string? grade = null,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var query = session.Query<LearningPathDocument>()
            .Where(p => p.IsPublished);

        if (!string.IsNullOrEmpty(subject))
            query = query.Where(p => p.Subject == subject);

        if (!string.IsNullOrEmpty(grade))
            query = query.Where(p => p.TargetGrade == grade);

        return await query.OrderBy(p => p.Difficulty).ToListAsync(ct);
    }

    public async Task<LearningPathDetail?> GetLearningPathDetailAsync(
        string pathId, 
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        
        var path = await session.Query<LearningPathDocument>()
            .FirstOrDefaultAsync(p => p.PathId == pathId, ct);
        
        if (path == null) return null;

        var conceptDetails = new List<PathConceptDetail>();
        foreach (var pc in path.Concepts.OrderBy(c => c.SequenceOrder))
        {
            var concept = await GetConceptAsync(pc.ConceptId, ct);
            if (concept != null)
            {
                conceptDetails.Add(new PathConceptDetail
                {
                    Concept = concept,
                    SequenceOrder = pc.SequenceOrder,
                    IsRequired = pc.IsRequired,
                    UnlockCondition = pc.UnlockCondition
                });
            }
        }

        return new LearningPathDetail
        {
            Path = path,
            Concepts = conceptDetails
        };
    }

    public async Task<IReadOnlyList<ConceptDocument>> SearchConceptsAsync(
        string query,
        string? subject = null,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var lowerQuery = query.ToLowerInvariant();
        
        var concepts = await session.Query<ConceptDocument>()
            .Where(c => 
                c.Name.ToLower().Contains(lowerQuery) ||
                (c.Description != null && c.Description.ToLower().Contains(lowerQuery)) ||
                c.Topics.Any(t => t.ToLower().Contains(lowerQuery)))
            .ToListAsync(ct);

        if (!string.IsNullOrEmpty(subject))
            concepts = concepts.Where(c => c.Subject == subject).ToList();

        return concepts;
    }

    public async Task<IReadOnlyList<ConceptDocument>> GetBagrutConceptsAsync(
        string? subject = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var query = session.Query<ConceptDocument>()
            .Where(c => c.IsInBagrut);

        if (!string.IsNullOrEmpty(subject))
            query = query.Where(c => c.Subject == subject);

        return await query
            .OrderByDescending(c => c.BagrutFrequencyScore)
            .Take(limit)
            .ToListAsync(ct);
    }

    private async Task BuildSuccessorRelationships(CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var concepts = await session.Query<ConceptDocument>().ToListAsync(ct);

        // Build successor map (reverse of prerequisites)
        var successorMap = new Dictionary<string, List<string>>();
        foreach (var concept in concepts)
        {
            foreach (var prereqId in concept.PrerequisiteIds)
            {
                if (!successorMap.ContainsKey(prereqId))
                    successorMap[prereqId] = new List<string>();
                successorMap[prereqId].Add(concept.ConceptId);
            }
        }

        // Update concepts with successors
        foreach (var concept in concepts)
        {
            if (successorMap.TryGetValue(concept.ConceptId, out var successors))
            {
                concept.SuccessorIds = successors;
                session.Store(concept);
            }
        }

        await session.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Concept with its graph relationships.
/// </summary>
public class ConceptGraph
{
    public ConceptDocument Concept { get; set; } = null!;
    public IReadOnlyList<ConceptDocument> Prerequisites { get; set; } = new List<ConceptDocument>();
    public IReadOnlyList<ConceptDocument> Successors { get; set; } = new List<ConceptDocument>();
    public IReadOnlyList<ConceptDocument> Parents { get; set; } = new List<ConceptDocument>();
}

/// <summary>
/// Learning path with full concept details.
/// </summary>
public class LearningPathDetail
{
    public LearningPathDocument Path { get; set; } = null!;
    public IReadOnlyList<PathConceptDetail> Concepts { get; set; } = new List<PathConceptDetail>();
}

public class PathConceptDetail
{
    public ConceptDocument Concept { get; set; } = null!;
    public int SequenceOrder { get; set; }
    public bool IsRequired { get; set; }
    public string? UnlockCondition { get; set; }
}
