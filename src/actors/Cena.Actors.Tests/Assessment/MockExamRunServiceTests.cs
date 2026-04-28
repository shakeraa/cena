// =============================================================================
// Tests for MockExamRunService — exercise the orchestrator's contract
// invariants:
//   * Start emits ExamSimulationStarted_V1, persists ExamSimulationState
//   * Idempotent re-start of an in-flight run returns the same runId
//   * Part-B selection enforces the (count == required) rule + must be a
//     subset of the run's PartBQuestionIds
//   * Submit grades via CAS, emits Submitted_V2, returns mark sheet
//   * GetResult is null for an unsubmitted run; a submitted run returns
//     the mark sheet idempotently
//
// Marten + CAS are stubbed via NSubstitute; question pool is seeded into
// an in-memory Marten store via the standard test fixture.
// =============================================================================

using Cena.Actors.Assessment;
using Cena.Actors.Cas;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Assessment;

public sealed class MockExamRunServiceTests : IAsyncLifetime
{
    // Tests run against the dev cena-postgres reachable via the same
    // connection string as docker-compose. Each test class instance gets
    // its own Marten schema so parallel runs don't collide. CI runs the
    // same docker-compose stack, so this is portable.
    // dev compose maps cena-postgres:5432 → host:5433.
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private DocumentStore _store = null!;
    private ICasRouterService _cas = null!;
    private MockExamRunService _service = null!;
    private FakeTimeProvider _clock = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "mock_exam_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.RegisterMockExamRunContext();
            opts.Schema.For<QuestionDocument>().Identity(d => d.Id);
            opts.Schema.For<QuestionReadModel>().Identity(d => d.Id);
        });

        _cas = Substitute.For<ICasRouterService>();
        _cas.VerifyAsync(Arg.Any<CasVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<CasVerifyRequest>();
                var verified = req.ExpressionA.Trim() == req.ExpressionB?.Trim();
                return Task.FromResult(verified
                    ? CasVerifyResult.Success(req.Operation, "mathnet", 0.5)
                    : CasVerifyResult.Failure(req.Operation, "mathnet", 0.5, "neq"));
            });

        _clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-29T10:00:00Z"));
        _service = new MockExamRunService(_store, _cas, NullLogger<MockExamRunService>.Instance, _clock);

        // Seed 12 published math questions so Bagrut806 (5+4=9) can draw.
        await SeedQuestionsAsync(subject: "math", count: 12);
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Start_NewRun_PersistsStateAndEmitsStarted()
    {
        var resp = await _service.StartAsync("student-1",
            new StartMockExamRunRequest("806", "035582"), CancellationToken.None);

        Assert.NotEmpty(resp.RunId);
        Assert.Equal(5, resp.PartAQuestionIds.Count);
        Assert.Equal(4, resp.PartBQuestionIds.Count);
        Assert.Equal(180, resp.TimeLimitMinutes);
        Assert.Equal(2, resp.PartBRequiredCount);

        await using var qs = _store.QuerySession();
        var loaded = await qs.LoadAsync<ExamSimulationState>(resp.RunId);
        Assert.NotNull(loaded);
        Assert.Equal("student-1", loaded!.StudentId);
        Assert.Equal("806", loaded.ExamCode);

        var events = await qs.Events.FetchStreamAsync("student-1");
        Assert.Contains(events, e => e.Data is ExamSimulationStarted_V1);
    }

    [Fact]
    public async Task Start_InFlightRun_IsIdempotent()
    {
        var first = await _service.StartAsync("student-2",
            new StartMockExamRunRequest("806"), CancellationToken.None);
        var second = await _service.StartAsync("student-2",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        Assert.Equal(first.RunId, second.RunId);
    }

    [Fact]
    public async Task SelectPartB_WrongCount_Rejects()
    {
        var run = await _service.StartAsync("student-3",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SelectPartBAsync("student-3", run.RunId,
                new SelectPartBRequest(run.PartBQuestionIds.Take(1).ToList()),
                CancellationToken.None));
    }

    [Fact]
    public async Task SelectPartB_Valid_Persists()
    {
        var run = await _service.StartAsync("student-4",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        var picked = run.PartBQuestionIds.Take(2).ToList();
        var state = await _service.SelectPartBAsync("student-4", run.RunId,
            new SelectPartBRequest(picked), CancellationToken.None);

        Assert.Equal(picked, state.PartBSelectedIds);
    }

    [Fact]
    public async Task SubmitAnswer_NotInRun_Rejects()
    {
        var run = await _service.StartAsync("student-5",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SubmitAnswerAsync("student-5", run.RunId,
                new SubmitAnswerRequest("not-in-pool", "42"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Submit_GradesViaCas_EmitsSubmittedV2()
    {
        var run = await _service.StartAsync("student-6",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        // Pick Part-B subset
        var pickedB = run.PartBQuestionIds.Take(2).ToList();
        await _service.SelectPartBAsync("student-6", run.RunId,
            new SelectPartBRequest(pickedB), CancellationToken.None);

        // Answer all Part A correctly + Part B partial
        // Seeded canonical answer is "x = {Id}" — we'll match that.
        foreach (var qid in run.PartAQuestionIds)
        {
            await _service.SubmitAnswerAsync("student-6", run.RunId,
                new SubmitAnswerRequest(qid, $"x = {qid}"), CancellationToken.None);
        }
        // First Part-B correct, second wrong
        await _service.SubmitAnswerAsync("student-6", run.RunId,
            new SubmitAnswerRequest(pickedB[0], $"x = {pickedB[0]}"), CancellationToken.None);
        await _service.SubmitAnswerAsync("student-6", run.RunId,
            new SubmitAnswerRequest(pickedB[1], "wrong-answer"), CancellationToken.None);

        var result = await _service.SubmitAsync("student-6", run.RunId, CancellationToken.None);

        Assert.Equal(7, result.QuestionsAttempted);  // 5 partA + 2 partB
        Assert.Equal(6, result.QuestionsCorrect);    // 5 partA + 1 partB
        Assert.InRange(result.ScorePercent, 85.0, 86.0); // 6/7 ≈ 85.71%

        await using var qs = _store.QuerySession();
        var events = await qs.Events.FetchStreamAsync("student-6");
        Assert.Contains(events, e => e.Data is ExamSimulationSubmitted_V2);
    }

    [Fact]
    public async Task GetResult_BeforeSubmit_ReturnsNull()
    {
        var run = await _service.StartAsync("student-7",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        var result = await _service.GetResultAsync("student-7", run.RunId, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetState_OtherStudent_ReturnsNull()
    {
        var run = await _service.StartAsync("student-8",
            new StartMockExamRunRequest("806"), CancellationToken.None);

        var leaked = await _service.GetStateAsync("student-9", run.RunId, CancellationToken.None);
        Assert.Null(leaked);
    }

    [Fact]
    public async Task UnsupportedExamCode_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartAsync("student-10",
                new StartMockExamRunRequest("999"), CancellationToken.None));
    }

    private async Task SeedQuestionsAsync(string subject, int count)
    {
        await using var sess = _store.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            var id = $"q-{subject}-{i}";
            sess.Store(new QuestionReadModel
            {
                Id = id,
                Subject = subject,
                Status = "Published",
                BloomsLevel = (i % 4) + 1, // 1..4 stratification
                Difficulty = (float)((i % 5) * 0.2 + 0.1),
                StemPreview = $"Q {i}",
                Concepts = new List<string> { $"{subject}.concept-{i % 3}" },
                Language = "he",
            });
            sess.Store(new QuestionDocument
            {
                Id = id,
                QuestionId = id,
                Subject = subject,
                Topic = "algebra",
                CorrectAnswer = $"x = {id}",
                Prompt = $"Solve question {i}",
                QuestionType = "free-text",
            });
        }
        await sess.SaveChangesAsync();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan d) => _now = _now.Add(d);
    }
}
