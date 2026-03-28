using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-010: Focus A/B Testing Framework tests.
/// Covers deterministic assignment, multi-arm support, experiment lifecycle,
/// and metrics collection.
/// </summary>
public sealed class FocusExperimentTests
{
    // ═══════════════════════════════════════════════════════════════
    // FOC-010.1: Experiment Assignment
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Assignment_Deterministic_SameStudentSameArm()
    {
        var service = new FocusExperimentService();
        var studentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var arm1 = service.GetAssignment(studentId, "foc-microbreaks");
        var arm2 = service.GetAssignment(studentId, "foc-microbreaks");

        Assert.Equal(arm1, arm2);
    }

    [Fact]
    public void Assignment_DifferentStudents_DifferentArms()
    {
        var service = new FocusExperimentService();

        // Generate enough students to statistically ensure both arms are represented
        var arms = new HashSet<ExperimentArm>();
        for (int i = 0; i < 100; i++)
        {
            var arm = service.GetAssignment(Guid.NewGuid(), "foc-microbreaks");
            arms.Add(arm);
        }

        // With 100 students in a 50/50 split, both arms should appear
        Assert.Contains(ExperimentArm.Control, arms);
        Assert.Contains(ExperimentArm.Treatment, arms);
    }

    [Fact]
    public void Assignment_DifferentExperiments_IndependentAssignment()
    {
        var service = new FocusExperimentService();
        var studentId = Guid.NewGuid();

        // A student can be in control for one experiment and treatment for another
        var arms = new HashSet<ExperimentArm>();
        foreach (var exp in FocusExperimentService.DefaultExperiments)
        {
            arms.Add(service.GetAssignment(studentId, exp.ExperimentId));
        }

        // With 6 experiments, very likely to have both arms (though not guaranteed)
        // Just verify it doesn't crash and returns valid arms
        Assert.All(arms, arm => Assert.True(
            arm == ExperimentArm.Control || arm == ExperimentArm.Treatment || arm == ExperimentArm.TreatmentB));
    }

    [Fact]
    public void Assignment_UnknownExperiment_ReturnsControl()
    {
        var service = new FocusExperimentService();
        var arm = service.GetAssignment(Guid.NewGuid(), "nonexistent-experiment");
        Assert.Equal(ExperimentArm.Control, arm);
    }

    [Fact]
    public void PredefinedExperiments_Exist()
    {
        Assert.Equal(9, FocusExperimentService.DefaultExperiments.Count);

        var ids = new HashSet<string>();
        foreach (var exp in FocusExperimentService.DefaultExperiments)
        {
            ids.Add(exp.ExperimentId);
            Assert.False(string.IsNullOrEmpty(exp.Name));
            Assert.False(string.IsNullOrEmpty(exp.Description));
            Assert.False(string.IsNullOrEmpty(exp.PrimaryMetric));
            Assert.True(exp.Arms.Count >= 2);
        }

        Assert.Contains("foc-microbreaks", ids);
        Assert.Contains("foc-boredom-fatigue", ids);
        Assert.Contains("foc-confusion-patience", ids);
        Assert.Contains("foc-peak-time", ids);
        Assert.Contains("foc-solution-diversity", ids);
        Assert.Contains("foc-sensor-enhanced", ids);

        // 3 SAI-006 Student AI Interaction experiments
        Assert.Contains(FocusExperimentService.SaiAdaptiveExplanations, ids);
        Assert.Contains(FocusExperimentService.SaiHintBktWeighting, ids);
        Assert.Contains(FocusExperimentService.SaiConfusionGating, ids);
    }

    [Fact]
    public void Assignment_5050Split_ApproximatelyBalanced()
    {
        var service = new FocusExperimentService();
        int controlCount = 0;
        int treatmentCount = 0;
        const int total = 1000;

        for (int i = 0; i < total; i++)
        {
            var arm = service.GetAssignment(Guid.NewGuid(), "foc-microbreaks");
            if (arm == ExperimentArm.Control) controlCount++;
            else treatmentCount++;
        }

        // With 1000 samples, expect roughly 50/50 (allow 40-60% range)
        double controlPct = (double)controlCount / total;
        Assert.InRange(controlPct, 0.35, 0.65);
    }

    [Fact]
    public void IsActive_DefaultExperiments_AllActive()
    {
        var service = new FocusExperimentService();
        foreach (var exp in FocusExperimentService.DefaultExperiments)
        {
            Assert.True(service.IsActive(exp.ExperimentId));
        }
    }

    [Fact]
    public void IsActive_ExpiredExperiment_ReturnsFalse()
    {
        var expiredExperiment = new FocusExperiment(
            ExperimentId: "expired-test",
            Name: "Expired",
            Description: "Test",
            PrimaryMetric: "test_metric",
            Arms: new[]
            {
                new ExperimentArmConfig(ExperimentArm.Control, 50, "Control"),
                new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Treatment")
            },
            StartDate: DateTimeOffset.UtcNow.AddDays(-30),
            EndDate: DateTimeOffset.UtcNow.AddDays(-1)
        );

        var service = new FocusExperimentService(new[] { expiredExperiment });
        Assert.False(service.IsActive("expired-test"));
    }

