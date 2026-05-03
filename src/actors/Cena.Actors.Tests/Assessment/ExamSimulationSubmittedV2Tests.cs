// =============================================================================
// Cena Platform — prr-013 V1→V2 migration tests for ExamSimulationSubmitted.
//
// Purpose: lock the 2026-04-20 retirement of `ReadinessLowerBound` /
// `ReadinessUpperBound` from the on-stream exam-simulation shape.
//
// Three invariants are exercised here:
//
//   1. V2 is the clean replacement — constructing an instance works with
//      NO readiness fields at all, and its declared record type has
//      exactly the expected parameter shape (no readiness leaked back in).
//
//   2. V1 is [Obsolete] and has no in-tree consumers. A 2026-04-20 grep
//      confirmed that no aggregate / projection / service reads V1. This
//      test encodes that as an arch-style assertion: if any production
//      source file outside Events/ starts constructing a
//      `ExamSimulationSubmitted_V1`, the test fails so the regression is
//      caught at PR time instead of via a grep during the next audit.
//
//   3. Historical V1 events remain constructable (for Marten replay
//      tolerance only) and their readiness scalars can be projected down
//      to the V2 shape by ignoring them — a handler that reads either V1
//      or V2 should treat V1's readiness fields as absent.
//
// See ADR-0043 "Sibling change 2026-04-20: V1→V2 readiness field migration"
// and the allowlist notes in NoAtRiskPersistenceTest / NoThetaInOutboundDtoTest.
// =============================================================================

using System.Reflection;

using Cena.Actors.Events;

namespace Cena.Actors.Tests.Assessment;

public sealed class ExamSimulationSubmittedV2Tests
{
    // ---------------------------------------------------------------------
    // Invariant 1 — V2 is the clean replacement.
    // ---------------------------------------------------------------------

    [Fact]
    public void V2_Constructs_Without_Readiness_Fields()
    {
        var v2 = new ExamSimulationSubmitted_V2(
            StudentId: "stu-007",
            SimulationId: "sim-42",
            QuestionsAttempted: 7,
            QuestionsCorrect: 5,
            ScorePercent: 71.4,
            TimeTaken: TimeSpan.FromMinutes(145),
            VisibilityWarnings: 1,
            SubmittedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("stu-007", v2.StudentId);
        Assert.Equal("sim-42", v2.SimulationId);
        Assert.Equal(5, v2.QuestionsCorrect);
        Assert.Equal(71.4, v2.ScorePercent);
    }

    [Fact]
    public void V2_Record_Shape_Has_No_Readiness_Parameters()
    {
        // The primary-constructor parameter list is the canonical shape for
        // a C# record. If anyone re-adds a readiness scalar to V2, the
        // property-name set below must change — which is exactly the
        // signal we want to catch.
        var propertyNames = typeof(ExamSimulationSubmitted_V2)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Positive: the cleaned-up V2 shape.
        Assert.Contains("StudentId", propertyNames);
        Assert.Contains("SimulationId", propertyNames);
        Assert.Contains("QuestionsAttempted", propertyNames);
        Assert.Contains("QuestionsCorrect", propertyNames);
        Assert.Contains("ScorePercent", propertyNames);
        Assert.Contains("TimeTaken", propertyNames);
        Assert.Contains("VisibilityWarnings", propertyNames);
        Assert.Contains("SubmittedAt", propertyNames);

        // Negative: readiness scalars must not be on V2. This is the whole
        // point of the 2026-04-20 V1→V2 migration (ADR-0043 sibling note).
        Assert.DoesNotContain("ReadinessLowerBound", propertyNames);
        Assert.DoesNotContain("ReadinessUpperBound", propertyNames);
    }

    // ---------------------------------------------------------------------
    // Invariant 2 — no production code path emits V1.
    //
    // Scope: only the Cena.Actors project tree (production) is scanned.
    // Events/ExamSimulationEvents.cs is excluded because it declares V1;
    // every other .cs file is fair game.
    // ---------------------------------------------------------------------

    [Fact]
    public void No_Production_Code_Path_Constructs_V1()
    {
        var repoRoot = FindRepoRoot();
        var actorsDir = Path.Combine(repoRoot, "src", "actors", "Cena.Actors");
        Assert.True(Directory.Exists(actorsDir),
            $"Expected Cena.Actors directory at {actorsDir}");

        var declarationFile = Path.Combine(
            actorsDir, "Events", "ExamSimulationEvents.cs");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
            actorsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            // The declaration site itself is exempt — V1 is still declared
            // there for Marten replay tolerance.
            if (string.Equals(file, declarationFile, StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            // Constructor-invocation pattern: `new ExamSimulationSubmitted_V1(`
            // or just `ExamSimulationSubmitted_V1(` (target-typed new).
            if (text.Contains("ExamSimulationSubmitted_V1(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "ExamSimulationSubmitted_V1 is [Obsolete] per prr-013 (2026-04-20). "
            + "New emitters must use ExamSimulationSubmitted_V2. See ADR-0043 "
            + "sibling note. Offending files:\n  "
            + string.Join("\n  ", offenders));
    }

    // ---------------------------------------------------------------------
    // Invariant 3 — V1 remains constructable for historical replay, and
    // a handler that reads either V1 or V2 should treat V1's readiness
    // fields as absent when projecting down to the V2 shape.
    // ---------------------------------------------------------------------

    [Fact]
    public void Historical_V1_Event_Is_Constructable_And_Projects_To_V2_Shape_Without_Readiness()
    {
        // Constructing V1 raises the [Obsolete] compiler warning — we
        // suppress it here deliberately because this test exists precisely
        // to verify the replay-tolerance shape.
#pragma warning disable CS0618
        var v1 = new ExamSimulationSubmitted_V1(
            StudentId: "stu-007",
            SimulationId: "sim-42",
            QuestionsAttempted: 7,
            QuestionsCorrect: 5,
            ScorePercent: 71.4,
            TimeTaken: TimeSpan.FromMinutes(145),
            VisibilityWarnings: 1,
            // V1 carried these; we read them here only to prove they are
            // ignored by the V2-shaped projection.
            ReadinessLowerBound: -0.42,
            ReadinessUpperBound: 0.18,
            SubmittedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
#pragma warning restore CS0618

        // A handler that needs to read either V1 or V2 can fold V1 into
        // V2's shape by dropping the readiness fields. This is the
        // reference implementation of that mapping. Any real handler
        // should follow the same "readiness is ignored" rule.
        var v2Shape = new ExamSimulationSubmitted_V2(
            StudentId: v1.StudentId,
            SimulationId: v1.SimulationId,
            QuestionsAttempted: v1.QuestionsAttempted,
            QuestionsCorrect: v1.QuestionsCorrect,
            ScorePercent: v1.ScorePercent,
            TimeTaken: v1.TimeTaken,
            VisibilityWarnings: v1.VisibilityWarnings,
            SubmittedAt: v1.SubmittedAt);

        Assert.Equal(v1.StudentId, v2Shape.StudentId);
        Assert.Equal(v1.SimulationId, v2Shape.SimulationId);
        Assert.Equal(v1.ScorePercent, v2Shape.ScorePercent);
        Assert.Equal(v1.SubmittedAt, v2Shape.SubmittedAt);

        // The readiness scalars on V1 are intentionally NOT copied. Per
        // ADR-0003 + RDY-080 they are session-scoped — if a session actor
        // recomputes a SessionRiskAssessment it lives on the session actor
        // only and never crosses the persistence boundary.
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Repo root (CLAUDE.md) not found from test binary dir.");
    }
}
