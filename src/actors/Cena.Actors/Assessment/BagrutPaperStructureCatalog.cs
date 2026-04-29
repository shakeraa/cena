// =============================================================================
// Cena Platform — BagrutPaperStructureCatalog
//
// Marten-first catalog with in-memory canonical fallbacks. The catalog
// answers "what does paper 035582 look like" with a deterministic
// shape (sections × slots × topic + bloom + points) the runner can
// drive question selection from.
//
// Seeded structures:
//   - 806/default — Math 5U canonical (5 short + 4 long, choose 2; 100 pts)
//   - 806/035582  — Math 5U Summer 2024 Moed A (per persona-ministry research)
//   - 806/035581  — Math 5U Summer 2023 Moed A
//   - 807/default — Math 4U canonical (5 short + 4 long, choose 2; 100 pts)
//   - 036/default — Physics canonical (4 short + 5 long, choose 3)
//
// Topic ids align with QuestionDocument.Topic so the selector can
// match. The "default" structures use coarse topic families
// (algebra / calculus / vectors / probability / trig); per-paper
// structures use finer-grained topic ids where we have the data.
//
// Marten persistence: structures get upserted at startup so a future
// admin authoring surface can edit them without code changes. Today
// the in-memory bake is the source of truth.
// =============================================================================

using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment;

public sealed class BagrutPaperStructureCatalog : IBagrutPaperStructureCatalog
{
    private readonly IDocumentStore _store;
    private readonly ILogger<BagrutPaperStructureCatalog> _logger;

    public BagrutPaperStructureCatalog(
        IDocumentStore store, ILogger<BagrutPaperStructureCatalog> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<BagrutPaperStructureDocument> GetAsync(
        string examCode, string? paperCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(examCode);

        await using var qs = _store.QuerySession();

        // Try paper-specific first.
        if (!string.IsNullOrWhiteSpace(paperCode))
        {
            var byPaper = await qs.LoadAsync<BagrutPaperStructureDocument>(
                BagrutPaperStructureDocument.ComposeId(examCode, paperCode), ct);
            if (byPaper is not null) return byPaper;
        }

        // Fall back to default for the exam code.
        var byExam = await qs.LoadAsync<BagrutPaperStructureDocument>(
            BagrutPaperStructureDocument.ComposeId(examCode, null), ct);
        if (byExam is not null) return byExam;

        // Last resort: synthesize from in-memory baked seeds.
        var baked = SeededStructures.FirstOrDefault(s =>
            s.ExamCode == examCode &&
            (string.IsNullOrEmpty(paperCode) || s.PaperCode == paperCode || s.PaperCode is null));
        if (baked is null)
            throw new InvalidOperationException(
                $"No BagrutPaperStructure available for examCode={examCode} paperCode={paperCode ?? "(default)"}.");

        _logger.LogWarning(
            "[STRUCTURE] DB miss for {ExamCode}/{PaperCode}; using in-memory bake. Run UpsertSeedStructuresAsync once at startup to persist.",
            examCode, paperCode ?? "(default)");
        return baked;
    }

    /// <summary>
    /// Upserts the in-memory seed structures into Marten. Idempotent
    /// (Marten Store is upsert by Id). Called once from
    /// <see cref="MockExamStartupSeeder"/>.
    /// </summary>
    internal async Task UpsertSeedStructuresAsync(CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        foreach (var s in SeededStructures)
            session.Store(s);
        await session.SaveChangesAsync(ct);
        _logger.LogInformation(
            "[STRUCTURE] Seeded {Count} canonical Bagrut paper structures into Marten",
            SeededStructures.Count);
    }

    // =========================================================================
    // SEED DATA — canonical structures
    // =========================================================================
    //
    // Topic ids MUST match published QuestionDocument.Topic / .ConceptId
    // values so the selector can find candidates. The dev seeder
    // (MockExamDevDataSeeder) creates published items that span these
    // exact topic ids.
    //
    // Real-world references:
    //   - 806/default: Ministry "Math 5U" structure (3hr, 5+4 choose 2)
    //   - 806/035582: persona-ministry findings — Summer 2024 Moed A,
    //     algebra/trig/calc Section A; calc/probability/vectors Section B
    //   - 806/035581: Summer 2023 Moed A — same shape, slight topic shift
    //   - 807/default: Ministry "Math 4U" (3hr, 5+4 choose 2; lighter calc)
    //   - 036/default: Ministry physics 5U (3hr, 4+5 choose 3)
    // =========================================================================

    private static readonly IReadOnlyList<BagrutPaperStructureDocument> SeededStructures = new[]
    {
        // ───────────── 806/default — Math 5U canonical ─────────────
        new BagrutPaperStructureDocument
        {
            Id = "806/default",
            ExamCode = "806",
            PaperCode = null,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra",     2, 3, 14, "Algebraic manipulation"),
                        new(2, "math.trigonometry",2, 3, 14, "Trig identities / equations"),
                        new(3, "math.calculus",    2, 3, 14, "Derivative / basic integral"),
                        new(4, "math.functions",   2, 3, 14, "Function analysis"),
                        new(5, "math.geometry",    2, 3, 14, "Plane geometry"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus",    3, 4, 15, "Long calculus problem"),
                        new(2, "math.probability", 3, 4, 15, "Probability"),
                        new(3, "math.vectors",     3, 4, 15, "Vectors / 3D geometry"),
                        new(4, "math.growthDecay", 3, 4, 15, "Exponential / log model"),
                    }),
            },
        },