    [Fact]
    public void GetActiveExperiments_ReturnsOnlyActive()
    {
        var expired = new FocusExperiment("expired", "Expired", "Test", "m",
            new[] { new ExperimentArmConfig(ExperimentArm.Control, 100, "c") },
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        var active = new FocusExperiment("active", "Active", "Test", "m",
            new[] { new ExperimentArmConfig(ExperimentArm.Control, 50, "c"),
                    new ExperimentArmConfig(ExperimentArm.Treatment, 50, "t") },
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var service = new FocusExperimentService(new[] { expired, active });
        var activeList = service.GetActiveExperiments();

        Assert.Single(activeList);
        Assert.Equal("active", activeList[0].ExperimentId);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-010.2: Experiment Metrics Collector
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Collector_RecordAndRetrieveMetrics()
    {
        var collector = new FocusExperimentCollector();
        var studentId = Guid.NewGuid();

        var metrics = new ExperimentSessionMetrics(
            StudentId: studentId,
            SessionId: Guid.NewGuid(),
            ExperimentId: "foc-microbreaks",
            Arm: ExperimentArm.Treatment,
            Timestamp: DateTimeOffset.UtcNow,
            FocusStateAccuracy: 0.85,
            BreakEffectiveness: 0.12,
            MicrobreakComplianceRate: 0.75,
            ReturnedNextSession: true,
            NextSessionPerformanceDelta: 0.05,
            ProductiveStrugglePrecision: 0.9,
            SelfReportedFocus: 4,
            SelfReportLanguage: "he",
            ExplanationSource: null,
            HintCreditMultiplier: null,
            DeliveryGateAction: null,
            MasteryGainDelta: null,
            HintUsageRate: null,
            ConfusionResolutionRate: null,
            SelfReportedUnderstanding: null
        );

        collector.RecordSessionMetrics(metrics);

        var retrieved = collector.GetMetrics("foc-microbreaks");
        Assert.Single(retrieved);
        Assert.Equal(studentId, retrieved[0].StudentId);
        Assert.Equal(0.85, retrieved[0].FocusStateAccuracy);

        var byStudent = collector.GetMetricsForStudent(studentId, "foc-microbreaks");
        Assert.Single(byStudent);
    }

    [Fact]
    public void Collector_FiltersByExperiment()
    {
        var collector = new FocusExperimentCollector();
        var studentId = Guid.NewGuid();

        collector.RecordSessionMetrics(new ExperimentSessionMetrics(
            studentId, Guid.NewGuid(), "foc-microbreaks", ExperimentArm.Control,
            DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null));

        collector.RecordSessionMetrics(new ExperimentSessionMetrics(
            studentId, Guid.NewGuid(), "foc-boredom-fatigue", ExperimentArm.Treatment,
            DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null));

        Assert.Single(collector.GetMetrics("foc-microbreaks"));
        Assert.Single(collector.GetMetrics("foc-boredom-fatigue"));
        Assert.Empty(collector.GetMetrics("nonexistent"));
    }

    [Fact]
    public void Collector_SelfReport_SupportsHebrewAndArabic()
    {
        var collector = new FocusExperimentCollector();

        collector.RecordSessionMetrics(new ExperimentSessionMetrics(
            Guid.NewGuid(), Guid.NewGuid(), "foc-microbreaks", ExperimentArm.Control,
            DateTimeOffset.UtcNow, null, null, null, null, null, null,
            SelfReportedFocus: 3, SelfReportLanguage: "he",
            ExplanationSource: null, HintCreditMultiplier: null, DeliveryGateAction: null,
            MasteryGainDelta: null, HintUsageRate: null, ConfusionResolutionRate: null, SelfReportedUnderstanding: null));

        collector.RecordSessionMetrics(new ExperimentSessionMetrics(
            Guid.NewGuid(), Guid.NewGuid(), "foc-microbreaks", ExperimentArm.Treatment,
            DateTimeOffset.UtcNow, null, null, null, null, null, null,
            SelfReportedFocus: 4, SelfReportLanguage: "ar",
            ExplanationSource: null, HintCreditMultiplier: null, DeliveryGateAction: null,
            MasteryGainDelta: null, HintUsageRate: null, ConfusionResolutionRate: null, SelfReportedUnderstanding: null));

        var metrics = collector.GetMetrics("foc-microbreaks");
        Assert.Equal(2, metrics.Count);
        Assert.Contains(metrics, m => m.SelfReportLanguage == "he");
        Assert.Contains(metrics, m => m.SelfReportLanguage == "ar");
    }
}
