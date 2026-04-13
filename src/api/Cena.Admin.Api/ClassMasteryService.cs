// =============================================================================
// Cena Platform — Class Mastery Service (MASTERY-002)
// Anonymous class-level mastery statistics with k≥10 anonymity threshold.
//
// Teacher dashboard shows aggregated mastery per topic. No individual
// student names visible. Heatmap: rows=topics, columns=mastery bands.
// =============================================================================

using Cena.Actors.Events;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

/// <summary>
/// MASTERY-002: Provides anonymous class-level mastery statistics.
/// Only returns data when at least <see cref="MinStudentsForAnonymity"/>
/// students have attempted a topic (k-anonymity threshold).
/// </summary>
public sealed class ClassMasteryService
{
    /// <summary>
    /// Minimum number of students who must have attempted a topic before
    /// class-level statistics are shown. Prevents de-anonymization.
    /// </summary>
    public const int MinStudentsForAnonymity = 10;

    private readonly IDocumentStore _store;
    private readonly ILogger<ClassMasteryService> _logger;

    public ClassMasteryService(IDocumentStore store, ILogger<ClassMasteryService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Returns class-level mastery heatmap data for a classroom.
    /// Topics with fewer than k students are suppressed.
    /// </summary>
    public async Task<ClassMasteryHeatmap> GetClassMasteryAsync(
        string classroomId,
        string? schoolId,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Get all student profiles in this classroom's school
        var students = await session.Query<StudentProfileSnapshot>()
            .Where(s => schoolId == null || s.SchoolId == schoolId)
            .ToListAsync(ct);

        // Build per-topic aggregation
        var topicStats = new Dictionary<string, TopicMasteryAggregate>();

        foreach (var student in students)
        {
            foreach (var (conceptId, mastery) in student.ConceptMastery)
            {
                if (!topicStats.TryGetValue(conceptId, out var agg))
                {
                    agg = new TopicMasteryAggregate { TopicId = conceptId };
                    topicStats[conceptId] = agg;
                }

                agg.StudentCount++;
                agg.AddMastery(mastery.PKnown);
            }
        }

        // Filter by anonymity threshold
        var rows = topicStats.Values
            .Where(t => t.StudentCount >= MinStudentsForAnonymity)
            .Select(t => new TopicMasteryRow
            {
                TopicId = t.TopicId,
                StudentCount = t.StudentCount,
                Band0To20 = t.Band0To20,
                Band20To40 = t.Band20To40,
                Band40To60 = t.Band40To60,
                Band60To80 = t.Band60To80,
                Band80To100 = t.Band80To100,
                MeanMastery = t.TotalMastery / t.StudentCount
            })
            .OrderBy(r => r.MeanMastery)
            .ToList();

        var suppressed = topicStats.Count - rows.Count;

        _logger.LogDebug(
            "MASTERY-002: ClassMastery for {ClassroomId}: {TopicCount} topics shown, {Suppressed} suppressed (k<{K})",
            classroomId, rows.Count, suppressed, MinStudentsForAnonymity);

        return new ClassMasteryHeatmap
        {
            ClassroomId = classroomId,
            Rows = rows,
            SuppressedTopicCount = suppressed,
            AnonymityThreshold = MinStudentsForAnonymity,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Aggregation helper for building mastery bands.
/// </summary>
internal sealed class TopicMasteryAggregate
{
    public string TopicId { get; set; } = "";
    public int StudentCount { get; set; }
    public double TotalMastery { get; set; }
    public int Band0To20 { get; set; }
    public int Band20To40 { get; set; }
    public int Band40To60 { get; set; }
    public int Band60To80 { get; set; }
    public int Band80To100 { get; set; }

    public void AddMastery(double pKnown)
    {
        TotalMastery += pKnown;
        var percent = pKnown * 100;
        if (percent < 20) Band0To20++;
        else if (percent < 40) Band20To40++;
        else if (percent < 60) Band40To60++;
        else if (percent < 80) Band60To80++;
        else Band80To100++;
    }
}

public sealed record ClassMasteryHeatmap
{
    public string ClassroomId { get; init; } = "";
    public IReadOnlyList<TopicMasteryRow> Rows { get; init; } = Array.Empty<TopicMasteryRow>();
    public int SuppressedTopicCount { get; init; }
    public int AnonymityThreshold { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record TopicMasteryRow
{
    public string TopicId { get; init; } = "";
    public int StudentCount { get; init; }
    public int Band0To20 { get; init; }
    public int Band20To40 { get; init; }
    public int Band40To60 { get; init; }
    public int Band60To80 { get; init; }
    public int Band80To100 { get; init; }
    public double MeanMastery { get; init; }
}
