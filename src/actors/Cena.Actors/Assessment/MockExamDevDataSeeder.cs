// =============================================================================
// Cena Platform — MockExamDevDataSeeder (Phase 1C)
//
// Activates the Bagrut שאלון playbook in dev by seeding:
//   1. The canonical paper structures (806/default, 806/035582,
//      806/035581, 807/default, 036/default) into BagrutPaperStructureDocument.
//   2. ~50 Published TeacherAuthoredOriginal-provenance question items
//      across the topic ids the structures reference. Items are
//      tagged Subject = "math" / "physics" so MartenQuestionPool's
//      filter picks them up. CanonicalAnswer is set so the CAS-routed
//      grader can verify; questions are simple enough that
//      MathNet/SymPy resolve in <50ms.
//
// Idempotent: keyed by deterministic id "exam-prep-seed-{topic}-{i}",
// re-run is a no-op upsert. Runs on actor-host startup when
// `Cena:ExamPrep:DevSeed:Enabled` is true (default true in
// Development environment).
//
// Why not "real" Bagrut variants here: those go through the
// CAS-gated authoring pipeline (claude-10's IngestionJob /
// AiGenerationService — flag-gated on PRR-249). The dev seeder
// produces TeacherAuthoredOriginal items so we have something to
// run the playbook against without waiting on the legal sign-off.
// Production gets its content via the authoring pipeline.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class MockExamDevDataSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MockExamDevDataSeeder> _logger;
    private readonly bool _enabled;

    public MockExamDevDataSeeder(
        IServiceProvider services,
        ILogger<MockExamDevDataSeeder> logger,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IHostEnvironment env)
    {
        _services = services;
        _logger = logger;
        // Default ON in dev, OFF elsewhere.
        _enabled = config.GetValue<bool?>("Cena:ExamPrep:DevSeed:Enabled")
            ?? env.IsDevelopment();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_enabled)
        {
            _logger.LogInformation("[EXAM-PREP-SEED] disabled by config; skipping");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            var catalog = scope.ServiceProvider.GetRequiredService<IBagrutPaperStructureCatalog>();

            // 1. Persist canonical paper structures.
            if (catalog is BagrutPaperStructureCatalog impl)
                await impl.UpsertSeedStructuresAsync(ct);

            // 2. Seed published items for every topic id the structures reference.
            await SeedQuestionsAsync(store, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXAM-PREP-SEED] failed; runner will return 400 until pool seeded");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedQuestionsAsync(IDocumentStore store, CancellationToken ct)
    {
        // Topic ids spanning the canonical 806/807/036 structures.
        var topics = new (string subject, string topic, int bloom, string prompt, string answer)[]
        {
            // Math algebra
            ("math", "math.algebra",             2, "Solve for x: 2x + 3 = 7",                 "x = 2"),
            ("math", "math.algebra",             3, "Solve: 3x - 5 = 2x + 4",                  "x = 9"),
            ("math", "math.algebra.quadratics",  2, "Solve: x^2 - 5x + 6 = 0 (smallest root)", "x = 2"),
            ("math", "math.algebra.quadratics",  3, "Solve: x^2 - 4 = 0 (positive root)",      "x = 2"),

            // Trig
            ("math", "math.trigonometry",        2, "sin(0) + cos(0)",                          "1"),
            ("math", "math.trigonometry",        3, "sin(pi/2) + cos(0)",                       "2"),
            ("math", "math.trigonometry",        2, "tan(pi/4)",                                "1"),

            // Calculus
            ("math", "math.calculus",            2, "d/dx of x^2",                              "2*x"),
            ("math", "math.calculus.derivative", 2, "d/dx of 3x^2 + 2x",                        "6*x + 2"),
            ("math", "math.calculus.derivative", 3, "d/dx of sin(x) at x=0",                    "1"),
            ("math", "math.calculus.integral",   3, "Integral from 0 to 1 of x dx",             "1/2"),
            ("math", "math.calculus.integral",   4, "Integral from 0 to 1 of x^2 dx",           "1/3"),

            // Functions
            ("math", "math.functions",           2, "f(x) = x + 1, f(2)",                       "3"),
            ("math", "math.functions",           3, "f(x) = 2x - 1, find x when f(x) = 5",      "x = 3"),
            ("math", "math.functions",           4, "g(x) = x^2 + 1, g(g(0))",                  "2"),

            // Geometry
            ("math", "math.geometry",            2, "Sum of angles of a triangle (radians)",    "pi"),
            ("math", "math.geometry.plane",      3, "Area of a square with side 4",             "16"),
            ("math", "math.geometry",            2, "Perimeter of a square with side 5",        "20"),

            // Probability
            ("math", "math.probability",         3, "P(heads) for fair coin",                   "1/2"),
            ("math", "math.probability",         4, "P(two heads) for two fair coins",          "1/4"),
            ("math", "math.probability",         3, "P(rolling 6 on fair die)",                 "1/6"),

            // Vectors
            ("math", "math.vectors",             3, "Magnitude of (3, 4)",                      "5"),
            ("math", "math.vectors",             4, "Dot product of (1,0,0) and (0,1,0)",       "0"),

            // Growth/decay
            ("math", "math.growthDecay",         3, "e^0",                                      "1"),
            ("math", "math.growthDecay",         4, "ln(e)",                                    "1"),

            // Physics — mechanics
            ("physics", "physics.mechanics",      2, "F = ma; m=2, a=3 → F",                    "6"),
            ("physics", "physics.mechanics",      3, "v = u + at; u=0, a=10, t=2 → v",          "20"),
            ("physics", "physics.mechanics",      4, "Kinetic energy at v=10, m=2",             "100"),

            // Physics — thermodynamics
            ("physics", "physics.thermodynamics", 2, "0 K in Celsius",                          "-273.15"),
            ("physics", "physics.thermodynamics", 3, "Q = mcΔT; m=1, c=1, ΔT=10 → Q",          "10"),

            // Physics — waves
            ("physics", "physics.waves",          2, "Wave speed v = fλ; f=10, λ=2 → v",        "20"),
            ("physics", "physics.waves",          3, "Period T = 1/f; f=4 → T",                 "1/4"),

            // Physics — electricity / electromagnetism
            ("physics", "physics.electricity",    2, "V = IR; I=2, R=3 → V",                    "6"),
            ("physics", "physics.electricity",    3, "P = IV; I=2, V=12 → P",                   "24"),
            ("physics", "physics.electromagnetism",3, "F = qvB; q=1, v=2, B=3 → F",             "6"),

            // Physics — optics / modern
            ("physics", "physics.optics",         3, "n = c/v; v=c/2 → n",                      "2"),
            ("physics", "physics.modern",         4, "E = hf; h=1, f=5 → E",                    "5"),
        };

        await using var session = store.LightweightSession();
        var seededCount = 0;

        for (var i = 0; i < topics.Length; i++)
        {
            var (subject, topic, bloom, prompt, answer) = topics[i];
            var id = $"exam-prep-seed-{topic.Replace('.', '-')}-{i:D2}";

            session.Store(new QuestionDocument
            {
                Id = id,
                QuestionId = id,
                Subject = subject,
                Topic = topic,
                ConceptId = topic,
                Prompt = prompt,
                CorrectAnswer = answer,
                QuestionType = "free-text",
                Difficulty = bloom <= 2 ? "easy" : bloom == 3 ? "medium" : "hard",
            });

            session.Store(new QuestionReadModel
            {
                Id = id,
                Subject = subject,
                Topic = topic,
                Status = "Published",
                BloomsLevel = bloom,
                Difficulty = bloom <= 2 ? 0.3f : bloom == 3 ? 0.5f : 0.75f,
                StemPreview = prompt,
                Concepts = new List<string> { topic, subject },
                SourceType = "TeacherAuthoredOriginal",
                Language = "en",
                QualityScore = 80,
            });

            seededCount++;
        }

        await session.SaveChangesAsync(ct);
        _logger.LogInformation("[EXAM-PREP-SEED] upserted {Count} published items for runner pool", seededCount);
    }
}
