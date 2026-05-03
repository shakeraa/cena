// =============================================================================
// MockExamRunServiceTests — fixture / setup helpers extracted from the test
// class so the file-size 500-LOC ratchet (FileSize500LocTest, ADR-0012) does
// not engage. Test bodies stay in the sibling MockExamRunServiceTests.cs;
// this partial owns InitializeAsync / DisposeAsync / SeedQuestionsAsync /
// FakeTimeProvider — the boilerplate that doesn't change per assertion.
// =============================================================================

using Cena.Actors.Assessment;
using Cena.Actors.Cas;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Assessment;

public sealed partial class MockExamRunServiceTests : IAsyncLifetime
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
        var catalog = new BagrutPaperStructureCatalog(
            _store, NullLogger<BagrutPaperStructureCatalog>.Instance);
        // Persist seed structures so the service can resolve them during tests.
        await catalog.UpsertSeedStructuresAsync(CancellationToken.None);
        var gate = new ItemDeliveryGate(NullLogger<ItemDeliveryGate>.Instance);
        _service = new MockExamRunService(_store, _cas, catalog, gate, NullLogger<MockExamRunService>.Instance, _clock);

        // Seed published math questions across all topics referenced by
        // the seeded BagrutPaperStructure (806/*). 12 topics × 3 blooms =
        // 36 items — enough headroom for Bagrut806 (9 slots) + Part B
        // diversity probes.
        await SeedQuestionsAsync(subject: "math", count: 36);
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedQuestionsAsync(string subject, int count)
    {
        // Topic ids that match the seeded BagrutPaperStructure (806/default
        // and 806/035582). Includes both fine-grained topics ("math.algebra
        // .quadratics") and coarse families ("math.algebra"). This way
        // the slot draw matches deterministically — no need for the
        // structure-fallback path during the happy-path tests.
        var topics = new[]
        {
            "math.algebra", "math.algebra.quadratics", "math.trigonometry",
            "math.calculus", "math.calculus.derivative", "math.calculus.integral",
            "math.functions", "math.geometry", "math.geometry.plane",
            "math.probability", "math.vectors", "math.growthDecay",
        };

        await using var sess = _store.LightweightSession();
        var idx = 0;
        foreach (var topic in topics)
        {
            // Each topic gets 3 items at bloom levels 2, 3, 4 so any slot
            // band (2-3 for Part A, 3-4 for Part B) finds candidates.
            for (var bloom = 2; bloom <= 4; bloom++)
            {
                var id = $"q-{subject}-{idx++}";
                sess.Store(new QuestionReadModel
                {
                    Id = id,
                    Subject = subject,
                    Status = "Published",
                    BloomsLevel = bloom,
                    Difficulty = (float)(bloom * 0.2 + 0.1),
                    StemPreview = $"Test Q for {topic} bloom={bloom}",
                    Concepts = new List<string> { topic, subject },
                    Language = "he",
                });
                sess.Store(new QuestionDocument
                {
                    Id = id,
                    QuestionId = id,
                    Subject = subject,
                    Topic = topic,
                    CorrectAnswer = $"x = {id}",
                    Prompt = $"Solve question {idx}",
                    QuestionType = "free-text",
                });
            }
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
