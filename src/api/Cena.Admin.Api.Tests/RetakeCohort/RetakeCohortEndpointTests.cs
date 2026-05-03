// =============================================================================
// Cena Platform — RetakeCohortEndpoint integration tests (prr-238)
//
// Exercises the endpoint handler directly against the in-memory reader.
// Pins:
//
//   (a) ADMIN on own institute → 200 + expected cohort.
//   (b) ADMIN on different institute → 403.
//   (c) SUPER_ADMIN cross-institute → 200.
//   (d) Response carries RetrievalStrengthFraming=true so downstream
//       admin-UI clients can render the retrieval-practice framing.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.StudentPlan;
using Cena.Admin.Api.Features.RetakeCohort;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.RetakeCohort;

public sealed class RetakeCohortEndpointTests
{
    private static ExamTarget MakeTarget(string id, ReasonTag reason)
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
            ArchivedAt: null);
    }

    private sealed class ScriptedPlanReader : IStudentPlanReader
    {
        private readonly Dictionary<string, IReadOnlyList<ExamTarget>> _map;
        public ScriptedPlanReader(Dictionary<string, IReadOnlyList<ExamTarget>> map) { _map = map; }
        public Task<IReadOnlyList<ExamTarget>> ListTargetsAsync(string studentAnonId, bool includeArchived = false, CancellationToken ct = default)
        {
            _map.TryGetValue(studentAnonId, out var list);
            return Task.FromResult<IReadOnlyList<ExamTarget>>(list ?? Array.Empty<ExamTarget>());
        }
        public Task<ExamTarget?> FindTargetAsync(string studentAnonId, ExamTargetId targetId, CancellationToken ct = default)
        {
            _map.TryGetValue(studentAnonId, out var list);
            return Task.FromResult(list?.FirstOrDefault(t => t.Id == targetId));
        }
    }

    private static IRetakeCohortReader BuildReader(string institute)
    {
        var directory = new StaticStudentDirectory(new[]
        {
            ("student-a", institute),
            ("student-b", institute),
        });
        var plans = new Dictionary<string, IReadOnlyList<ExamTarget>>
        {
            ["student-a"] = new[] { MakeTarget("t-a", ReasonTag.Retake) },
            ["student-b"] = new[] { MakeTarget("t-b", ReasonTag.NewSubject) },
        };
        return new InMemoryRetakeCohortReader(directory, new ScriptedPlanReader(plans));
    }

    [Fact]
    public async Task Handle_AdminOwnInstitute_Returns200WithRetakeStudents()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("role", "ADMIN"),
            new Claim("institute_id", "inst-own"),
        }, "test"));
        var reader = BuildReader("inst-own");

        var result = await RetakeCohortEndpoint.HandleAsync(
            "inst-own", http, reader,
            NullLogger<RetakeCohortEndpoint.RetakeCohortMarker>.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<RetakeCohortResponseDto>>(result);
        Assert.True(ok.Value!.RetrievalStrengthFraming);
        Assert.Single(ok.Value.Students);
        Assert.Equal("student-a", ok.Value.Students[0].StudentAnonId);
        Assert.Contains("BAGRUT_MATH_5U", ok.Value.Students[0].RetakeExamCodes);
    }

    [Fact]
    public async Task Handle_AdminDifferentInstitute_Returns403()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("role", "ADMIN"),
            new Claim("institute_id", "inst-own"),
        }, "test"));
        var reader = BuildReader("inst-other");

        var result = await RetakeCohortEndpoint.HandleAsync(
            "inst-other", http, reader,
            NullLogger<RetakeCohortEndpoint.RetakeCohortMarker>.Instance,
            CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task Handle_SuperAdminCrossInstitute_Returns200()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("role", "SUPER_ADMIN"),
        }, "test"));
        var reader = BuildReader("inst-other");

        var result = await RetakeCohortEndpoint.HandleAsync(
            "inst-other", http, reader,
            NullLogger<RetakeCohortEndpoint.RetakeCohortMarker>.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<RetakeCohortResponseDto>>(result);
        Assert.NotNull(ok.Value);
    }
}
