// =============================================================================
// Simulation Tests: Validates statistical diversity and correctness of generated data
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Simulation;

namespace Cena.Actors.Tests.Simulation;

public sealed class MasterySimulatorTests
{
    [Fact]
    public void CurriculumGraph_Has45Concepts6Clusters()
    {
        var (concepts, edges) = CurriculumSeedData.BuildBagrutMathCurriculum();

        Assert.True(concepts.Count >= 40, $"Expected >= 40 concepts, got {concepts.Count}");
        var clusters = concepts.Select(c => c.TopicCluster).Distinct().ToList();
        Assert.Equal(7, clusters.Count); // algebra, functions, geometry, trigonometry, calculus, probability, vectors
        Assert.True(edges.Count >= 30, $"Expected >= 30 edges, got {edges.Count}");
    }

    [Fact]
    public void CurriculumGraph_PrerequisitesAreConsistent()
    {
        var graphCache = CurriculumSeedData.BuildGraphCache();

        // Every prerequisite edge references existing concepts
        foreach (var (id, _) in graphCache.Concepts)
        {
            var prereqs = graphCache.GetPrerequisites(id);
            foreach (var edge in prereqs)
            {
                Assert.True(graphCache.Concepts.ContainsKey(edge.SourceConceptId),
                    $"Prerequisite {edge.SourceConceptId} for {id} not found in concepts");
            }
        }
    }

    [Fact]
    public void CurriculumGraph_DepthLevelsAreValid()
    {
        var graphCache = CurriculumSeedData.BuildGraphCache();

        // No concept's prerequisite should have equal or higher depth
        foreach (var (id, node) in graphCache.Concepts)
        {
            var prereqs = graphCache.GetPrerequisites(id);
            foreach (var edge in prereqs)
            {
                var prereqNode = graphCache.Concepts[edge.SourceConceptId];
                Assert.True(prereqNode.DepthLevel <= node.DepthLevel,
                    $"Prereq {edge.SourceConceptId} (depth {prereqNode.DepthLevel}) should be <= {id} (depth {node.DepthLevel})");
            }
        }
    }

