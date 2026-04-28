// =============================================================================
// Cena Platform — Bagrut Corpus Dev-Bootstrap Seed (PRR-251)
//
// Closes the PRR-250 §2 BLOCKER: BagrutCorpusItemDocument has no Marten
// table in dev because PRR-242 ingestion was never replayed in a dev
// environment. ADR-0059 §3+§4 reference-page filter scope is moot until
// the corpus is populated.
//
// What this seeds:
//   10 SYNTHETIC corpus items spanning math 035 (3U/4U/5U), math 806
//   and physics 036, across 2024 קיץ Moed A + B. Each item is clearly
//   marked `[DEV-FIXTURE]` in RawText + IngestedBy="dev-seeder" so a
//   human reviewer can tell synthetic from real production ingest at a
//   glance.
//
// What this is NOT:
//   * NOT real Bagrut content — Ministry exam content lives behind
//     legal-sensitive admin uploads. Real corpus production ingest happens
//     via POST /api/admin/ingestion/bagrut (BagrutPdfIngestionService),
//     guarded by SUPER_ADMIN role.
//   * NOT a substitute for production corpus — when a real environment
//     stand-up needs a real corpus, ops runs the admin upload flow.
//
// Idempotent: re-running on every admin-api startup is safe (deterministic
// ids via BagrutCorpusItemDocument.ComposeId; Marten upsert).
//
// Why a separate seeder rather than re-running production ingestion in
// dev:
//   1. Production ingestion needs Mathpix/Gemini OCR API keys + paid
//      cloud calls per page. Not appropriate for a dev bootstrap.
//   2. Real Bagrut PDFs are legal-sensitive (Ministry of Education
//      reference material per memory project_bagrut_reference_only).
//      Checking sample PDFs into the repo is not OK.
//   3. The downstream consumers (BagrutCorpusService, similarity-checker,
//      reference-library API) only care about row shape — synthetic
//      rows fully exercise the API.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seeds 10 synthetic BagrutCorpusItemDocument rows for dev environments.
/// Idempotent: deterministic ids via ComposeId; Marten upsert handles re-run.
/// </summary>
public static class BagrutCorpusSeedData
{
    /// <summary>
    /// Marker that lands in every seeded row's <c>IngestedBy</c> field so
    /// integration tests + architecture tests can distinguish synthetic
    /// from real production ingest.
    /// </summary>
    public const string DevSeederMarker = "dev-seeder";

    /// <summary>
    /// Marker prefix on every seeded row's <c>RawText</c>. Even if the
    /// corpus accidentally leaked into a student-facing surface (which
    /// is forbidden by ADR-0043 + ADR-0059), the visible text would
    /// shout DEV-FIXTURE rather than imitate Ministry content.
    /// </summary>
    public const string DevFixturePrefix = "[DEV-FIXTURE]";

    /// <summary>Number of items the seeder writes (sanity-test ground truth).</summary>
    public const int SeededItemCount = 10;

    public static async Task SeedAsync(IDocumentStore store, ILogger logger)
    {
        logger.LogInformation("[bagrut-corpus-seed] starting — {Count} synthetic items", SeededItemCount);

        var items = BuildItems(DateTimeOffset.UtcNow);
        await using var session = store.LightweightSession();
        foreach (var item in items)
        {
            session.Store(item);
        }
        await session.SaveChangesAsync();

        logger.LogInformation("[bagrut-corpus-seed] persisted {Count} items (ids: {Ids})",
            items.Count,
            string.Join(", ", items.Select(i => i.Id)));
    }

