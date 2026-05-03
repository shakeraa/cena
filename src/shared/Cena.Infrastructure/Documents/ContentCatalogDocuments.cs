// =============================================================================
// Cena Platform — Content Catalog Documents (STB-08b)
// Concept graph and content catalog for knowledge organization
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// A concept in the knowledge graph (e.g., "Linear Equations", "Newton's Laws").
/// </summary>
public class ConceptDocument
{
    public string Id { get; set; } = "";
    public string ConceptId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    
    /// <summary>
    /// Parent concept IDs for hierarchical organization.
    /// </summary>
    public List<string> ParentConceptIds { get; set; } = new();
    
    /// <summary>
    /// Prerequisite concept IDs - must master these before this concept.
    /// </summary>
    public List<string> PrerequisiteIds { get; set; } = new();
    
    /// <summary>
    /// Child/successor concepts that build on this one.
    /// </summary>
    public List<string> SuccessorIds { get; set; } = new();
    
    /// <summary>
    /// Estimated difficulty 0.0-1.0
    /// </summary>
    public double Difficulty { get; set; }
    
    /// <summary>
    /// Approximate questions needed to master
    /// </summary>
    public int EstimatedQuestionsToMaster { get; set; } = 10;
    
    /// <summary>
    /// Topics/tags for filtering
    /// </summary>
    public List<string> Topics { get; set; } = new();
    
    /// <summary>
    /// Bagrut relevance (Israeli matriculation exam)
    /// </summary>
    public bool IsInBagrut { get; set; }
    
    /// <summary>
    /// Bagrut exam frequency score (0-100)
    /// </summary>
    public int BagrutFrequencyScore { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Learning path - curated sequence of concepts for a goal.
/// </summary>
public class LearningPathDocument
{
    public string Id { get; set; } = "";
    public string PathId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subject { get; set; } = "";
    public string TargetGrade { get; set; } = "";
    
    /// <summary>
    /// Ordered list of concept IDs in the path.
    /// </summary>
    public List<PathConcept> Concepts { get; set; } = new();
    
    /// <summary>
    /// Estimated hours to complete
    /// </summary>
    public int EstimatedHours { get; set; }
    
    /// <summary>
    /// Difficulty level: beginner | intermediate | advanced
    /// </summary>
    public string Difficulty { get; set; } = "beginner";
    
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A concept within a learning path with sequence info.
/// </summary>
public class PathConcept
{
    public string ConceptId { get; set; } = "";
    public int SequenceOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? UnlockCondition { get; set; }
}

/// <summary>
/// Content catalog seed data entry for JSON import.
/// </summary>
public class ContentSeedData
{
    public List<ConceptSeedEntry> Concepts { get; set; } = new();
    public List<LearningPathSeedEntry> LearningPaths { get; set; } = new();
}

public class ConceptSeedEntry
{
    public string ConceptId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Parents { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();
    public double Difficulty { get; set; }
    public List<string> Topics { get; set; } = new();
    public bool IsInBagrut { get; set; }
    public int BagrutFrequency { get; set; }
}

public class LearningPathSeedEntry
{
    public string PathId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Subject { get; set; } = "";
    public string TargetGrade { get; set; } = "";
    public List<string> ConceptIds { get; set; } = new();
    public int EstimatedHours { get; set; }
    public string Difficulty { get; set; } = "beginner";
}
