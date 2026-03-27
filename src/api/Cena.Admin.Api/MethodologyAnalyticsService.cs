// =============================================================================
// Cena Platform -- Methodology Analytics Service
// ADM-011: Methodology effectiveness and stagnation monitoring
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IMethodologyAnalyticsService
{
    Task<MethodologyEffectivenessResponse> GetEffectivenessAsync();
    Task<StagnationMonitorResponse> GetStagnationMonitorAsync();
    Task<McmGraphResponse> GetMcmGraphAsync();
    Task<bool> UpdateMcmEdgeAsync(string source, string target, float confidence);
}

public sealed class MethodologyAnalyticsService : IMethodologyAnalyticsService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MethodologyAnalyticsService> _logger;

    public MethodologyAnalyticsService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<MethodologyAnalyticsService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<MethodologyEffectivenessResponse> GetEffectivenessAsync()
    {
        var random = new Random(42);
        var methodologies = new[] { "Socratic", "WorkedExample", "Feynman", "RetrievalPractice", "SpacedRepetition" };
        var errorTypes = new[] { "Conceptual", "Procedural", "Motivational" };

        var comparisons = new List<MethodologyComparison>();
        foreach (var method in methodologies)
        {
            var byErrorType = errorTypes.Select(et => new ErrorTypeEffectiveness(
                et,
                random.Next(3, 15) + random.NextSingle() * 2f,
                0.4f + random.NextSingle() * 0.5f,
                random.Next(50, 500))).ToList();

            comparisons.Add(new MethodologyComparison(method, byErrorType));
        }

        var switchTriggers = new List<SwitchTriggerBreakdown>
        {
            new("stagnation", random.Next(200, 500), 0),
            new("student_requested", random.Next(50, 150), 0),
            new("mcm_recommendation", random.Next(100, 300), 0)
        };
        var totalTriggers = switchTriggers.Sum(t => t.Count);
        switchTriggers = switchTriggers.Select(t => t with { Percentage = (float)Math.Round(t.Count * 100f / totalTriggers, 1) }).ToList();

        var trend = new List<StagnationTrendPoint>();
        for (int i = 6; i >= 0; i--)
        {
            trend.Add(new StagnationTrendPoint(
                DateTimeOffset.UtcNow.AddDays(-i).ToString("yyyy-MM-dd"),
                random.Next(10, 50),
                random.Next(5, 30)));
        }

        return new MethodologyEffectivenessResponse(
            comparisons,
            switchTriggers,
            trend,
            0.05f + random.NextSingle() * 0.05f);
    }

    public async Task<StagnationMonitorResponse> GetStagnationMonitorAsync()
    {
        var random = new Random(42);
        var students = new List<StagnatingStudent>();

        for (int i = 0; i < 10; i++)
        {
            students.Add(new StagnatingStudent(
                $"stu-stag-{i}",
                $"Student {i + 1}",
                $"class-{i % 3 + 1}",
                $"Concept Cluster {i % 5 + 1}",
                0.6f + random.NextSingle() * 0.3f,
                random.Next(5, 20),
                random.Next(2, 10),
                new[] { "Socratic", "WorkedExample" }.ToList()));
        }

        var resistant = new List<MentorResistantConcept>
        {
            new("calc-003", "Advanced Integration", "Math", 12, new[] { "Socratic", "WorkedExample", "Feynman", "RetrievalPractice" }.ToList()),
            new("phy-004", "Electromagnetic Induction", "Physics", 8, new[] { "Socratic", "WorkedExample", "Feynman" }.ToList())
        };

        return new StagnationMonitorResponse(students, resistant);
    }

    public async Task<McmGraphResponse> GetMcmGraphAsync()
    {
        var nodes = new List<McmNode>
        {
            // Error types
            new("error-conceptual", "error_type", "Conceptual", null),
            new("error-procedural", "error_type", "Procedural", null),
            new("error-motivational", "error_type", "Motivational", null),
            // Concept categories
            new("cat-algebra", "concept_category", "Algebra", "Math"),
            new("cat-calculus", "concept_category", "Calculus", "Math"),
            new("cat-kinematics", "concept_category", "Kinematics", "Physics"),
            // Methodologies
            new("method-socratic", "methodology", "Socratic", null),
            new("method-worked", "methodology", "Worked Example", null),
            new("method-feynman", "methodology", "Feynman", null),
            new("method-retrieval", "methodology", "Retrieval Practice", null)
        };

        var edges = new List<McmEdge>
        {
            new("error-conceptual", "method-feynman", 0.85f, 450, true),
            new("error-conceptual", "method-socratic", 0.75f, 380, true),
            new("error-procedural", "method-worked", 0.90f, 520, true),
            new("error-procedural", "method-retrieval", 0.70f, 340, true),
            new("error-motivational", "method-retrieval", 0.80f, 290, true),
            new("cat-algebra", "method-worked", 0.78f, 410, true),
            new("cat-calculus", "method-socratic", 0.82f, 350, true),
            new("cat-kinematics", "method-feynman", 0.88f, 280, true)
        };

        return new McmGraphResponse(nodes, edges);
    }

    public async Task<bool> UpdateMcmEdgeAsync(string source, string target, float confidence)
    {
        _logger.LogInformation("Updating MCM edge {Source} -> {Target} to confidence {Confidence}", source, target, confidence);
        return true;
    }
}