    /// <summary>
    /// Public for testability. Builds the canonical fixture list — same
    /// items every call, ids deterministic. Tests assert against this
    /// shape directly.
    /// </summary>
    public static IReadOnlyList<BagrutCorpusItemDocument> BuildItems(DateTimeOffset ingestedAt)
    {
        // 035 = math (Hebrew stream / 5U), 035581 = canonical 5U paper
        // 036 = physics (5U).
        // We mint two question-papers (קיץ 2024 Moed A + B) × 5 questions
        // per paper to get 10 rows.

        var stem35A = new[]
        {
            "[DEV-FIXTURE] solve x^2 - 5x + 6 = 0",
            "[DEV-FIXTURE] differentiate f(x) = 3x^4 - 2x^2 + 1",
            "[DEV-FIXTURE] integrate ∫ (2x + 3) dx from 0 to 4",
            "[DEV-FIXTURE] find lim_{x→0} (sin x)/x",
            "[DEV-FIXTURE] given series sum_{n=1}^∞ 1/n^2, classify convergence",
        };

        var stemPhysicsB = new[]
        {
            "[DEV-FIXTURE] block on inclined plane with μ=0.3, find acceleration",
            "[DEV-FIXTURE] charged particle in uniform B-field, compute radius of motion",
            "[DEV-FIXTURE] simple pendulum L=2m, find period of small oscillation",
            "[DEV-FIXTURE] photon energy at λ=500nm, compute eV",
            "[DEV-FIXTURE] Carnot cycle, T_h=600K T_c=300K, compute efficiency",
        };

        var topicsMath = new[]
        {
            "algebra.quadratics",
            "calculus.derivatives",
            "calculus.integrals",
            "calculus.limits",
            "analysis.series",
        };

        var topicsPhysics = new[]
        {
            "mechanics.inclined-plane",
            "electromagnetism.lorentz-force",
            "mechanics.simple-pendulum",
            "quantum.photon-energy",
            "thermodynamics.carnot",
        };

        var items = new List<BagrutCorpusItemDocument>(SeededItemCount);

        // Paper 1 — Math 5U Hebrew, קיץ 2024 Moed A.
        for (int i = 0; i < 5; i++)
        {
            var qn = i + 1;
            items.Add(new BagrutCorpusItemDocument
            {
                Id = BagrutCorpusItemDocument.ComposeId("035", "035581", qn),
                MinistrySubjectCode = "035",
                MinistryQuestionPaperCode = "035581",
                Units = 5,
                TrackKey = "5U",
                Year = 2024,
                Season = BagrutCorpusSeason.Summer,
                Moed = "A",
                QuestionNumber = qn,
                TopicId = topicsMath[i],
                Stream = BagrutCorpusStream.Hebrew,
                RawText = stem35A[i],
                NormalisedStem = NormaliseStem(stem35A[i]),
                LatexContent = null,
                SourcePdfId = "dev-fixture:math-035581-2024-A",
                IngestConfidence = 1.0, // Synthetic = perfect.
                IngestedAt = ingestedAt,
                IngestedBy = DevSeederMarker,
            });
        }

        // Paper 2 — Physics 5U Hebrew, קיץ 2024 Moed B.
        for (int i = 0; i < 5; i++)
        {
            var qn = i + 1;
            items.Add(new BagrutCorpusItemDocument
            {
                Id = BagrutCorpusItemDocument.ComposeId("036", "036991", qn),
                MinistrySubjectCode = "036",
                MinistryQuestionPaperCode = "036991",
                Units = 5,
                TrackKey = "5U",
                Year = 2024,
                Season = BagrutCorpusSeason.Summer,
                Moed = "B",
                QuestionNumber = qn,
                TopicId = topicsPhysics[i],
                Stream = BagrutCorpusStream.Hebrew,
                RawText = stemPhysicsB[i],
                NormalisedStem = NormaliseStem(stemPhysicsB[i]),
                LatexContent = null,
                SourcePdfId = "dev-fixture:physics-036991-2024-B",
                IngestConfidence = 1.0,
                IngestedAt = ingestedAt,
                IngestedBy = DevSeederMarker,
            });
        }

        return items;
    }

    /// <summary>
    /// Approximate normalisation matching what BagrutCorpusExtractor produces:
    /// lowercase, punctuation stripped, whitespace collapsed. Real production
    /// normalisation lives next to <c>MinistrySimilarityChecker</c>; this is
    /// good enough for seed data shape.
    /// </summary>
    private static string NormaliseStem(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lower.Length);
        var prevSpace = false;
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                prevSpace = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            // else: drop punctuation
        }
        return sb.ToString().Trim();
    }
}
