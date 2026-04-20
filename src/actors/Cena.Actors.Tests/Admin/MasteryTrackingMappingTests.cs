// =============================================================================
// Cena Platform -- MasteryTrackingService mapping tests (ADM-016 hardening)
// Exercises the pure internal static helpers. No Marten, no DB.
// =============================================================================

using Cena.Actors.Events;
using Cena.Admin.Api;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Admin;

public sealed class MasteryTrackingMappingTests
{
    private static readonly DateTimeOffset Today = new(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BuildMasteryOverview_EmptyRollups_ReturnsEmpty()
    {
        var result = MasteryTrackingService.BuildMasteryOverview(Array.Empty<ClassMasteryRollupDocument>());
        Assert.Empty(result.Distribution);
        Assert.Empty(result.SubjectBreakdown);
        // AtRiskCount assertion removed per prr-013 follow-up — field retired from MasteryOverviewResponse DTO.
    }

    [Fact]
    public void BuildMasteryOverview_AggregatesDistributionAcrossClasses()
    {
        var rollups = new[]
        {
            new ClassMasteryRollupDocument
            {
                Id = "c1:2026-04-10",
                ClassId = "c1",
                ClassName = "Class 1",
                SchoolId = "dev-school",
                Date = Today,
                BeginnerCount = 2,
                DevelopingCount = 3,
                ProficientCount = 4,
                MasterCount = 1,
                LearningVelocity = 2.5f,
                LearningVelocityChange = 0.1f,
                AtRiskCount = 2,
                SubjectBreakdown = new List<ClassMasterySubjectSlot>
                {
                    new() { Subject = "Math", AvgMasteryLevel = 0.65f, ConceptCount = 42, MasteredCount = 20 },
                },
            },
            new ClassMasteryRollupDocument
            {
                Id = "c2:2026-04-10",
                ClassId = "c2",
                ClassName = "Class 2",
                SchoolId = "dev-school",
                Date = Today,
                BeginnerCount = 1,
                DevelopingCount = 2,
                ProficientCount = 3,
                MasterCount = 2,
                LearningVelocity = 3.0f,
                LearningVelocityChange = 0.2f,
                AtRiskCount = 1,
                SubjectBreakdown = new List<ClassMasterySubjectSlot>
                {
                    new() { Subject = "Math", AvgMasteryLevel = 0.70f, ConceptCount = 42, MasteredCount = 22 },
                },
            },
        };

        var result = MasteryTrackingService.BuildMasteryOverview(rollups);

        Assert.Equal(4, result.Distribution.Count);
        Assert.Equal("Beginner", result.Distribution[0].Level);
        Assert.Equal(3, result.Distribution[0].Count);   // 2 + 1
        Assert.Equal(5, result.Distribution[1].Count);   // 3 + 2
        Assert.Equal(7, result.Distribution[2].Count);   // 4 + 3
        Assert.Equal(3, result.Distribution[3].Count);   // 1 + 2
        // AtRiskCount assertion removed per prr-013 follow-up — field retired from MasteryOverviewResponse DTO.
        // The persistence-side ClassMasteryRollupDocument.AtRiskCount is retained (not in DTO ban scope).
        Assert.Single(result.SubjectBreakdown);
        Assert.Equal("Math", result.SubjectBreakdown[0].Subject);
        Assert.InRange(result.SubjectBreakdown[0].AvgMasteryLevel, 0.67f, 0.68f); // (0.65+0.70)/2
    }

    [Fact]
    public void BuildStudentMasteryDetail_MapsConceptMasteryToCatalog()
    {
        var snapshot = new StudentProfileSnapshot
        {
            StudentId = "stu-a",
            FullName = "Alice",
            SchoolId = "dev-school",
            ConceptMastery =
            {
                ["M-ALG-01"] = new ConceptMasteryState { PKnown = 0.9, LastAttemptedAt = Today, TotalAttempts = 10, CorrectCount = 9 },
                ["M-ALG-02"] = new ConceptMasteryState { PKnown = 0.4, LastAttemptedAt = Today, TotalAttempts = 6, CorrectCount = 3 },
            },
        };

        var detail = MasteryTrackingService.BuildStudentMasteryDetail("stu-a", snapshot);

        Assert.Equal("stu-a", detail.StudentId);
        Assert.Equal("Alice", detail.StudentName);
        Assert.NotEmpty(detail.KnowledgeMap);
        var algebra1 = detail.KnowledgeMap.First(c => c.ConceptId == "M-ALG-01");
        Assert.Equal("mastered", algebra1.Status);
        Assert.Equal(0.9f, algebra1.MasteryLevel, 2);
        var algebra2 = detail.KnowledgeMap.First(c => c.ConceptId == "M-ALG-02");
        Assert.Equal("in_progress", algebra2.Status);
    }

    [Fact]
    public void BuildStudentMasteryDetail_UnlocksNextConceptsWhenPrereqsMastered()
    {
        // With all algebra prereqs mastered, M-ALG-02 should be "mastered"
        // and unlocked concepts past the prereq graph should not be "locked"
        var snapshot = new StudentProfileSnapshot
        {
            StudentId = "stu-a",
            SchoolId = "dev-school",
            ConceptMastery =
            {
                ["M-ALG-01"] = new ConceptMasteryState { PKnown = 0.95, LastAttemptedAt = Today },
                ["M-ALG-02"] = new ConceptMasteryState { PKnown = 0.92, LastAttemptedAt = Today },
            },
        };

        var detail = MasteryTrackingService.BuildStudentMasteryDetail("stu-a", snapshot);

        // M-ALG-04 has prereq M-ALG-02 (mastered in our snapshot) and no mastery
        // in the snapshot → should be "available" not "locked"
        var quadratic = detail.KnowledgeMap.First(c => c.ConceptId == "M-ALG-04");
        Assert.Equal("available", quadratic.Status);
    }

    [Fact]
    public void BuildMasteryHistory_UsesLastAttemptedTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new StudentProfileSnapshot
        {
            StudentId = "stu-a",
            ConceptMastery =
            {
                ["M-ALG-01"] = new ConceptMasteryState { PKnown = 0.8, LastAttemptedAt = now.AddDays(-2) },
                ["M-ALG-02"] = new ConceptMasteryState { PKnown = 0.9, LastAttemptedAt = now.AddDays(-3) },
                ["M-ALG-03"] = new ConceptMasteryState { PKnown = 0.3, LastAttemptedAt = now.AddDays(-40) },
            },
        };

        var history = MasteryTrackingService.BuildMasteryHistory(snapshot);

        Assert.Equal(7, history.Count);
        // The most recent week should have 2 attempted
        var mostRecent = history.Last();
        Assert.True(mostRecent.ConceptsAttempted >= 1);
    }

