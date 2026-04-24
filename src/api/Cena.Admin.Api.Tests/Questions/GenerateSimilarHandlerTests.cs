// =============================================================================
// Cena Platform — GenerateSimilarHandler tests (RDY-058)
//
// Pure handler tests — no TestServer, no endpoint wrapper. Exercises the
// full control flow of /api/admin/questions/{id}/generate-similar:
//   - source load (happy + 404)
//   - default + custom difficulty band
//   - language override + fallback
//   - count clamping
//   - AiGenerationService invocation shape
//   - CAS-failure counter propagation (response is returned unchanged)
//
// Marten session is substituted through IDocumentStore; the real
// AiGenerationService is substituted too so we don't hit an LLM from a
// unit test.
// =============================================================================

using Cena.Actors.Questions;
using Cena.Admin.Api;
using Cena.Admin.Api.QualityGate;
using Cena.Admin.Api.Questions;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Questions;

public sealed class GenerateSimilarHandlerTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();
    private readonly IDocumentSession _writeSession = Substitute.For<IDocumentSession>();
    private readonly IAiGenerationService _ai = Substitute.For<IAiGenerationService>();
    private readonly IQualityGateService _quality = Substitute.For<IQualityGateService>();
    private readonly ILogger _logger = NullLogger.Instance;

    public GenerateSimilarHandlerTests()
    {
        _store.QuerySession().Returns(_querySession);
        _store.LightweightSession().Returns(_writeSession);
        // writeSession.Events is not stubbed — the handler's provenance
        // append runs inside a try/catch that logs + swallows any failure
        // (best-effort provenance). Unit tests don't need the append to
        // actually succeed; we only care about the returned IResult.
    }

    private void SeedSource(QuestionReadModel? source) =>
        _querySession.LoadAsync<QuestionReadModel>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(source);

    private void SeedAi(BatchGenerateResponse response) =>
        _ai.BatchGenerateAsync(Arg.Any<BatchGenerateRequest>(), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(response));

    private static QuestionReadModel SourceQuestion(
        string id = "q-1",
        string subject = "math",
        string topic = "derivatives",
        string grade = "5 Units",
        int bloom = 3,
        float difficulty = 0.6f,
        string language = "he") =>
        new()
        {
            Id = id,
            Subject = subject,
            Topic = topic,
            Grade = grade,
            BloomsLevel = bloom,
            Difficulty = difficulty,
            Language = language,
            StemPreview = "Find the derivative of x^2",
        };

    private static BatchGenerateResponse EmptyAiResponse(int total = 3) =>
        new(Success: true,
            Results: Array.Empty<BatchGenerateResult>(),
            TotalGenerated: total,
            PassedQualityGate: total,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet-4.5",
            Error: null);

    // -----------------------------------------------------------------------
    // Minimal IResult readers — same shape as the Bagrut endpoint tests.
    // -----------------------------------------------------------------------
    private static int StatusOf(IResult r)
    {
        var ctx = Ctx();
        r.ExecuteAsync(ctx).GetAwaiter().GetResult();
        return ctx.Response.StatusCode;
    }
    private static string BodyOf(IResult r)
    {
        var ctx = Ctx();
        r.ExecuteAsync(ctx).GetAwaiter().GetResult();
        ctx.Response.Body.Position = 0;
        using var sr = new StreamReader(ctx.Response.Body);
        return sr.ReadToEnd();
    }
    private static DefaultHttpContext Ctx() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    // -----------------------------------------------------------------------
    [Fact]
    public async Task Happy_Path_Returns_BatchGenerateResponse()
    {
        SeedSource(SourceQuestion());
        SeedAi(EmptyAiResponse(total: 3));

        var result = await GenerateSimilarHandler.HandleAsync(
            questionId: "q-1",
            body: new GenerateSimilarRequest(Count: 3),
            store: _store,
            ai: _ai,
            qualityGate: _quality,
            generatedBy: "curator-1",
            logger: _logger);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Contains("claude-sonnet-4.5", BodyOf(result));
    }

    [Fact]
    public async Task Source_Not_Found_Returns_404()
    {
        SeedSource(null);

        var result = await GenerateSimilarHandler.HandleAsync(
            "missing", new GenerateSimilarRequest(), _store, _ai, _quality, "c1", _logger);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Contains("question_not_found", BodyOf(result));
    }

    [Fact]
    public async Task Default_Difficulty_Band_Is_Source_Plus_Minus_0_15()
    {
        SeedSource(SourceQuestion(difficulty: 0.6f));
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1", new GenerateSimilarRequest(Count: 2), _store, _ai, _quality, "c1", _logger);

        Assert.NotNull(captured);
        Assert.InRange(captured!.MinDifficulty, 0.449f, 0.451f);   // 0.6 - 0.15
        Assert.InRange(captured.MaxDifficulty, 0.749f, 0.751f);    // 0.6 + 0.15
    }

    [Fact]
    public async Task Default_Difficulty_Band_Clamps_At_Extremes()
    {
        SeedSource(SourceQuestion(difficulty: 0.05f));
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1", new GenerateSimilarRequest(), _store, _ai, _quality, "c1", _logger);

        // source - 0.15 = -0.10 → clamps to 0
        Assert.Equal(0f, captured!.MinDifficulty);
        Assert.InRange(captured.MaxDifficulty, 0.199f, 0.201f);
    }

    [Fact]
    public async Task Custom_Difficulty_Band_Is_Honored()
    {
        SeedSource(SourceQuestion(difficulty: 0.6f));
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1",
            new GenerateSimilarRequest(MinDifficulty: 0.8f, MaxDifficulty: 0.95f),
            _store, _ai, _quality, "c1", _logger);

        Assert.Equal(0.8f, captured!.MinDifficulty);
        Assert.Equal(0.95f, captured.MaxDifficulty);
    }

    [Fact]
    public async Task Difficulty_Band_Flipped_Is_Swapped()
    {
        SeedSource(SourceQuestion());
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1",
            new GenerateSimilarRequest(MinDifficulty: 0.9f, MaxDifficulty: 0.3f),
            _store, _ai, _quality, "c1", _logger);

        Assert.Equal(0.3f, captured!.MinDifficulty);
        Assert.Equal(0.9f, captured.MaxDifficulty);
    }

    [Fact]
    public async Task Language_Override_Wins_Over_Source()
    {
        SeedSource(SourceQuestion(language: "he"));
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1",
            new GenerateSimilarRequest(Language: "en"),
            _store, _ai, _quality, "c1", _logger);

        Assert.Equal("en", captured!.Language);
    }

    [Fact]
    public async Task Language_Default_Falls_Back_To_Source()
    {
        SeedSource(SourceQuestion(language: "ar"));
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1", new GenerateSimilarRequest(Language: null), _store, _ai, _quality, "c1", _logger);

        Assert.Equal("ar", captured!.Language);
    }

    [Theory]
    [InlineData(0, 1)]          // below floor → clamps to 1
    [InlineData(-5, 1)]         // negative → clamps to 1
    [InlineData(25, 20)]        // above ceiling → clamps to 20
    [InlineData(7, 7)]          // in-band → passes
    public async Task Count_Is_Clamped_To_1_20(int requested, int expected)
    {
        SeedSource(SourceQuestion());
        SeedAi(EmptyAiResponse());

        BatchGenerateRequest? captured = null;
        _ai.BatchGenerateAsync(Arg.Do<BatchGenerateRequest>(r => captured = r), Arg.Any<IQualityGateService>())
            .Returns(Task.FromResult(EmptyAiResponse()));

        await GenerateSimilarHandler.HandleAsync(
            "q-1", new GenerateSimilarRequest(Count: requested), _store, _ai, _quality, "c1", _logger);

        Assert.Equal(expected, captured!.Count);
    }

    [Fact]
    public async Task Cas_Failure_Counters_Flow_Through_Unchanged()
    {
        SeedSource(SourceQuestion());
        var withDrops = new BatchGenerateResponse(
            Success: true,
            Results: Array.Empty<BatchGenerateResult>(),
            TotalGenerated: 2,
            PassedQualityGate: 2,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet-4.5",
            Error: null,
            DroppedForCasFailure: 3,
            CasDropReasons: new[]
            {
                new CasDropReason("x^2 =", "sympy", "malformed", 42.1),
                new CasDropReason("y =", "mathnet", "unverifiable", 7.0),
            });
        SeedAi(withDrops);

        var result = await GenerateSimilarHandler.HandleAsync(
            "q-1", new GenerateSimilarRequest(Count: 5),
            _store, _ai, _quality, "c1", _logger);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        var body = BodyOf(result);
        Assert.Contains("droppedForCasFailure", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", body);
    }

    [Fact]
    public async Task Empty_Question_Id_Returns_400()
    {
        var result = await GenerateSimilarHandler.HandleAsync(
            "", new GenerateSimilarRequest(), _store, _ai, _quality, "c1", _logger);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("invalid_id", BodyOf(result));
    }

    [Fact]
    public void ResolveDifficultyBand_Inherits_From_Source_When_Body_Empty()
    {
        var src = SourceQuestion(difficulty: 0.4f);
        var (min, max) = GenerateSimilarHandler.ResolveDifficultyBand(
            src, new GenerateSimilarRequest());

        Assert.InRange(min, 0.249f, 0.251f);
        Assert.InRange(max, 0.549f, 0.551f);
    }
}