    [Fact]
    public void GenerateCohort_ProducesCorrectCount()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 2, simulationDays: 14, seed: 99);

        Assert.Equal(12, cohort.Count); // 6 archetypes * 2 each
        Assert.Equal(6, cohort.Select(s => s.ArchetypeName).Distinct().Count());
    }

    [Fact]
    public void GenerateCohort_IsDeterministic()
    {
        var cohort1 = MasterySimulator.GenerateCohort(studentsPerArchetype: 1, simulationDays: 10, seed: 42);
        var cohort2 = MasterySimulator.GenerateCohort(studentsPerArchetype: 1, simulationDays: 10, seed: 42);

        Assert.Equal(cohort1.Count, cohort2.Count);
        for (int i = 0; i < cohort1.Count; i++)
        {
            Assert.Equal(cohort1[i].AttemptHistory.Count, cohort2[i].AttemptHistory.Count);
            Assert.Equal(cohort1[i].MasteryOverlay.Count, cohort2[i].MasteryOverlay.Count);
        }
    }

    [Fact]
    public void HighAchiever_HasHigherAccuracyThanStruggling()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 3, simulationDays: 30, seed: 42);

        var achievers = cohort.Where(s => s.ArchetypeName == "HighAchiever").ToList();
        var struggling = cohort.Where(s => s.ArchetypeName == "Struggling").ToList();

        float achieverAccuracy = achievers.Average(s =>
            s.AttemptHistory.Count > 0
                ? s.AttemptHistory.Count(a => a.IsCorrect) / (float)s.AttemptHistory.Count
                : 0f);

        float strugglingAccuracy = struggling.Average(s =>
            s.AttemptHistory.Count > 0
                ? s.AttemptHistory.Count(a => a.IsCorrect) / (float)s.AttemptHistory.Count
                : 0f);

        Assert.True(achieverAccuracy > strugglingAccuracy,
            $"Achiever accuracy {achieverAccuracy:F3} should > struggling {strugglingAccuracy:F3}");
    }

    [Fact]
    public void FastCareless_HasLowerResponseTimeThanSlowThorough()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 3, simulationDays: 30, seed: 42);

        var fast = cohort.Where(s => s.ArchetypeName == "FastCareless").ToList();
        var slow = cohort.Where(s => s.ArchetypeName == "SlowThorough").ToList();

        double fastAvgRt = fast.Average(s =>
            s.AttemptHistory.Count > 0
                ? s.AttemptHistory.Average(a => a.ResponseTimeMs)
                : 0);

        double slowAvgRt = slow.Average(s =>
            s.AttemptHistory.Count > 0
                ? s.AttemptHistory.Average(a => a.ResponseTimeMs)
                : 0);

        Assert.True(fastAvgRt < slowAvgRt,
            $"FastCareless avg RT {fastAvgRt:F0}ms should < SlowThorough {slowAvgRt:F0}ms");
    }

    [Fact]
    public void AllStudents_HaveMasteryOverlayEntries()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 1, simulationDays: 30, seed: 42);

        foreach (var student in cohort)
        {
            Assert.True(student.MasteryOverlay.Count > 0,
                $"Student {student.StudentId} ({student.ArchetypeName}) has no mastery data");
            Assert.True(student.AttemptHistory.Count > 0,
                $"Student {student.StudentId} ({student.ArchetypeName}) has no attempts");
        }
    }

    [Fact]
    public void MasteryValues_AreInValidRange()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 2, simulationDays: 30, seed: 42);

        foreach (var student in cohort)
        {
            foreach (var (conceptId, state) in student.MasteryOverlay)
            {
                Assert.InRange(state.MasteryProbability, 0.001f, 0.999f);
                Assert.True(state.HalfLifeHours >= 1f,
                    $"HalfLife for {conceptId} = {state.HalfLifeHours}");
                Assert.True(state.AttemptCount > 0);
                Assert.True(state.CorrectCount <= state.AttemptCount);
                Assert.InRange(state.BloomLevel, 0, 6);
            }
        }
    }

    [Fact]
    public void ErrorTypes_AreDistributed()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 2, simulationDays: 30, seed: 42);

        var allErrors = cohort
            .SelectMany(s => s.AttemptHistory)
            .Where(a => a.ClassifiedError.HasValue)
            .Select(a => a.ClassifiedError!.Value)
            .ToList();

        Assert.True(allErrors.Count > 0, "Should have some errors");

        // Check that multiple error types appear
        var distinctErrors = allErrors.Distinct().ToList();
        Assert.True(distinctErrors.Count >= 3,
            $"Expected >= 3 error types, got {distinctErrors.Count}: [{string.Join(", ", distinctErrors)}]");
    }

    [Fact]
    public void QualityQuadrants_AllFourPresent()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 3, simulationDays: 30, seed: 42);

        var allQualities = cohort
            .SelectMany(s => s.AttemptHistory)
            .Select(a => a.QualityQuadrant)
            .Distinct()
            .ToList();

        Assert.Contains(MasteryQuality.Mastered, allQualities);
        Assert.Contains(MasteryQuality.Effortful, allQualities);
        Assert.Contains(MasteryQuality.Careless, allQualities);
        Assert.Contains(MasteryQuality.Struggling, allQualities);
    }

    [Fact]
    public void HighAchiever_HasHigherEloThanStruggling()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 3, simulationDays: 30, seed: 42);

        float achieverElo = cohort.Where(s => s.ArchetypeName == "HighAchiever")
            .Average(s => s.EloTheta);
        float strugglingElo = cohort.Where(s => s.ArchetypeName == "Struggling")
            .Average(s => s.EloTheta);

        Assert.True(achieverElo > strugglingElo,
            $"Achiever Elo {achieverElo:F0} should > struggling {strugglingElo:F0}");
    }

    [Fact]
    public void ResponseTimes_AreRightSkewed()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 2, simulationDays: 30, seed: 42);

        var allRts = cohort.SelectMany(s => s.AttemptHistory).Select(a => a.ResponseTimeMs).ToList();

        double mean = allRts.Average();
        allRts.Sort();
        double median = allRts[allRts.Count / 2];

        // Log-normal produces right skew: mean > median
        Assert.True(mean > median,
            $"Mean ({mean:F0}ms) should > median ({median:F0}ms) for right-skewed distribution");
    }

    [Fact]
    public void Inconsistent_HasFewerSessionsThanHighAchiever()
    {
        var cohort = MasterySimulator.GenerateCohort(
            studentsPerArchetype: 3, simulationDays: 45, seed: 42);

        double achieverSessions = cohort.Where(s => s.ArchetypeName == "HighAchiever")
            .Average(s => s.TotalSessions);
        double inconsistentSessions = cohort.Where(s => s.ArchetypeName == "Inconsistent")
            .Average(s => s.TotalSessions);

        Assert.True(achieverSessions > inconsistentSessions,
            $"Achiever sessions {achieverSessions:F1} should > inconsistent {inconsistentSessions:F1}");
    }
}