    [Fact]
    public void BuildClassMastery_FallsBackToCatalogWhenDifficultyEmpty()
    {
        var rollup = new ClassMasteryRollupDocument
        {
            Id = "c1:2026-04-10",
            ClassId = "c1",
            ClassName = "Class 1",
            SchoolId = "dev-school",
            Date = Today,
            AvgMastery = 0.65f,
        };

        var response = MasteryTrackingService.BuildClassMastery(
            rollup,
            Array.Empty<StudentProfileSnapshot>(),
            Array.Empty<ConceptDifficultyDocument>());

        Assert.Equal("c1", response.ClassId);
        Assert.Equal(5, response.Concepts.Count);
        Assert.False(response.Pacing.ReadyToAdvance); // 0.65 < 0.70
        Assert.Empty(response.DifficultyAnalysis);
    }

    [Fact]
    public void BuildClassMastery_ReadyToAdvanceWhenAvgMasteryHigh()
    {
        var rollup = new ClassMasteryRollupDocument
        {
            Id = "c1:2026-04-10",
            ClassId = "c1",
            ClassName = "Class 1",
            SchoolId = "dev-school",
            AvgMastery = 0.82f,
        };
        var difficulty = new[]
        {
            new ConceptDifficultyDocument
            {
                SchoolId = "dev-school", ConceptId = "M-ALG-04", ConceptName = "Quadratics",
                AvgMastery = 0.55f, StruggleRate = 0.6f, TotalAttempts = 300
            },
        };

        var response = MasteryTrackingService.BuildClassMastery(
            rollup,
            Array.Empty<StudentProfileSnapshot>(),
            difficulty);

        Assert.True(response.Pacing.ReadyToAdvance);
        Assert.Contains("M-ALG-04", response.Pacing.ConceptsToReview);
    }

    // Removed per prr-013 follow-up: BuildAtRiskStudentList_DeduplicatesByLatestDate
    // tested MasteryTrackingService.BuildAtRiskStudentList which was retired along with
    // the AtRiskStudentsResponse DTO (ADR-0012 + RDY-080 prediction-surface ban).
    // See pre-release-review/reviews/audit/group-a-caller-audit.md R-22 for the
    // compliance-defect rationale. Follow-up work will fully retire AtRiskStudentDocument
    // from persistence + Marten projection + admin SPA; that test will return only if the
    // new session-scoped teacher-facing view reintroduces equivalent logic.

    [Fact]
    public void BagrutConceptCatalog_ExposesAllCoreSubjects()
    {
        var subjects = BagrutConceptCatalog.All
            .Select(c => c.Subject)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        Assert.Contains("Math", subjects);
        Assert.Contains("Physics", subjects);
        Assert.Contains("Chemistry", subjects);
        Assert.Contains("Biology", subjects);
        Assert.Contains("CS", subjects);
        Assert.Contains("English", subjects);
    }
}
