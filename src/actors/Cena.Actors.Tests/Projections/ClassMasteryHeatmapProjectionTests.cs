// =============================================================================
// Cena Platform — ClassMasteryHeatmapProjection unit tests (RDY-070 Phase 1A)
//
// Covers:
//   - Rebuild determinism:      same events → identical document (Dina's ask)
//   - Event re-ordering safety: last-write-wins on mastery, SampleSize stable
//   - Roster guard:             attempts from non-enrolled students are no-ops
//   - Topic resolution gate:    concepts without a Ministry topic are no-ops
//   - Withdrawal:               removing a student preserves historical cells
//   - Performance:              32 students × 10 topics × N attempts under 1 s
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Events;
using Cena.Actors.Projections;

namespace Cena.Actors.Tests.Projections;

public class ClassMasteryHeatmapProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

    private const string Institute = "inst-cena";
    private const string Classroom = "classroom-9a";

    // Small fixed resolver used across most tests.
    private static readonly StubResolver BasicResolver = new(new Dictionary<string, string>
    {
        ["lo-alg-linear-equations"]    = "algebra-review",
        ["lo-alg-quadratic-equations"] = "algebra-review",
        ["lo-fn-definition"]           = "functions-and-graphs",
        ["lo-trig-unit-circle"]        = "trigonometry",
        ["lo-calc-derivative-rules"]   = "derivatives",
    });

    // ── Rebuild / determinism ────────────────────────────────────────────────

    [Fact]
    public void Rebuild_FromSameEvents_ProducesIdenticalDocument()
    {
        var projection = new ClassMasteryHeatmapProjection();

        var roster = new[] { "student-1", "student-2" };
        var attempts = new[]
        {
            new AttemptSample("student-1", "lo-alg-linear-equations",  0.40, T0.AddMinutes(1)),
            new AttemptSample("student-1", "lo-alg-linear-equations",  0.55, T0.AddMinutes(2)),
            new AttemptSample("student-2", "lo-fn-definition",         0.30, T0.AddMinutes(3)),
            new AttemptSample("student-2", "lo-trig-unit-circle",      0.70, T0.AddMinutes(4)),
        };

        var first = projection.Rebuild(Institute, Classroom, roster, attempts, BasicResolver);
        var second = projection.Rebuild(Institute, Classroom, roster, attempts, BasicResolver);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.AttemptCount, second.AttemptCount);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Equal(first.StudentAnonIds, second.StudentAnonIds);
        Assert.Equal(first.TopicSlugs, second.TopicSlugs);
        Assert.Equal(first.Cells.Count, second.Cells.Count);
        foreach (var (key, cell) in first.Cells)
        {
            Assert.True(second.Cells.TryGetValue(key, out var twin),
                $"cell {key} missing in second rebuild");
            Assert.Equal(cell.Mastery, twin!.Mastery, 6);
            Assert.Equal(cell.SampleSize, twin.SampleSize);
            Assert.Equal(cell.LastAttemptAt, twin.LastAttemptAt);
        }
    }

    [Fact]
    public void Rebuild_RegardlessOfEventOrder_YieldsLatestMasteryPerCell()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var roster = new[] { "student-1" };

        var inOrder = new[]
        {
            new AttemptSample("student-1", "lo-alg-linear-equations", 0.40, T0.AddMinutes(1)),
            new AttemptSample("student-1", "lo-alg-linear-equations", 0.62, T0.AddMinutes(2)),
            new AttemptSample("student-1", "lo-alg-linear-equations", 0.81, T0.AddMinutes(3)),
        };
        var reversed = inOrder.Reverse().ToArray();

        var a = projection.Rebuild(Institute, Classroom, roster, inOrder,  BasicResolver);
        var b = projection.Rebuild(Institute, Classroom, roster, reversed, BasicResolver);

        var key = ClassMasteryHeatmapDocument.CellKey("student-1", "algebra-review");
        Assert.Equal(0.81, a.Cells[key].Mastery, 6);
        Assert.Equal(0.81, b.Cells[key].Mastery, 6);
        Assert.Equal(3,    a.Cells[key].SampleSize);
        Assert.Equal(3,    b.Cells[key].SampleSize);
    }

    [Fact]
    public void EmptyRoster_YieldsEmptyDocument()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var doc = projection.Rebuild(Institute, Classroom,
            Array.Empty<string>(), Array.Empty<AttemptSample>(), BasicResolver);

        Assert.Equal($"heatmap-{Institute}-{Classroom}", doc.Id);
        Assert.Empty(doc.StudentAnonIds);
        Assert.Empty(doc.TopicSlugs);
        Assert.Empty(doc.Cells);
        Assert.Equal(0, doc.AttemptCount);
    }

    // ── Roster + resolver guards ─────────────────────────────────────────────

    [Fact]
    public void AttemptsFromNonEnrolledStudents_AreIgnored()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var roster = new[] { "student-1" };
        var attempts = new[]
        {
            new AttemptSample("student-1", "lo-alg-linear-equations", 0.5, T0.AddMinutes(1)),
            new AttemptSample("poacher-42", "lo-fn-definition",       0.9, T0.AddMinutes(2)),
        };

        var doc = projection.Rebuild(Institute, Classroom, roster, attempts, BasicResolver);

        Assert.Single(doc.StudentAnonIds);
        Assert.Single(doc.Cells);
        Assert.DoesNotContain(doc.Cells,
            kv => kv.Key.StartsWith("poacher-42|", StringComparison.Ordinal));
    }

    [Fact]
    public void ConceptsWithoutTopicMapping_AreIgnored()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var roster = new[] { "student-1" };
        var attempts = new[]
        {
            new AttemptSample("student-1", "lo-alg-linear-equations", 0.5, T0.AddMinutes(1)),
            new AttemptSample("student-1", "lo-uncharted-territory",  0.9, T0.AddMinutes(2)),
        };

        var doc = projection.Rebuild(Institute, Classroom, roster, attempts, BasicResolver);

        Assert.Single(doc.TopicSlugs);
        Assert.Equal("algebra-review", doc.TopicSlugs[0]);
        Assert.Single(doc.Cells);
    }

    // ── Enrollment lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void Withdrawal_RemovesFromRosterButPreservesCells()
    {
        var projection = new ClassMasteryHeatmapProjection();

        var doc = projection.NewDocument(Institute, Classroom);
        projection.ApplyEnrollment(doc, "student-1");
        projection.ApplyEnrollment(doc, "student-2");

        projection.ApplyAttempt(doc, "student-1", "lo-alg-linear-equations", 0.6, T0.AddMinutes(1), BasicResolver);
        projection.ApplyAttempt(doc, "student-2", "lo-fn-definition",        0.4, T0.AddMinutes(2), BasicResolver);

        projection.ApplyWithdrawal(doc, "student-1");

        Assert.DoesNotContain("student-1", doc.StudentAnonIds);
        Assert.Contains("student-2", doc.StudentAnonIds);

        // Historical cell for the withdrawn student is preserved so
        // end-of-term review still shows where they stood.
        var key = ClassMasteryHeatmapDocument.CellKey("student-1", "algebra-review");
        Assert.True(doc.Cells.ContainsKey(key));
    }

    [Fact]
    public void EnrollmentIsIdempotent()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var doc = projection.NewDocument(Institute, Classroom);

        projection.ApplyEnrollment(doc, "student-1");
        projection.ApplyEnrollment(doc, "student-1");
        projection.ApplyEnrollment(doc, "student-1");

        Assert.Single(doc.StudentAnonIds);
    }

    // ── Typed event overloads ────────────────────────────────────────────────

    [Fact]
    public void Apply_TypedAttemptEvents_UpdatesCell()
    {
        var projection = new ClassMasteryHeatmapProjection();
        var doc = projection.NewDocument(Institute, Classroom);
        projection.ApplyEnrollment(doc, "student-1");

        var v1 = new ConceptAttempted_V1(
            StudentId: "student-1", ConceptId: "lo-alg-linear-equations", SessionId: "s1",
            IsCorrect: true, ResponseTimeMs: 1000, QuestionId: "q1", QuestionType: "mcq",
            MethodologyActive: "adaptive", ErrorType: "", PriorMastery: 0.30,
            PosteriorMastery: 0.45, HintCountUsed: 0, WasSkipped: false, AnswerHash: "a1",
            BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
            Timestamp: T0.AddMinutes(1));

        var v2 = new ConceptAttempted_V2(
            StudentId: "student-1", ConceptId: "lo-alg-linear-equations", SessionId: "s1",
            IsCorrect: true, ResponseTimeMs: 1200, QuestionId: "q2", QuestionType: "mcq",
            MethodologyActive: "adaptive", ErrorType: "", PriorMastery: 0.45,
            PosteriorMastery: 0.65, HintCountUsed: 0, WasSkipped: false, AnswerHash: "a2",
            BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
            Timestamp: T0.AddMinutes(2), Duration: TimeSpan.FromSeconds(10));

        var v3 = new ConceptAttempted_V3(
            StudentId: "student-1", ConceptId: "lo-alg-linear-equations", SessionId: "s1",
            IsCorrect: true, ResponseTimeMs: 900, QuestionId: "q3", QuestionType: "mcq",
            MethodologyActive: "adaptive", ErrorType: "", PriorMastery: 0.65,
            PosteriorMastery: 0.80, HintCountUsed: 0, WasSkipped: false, AnswerHash: "a3",
            BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
            Timestamp: T0.AddMinutes(3), Duration: TimeSpan.FromSeconds(12),
            EnrollmentId: "enr-1");

        projection.Apply(doc, v1, BasicResolver);
        projection.Apply(doc, v2, BasicResolver);
        projection.Apply(doc, v3, BasicResolver);

        var key = ClassMasteryHeatmapDocument.CellKey("student-1", "algebra-review");
        Assert.Equal(0.80, doc.Cells[key].Mastery, 6);
        Assert.Equal(3,    doc.Cells[key].SampleSize);
        Assert.Equal(3,    doc.AttemptCount);
    }

    // ── Performance ──────────────────────────────────────────────────────────

    [Fact]
    public void Rebuild_32Students_10Topics_4Attempts_Each_Under500ms()
    {
        // Ofir's 32-student classroom × the 10 authored topics × 4 attempts
        // per (student, topic) = 1,280 attempts. The perf budget is 1 s
        // end-to-end; the projection itself should finish well under 500 ms.
        var projection = new ClassMasteryHeatmapProjection();
        var roster = Enumerable.Range(1, 32).Select(i => $"student-{i:D2}").ToArray();
        var topics = new[]
        {
            "algebra-review", "functions-and-graphs", "analytic-geometry",
            "trigonometry", "sequences-and-series", "limits-and-continuity",
            "derivatives", "applications-of-derivatives", "integrals",
            "probability-and-statistics",
        };
        var conceptByTopic = topics.ToDictionary(t => $"lo-{t}", t => t);
        var resolver = new StubResolver(conceptByTopic);

        var attempts = new List<AttemptSample>();
        var rng = new Random(42);
        foreach (var student in roster)
        {
            foreach (var topic in topics)
            {
                for (int i = 0; i < 4; i++)
                {
                    attempts.Add(new AttemptSample(
                        student,
                        $"lo-{topic}",
                        Math.Clamp(rng.NextDouble(), 0, 1),
                        T0.AddMinutes(i)));
                }
            }
        }

        var sw = Stopwatch.StartNew();
        var doc = projection.Rebuild(Institute, Classroom, roster, attempts, resolver);
        sw.Stop();

        Assert.Equal(32, doc.StudentAnonIds.Count);
        Assert.Equal(10, doc.TopicSlugs.Count);
        Assert.Equal(32 * 10, doc.Cells.Count);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"rebuild took {sw.ElapsedMilliseconds}ms, budget 500ms");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private sealed class StubResolver : IConceptTopicResolver
    {
        private readonly IReadOnlyDictionary<string, string> _map;
        public StubResolver(IReadOnlyDictionary<string, string> map) => _map = map;
        public string? TopicSlugFor(string conceptId)
            => _map.TryGetValue(conceptId, out var slug) ? slug : null;
    }
}
