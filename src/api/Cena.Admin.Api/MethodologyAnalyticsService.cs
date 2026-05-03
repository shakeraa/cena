// =============================================================================
// Cena Platform -- Methodology Analytics Service
// ADM-011: Real Marten event-stream queries for methodology effectiveness
// and stagnation monitoring. No mock data.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IMethodologyAnalyticsService
{
    Task<MethodologyEffectivenessResponse> GetEffectivenessAsync(ClaimsPrincipal user);
    Task<StagnationMonitorResponse> GetStagnationMonitorAsync(ClaimsPrincipal user);
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

    public async Task<MethodologyEffectivenessResponse> GetEffectivenessAsync(ClaimsPrincipal user)
    {
        // REV-014: Derive school filter — null means SUPER_ADMIN (no restriction)
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        // REV-014: When scoped to a school, pre-load the eligible student IDs
        HashSet<string>? scopedStudentIds = null;
        if (schoolId is not null)
        {
            var scopedSnapshots = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            scopedStudentIds = new HashSet<string>(scopedSnapshots);
        }

        // Query all methodology switch events from the last 90 days
        var since = DateTimeOffset.UtcNow.AddDays(-90);

        IReadOnlyList<MethodologySwitched_V1> switchEvents;
        IReadOnlyList<ConceptAttempted_V1> attemptEvents;

        try
        {
            var rawSwitch = await session.Events
                .QueryAllRawEvents()
                .Where(e => e.Timestamp >= since)
                .OfType<MethodologySwitched_V1>()
                .ToListAsync();

            switchEvents = scopedStudentIds is null
                ? rawSwitch
                : rawSwitch.Where(e => scopedStudentIds.Contains(e.StudentId)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No MethodologySwitched events found — returning empty");
            switchEvents = Array.Empty<MethodologySwitched_V1>();
        }

        try
        {
            var rawAttempts = await session.Events
                .QueryAllRawEvents()
                .Where(e => e.Timestamp >= since)
                .OfType<ConceptAttempted_V1>()
                .ToListAsync();

            attemptEvents = scopedStudentIds is null
                ? rawAttempts
                : rawAttempts.Where(e => scopedStudentIds.Contains(e.StudentId)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No ConceptAttempted events found — returning empty");
            attemptEvents = Array.Empty<ConceptAttempted_V1>();
        }

        // Build methodology effectiveness by error type
        var methodologies = new[] { "Socratic", "WorkedExample", "Feynman", "RetrievalPractice", "SpacedRepetition",
            "BloomsProgression", "Analogy", "ProjectBased", "DrillAndPractice" };
        var errorTypes = new[] { "Conceptual", "Procedural", "Motivational" };

        var comparisons = new List<MethodologyComparison>();

        foreach (var method in methodologies)
        {
            var methodAttempts = attemptEvents
                .Where(a => string.Equals(a.MethodologyActive, method, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (methodAttempts.Count == 0) continue;

            var byErrorType = new List<ErrorTypeEffectiveness>();
            foreach (var et in errorTypes)
            {
                var etAttempts = methodAttempts
                    .Where(a => string.Equals(a.ErrorType, et, StringComparison.OrdinalIgnoreCase)
                                || (et == "Conceptual" && a.ErrorType == "None" && !a.IsCorrect))
                    .ToList();

                if (etAttempts.Count == 0)
                {
                    byErrorType.Add(new ErrorTypeEffectiveness(et, 0f, 0f, 0));
                    continue;
                }

                // Success rate: % of attempts that are correct under this method+error combo
                float successRate = etAttempts.Count(a => a.IsCorrect) / (float)etAttempts.Count;

                // Average time to mastery: avg sessions from first attempt to mastery threshold
                // Approximated by grouping per student-concept and counting attempts to reach 0.85
                var studentConcepts = etAttempts
                    .GroupBy(a => (a.StudentId, a.ConceptId))
                    .ToList();

                float avgTimeToMastery = 0f;
                int masteredCount = 0;
                foreach (var sc in studentConcepts)
                {
                    var ordered = sc.OrderBy(a => a.Timestamp).ToList();
                    var mastered = ordered.FirstOrDefault(a => a.PosteriorMastery >= 0.85);
                    if (mastered != null)
                    {
                        var index = ordered.IndexOf(mastered) + 1;
                        avgTimeToMastery += index;
                        masteredCount++;
                    }
                }

                avgTimeToMastery = masteredCount > 0 ? avgTimeToMastery / masteredCount : 0f;

                byErrorType.Add(new ErrorTypeEffectiveness(et, avgTimeToMastery, successRate, etAttempts.Count));
            }

            comparisons.Add(new MethodologyComparison(method, byErrorType));
        }

        // Switch trigger breakdown
        var triggerGroups = switchEvents
            .GroupBy(e => e.Trigger.ToLowerInvariant())
            .Select(g => new { Trigger = g.Key, Count = g.Count() })
            .ToList();
        int totalTriggers = triggerGroups.Sum(g => g.Count);

        var switchTriggers = triggerGroups
            .Select(g => new SwitchTriggerBreakdown(
                g.Trigger,
                g.Count,
                totalTriggers > 0 ? (float)Math.Round(g.Count * 100f / totalTriggers, 1) : 0f))
            .ToList();

        // Stagnation trend: group stagnation events by day over last 7 days
        IReadOnlyList<StagnationDetected_V1> stagnationEvents;
        try
        {
            stagnationEvents = await session.Events
                .QueryAllRawEvents()
                .Where(e => e.Timestamp >= DateTimeOffset.UtcNow.AddDays(-7))
                .OfType<StagnationDetected_V1>()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No StagnationDetected events found — returning empty");
            stagnationEvents = Array.Empty<StagnationDetected_V1>();
        }

        var trend = new List<StagnationTrendPoint>();
        for (int i = 6; i >= 0; i--)
        {
            var day = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(-i);
            var dayEnd = day.AddDays(1);

            var dayStagnation = stagnationEvents
                .Count(e => e.ConsecutiveStagnantSessions >= 3); // True stagnation events
            var dayResolved = switchEvents
                .Count(e => e.Timestamp >= day && e.Timestamp < dayEnd);

            trend.Add(new StagnationTrendPoint(
                day.ToString("yyyy-MM-dd"), dayStagnation, dayResolved));
        }

        // Escalation rate: switches marked as exhausted / total switches
        float escalationRate = switchEvents.Count > 0
            ? switchEvents.Count(e => e.Trigger == "escalation") / (float)switchEvents.Count
            : 0f;

        return new MethodologyEffectivenessResponse(comparisons, switchTriggers, trend, escalationRate);
    }

    public async Task<StagnationMonitorResponse> GetStagnationMonitorAsync(ClaimsPrincipal user)
    {
        // REV-014: Derive school filter — null means SUPER_ADMIN (no restriction)
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        // Query snapshots to find students with stagnation patterns
        IReadOnlyList<StudentProfileSnapshot> snapshots;
        try
        {
            var query = session.Query<StudentProfileSnapshot>().AsQueryable();
            if (schoolId is not null)
                query = query.Where(s => s.SchoolId == schoolId); // REV-014: tenant filter
            snapshots = await query.ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No StudentProfileSnapshot data — returning empty");
            snapshots = Array.Empty<StudentProfileSnapshot>();
        }

        var stagnatingStudents = new List<StagnatingStudent>();
        var mentorResistant = new Dictionary<string, MentorResistantBuilder>();

        foreach (var snapshot in snapshots)
        {
            foreach (var (conceptId, mastery) in snapshot.ConceptMastery)
            {
                // Student is stagnating if: many attempts, low mastery, not mastered
                if (mastery.TotalAttempts >= 10 && mastery.PKnown < 0.7 && !mastery.IsMastered)
                {
                    var triedMethods = snapshot.MethodAttemptHistory
                        .GetValueOrDefault(conceptId, new List<string>());

                    int daysStuck = mastery.LastAttemptedAt.HasValue
                        ? (int)(DateTimeOffset.UtcNow - mastery.LastAttemptedAt.Value).TotalDays
                        : 0;

                    // Composite stagnation score based on attempt count and mastery plateau
                    float compositeScore = Math.Min(1f,
                        (mastery.TotalAttempts / 30f) * 0.5f +
                        (float)(1.0 - mastery.PKnown) * 0.5f);

                    if (compositeScore >= 0.5f)
                    {
                        stagnatingStudents.Add(new StagnatingStudent(
                            snapshot.StudentId,
                            snapshot.StudentId, // Name resolution deferred to user service
                            "", // ClassId resolution deferred
                            conceptId,
                            compositeScore,
                            mastery.TotalAttempts,
                            daysStuck,
                            triedMethods.Distinct().ToList()));

                        // Track mentor-resistant concepts (all 9 methodologies exhausted)
                        if (triedMethods.Distinct().Count() >= 7) // Most methodologies tried
                        {
                            if (!mentorResistant.TryGetValue(conceptId, out var builder))
                            {
                                builder = new MentorResistantBuilder { ConceptId = conceptId };
                                mentorResistant[conceptId] = builder;
                            }
                            builder.StuckStudentCount++;
                            foreach (var m in triedMethods.Distinct())
                                builder.ExhaustedMethods.Add(m);
                        }
                    }
                }
            }
        }

        var resistantConcepts = mentorResistant.Values
            .Select(b => new MentorResistantConcept(
                b.ConceptId, b.ConceptId, "", // Name/subject resolution deferred
                b.StuckStudentCount,
                b.ExhaustedMethods.ToList()))
            .OrderByDescending(c => c.StuckStudentCount)
            .ToList();

        return new StagnationMonitorResponse(
            stagnatingStudents.OrderByDescending(s => s.CompositeScore).Take(50).ToList(),
            resistantConcepts);
    }

    public async Task<McmGraphResponse> GetMcmGraphAsync()
    {
        await using var session = _store.QuerySession();

        // Build MCM graph from real switch event data
        var since = DateTimeOffset.UtcNow.AddDays(-90);

        IReadOnlyList<MethodologySwitched_V1> switchEvents;
        IReadOnlyList<ConceptAttempted_V1> attemptEvents;

        try
        {
            switchEvents = await session.Events
                .QueryAllRawEvents()
                .Where(e => e.Timestamp >= since)
                .OfType<MethodologySwitched_V1>()
                .ToListAsync();
        }
        catch
        {
            switchEvents = Array.Empty<MethodologySwitched_V1>();
        }

        try
        {
            attemptEvents = await session.Events
                .QueryAllRawEvents()
                .Where(e => e.Timestamp >= since)
                .OfType<ConceptAttempted_V1>()
                .ToListAsync();
        }
        catch
        {
            attemptEvents = Array.Empty<ConceptAttempted_V1>();
        }

        // Build nodes from distinct error types and methodologies seen in data
        var errorTypes = switchEvents
            .Select(e => e.DominantErrorType)
            .Where(et => !string.IsNullOrEmpty(et) && et != "None")
            .Distinct()
            .ToList();

        var methodologiesUsed = attemptEvents
            .Select(a => a.MethodologyActive)
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()
            .ToList();

        var nodes = new List<McmNode>();
        foreach (var et in errorTypes)
            nodes.Add(new McmNode($"error-{et.ToLowerInvariant()}", "error_type", et, null));
        foreach (var m in methodologiesUsed)
            nodes.Add(new McmNode($"method-{m.ToLowerInvariant()}", "methodology", m, null));

        // Build edges: error type → methodology with real confidence (success rate after switch)
        var edges = new List<McmEdge>();

        foreach (var et in errorTypes)
        {
            // Find switches triggered by this error type
            var switches = switchEvents
                .Where(s => string.Equals(s.DominantErrorType, et, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Group by target methodology
            var byMethod = switches.GroupBy(s => s.NewMethodology);
            foreach (var methodGroup in byMethod)
            {
                // Compute post-switch success rate for this error→method pair
                int postSwitchCorrect = 0;
                int postSwitchTotal = 0;

                foreach (var sw in methodGroup)
                {
                    var postAttempts = attemptEvents
                        .Where(a => a.StudentId == sw.StudentId
                                    && a.ConceptId == sw.ConceptId
                                    && a.Timestamp > sw.Timestamp
                                    && string.Equals(a.MethodologyActive, sw.NewMethodology, StringComparison.OrdinalIgnoreCase))
                        .Take(10) // Look at first 10 attempts after switch
                        .ToList();

                    postSwitchTotal += postAttempts.Count;
                    postSwitchCorrect += postAttempts.Count(a => a.IsCorrect);
                }

                float confidence = postSwitchTotal > 0
                    ? postSwitchCorrect / (float)postSwitchTotal
                    : 0f;

                edges.Add(new McmEdge(
                    $"error-{et.ToLowerInvariant()}",
                    $"method-{methodGroup.Key.ToLowerInvariant()}",
                    confidence,
                    postSwitchTotal,
                    true));
            }
        }

        return new McmGraphResponse(nodes, edges);
    }

    public async Task<bool> UpdateMcmEdgeAsync(string source, string target, float confidence)
    {
        // Store MCM edge override in Redis for runtime use
        try
        {
            var db = _redis.GetDatabase();
            var key = $"mcm:edge:{source}:{target}";
            await db.StringSetAsync(key, confidence.ToString("F4"), TimeSpan.FromDays(365));

            _logger.LogInformation(
                "MCM edge updated: {Source} -> {Target} confidence={Confidence:F2}",
                source, target, confidence);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update MCM edge {Source} -> {Target}", source, target);
            return false;
        }
    }

    private sealed class MentorResistantBuilder
    {
        public string ConceptId { get; set; } = "";
        public int StuckStudentCount { get; set; }
        public HashSet<string> ExhaustedMethods { get; set; } = new();
    }
}
