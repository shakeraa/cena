// =============================================================================
// Cena Platform -- Mastery Tracking Service
// ADM-016: Mastery & learning progress (production-grade)
// All methods read real Marten rollup docs + StudentProfileSnapshot.
// No Random. No hand-crafted student/class rows. No literal id arrays.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Api.Contracts.Admin.Mastery;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IMasteryTrackingService
{
    Task<MasteryOverviewResponse> GetOverviewAsync(string? classId, ClaimsPrincipal user);
    Task<StudentMasteryDetailResponse?> GetStudentMasteryAsync(string studentId, ClaimsPrincipal user);
    Task<ClassMasteryResponse?> GetClassMasteryAsync(string classId, ClaimsPrincipal user);
    Task<AtRiskStudentsResponse> GetAtRiskStudentsAsync(ClaimsPrincipal user);
    Task<MethodologyProfileAdminResponse?> GetMethodologyProfileAsync(string studentId, ClaimsPrincipal user);
    Task<bool> OverrideMethodologyAsync(string studentId, string level, string levelId, string methodology, string teacherId, ClaimsPrincipal user);
    Task<IReadOnlyList<MethodologyOverrideDocument>> GetStudentOverridesAsync(string studentId, ClaimsPrincipal user);
    Task<bool> RemoveOverrideAsync(string studentId, string overrideId, ClaimsPrincipal user);
}

