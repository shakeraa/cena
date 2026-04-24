// =============================================================================
// Cena Platform — RetakeCohortReader tests (prr-238)
//
// Exercises the reader in isolation against the in-memory plan store +
// a static directory. Asserts:
//
//   (a) Students with zero active Retake targets are excluded.
//   (b) Students with one or more active Retake targets are included.
//   (c) Archived Retake targets do NOT count (reader uses active-only).
//   (d) Institute filter on the directory is honoured.
// =============================================================================

using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class RetakeCohortReaderTests
{
    private static ExamTarget MakeTarget(string id, ReasonTag? reason, bool archived = false)
    {
        return new ExamTarget(
            Id: new ExamTargetId(id),
            Source: ExamTargetSource.Student,
            AssignedById: new UserId("student-1"),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            QuestionPaperCodes: new[] { "035581" },
            Sitting: new SittingCode("2026-2027", SittingSeason.Summer, SittingMoed.A),
            PerPaperSittingOverride: null,
            WeeklyHours: 5,
            ReasonTag: reason,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            ArchivedAt: archived ? DateTimeOffset.UtcNow.AddDays(-1) : null);
    }

    private sealed class ScriptedPlanReader : IStudentPlanReader
    {
        private readonly Dictionary<string, IReadOnlyList<ExamTarget>> _map;

        public ScriptedPlanReader(Dictionary<string, IReadOnlyList<ExamTarget>> map)
        {
            _map = map;
        }

        public Task<IReadOnlyList<ExamTarget>> ListTargetsAsync(
            string studentAnonId,
            bool includeArchived = false,
            CancellationToken ct = default)
        {
            if (!_map.TryGetValue(studentAnonId, out var list))
            {
                return Task.FromResult<IReadOnlyList<ExamTarget>>(Array.Empty<ExamTarget>());
            }
            if (includeArchived) return Task.FromResult(list);
            return Task.FromResult<IReadOnlyList<ExamTarget>>(
                list.Where(t => t.IsActive).ToArray());
        }

        public Task<ExamTarget?> FindTargetAsync(
            string studentAnonId,
            ExamTargetId targetId,
            CancellationToken ct = default)
        {
            _map.TryGetValue(studentAnonId, out var list);
            return Task.FromResult(list?.FirstOrDefault(t => t.Id == targetId));
        }
    }

    [Fact]
    public async Task ListRetakeCohort_IncludesOnlyStudentsWithActiveRetakeTargets()
    {
        var directory = new StaticStudentDirectory(new[]
        {
            ("student-retake", "inst-1"),
            ("student-newsubject", "inst-1"),
            ("student-mixed", "inst-1"),
            ("student-archived-retake", "inst-1"),
        });
        var plans = new Dictionary<string, IReadOnlyList<ExamTarget>>
        {
            ["student-retake"] = new[] { MakeTarget("t-1", ReasonTag.Retake) },
            ["student-newsubject"] = new[] { MakeTarget("t-2", ReasonTag.NewSubject) },
            ["student-mixed"] = new[]
            {
                MakeTarget("t-3a", ReasonTag.NewSubject),
                MakeTarget("t-3b", ReasonTag.Retake),
            },
            ["student-archived-retake"] = new[] { MakeTarget("t-4", ReasonTag.Retake, archived: true) },
        };
        var reader = new InMemoryRetakeCohortReader(directory, new ScriptedPlanReader(plans));

        var rows = await reader.ListRetakeCohortAsync("inst-1");

        var anonIds = rows.Select(r => r.StudentAnonId).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "student-mixed", "student-retake" }, anonIds);
        var mixedRow = rows.First(r => r.StudentAnonId == "student-mixed");
        Assert.Single(mixedRow.RetakeTargets);
        Assert.Equal(ReasonTag.Retake, mixedRow.RetakeTargets[0].ReasonTag);
    }

    [Fact]
    public async Task ListRetakeCohort_FiltersByInstitute()
    {
        var directory = new StaticStudentDirectory(new[]
        {
            ("student-a", "inst-1"),
            ("student-b", "inst-2"),
        });
        var plans = new Dictionary<string, IReadOnlyList<ExamTarget>>
        {
            ["student-a"] = new[] { MakeTarget("t-a", ReasonTag.Retake) },
            ["student-b"] = new[] { MakeTarget("t-b", ReasonTag.Retake) },
        };
        var reader = new InMemoryRetakeCohortReader(directory, new ScriptedPlanReader(plans));

        var inst1 = await reader.ListRetakeCohortAsync("inst-1");
        var inst2 = await reader.ListRetakeCohortAsync("inst-2");

        Assert.Single(inst1);
        Assert.Equal("student-a", inst1[0].StudentAnonId);
        Assert.Single(inst2);
        Assert.Equal("student-b", inst2[0].StudentAnonId);
    }

    [Fact]
    public async Task ListRetakeCohort_EmptyInstitute_ReturnsEmpty()
    {
        var directory = new StaticStudentDirectory(Array.Empty<(string, string)>());
        var plans = new Dictionary<string, IReadOnlyList<ExamTarget>>();
        var reader = new InMemoryRetakeCohortReader(directory, new ScriptedPlanReader(plans));

        var rows = await reader.ListRetakeCohortAsync("inst-empty");

        Assert.Empty(rows);
    }
}