        // ───────────── 806/035582 — Summer 2024 Moed A ─────────────
        new BagrutPaperStructureDocument
        {
            Id = "806/035582",
            ExamCode = "806",
            PaperCode = "035582",
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra.quadratics", 2, 3, 14, "Quadratic equation system"),
                        new(2, "math.trigonometry",       2, 3, 14, "Trig identities (Summer 2024 favored sin/cos sum)"),
                        new(3, "math.calculus.derivative",2, 3, 14, "Derivative + extrema"),
                        new(4, "math.functions",          2, 3, 14, "Function analysis"),
                        new(5, "math.geometry.plane",     2, 3, 14, "Plane geometry proof"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus.integral",  3, 4, 15, "Integral application"),
                        new(2, "math.probability",        3, 4, 15, "Probability / combinatorics"),
                        new(3, "math.vectors",            3, 4, 15, "3D vectors"),
                        new(4, "math.growthDecay",        3, 4, 15, "Growth-decay"),
                    }),
            },
        },

        // ───────────── 806/035581 — Summer 2023 Moed A ─────────────
        new BagrutPaperStructureDocument
        {
            Id = "806/035581",
            ExamCode = "806",
            PaperCode = "035581",
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra",            2, 3, 14, "Algebra"),
                        new(2, "math.trigonometry",       2, 3, 14, "Trig"),
                        new(3, "math.calculus.derivative",2, 3, 14, "Derivative"),
                        new(4, "math.functions",          2, 3, 14, "Function analysis"),
                        new(5, "math.geometry",           2, 3, 14, "Geometry"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus.integral",  3, 4, 15, "Integration"),
                        new(2, "math.probability",        3, 4, 15, "Probability"),
                        new(3, "math.vectors",            3, 4, 15, "Vectors"),
                        new(4, "math.functions",          3, 4, 15, "Function modeling"),
                    }),
            },
        },

        // ───────────── 807/default — Math 4U canonical ─────────────
        new BagrutPaperStructureDocument
        {
            Id = "807/default",
            ExamCode = "807",
            PaperCode = null,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra",            1, 3, 14, "Algebra"),
                        new(2, "math.trigonometry",       1, 3, 14, "Trig basics"),
                        new(3, "math.calculus.derivative",2, 3, 14, "Derivative basics"),
                        new(4, "math.functions",          1, 3, 14, "Function analysis"),
                        new(5, "math.geometry",           1, 3, 14, "Geometry"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus.derivative",2, 3, 15, "Derivative app"),
                        new(2, "math.probability",        2, 3, 15, "Probability basics"),
                        new(3, "math.functions",          2, 3, 15, "Function modeling"),
                        new(4, "math.geometry",           2, 3, 15, "Geometry proof"),
                    }),
            },
        },

        // ───────────── 036/default — Physics canonical ─────────────
        new BagrutPaperStructureDocument
        {
            Id = "036/default",
            ExamCode = "036",
            PaperCode = null,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 4,
                    FallbackTopicId: "physics",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "physics.mechanics",     2, 3, 17, "Newtonian mechanics"),
                        new(2, "physics.thermodynamics",2, 3, 17, "Thermodynamics"),
                        new(3, "physics.waves",         2, 3, 17, "Waves / sound"),
                        new(4, "physics.electricity",   2, 3, 17, "Electric circuits"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 3,
                    FallbackTopicId: "physics",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "physics.mechanics",      3, 4, 11, "Mechanics — long problem"),
                        new(2, "physics.electromagnetism",3, 4, 11, "Electromagnetism"),
                        new(3, "physics.optics",         3, 4, 11, "Optics"),
                        new(4, "physics.modern",         3, 4, 11, "Modern physics"),
                        new(5, "physics.thermodynamics", 3, 4, 11, "Thermo — long"),
                    }),
            },
        },
    };
}