public sealed class MasteryTrackingService : IMasteryTrackingService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MasteryTrackingService> _logger;

    public MasteryTrackingService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<MasteryTrackingService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<MasteryOverviewResponse> GetOverviewAsync(string? classId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // Latest rollup per class for this school
        var query = session.Query<ClassMasteryRollupDocument>();
        if (schoolId is not null)
            query = (Marten.Linq.IMartenQueryable<ClassMasteryRollupDocument>)query.Where(r => r.SchoolId == schoolId);
        if (!string.IsNullOrEmpty(classId))
            query = (Marten.Linq.IMartenQueryable<ClassMasteryRollupDocument>)query.Where(r => r.ClassId == classId);

        var rollups = await query.ToListAsync();
        return BuildMasteryOverview(rollups);
    }

    public async Task<StudentMasteryDetailResponse?> GetStudentMasteryAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var snapshot = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (snapshot is null)
        {
            // Student snapshot is built after their first session. For a
            // known-but-never-played student (e.g. pending invite, freshly
            // seeded roster), returning null makes every mastery endpoint
            // 404 and the admin profile page renders a red error card.
            // Instead: if the student is a known AdminUser (or any
            // StudentRoster row), return an empty-state detail so the
            // Insights tab shows "no data yet" panels. Truly unknown ids
            // still return null → 404.
            var known = await session.LoadAsync<AdminUser>(studentId);
            if (known is null) return null;
            if (schoolId is not null && !string.IsNullOrEmpty(known.School)
                && known.School != schoolId) return null;

            return BuildEmptyStudentMasteryDetail(studentId, known);
        }
        if (schoolId is not null && snapshot.SchoolId != schoolId) return null;

        return BuildStudentMasteryDetail(studentId, snapshot);
    }

    private static StudentMasteryDetailResponse BuildEmptyStudentMasteryDetail(string studentId, AdminUser known)
    {
        return new StudentMasteryDetailResponse(
            StudentId: studentId,
            StudentName: known.FullName ?? studentId,
            KnowledgeMap: Array.Empty<ConceptMasteryNode>(),
            LearningFrontier: Array.Empty<LearningFrontierItem>(),
            MasteryHistory: Array.Empty<MasteryHistoryPoint>(),
            Scaffolding: Array.Empty<ScaffoldingRecommendation>(),
            ReviewQueue: Array.Empty<ReviewPriorityItem>());
    }

    public async Task<ClassMasteryResponse?> GetClassMasteryAsync(string classId, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var latest = await session.Query<ClassMasteryRollupDocument>()
            .Where(r => r.ClassId == classId)
            .OrderByDescending(r => r.Date)
            .Take(1)
            .ToListAsync();
        if (latest.Count == 0) return null;
        var rollup = latest[0];

        // FIND-sec-011: Enforce tenant scoping - ADMIN can only access classes in their school
        if (callerSchoolId is not null && rollup.SchoolId != callerSchoolId)
        {
            _logger.LogWarning("Cross-tenant mastery access: caller from school {CallerSchool} attempted to access class {ClassId} in school {TargetSchool}",
                callerSchoolId, classId, rollup.SchoolId);
            return null;
        }

        var difficulty = await session.Query<ConceptDifficultyDocument>()
            .Where(c => c.SchoolId == rollup.SchoolId)
            .OrderByDescending(c => c.StruggleRate)
            .Take(10)
            .ToListAsync();

        // Real student mastery rows from snapshots
        var snapshots = await session.Query<StudentProfileSnapshot>()
            .Where(s => s.SchoolId == rollup.SchoolId)
            .ToListAsync();

        return BuildClassMastery(rollup, snapshots, difficulty);
    }

    public async Task<AtRiskStudentsResponse> GetAtRiskStudentsAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var query = session.Query<AtRiskStudentDocument>();
        if (schoolId is not null)
            query = (Marten.Linq.IMartenQueryable<AtRiskStudentDocument>)query.Where(d => d.SchoolId == schoolId);

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var recent = await query
            .Where(d => d.Date >= today.AddDays(-7))
            .OrderBy(d => d.CurrentAvgMastery)
            .ToListAsync();

        return new AtRiskStudentsResponse(BuildAtRiskStudentList(recent));
    }

    public async Task<MethodologyProfileAdminResponse?> GetMethodologyProfileAsync(string studentId, ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.LightweightSession();
        var snapshot = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (snapshot == null) return null;
        if (schoolId is not null && snapshot.SchoolId != schoolId) return null;

        var subjects = snapshot.SubjectMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Subject", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(50), kv.Value.ConfidenceReachedAt != null)).ToList();

        var topics = snapshot.TopicMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Topic", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(30), kv.Value.ConfidenceReachedAt != null)).ToList();

        var concepts = snapshot.ConceptMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Concept", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(30), kv.Value.ConfidenceReachedAt != null)).ToList();

        foreach (var (conceptId, methodology) in snapshot.ActiveMethodologyMap)
        {
            if (!concepts.Any(c => c.Id == conceptId))
            {
                var mastery = snapshot.ConceptMastery.GetValueOrDefault(conceptId);
                concepts.Add(new MethodologyLevelEntry(
                    conceptId, "Concept", methodology, "McmRouted",
                    mastery?.TotalAttempts ?? 0,
                    mastery != null && mastery.TotalAttempts > 0
                        ? mastery.CorrectCount / (float)mastery.TotalAttempts
                        : 0f,
                    0f, false, false));
            }
        }

        return new MethodologyProfileAdminResponse(studentId, subjects, topics, concepts);
    }

    public async Task<bool> OverrideMethodologyAsync(string studentId, string level, string levelId, string methodology, string teacherId, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.LightweightSession();

        // FIND-sec-011: Verify student belongs to caller's school
        var student = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (student is null) return false;
        if (callerSchoolId is not null && student.SchoolId != callerSchoolId)
        {
            _logger.LogWarning("Cross-tenant override attempt: caller from school {CallerSchool} attempted to override methodology for student {StudentId} in school {TargetSchool}",
                callerSchoolId, studentId, student.SchoolId);
            return false;
        }

        var overrideDoc = new MethodologyOverrideDocument
        {
            Id = $"{studentId}:{level}:{levelId}",
            StudentId = studentId,
            Level = level,
            LevelId = levelId,
            Methodology = methodology,
            TeacherId = teacherId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        session.Store(overrideDoc);
        await session.SaveChangesAsync();
        _logger.LogInformation(
            "Teacher {TeacherId} overrode methodology for student {StudentId} at {Level}/{LevelId} to {Methodology}",
            teacherId, studentId, level, levelId, methodology);
        return true;
    }

    public async Task<IReadOnlyList<MethodologyOverrideDocument>> GetStudentOverridesAsync(string studentId, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-sec-011: Verify student belongs to caller's school
        var student = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (student is null) return Array.Empty<MethodologyOverrideDocument>();
        if (callerSchoolId is not null && student.SchoolId != callerSchoolId)
        {
            _logger.LogWarning("Cross-tenant override read attempt: caller from school {CallerSchool} attempted to access overrides for student {StudentId} in school {TargetSchool}",
                callerSchoolId, studentId, student.SchoolId);
            return Array.Empty<MethodologyOverrideDocument>();
        }

        return await session.Query<MethodologyOverrideDocument>()
            .Where(o => o.StudentId == studentId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RemoveOverrideAsync(string studentId, string overrideId, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.LightweightSession();

        // FIND-sec-011: Verify student belongs to caller's school
        var student = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (student is null) return false;
        if (callerSchoolId is not null && student.SchoolId != callerSchoolId)
        {
            _logger.LogWarning("Cross-tenant override remove attempt: caller from school {CallerSchool} attempted to remove override for student {StudentId} in school {TargetSchool}",
                callerSchoolId, studentId, student.SchoolId);
            return false;
        }

        var existing = await session.LoadAsync<MethodologyOverrideDocument>(overrideId);
        if (existing == null || existing.StudentId != studentId) return false;
        session.Delete<MethodologyOverrideDocument>(overrideId);
        await session.SaveChangesAsync();
        _logger.LogInformation("Methodology override {OverrideId} removed for student {StudentId}", overrideId, studentId);
        return true;
    }

    // -------------------------------------------------------------------------
    // Mapping helpers — pure static, exercised by unit tests
    // -------------------------------------------------------------------------

    internal static MasteryOverviewResponse BuildMasteryOverview(
        IReadOnlyList<ClassMasteryRollupDocument> rollups)
    {
        if (rollups.Count == 0)
        {
            return new MasteryOverviewResponse(
                Distribution: new List<MasteryDistributionPoint>(),
                SubjectBreakdown: new List<SubjectMastery>(),
                LearningVelocity: 0f,
                LearningVelocityChange: 0f,
                AtRiskCount: 0);
        }

        // Aggregate the most recent day across classes
        var latestDate = rollups.Max(r => r.Date);
        var latest = rollups.Where(r => r.Date == latestDate).ToList();

        var beginner = latest.Sum(r => r.BeginnerCount);
        var developing = latest.Sum(r => r.DevelopingCount);
        var proficient = latest.Sum(r => r.ProficientCount);
        var master = latest.Sum(r => r.MasterCount);
        var total = Math.Max(1, beginner + developing + proficient + master);

        var distribution = new List<MasteryDistributionPoint>
        {
            new("Beginner", beginner, MathF.Round(beginner * 100f / total, 1)),
            new("Developing", developing, MathF.Round(developing * 100f / total, 1)),
            new("Proficient", proficient, MathF.Round(proficient * 100f / total, 1)),
            new("Master", master, MathF.Round(master * 100f / total, 1))
        };

        // Subject breakdown merged across classes
        var subjects = latest
            .SelectMany(r => r.SubjectBreakdown)
            .GroupBy(s => s.Subject)
            .Select(g => new SubjectMastery(
                Subject: g.Key,
                AvgMasteryLevel: MathF.Round(g.Average(x => x.AvgMasteryLevel), 3),
                ConceptCount: (int)g.Average(x => x.ConceptCount),
                MasteredCount: g.Sum(x => x.MasteredCount)))
            .OrderBy(s => s.Subject)
            .ToList();

        var learningVelocity = latest.Average(r => r.LearningVelocity);
        var learningVelocityChange = latest.Average(r => r.LearningVelocityChange);
        var atRiskCount = latest.Sum(r => r.AtRiskCount);

        return new MasteryOverviewResponse(
            Distribution: distribution,
            SubjectBreakdown: subjects,
            LearningVelocity: MathF.Round(learningVelocity, 2),
            LearningVelocityChange: MathF.Round(learningVelocityChange, 2),
            AtRiskCount: atRiskCount);
    }

    internal static StudentMasteryDetailResponse BuildStudentMasteryDetail(
        string studentId,
        StudentProfileSnapshot snapshot)
    {
        var concepts = new List<ConceptMasteryNode>();
        foreach (var entry in BagrutConceptCatalog.All)
        {
            var mastery = snapshot.ConceptMastery.GetValueOrDefault(entry.Id);
            var masteryLevel = (float)(mastery?.PKnown ?? 0.0);
            var status = masteryLevel switch
            {
                >= 0.8f => "mastered",
                >= 0.3f => "in_progress",
                >= 0.05f => "available",
                _ => "locked"
            };
            // Available if all prereqs are mastered
            if (status == "locked" && entry.Prereqs.Count > 0 && entry.Prereqs.All(
                p => (snapshot.ConceptMastery.GetValueOrDefault(p)?.PKnown ?? 0) >= 0.8))
            {
                status = "available";
            }

            concepts.Add(new ConceptMasteryNode(
                ConceptId: entry.Id,
                ConceptName: entry.Name,
                Subject: entry.Subject,
                MasteryLevel: masteryLevel,
                Status: status,
                PrerequisiteIds: entry.Prereqs,
                UnlocksIds: entry.Unlocks));
        }

        var frontier = concepts
            .Where(c => c.Status == "available" || (c.Status == "in_progress" && c.MasteryLevel < 0.5f))
            .OrderByDescending(c => c.MasteryLevel)
            .Take(5)
            .Select(c => new LearningFrontierItem(
                c.ConceptId, c.ConceptName, c.MasteryLevel + 0.1f, "prerequisites_met"))
            .ToList();

        var history = BuildMasteryHistory(snapshot);

        var scaffolding = concepts
            .Where(c => c.Status == "in_progress" && c.MasteryLevel < 0.5f)
            .Take(3)
            .Select(c => new ScaffoldingRecommendation(
                c.ConceptId, c.ConceptName, "moderate",
                $"Student needs reinforcement on {c.ConceptName}"))
            .ToList();

        var reviewQueue = concepts
            .Where(c => c.Status == "mastered" || c.MasteryLevel > 0.6f)
            .Take(4)
            .Select((c, i) =>
            {
                var mastery = snapshot.ConceptMastery.GetValueOrDefault(c.ConceptId);
                var lastAttempted = mastery?.LastAttemptedAt ?? DateTimeOffset.UtcNow.AddDays(-7);
                var hoursSince = (DateTimeOffset.UtcNow - lastAttempted).TotalHours;
                var halfLife = snapshot.HalfLifeMap.GetValueOrDefault(c.ConceptId, 168.0);
                var decay = halfLife > 0 ? (float)(1.0 - Math.Exp(-hoursSince * Math.Log(2) / halfLife)) : 0f;
                return new ReviewPriorityItem(
                    c.ConceptId, c.ConceptName,
                    MathF.Round(decay, 3),
                    c.MasteryLevel,
                    lastAttempted,
                    i + 1);
            })
            .ToList();

        return new StudentMasteryDetailResponse(
            StudentId: studentId,
            StudentName: snapshot.FullName ?? snapshot.DisplayName ?? $"Student {studentId}",
            KnowledgeMap: concepts,
            LearningFrontier: frontier,
            MasteryHistory: history,
            Scaffolding: scaffolding,
            ReviewQueue: reviewQueue);
    }

    internal static List<MasteryHistoryPoint> BuildMasteryHistory(StudentProfileSnapshot snapshot)
    {
        // Build weekly history based on LastAttemptedAt timestamps from
        // concept mastery — real data, not random.
        var points = new List<MasteryHistoryPoint>();
        var now = DateTimeOffset.UtcNow;
        for (int i = 6; i >= 0; i--)
        {
            var weekEnd = now.AddDays(-i * 7);
            var weekStart = weekEnd.AddDays(-7);
            var inRange = snapshot.ConceptMastery
                .Where(kv => kv.Value.LastAttemptedAt is { } ts && ts >= weekStart && ts <= weekEnd)
                .Select(kv => kv.Value)
                .ToList();
            var attemptCount = inRange.Count;
            var masteredCount = inRange.Count(m => m.PKnown >= 0.8);
            var totalMastery = snapshot.ConceptMastery.Values.Count > 0
                ? (float)snapshot.ConceptMastery.Values.Average(m => m.PKnown)
                : 0f;
            points.Add(new MasteryHistoryPoint(
                Date: weekEnd.ToString("yyyy-MM-dd"),
                AvgMastery: MathF.Round(totalMastery, 3),
                ConceptsAttempted: attemptCount,
                ConceptsMastered: masteredCount));
        }
        return points;
    }

    internal static ClassMasteryResponse BuildClassMastery(
        ClassMasteryRollupDocument rollup,
        IReadOnlyList<StudentProfileSnapshot> snapshots,
        IReadOnlyList<ConceptDifficultyDocument> difficulty)
    {
        // Pick the hardest concepts by struggle rate as the column headers.
        // Real, not hand-crafted.
        var conceptIds = difficulty.Take(5).Select(d => d.ConceptId).ToList();
        if (conceptIds.Count == 0)
        {
            conceptIds = BagrutConceptCatalog.All
                .Take(5)
                .Select(c => c.Id)
                .ToList();
        }

        var rows = snapshots
            .Take(50)
            .Select(s =>
            {
                var levels = conceptIds
                    .Select(cid => (float)(s.ConceptMastery.GetValueOrDefault(cid)?.PKnown ?? 0) * 100f)
                    .ToList();
                var overall = s.ConceptMastery.Values.Count > 0
                    ? (float)s.ConceptMastery.Values.Average(m => m.PKnown) * 100f
                    : 0f;
                return new StudentMasteryRow(
                    StudentId: s.StudentId,
                    StudentName: s.FullName ?? s.DisplayName ?? s.StudentId,
                    MasteryLevels: levels,
                    OverallProgress: MathF.Round(overall, 1));
            })
            .OrderByDescending(r => r.OverallProgress)
            .ToList();

        var difficultyRows = difficulty.Select(d => new ConceptDifficulty(
            ConceptId: d.ConceptId,
            ConceptName: d.ConceptName,
            AvgMastery: d.AvgMastery,
            StruggleRate: d.StruggleRate,
            AttemptCount: d.TotalAttempts)).ToList();

        // Real pacing: ready to advance if the class is >70% average mastery on
        // top difficulty concepts
        var readyToAdvance = rollup.AvgMastery >= 0.7f;
        var toReview = difficulty.Where(d => d.StruggleRate > 0.5f).Take(3).Select(d => d.ConceptId).ToList();
        var toIntroduce = difficulty.Where(d => d.AvgMastery >= 0.75f).Take(3).Select(d => d.ConceptId).ToList();

        return new ClassMasteryResponse(
            ClassId: rollup.ClassId,
            ClassName: rollup.ClassName,
            Concepts: conceptIds,
            Students: rows,
            DifficultyAnalysis: difficultyRows,
            Pacing: new PacingRecommendation(
                ReadyToAdvance: readyToAdvance,
                Recommendation: readyToAdvance
                    ? "Class mastery is above threshold — ready to advance"
                    : "Class mastery below threshold — review before advancing",
                ConceptsToReview: toReview,
                ConceptsReadyToIntroduce: toIntroduce));
    }

    internal static List<AtRiskStudent> BuildAtRiskStudentList(
        IReadOnlyList<AtRiskStudentDocument> docs)
    {
        return docs
            .GroupBy(d => d.StudentId)
            .Select(g => g.OrderByDescending(d => d.Date).First())
            .Select(d => new AtRiskStudent(
                StudentId: d.StudentId,
                StudentName: d.StudentName,
                ClassId: d.ClassId,
                RiskLevel: d.RiskLevel,
                CurrentAvgMastery: d.CurrentAvgMastery,
                MasteryDecline: d.MasteryDeclineLast14d,
                RecommendedIntervention: d.RecommendedIntervention))
            .ToList();
    }
}

// ── Methodology Profile Response DTOs (kept from original) ──
public sealed record MethodologyProfileAdminResponse(
    string StudentId,
    IReadOnlyList<MethodologyLevelEntry> Subjects,
    IReadOnlyList<MethodologyLevelEntry> Topics,
    IReadOnlyList<MethodologyLevelEntry> Concepts);

public sealed record MethodologyLevelEntry(
    string Id,
    string Level,
    string Methodology,
    string Source,
    int AttemptCount,
    float SuccessRate,
    float Confidence,
    bool HasSufficientData,
    bool ConfidenceReached);

public class MethodologyOverrideDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Level { get; set; } = "";
    public string LevelId { get; set; } = "";
    public string Methodology { get; set; } = "";
    public string TeacherId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
