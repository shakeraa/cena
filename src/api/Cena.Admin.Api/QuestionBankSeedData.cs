// =============================================================================
// Cena Platform — Question Bank Seed Data
// 1,000 Bagrut-aligned questions across 6 subjects, 3 languages, 3 source types
// ~100 hand-crafted + ~900 programmatically generated (seeded RNG for reproducibility)
// Distribution: Math 350, Physics 200, Chemistry 150, Biology 100, CS 100, English 100
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Questions;
using Cena.Infrastructure.Seed;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public static class QuestionBankSeedData
{
    public static async Task SeedQuestionsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.QuerySession();
        var existingCount = await session.Query<QuestionReadModel>().CountAsync();

        // If we already have ~1000 questions, skip seeding entirely
        if (existingCount >= 999)
        {
            var auditCount = await session.Query<ModerationAuditDocument>().CountAsync();
            if (auditCount == 0)
            {
                logger.LogInformation("Questions exist but no moderation audit docs — seeding audit trail...");
                await SeedModerationAuditDocsAsync(store, logger);
            }
            else
            {
                logger.LogInformation("Question bank already has {Count} questions + {Audits} audit docs, skipping seed",
                    existingCount, auditCount);
            }
            return;
        }

        logger.LogInformation("Seeding question bank — have {Existing}, targeting ~1000...", existingCount);

        // FIND-pedagogy-008 — ensure the canonical learning-objective set is
        // present and build a lookup for per-question backfill. The LO set is
        // seeded by LearningObjectiveSeedData earlier in the boot order; if
        // the caller skipped it we defensively seed from the static list now.
        await LearningObjectiveSeedData.SeedLearningObjectivesAsync(store, logger);
        var loObjectives = LearningObjectiveSeedData.GetSeedObjectives();

        // Pre-fetch existing stream keys to avoid collision with concurrent seeding
        await using var querySession = store.QuerySession();
        var existingStreamKeys = (await querySession.Events.QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith("q-"))
            .Select(e => e.StreamKey!)
            .ToListAsync())
            .ToHashSet();
        logger.LogInformation("Found {Count} existing question streams", existingStreamKeys.Count);

        await using var writeSession = store.LightweightSession();
        var now = DateTimeOffset.UtcNow;
        var auditRng = new Random(Seed); // seeded for reproducible audit data
        int seeded = 0;
        int skipped = 0;
        int withLo = 0;

        foreach (var q in GetSeedQuestions())
        {
            var id = $"q-{seeded + skipped + 1:D4}";

            if (existingStreamKeys.Contains(id))
            {
                skipped++;
                continue;
            }
            var options = q.Options.Select(o => new QuestionOptionData(
                o.Label, o.Text, $"<p>{o.Text}</p>", o.IsCorrect, o.Rationale)).ToList();

            // FIND-pedagogy-008 — backfill a plausible learning-objective id.
            // Match by subject + concept overlap; null is tolerated for
            // defensive reasons but should be extremely rare for seed data
            // (logs one warning per occurrence).
            var loId = LearningObjectiveSeedData.PickBestObjectiveId(
                q.Subject, q.Concepts, loObjectives);
            if (loId != null) withLo++;
            else
            {
                logger.LogWarning(
                    "Seed question {QuestionId} has no matching LearningObjective (subject={Subject}, concepts={Concepts})",
                    id, q.Subject, string.Join(",", q.Concepts));
            }

            object creationEvent = q.Source switch
            {
                "ai-generated" => new QuestionAiGenerated_V2(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    $"Generate a {q.Subject} question about {q.Topic} at Bloom level {q.Bloom}",
                    "claude-sonnet-4-6", 0.7f,
                    $"AI output for: {q.Stem}",
                    "seed-script",
                    q.Explanation,
                    now,
                    loId),
                "ingested" => new QuestionIngested_V2(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    $"doc-{seeded}", "https://edu.gov.il/bagrut", "bagrut-2024.pdf",
                    q.Stem, "seed-script", now,
                    q.Explanation,
                    loId),
                _ => new QuestionAuthored_V2(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    "seed-script", now,
                    q.Explanation,
                    loId)
            };

            writeSession.Events.StartStream<QuestionState>(id, creationEvent);

            // Quality score: normally distributed around 75, stddev 12
            var qualityScore = ClampInt(NextGaussian(auditRng, 75.0, 12.0), 10, 100);

            // Status distribution: 80% published, 15% approved, 5% draft
            var statusRoll = auditRng.NextDouble();
            var auditStatus = statusRoll < 0.80 ? ModerationItemStatus.Approved
                            : statusRoll < 0.95 ? ModerationItemStatus.Pending
                            : ModerationItemStatus.Pending;

            // Create matching ModerationAuditDocument for the moderation queue
            var auditDoc = new ModerationAuditDocument
            {
                Id = id,
                QuestionId = id,
                Status = auditStatus,
                SourceType = q.Source,
                AiQualityScore = qualityScore,
                StemPreview = q.Stem.Length > 120 ? q.Stem[..120] + "..." : q.Stem,
                Subject = q.Subject,
                Grade = q.Grade,
                Language = q.Language,
                CreatedBy = q.Source == "ai-generated" ? "System" : SeedAuthor(seeded),
                SubmittedAt = now.AddDays(-auditRng.Next(0, 30)).AddHours(-auditRng.Next(0, 24)),
                UpdatedAt = now,
            };
            writeSession.Store(auditDoc);

            seeded++;
        }

        await writeSession.SaveChangesAsync();
        logger.LogInformation(
            "Seeded {Count} new questions (skipped {Skipped} existing, {WithLo} tagged with LearningObjective), total now ~{Total}",
            seeded, skipped, withLo, seeded + skipped);
    }

    // ── MATH (25 questions) ──────────────────────────────────────────────

    private static IEnumerable<SeedQuestion> MathQuestions()
    {
        yield return Q("Solve for x: 2x + 6 = 14", "Math", "Linear Equations", "3 Units", 3, 0.3f, new[] { "linear-equations" }, "he", "authored",
            ("A", "4", true, null), ("B", "8", false, "Adds instead of subtracting"), ("C", "3", false, "Divides before subtracting"), ("D", "20", false, "Adds all numbers"));

        yield return Q("Solve the quadratic: x\u00B2 - 5x + 6 = 0", "Math", "Quadratic Equations", "4 Units", 3, 0.4f, new[] { "quadratic-equations" }, "he", "authored",
            ("A", "x = 2, x = 3", true, null), ("B", "x = -2, x = -3", false, "Sign error"), ("C", "x = 1, x = 6", false, "Wrong factoring"), ("D", "x = 5, x = 1", false, "Confuses sum and product"));

        yield return Q("Find the derivative of f(x) = x\u00B3 - 3x\u00B2 + 2x - 1", "Math", "Derivatives", "5 Units", 3, 0.6f, new[] { "derivatives" }, "he", "ai-generated",
            ("A", "3x\u00B2 - 6x + 2", true, null), ("B", "3x\u00B2 - 6x", false, "Forgets constant derivative"), ("C", "x\u00B2 - 3x + 2", false, "Wrong power rule"), ("D", "3x\u00B3 - 6x\u00B2 + 2x", false, "Doesn't reduce power"));

        yield return Q("Calculate \u222B\u2080\u00B2 (3x\u00B2 + 1)dx", "Math", "Integrals", "5 Units", 3, 0.8f, new[] { "integrals" }, "he", "authored",
            ("A", "10", true, null), ("B", "9", false, "Forgets constant term"), ("C", "12", false, "Upper bound only"), ("D", "8", false, "Wrong power rule"));

        yield return Q("Find the equation of the line through (1,3) and (4,9)", "Math", "Linear Equations", "3 Units", 3, 0.4f, new[] { "linear-equations", "analytic-geometry" }, "en", "authored",
            ("A", "y = 2x + 1", true, null), ("B", "y = 3x - 1", false, "Wrong slope"), ("C", "y = 2x + 3", false, "Wrong intercept"), ("D", "y = x + 2", false, "Slope = 1 error"));

        yield return Q("P(A\u222AB) given P(A)=0.4, P(B)=0.3, P(A\u2229B)=0.1", "Math", "Probability", "4 Units", 3, 0.5f, new[] { "probability" }, "en", "ai-generated",
            ("A", "0.6", true, null), ("B", "0.7", false, "Just adds P(A)+P(B)"), ("C", "0.8", false, "Adds all three"), ("D", "0.1", false, "Uses intersection only"));

        yield return Q("Sum of arithmetic sequence: a\u2081=3, d=4, n=20", "Math", "Sequences", "4 Units", 3, 0.5f, new[] { "sequences" }, "he", "ingested",
            ("A", "820", true, null), ("B", "80", false, "Only first and last"), ("C", "160", false, "Forgets n/2"), ("D", "400", false, "Uses wrong formula"));

        yield return Q("Solve: log\u2082(x) + log\u2082(x-2) = 3", "Math", "Logarithms", "5 Units", 4, 0.7f, new[] { "logarithms" }, "he", "authored",
            ("A", "4", true, null), ("B", "8", false, "Solves log\u2082(x)=3 directly"), ("C", "2", false, "Ignores second log"), ("D", "-2", false, "Extraneous solution"));

        yield return Q("Express z = 3+4i in polar form", "Math", "Complex Numbers", "5 Units", 3, 0.7f, new[] { "complex-numbers" }, "en", "authored",
            ("A", "5(cos 53.1\u00B0 + i sin 53.1\u00B0)", true, null), ("B", "7(cos 45\u00B0 + i sin 45\u00B0)", false, "Adds 3+4 for modulus"), ("C", "5(cos 36.9\u00B0 + i sin 36.9\u00B0)", false, "Swaps angle"), ("D", "25(cos 53.1\u00B0 + i sin 53.1\u00B0)", false, "Uses r\u00B2"));

        yield return Q("How many ways can 5 students be arranged in a line?", "Math", "Combinatorics", "4 Units", 2, 0.3f, new[] { "combinatorics" }, "en", "ai-generated",
            ("A", "120", true, null), ("B", "25", false, "Uses n\u00B2"), ("C", "10", false, "Uses C(5,2)"), ("D", "5", false, "Doesn't multiply"));

        yield return Q("Find all x satisfying |2x-3| < 5", "Math", "Inequalities", "3 Units", 3, 0.4f, new[] { "inequalities" }, "he", "authored",
            ("A", "-1 < x < 4", true, null), ("B", "x < 4", false, "Missing left bound"), ("C", "-4 < x < 1", false, "Sign errors"), ("D", "x > -1", false, "Missing right bound"));

        yield return Q("Evaluate lim(x\u21920) sin(3x)/x", "Math", "Limits", "5 Units", 3, 0.6f, new[] { "derivatives" }, "en", "ingested",
            ("A", "3", true, null), ("B", "1", false, "Forgets the 3"), ("C", "0", false, "L'H\u00F4pital error"), ("D", "\u221E", false, "Thinks it diverges"));

        yield return Q("\u05E4\u05EA\u05D5\u05E8 \u05D0\u05EA \u05D4\u05DE\u05E9\u05D5\u05D5\u05D0\u05D4: 3x - 7 = 14", "Math", "Linear Equations", "3 Units", 3, 0.3f, new[] { "linear-equations" }, "he", "authored",
            ("A", "x = 7", true, null), ("B", "x = 3", false, "\u05E9\u05D2\u05D9\u05D0\u05EA \u05D7\u05D9\u05E9\u05D5\u05D1"), ("C", "x = 21", false, "\u05E9\u05D5\u05DB\u05D7 \u05DC\u05D7\u05DC\u05E7"), ("D", "x = -7", false, "\u05E9\u05D2\u05D9\u05D0\u05EA \u05E1\u05D9\u05DE\u05DF"));

        yield return Q("\u0623\u0648\u062C\u062F \u0642\u064A\u0645\u0629 x \u0625\u0630\u0627 \u0643\u0627\u0646: log\u2082(x) = 5", "Math", "Logarithms", "4 Units", 3, 0.5f, new[] { "logarithms" }, "ar", "ai-generated",
            ("A", "32", true, null), ("B", "10", false, "\u064A\u062E\u0644\u0637 \u0645\u0639 log\u2081\u2080"), ("C", "25", false, "\u064A\u0636\u0631\u0628 2\u00D75"), ("D", "7", false, "\u064A\u062C\u0645\u0639 2+5"));

        yield return Q("\u0627\u062D\u0633\u0628 \u0645\u0633\u0627\u062D\u0629 \u0645\u062B\u0644\u062B \u0642\u0627\u0639\u062F\u062A\u0647 8 \u0633\u0645 \u0648\u0627\u0631\u062A\u0641\u0627\u0639\u0647 5 \u0633\u0645", "Math", "Geometry", "3 Units", 3, 0.3f, new[] { "analytic-geometry" }, "ar", "authored",
            ("A", "20 \u0633\u0645\u00B2", true, null), ("B", "40 \u0633\u0645\u00B2", false, "\u0646\u0633\u064A \u0627\u0644\u0642\u0633\u0645\u0629 \u0639\u0644\u0649 2"), ("C", "13 \u0633\u0645\u00B2", false, "\u062C\u0645\u0639 \u0627\u0644\u0642\u0627\u0639\u062F\u0629 \u0648\u0627\u0644\u0627\u0631\u062A\u0641\u0627\u0639"), ("D", "3 \u0633\u0645\u00B2", false, "\u0637\u0631\u062D \u0628\u062F\u0644\u0627\u064B \u0645\u0646 \u0627\u0644\u0636\u0631\u0628"));

        yield return Q("Solve the inequality: 2x - 3 > 7", "Math", "Inequalities", "3 Units", 3, 0.3f, new[] { "inequalities" }, "en", "authored",
            ("A", "x > 5", true, null), ("B", "x > 2", false, "Doesn't add 3"), ("C", "x < 5", false, "Wrong direction"), ("D", "x > 10", false, "Doesn't divide by 2"));

        yield return Q("Determine the slope of the tangent to y = x\u00B3 at x = 2", "Math", "Derivatives", "5 Units", 3, 0.6f, new[] { "derivatives" }, "en", "ai-generated",
            ("A", "12", true, null), ("B", "8", false, "Uses f(2) not f'(2)"), ("C", "6", false, "Wrong derivative"), ("D", "3", false, "Forgets power rule"));

        yield return Q("\u05D7\u05E9\u05D1 \u05D0\u05EA \u05D4\u05E0\u05D2\u05D6\u05E8\u05EA \u05E9\u05DC f(x) = sin(2x)", "Math", "Derivatives", "5 Units", 3, 0.5f, new[] { "derivatives", "trigonometry" }, "he", "authored",
            ("A", "2cos(2x)", true, null), ("B", "cos(2x)", false, "\u05E9\u05D5\u05DB\u05D7 \u05DB\u05DC\u05DC \u05E9\u05E8\u05E9\u05E8\u05EA"), ("C", "-2cos(2x)", false, "\u05E9\u05D2\u05D9\u05D0\u05EA \u05E1\u05D9\u05DE\u05DF"), ("D", "2sin(2x)", false, "\u05DC\u05D0 \u05DE\u05D7\u05DC\u05D9\u05E3 \u05DC-cos"));

        yield return Q("Find the area under f(x) = 2x + 1 from x = 0 to x = 3", "Math", "Integrals", "5 Units", 3, 0.5f, new[] { "integrals" }, "en", "authored",
            ("A", "12", true, null), ("B", "7", false, "Only evaluates f(3)"), ("C", "9", false, "Forgets constant"), ("D", "21", false, "Multiplies f(3) by 3"));

        yield return Q("\u05DE\u05E6\u05D0 \u05D0\u05EA \u05E9\u05D9\u05E4\u05D5\u05E2 \u05D4\u05D9\u05E9\u05E8 \u05D4\u05E2\u05D5\u05D1\u05E8 \u05D3\u05E8\u05DA (0,0) \u05D5-(6,3)", "Math", "Linear Equations", "3 Units", 3, 0.2f, new[] { "linear-equations" }, "he", "ingested",
            ("A", "0.5", true, null), ("B", "2", false, "\u05D4\u05E4\u05D5\u05DA \u05D0\u05EA \u05D4\u05D9\u05D7\u05E1"), ("C", "3", false, "\u05DE\u05E9\u05EA\u05DE\u05E9 \u05E8\u05E7 \u05D1-y"), ("D", "6", false, "\u05DE\u05E9\u05EA\u05DE\u05E9 \u05E8\u05E7 \u05D1-x"));

        yield return Q("Find the probability of rolling a sum of 7 with two dice", "Math", "Probability", "4 Units", 3, 0.5f, new[] { "probability" }, "en", "authored",
            ("A", "1/6", true, null), ("B", "1/12", false, "Counts only 3 combos"), ("C", "7/36", false, "Counts 7 not 6"), ("D", "1/36", false, "Only one pair"));

        yield return Q("\u062D\u0644\u0644 \u0627\u0644\u0639\u0648\u0627\u0645\u0644: x\u00B2 - 9", "Math", "Quadratic Equations", "3 Units", 3, 0.3f, new[] { "quadratic-equations" }, "ar", "authored",
            ("A", "(x+3)(x-3)", true, null), ("B", "(x-3)\u00B2", false, "\u064A\u0639\u062A\u0628\u0631\u0647 \u0645\u0631\u0628\u0639\u0627\u064B \u0643\u0627\u0645\u0644\u0627\u064B"), ("C", "(x+9)(x-1)", false, "\u062A\u062D\u0644\u064A\u0644 \u062E\u0627\u0637\u0626"), ("D", "(x-3)(x-3)", false, "\u064A\u0643\u0631\u0631 \u0646\u0641\u0633 \u0627\u0644\u0639\u0627\u0645\u0644"));

        yield return Q("Prove: if n is even, then n\u00B2 is even. Which approach works?", "Math", "Proof Techniques", "5 Units", 5, 0.8f, new[] { "proof-techniques" }, "en", "ai-generated",
            ("A", "Direct proof: let n = 2k, then n\u00B2 = 4k\u00B2 = 2(2k\u00B2)", true, null), ("B", "Try n=2,4,6 and observe pattern", false, "Induction from examples isn't proof"), ("C", "Assume n\u00B2 is odd, reach contradiction", false, "Correct idea but unnecessarily complex"), ("D", "Use mathematical induction on n", false, "Overkill for this statement"));

        yield return Q("Calculate the standard deviation of: 4, 7, 2, 9, 5, 8", "Math", "Statistics", "4 Units", 3, 0.6f, new[] { "statistics" }, "en", "authored",
            ("A", "\u22482.4", true, null), ("B", "5.83", false, "That's the mean"), ("C", "7", false, "That's the range"), ("D", "35", false, "That's the sum"));
    }

    // ── PHYSICS (20 questions) ───────────────────────────────────────────

    private static IEnumerable<SeedQuestion> PhysicsQuestions()
    {
        yield return Q("A car accelerates from rest at 2 m/s\u00B2. Velocity after 5 seconds?", "Physics", "Kinematics", "4 Units", 3, 0.5f, new[] { "kinematics" }, "en", "authored",
            ("A", "10 m/s", true, null), ("B", "7 m/s", false, "Uses wrong formula"), ("C", "25 m/s", false, "Uses distance formula"), ("D", "2.5 m/s", false, "Divides instead of multiplying"));

        yield return Q("A ball is thrown upward at 20 m/s. Calculate maximum height.", "Physics", "Kinematics", "4 Units", 3, 0.5f, new[] { "kinematics" }, "en", "ingested",
            ("A", "20 m", true, null), ("B", "40 m", false, "Doesn't divide by 2g"), ("C", "10 m", false, "Uses wrong g"), ("D", "2 m", false, "Divides v by g\u00B2"));

        yield return Q("Calculate work done by F=10N over d=5m at 30\u00B0 angle", "Physics", "Energy & Work", "4 Units", 3, 0.5f, new[] { "energy" }, "en", "authored",
            ("A", "43.3 J", true, null), ("B", "50 J", false, "Ignores angle"), ("C", "25 J", false, "Uses sin instead of cos"), ("D", "5 J", false, "Divides F by d"));

        yield return Q("Wave: frequency 500 Hz, wavelength 0.68 m. Find wave speed.", "Physics", "Waves", "4 Units", 3, 0.4f, new[] { "waves" }, "en", "ai-generated",
            ("A", "340 m/s", true, null), ("B", "735 m/s", false, "Divides f by \u03BB"), ("C", "0.00136 m/s", false, "Divides \u03BB by f"), ("D", "500.68 m/s", false, "Adds f and \u03BB"));

        yield return Q("Find equivalent resistance: three 6\u03A9 resistors in parallel", "Physics", "Electricity", "4 Units", 3, 0.5f, new[] { "electricity" }, "en", "ingested",
            ("A", "2 \u03A9", true, null), ("B", "18 \u03A9", false, "Adds (series)"), ("C", "6 \u03A9", false, "Single resistor"), ("D", "0.5 \u03A9", false, "Calculation error"));

        yield return Q("Kinetic energy of a 3 kg object at 4 m/s", "Physics", "Energy & Work", "3 Units", 3, 0.3f, new[] { "energy" }, "en", "authored",
            ("A", "24 J", true, null), ("B", "12 J", false, "Uses KE=mv"), ("C", "48 J", false, "Doubles"), ("D", "6 J", false, "Forgets squaring"));

        yield return Q("Projectile at 45\u00B0 with v\u2080=30 m/s. Find the range.", "Physics", "Kinematics", "5 Units", 3, 0.7f, new[] { "kinematics" }, "en", "ai-generated",
            ("A", "\u224890 m", true, null), ("B", "45 m", false, "Halves the result"), ("C", "180 m", false, "Doubles"), ("D", "30 m", false, "Uses v\u2080 as range"));

        yield return Q("Period of a simple pendulum of length 1.5m", "Physics", "Oscillations", "4 Units", 3, 0.5f, new[] { "oscillations" }, "en", "authored",
            ("A", "\u22482.46 s", true, null), ("B", "\u22481.23 s", false, "Forgets 2\u03C0"), ("C", "\u22480.64 s", false, "Uses 1/T"), ("D", "\u22485 s", false, "Wrong formula"));

        yield return Q("Magnetic force on a 2m wire carrying 5A in 0.3T field at 90\u00B0", "Physics", "Magnetism", "5 Units", 3, 0.6f, new[] { "magnetism" }, "en", "authored",
            ("A", "3 N", true, null), ("B", "30 N", false, "Wrong unit conversion"), ("C", "0.3 N", false, "Forgets length"), ("D", "10 N", false, "Uses only I\u00D7B"));

        yield return Q("Heat to raise 500g water from 20\u00B0C to 80\u00B0C", "Physics", "Thermodynamics", "4 Units", 3, 0.4f, new[] { "thermodynamics" }, "en", "ingested",
            ("A", "125.4 kJ", true, null), ("B", "60 kJ", false, "Wrong specific heat"), ("C", "250.8 kJ", false, "Doubles"), ("D", "4.18 kJ", false, "Uses 1g not 500g"));

        yield return Q("\u05DE\u05D4\u05D9 \u05D4\u05D4\u05EA\u05E0\u05D2\u05D3\u05D5\u05EA \u05D4\u05E9\u05E7\u05D5\u05DC\u05D4 \u05E9\u05DC \u05E9\u05DC\u05D5\u05E9\u05D4 \u05E0\u05D2\u05D3\u05D9\u05DD \u05D1\u05D8\u05D5\u05E8: 2\u03A9, 3\u03A9, 5\u03A9?", "Physics", "Electricity", "3 Units", 3, 0.3f, new[] { "electricity" }, "he", "authored",
            ("A", "10 \u03A9", true, null), ("B", "0.97 \u03A9", false, "\u05DE\u05D7\u05E9\u05D1 \u05DB\u05DE\u05E7\u05D1\u05D9\u05DC\u05D9\u05DD"), ("C", "30 \u03A9", false, "\u05DB\u05D5\u05E4\u05DC"), ("D", "3.33 \u03A9", false, "\u05DE\u05DE\u05D5\u05E6\u05E2"));

        yield return Q("Determine resultant force: 3N east and 4N north", "Physics", "Dynamics", "4 Units", 3, 0.5f, new[] { "dynamics" }, "en", "authored",
            ("A", "5 N", true, null), ("B", "7 N", false, "Adds magnitudes"), ("C", "1 N", false, "Subtracts"), ("D", "12 N", false, "Multiplies"));

        yield return Q("Calculate wavelength of light at 6\u00D710\u00B9\u2074 Hz (c=3\u00D710\u2078)", "Physics", "Waves", "5 Units", 3, 0.5f, new[] { "waves", "optics" }, "en", "ai-generated",
            ("A", "5\u00D710\u207B\u2077 m", true, null), ("B", "2\u00D710\u2076 m", false, "Multiplied"), ("C", "5\u00D710\u2077 m", false, "Wrong exponent"), ("D", "1.8\u00D710\u00B2\u00B3 m", false, "Multiplied f\u00D7c"));

        yield return Q("Satellite at 400km. Find orbital velocity. (R=6400km, g=10)", "Physics", "Gravitation", "5 Units", 4, 0.8f, new[] { "gravitation" }, "en", "authored",
            ("A", "\u22487.67 km/s", true, null), ("B", "\u22483.8 km/s", false, "Missing square root"), ("C", "\u224811.2 km/s", false, "Escape velocity"), ("D", "\u22480.4 km/s", false, "Uses h only"));

        yield return Q("Calculate acceleration on planet with mass 2M and radius 3R vs Earth", "Physics", "Gravitation", "5 Units", 4, 0.8f, new[] { "gravitation" }, "en", "ai-generated",
            ("A", "2g/9", true, null), ("B", "6g", false, "Multiplies"), ("C", "2g/3", false, "Forgets squaring R"), ("D", "g/3", false, "Only considers R"));

        yield return Q("Spring (k=200 N/m) compressed 0.1m. Calculate stored PE.", "Physics", "Energy & Work", "4 Units", 3, 0.4f, new[] { "energy" }, "en", "authored",
            ("A", "1 J", true, null), ("B", "20 J", false, "Doesn't square x"), ("C", "0.5 J", false, "Halves again"), ("D", "10 J", false, "Uses kx not \u00BDkx\u00B2"));

        yield return Q("12V battery, internal resistance 0.5\u03A9, external 5.5\u03A9. Current?", "Physics", "Electricity", "4 Units", 3, 0.5f, new[] { "electricity" }, "en", "ingested",
            ("A", "2 A", true, null), ("B", "2.18 A", false, "Ignores internal"), ("C", "24 A", false, "Uses P=IV"), ("D", "6 A", false, "Uses V/r only"));

        yield return Q("De Broglie wavelength of electron at 1.5\u00D710\u2076 m/s", "Physics", "Modern Physics", "5 Units", 3, 0.8f, new[] { "modern-physics" }, "en", "ai-generated",
            ("A", "\u22484.85\u00D710\u207B\u00B9\u00B9 m", true, null), ("B", "\u22481\u00D710\u207B\u00B9\u2070 m", false, "Wrong mass"), ("C", "\u22480.5 m", false, "Forgets Planck's constant"), ("D", "\u22481.5\u00D710\u207B\u00B3\u2074 m", false, "Calculation error"));

        yield return Q("Convex lens focal length from R\u2081=20cm, R\u2082=-30cm (n=1.5)", "Physics", "Optics", "5 Units", 4, 0.7f, new[] { "optics" }, "en", "authored",
            ("A", "24 cm", true, null), ("B", "12 cm", false, "Wrong sign convention"), ("C", "50 cm", false, "Adds radii"), ("D", "10 cm", false, "Halves result"));

        yield return Q("Momentum of a 5kg object moving at 10m/s", "Physics", "Momentum", "3 Units", 2, 0.2f, new[] { "momentum" }, "en", "authored",
            ("A", "50 kg\u00B7m/s", true, null), ("B", "15 kg\u00B7m/s", false, "Adds"), ("C", "2 kg\u00B7m/s", false, "Divides"), ("D", "500 kg\u00B7m/s", false, "Squares velocity"));
    }

    // ── CHEMISTRY (15 questions) ─────────────────────────────────────────

    private static IEnumerable<SeedQuestion> ChemistryQuestions()
    {
        yield return Q("What is the pH of a 0.01M HCl solution?", "Chemistry", "Acids & Bases", "4 Units", 3, 0.4f, new[] { "acids-bases" }, "he", "authored",
            ("A", "2", true, null), ("B", "12", false, "Confuses pH and pOH"), ("C", "0.01", false, "Uses concentration directly"), ("D", "7", false, "Thinks it's neutral"));

        yield return Q("How many moles of NaCl are in 117g? (M=58.5)", "Chemistry", "Stoichiometry", "3 Units", 3, 0.3f, new[] { "stoichiometry" }, "en", "authored",
            ("A", "2 mol", true, null), ("B", "0.5 mol", false, "Divides wrong way"), ("C", "117 mol", false, "Doesn't divide"), ("D", "58.5 mol", false, "Uses molar mass as answer"));

        yield return Q("Balance: Fe\u2082O\u2083 + CO \u2192 Fe + CO\u2082", "Chemistry", "Stoichiometry", "4 Units", 3, 0.6f, new[] { "stoichiometry" }, "en", "ingested",
            ("A", "Fe\u2082O\u2083 + 3CO \u2192 2Fe + 3CO\u2082", true, null), ("B", "Fe\u2082O\u2083 + CO \u2192 Fe + CO\u2082", false, "Not balanced"), ("C", "Fe\u2082O\u2083 + 2CO \u2192 2Fe + 2CO\u2082", false, "O not balanced"), ("D", "2Fe\u2082O\u2083 + 3CO \u2192 4Fe + 3CO\u2082", false, "O not balanced"));

        yield return Q("Draw the Lewis structure for CO\u2082", "Chemistry", "Chemical Bonding", "3 Units", 2, 0.3f, new[] { "chemical-bonding" }, "en", "authored",
            ("A", "O=C=O (double bonds)", true, null), ("B", "O-C-O (single bonds)", false, "Not enough electrons"), ("C", "O\u2261C-O (triple+single)", false, "Asymmetric bonding"), ("D", "C-O-O (chain)", false, "Wrong connectivity"));

        yield return Q("Electron configuration of Fe\u00B2\u207A", "Chemistry", "Atomic Structure", "4 Units", 2, 0.5f, new[] { "atomic-structure" }, "en", "ai-generated",
            ("A", "[Ar] 3d\u2076", true, null), ("B", "[Ar] 3d\u2074 4s\u00B2", false, "Doesn't remove 4s first"), ("C", "[Ar] 3d\u2078", false, "Adds electrons"), ("D", "[Ar] 4s\u00B2 3d\u2074", false, "Neutral Fe config"));

        yield return Q("Identify the oxidizing agent: Zn + CuSO\u2084 \u2192 ZnSO\u2084 + Cu", "Chemistry", "Redox", "4 Units", 4, 0.5f, new[] { "redox" }, "en", "authored",
            ("A", "Cu\u00B2\u207A (from CuSO\u2084)", true, null), ("B", "Zn", false, "That's the reducing agent"), ("C", "SO\u2084\u00B2\u207B", false, "Spectator ion"), ("D", "ZnSO\u2084", false, "Product, not reactant"));

        yield return Q("Calculate voltage of a Zn/Cu galvanic cell", "Chemistry", "Electrochemistry", "5 Units", 3, 0.7f, new[] { "electrochemistry" }, "en", "ai-generated",
            ("A", "1.10 V", true, null), ("B", "0.34 V", false, "Uses only Cu half-cell"), ("C", "-1.10 V", false, "Wrong sign"), ("D", "0.76 V", false, "Uses only Zn half-cell"));

        yield return Q("\u05DE\u05D4 \u05D4-pH \u05E9\u05DC \u05EA\u05DE\u05D9\u05E1\u05D4 \u05E0\u05D9\u05D8\u05E8\u05DC\u05D9\u05EA \u05D1-25\u00B0C?", "Chemistry", "Acids & Bases", "3 Units", 2, 0.2f, new[] { "acids-bases" }, "he", "authored",
            ("A", "7", true, null), ("B", "0", false, "\u05D7\u05D5\u05DE\u05E6\u05D4 \u05D7\u05D6\u05E7\u05D4"), ("C", "14", false, "\u05D1\u05E1\u05D9\u05E1 \u05D7\u05D6\u05E7"), ("D", "1", false, "pOH"));

        yield return Q("\u0645\u0627 \u0647\u0648 \u0627\u0644\u0631\u0642\u0645 \u0627\u0644\u0647\u064A\u062F\u0631\u0648\u062C\u064A\u0646\u064A \u0644\u0645\u062D\u0644\u0648\u0644 HCl \u0628\u062A\u0631\u0643\u064A\u0632 0.01 \u0645\u0648\u0644/\u0644\u062A\u0631\u061F", "Chemistry", "Acids & Bases", "4 Units", 3, 0.5f, new[] { "acids-bases" }, "ar", "ai-generated",
            ("A", "2", true, null), ("B", "12", false, "\u064A\u062E\u0644\u0637 \u0628\u064A\u0646 pH \u0648 pOH"), ("C", "0.01", false, "\u064A\u0633\u062A\u062E\u062F\u0645 \u0627\u0644\u062A\u0631\u0643\u064A\u0632"), ("D", "7", false, "\u064A\u0639\u062A\u0628\u0631\u0647 \u0645\u062A\u0639\u0627\u062F\u0644\u0627\u064B"));

        yield return Q("What type of bond forms in NaCl?", "Chemistry", "Chemical Bonding", "3 Units", 1, 0.2f, new[] { "chemical-bonding" }, "en", "authored",
            ("A", "Ionic bond", true, null), ("B", "Covalent bond", false, "Molecular compounds"), ("C", "Metallic bond", false, "Metal-metal"), ("D", "Hydrogen bond", false, "Intermolecular"));

        yield return Q("Identify reaction type: 2H\u2082O\u2082 \u2192 2H\u2082O + O\u2082", "Chemistry", "Reaction Types", "3 Units", 2, 0.3f, new[] { "reaction-rates" }, "en", "authored",
            ("A", "Decomposition", true, null), ("B", "Synthesis", false, "Confuses with combination"), ("C", "Single displacement", false, "No element replacing"), ("D", "Double displacement", false, "No ion exchange"));

        yield return Q("Hybridization of central carbon in CH\u2084?", "Chemistry", "Chemical Bonding", "4 Units", 2, 0.4f, new[] { "chemical-bonding" }, "en", "ingested",
            ("A", "sp\u00B3", true, null), ("B", "sp\u00B2", false, "3 bonds"), ("C", "sp", false, "2 bonds"), ("D", "d\u00B2sp\u00B3", false, "Octahedral"));

        yield return Q("Name IUPAC: CH\u2083CH\u2082CH(CH\u2083)CH\u2082OH", "Chemistry", "Organic Chemistry", "5 Units", 2, 0.6f, new[] { "organic-chemistry" }, "en", "authored",
            ("A", "3-methylbutan-1-ol", true, null), ("B", "2-methylbutan-4-ol", false, "Wrong numbering"), ("C", "3-methylbutanol", false, "Missing position"), ("D", "isopentanol", false, "Common name"));

        yield return Q("Calculate boiling point elevation of 1m glucose solution (Kb=0.512)", "Chemistry", "Solutions", "5 Units", 3, 0.6f, new[] { "solutions" }, "en", "ai-generated",
            ("A", "0.512\u00B0C", true, null), ("B", "1.024\u00B0C", false, "Doubles Kb"), ("C", "0.256\u00B0C", false, "Halves"), ("D", "5.12\u00B0C", false, "Wrong order of magnitude"));

        yield return Q("Predict products: CH\u2083COOH + NaOH \u2192", "Chemistry", "Acids & Bases", "4 Units", 3, 0.4f, new[] { "acids-bases", "organic-chemistry" }, "en", "authored",
            ("A", "CH\u2083COONa + H\u2082O", true, null), ("B", "CH\u2083COOH + Na", false, "Doesn't react"), ("C", "NaCH\u2083 + CO\u2082 + H\u2082O", false, "Breaks down acid"), ("D", "CH\u2083OH + NaCO\u2082", false, "Wrong products"));
    }

    // ── BIOLOGY (15 questions) ───────────────────────────────────────────

    private static IEnumerable<SeedQuestion> BiologyQuestions()
    {
        yield return Q("Compare mitosis and meiosis. Which is correct?", "Biology", "Cell Biology", "5 Units", 4, 0.7f, new[] { "cell-biology", "genetics" }, "en", "authored",
            ("A", "Meiosis produces 4 haploid cells; mitosis produces 2 diploid cells", true, null), ("B", "Mitosis occurs only in reproductive cells", false, "Reverses roles"), ("C", "Meiosis has no crossing over", false, "Crossing over is key"), ("D", "Both produce identical daughter cells", false, "Only mitosis does"));

        yield return Q("In Aa \u00D7 Aa cross, expected genotype ratio?", "Biology", "Genetics", "4 Units", 3, 0.5f, new[] { "genetics" }, "he", "ai-generated",
            ("A", "1:2:1 (AA:Aa:aa)", true, null), ("B", "3:1 (AA:aa)", false, "Phenotype ratio"), ("C", "1:1 (Aa:aa)", false, "Test cross"), ("D", "2:2 (AA:Aa)", false, "Ignores recessive"));

        yield return Q("Role of ATP synthase in oxidative phosphorylation", "Biology", "Cellular Respiration", "5 Units", 4, 0.7f, new[] { "respiration" }, "en", "authored",
            ("A", "Uses proton gradient to synthesize ATP from ADP + Pi", true, null), ("B", "Breaks down glucose into pyruvate", false, "That's glycolysis"), ("C", "Transports electrons along the chain", false, "That's ETC complexes"), ("D", "Produces NADH from NAD+", false, "That's dehydrogenases"));

        yield return Q("Describe light-dependent reactions of photosynthesis", "Biology", "Photosynthesis", "5 Units", 2, 0.5f, new[] { "photosynthesis" }, "en", "ingested",
            ("A", "Water is split, O\u2082 released, ATP and NADPH produced", true, null), ("B", "CO\u2082 is fixed into glucose", false, "That's Calvin cycle"), ("C", "Glucose is broken down for energy", false, "That's respiration"), ("D", "Starch is stored in chloroplasts", false, "That's a product of dark reactions"));

        yield return Q("Explain why antibiotics don't work against viruses", "Biology", "Immune System", "4 Units", 5, 0.6f, new[] { "immune-system" }, "en", "authored",
            ("A", "Viruses lack cell walls and ribosomes that antibiotics target", true, null), ("B", "Viruses are too small for antibiotics", false, "Size isn't the issue"), ("C", "Antibiotics only work on fungi", false, "They target bacteria"), ("D", "Viruses mutate too quickly", false, "Confuses with resistance"));

        yield return Q("\u05D4\u05E1\u05D1\u05E8 \u05D0\u05EA \u05EA\u05E4\u05E7\u05D9\u05D3 \u05D4\u05DE\u05D9\u05D8\u05D5\u05DB\u05D5\u05E0\u05D3\u05E8\u05D9\u05D4 \u05D1\u05EA\u05D0", "Biology", "Cell Biology", "4 Units", 2, 0.3f, new[] { "cell-biology" }, "he", "authored",
            ("A", "\u05D9\u05D9\u05E6\u05D5\u05E8 \u05D0\u05E0\u05E8\u05D2\u05D9\u05D4 (ATP) \u05D3\u05E8\u05DA \u05E0\u05E9\u05D9\u05DE\u05D4 \u05EA\u05D0\u05D9\u05EA", true, null), ("B", "\u05E4\u05D5\u05D8\u05D5\u05E1\u05D9\u05E0\u05EA\u05D6\u05D4", false, "\u05DE\u05D1\u05DC\u05D1\u05DC \u05E2\u05DD \u05DB\u05DC\u05D5\u05E8\u05D5\u05E4\u05DC\u05E1\u05D8"), ("C", "\u05D0\u05D7\u05E1\u05D5\u05DF \u05D7\u05D5\u05DE\u05E8 \u05D2\u05E0\u05D8\u05D9", false, "\u05DE\u05D1\u05DC\u05D1\u05DC \u05E2\u05DD \u05D2\u05E8\u05E2\u05D9\u05DF"), ("D", "\u05E4\u05D9\u05E8\u05D5\u05E7 \u05D7\u05DC\u05D1\u05D5\u05E0\u05D9\u05DD", false, "\u05DE\u05D1\u05DC\u05D1\u05DC \u05E2\u05DD \u05DC\u05D9\u05D6\u05D5\u05D6\u05D5\u05DD"));

        yield return Q("Which organelle packages and ships proteins?", "Biology", "Cell Biology", "3 Units", 1, 0.2f, new[] { "cell-biology" }, "en", "authored",
            ("A", "Golgi apparatus", true, null), ("B", "Endoplasmic reticulum", false, "ER synthesizes"), ("C", "Lysosome", false, "Lysosomes break down"), ("D", "Ribosome", false, "Ribosomes synthesize"));

        yield return Q("Function of DNA polymerase during replication", "Biology", "Molecular Biology", "4 Units", 2, 0.3f, new[] { "molecular-biology" }, "en", "ai-generated",
            ("A", "Adds nucleotides to growing DNA strand", true, null), ("B", "Unwinds the double helix", false, "That's helicase"), ("C", "Joins Okazaki fragments", false, "That's ligase"), ("D", "Removes RNA primers", false, "Different enzyme"));

        yield return Q("Explain natural selection leading to adaptation", "Biology", "Evolution", "5 Units", 5, 0.7f, new[] { "evolution" }, "en", "authored",
            ("A", "Organisms with advantageous traits survive more, passing traits to offspring", true, null), ("B", "Organisms deliberately change genes to adapt", false, "Lamarckian"), ("C", "Only strongest organisms survive every generation", false, "Oversimplified"), ("D", "Random mutations always improve fitness", false, "Mutations are random"));

        yield return Q("Compare r-selected and K-selected species", "Biology", "Ecology", "5 Units", 4, 0.6f, new[] { "ecology" }, "en", "authored",
            ("A", "r-selected: many offspring, low parental care; K-selected: few offspring, high care", true, null), ("B", "r-selected live longer than K-selected", false, "Opposite"), ("C", "K-selected reproduce faster", false, "r-selected do"), ("D", "No real difference exists", false, "Well-documented difference"));

        yield return Q("Calculate expected genotype ratio: dihybrid AaBb \u00D7 AaBb", "Biology", "Genetics", "5 Units", 3, 0.7f, new[] { "genetics" }, "en", "ingested",
            ("A", "9:3:3:1 phenotype ratio", true, null), ("B", "3:1 ratio", false, "That's monohybrid"), ("C", "1:1:1:1 ratio", false, "That's test cross"), ("D", "All identical", false, "That's homozygous cross"));

        yield return Q("Hardy-Weinberg equilibrium conditions", "Biology", "Evolution", "5 Units", 4, 0.7f, new[] { "evolution" }, "en", "ai-generated",
            ("A", "No mutation, random mating, no selection, large population, no migration", true, null), ("B", "Only no mutation is required", false, "All 5 conditions needed"), ("C", "Small populations maintain equilibrium better", false, "Opposite - drift"), ("D", "Natural selection must be present", false, "Must be absent"));

        yield return Q("Describe feedback mechanism for blood glucose", "Biology", "Human Physiology", "4 Units", 4, 0.6f, new[] { "human-physiology" }, "en", "authored",
            ("A", "Insulin lowers glucose; glucagon raises it — negative feedback", true, null), ("B", "Only insulin regulates glucose", false, "Glucagon also involved"), ("C", "Positive feedback amplifies glucose changes", false, "It's negative feedback"), ("D", "The liver plays no role", false, "Liver is central"));

        yield return Q("Explain the process of PCR", "Biology", "Biotechnology", "5 Units", 2, 0.5f, new[] { "biotechnology" }, "en", "authored",
            ("A", "Denature, anneal primers, extend with Taq polymerase — repeated cycles amplify DNA", true, null), ("B", "Mix DNA with restriction enzymes to cut it", false, "That's restriction digestion"), ("C", "Insert DNA into bacteria for cloning", false, "That's molecular cloning"), ("D", "Sequence DNA using fluorescent nucleotides", false, "That's Sanger sequencing"));

        yield return Q("Ribosomes role in protein synthesis", "Biology", "Molecular Biology", "3 Units", 2, 0.3f, new[] { "molecular-biology" }, "en", "ingested",
            ("A", "Translate mRNA into amino acid chains", true, null), ("B", "Transcribe DNA into mRNA", false, "That's RNA polymerase"), ("C", "Store genetic information", false, "That's the nucleus"), ("D", "Break down old proteins", false, "That's proteasomes"));
    }

    // ── COMPUTER SCIENCE (15 questions) ──────────────────────────────────

    private static IEnumerable<SeedQuestion> CsQuestions()
    {
        yield return Q("Time complexity of binary search on sorted array?", "Computer Science", "Algorithms", "5 Units", 1, 0.2f, new[] { "algorithms" }, "en", "authored",
            ("A", "O(log n)", true, null), ("B", "O(n)", false, "Linear search"), ("C", "O(n\u00B2)", false, "Bubble sort"), ("D", "O(1)", false, "Hash table"));

        yield return Q("Worst-case complexity of quicksort?", "Computer Science", "Sorting", "5 Units", 1, 0.3f, new[] { "algorithms", "sorting" }, "en", "authored",
            ("A", "O(n\u00B2)", true, null), ("B", "O(n log n)", false, "Average case"), ("C", "O(log n)", false, "Binary search"), ("D", "O(n)", false, "Linear"));

        yield return Q("Difference between stack and queue", "Computer Science", "Data Structures", "4 Units", 2, 0.3f, new[] { "data-structures" }, "en", "authored",
            ("A", "Stack: LIFO; Queue: FIFO", true, null), ("B", "Both are FIFO", false, "Stack is LIFO"), ("C", "Stack is faster than queue", false, "Same complexity"), ("D", "Queue allows random access", false, "Sequential access"));

        yield return Q("Write pseudocode for merge sort and its complexity", "Computer Science", "Sorting", "5 Units", 4, 0.7f, new[] { "algorithms", "sorting" }, "en", "ai-generated",
            ("A", "Divide, recursively sort halves, merge \u2014 O(n log n)", true, null), ("B", "Compare adjacent, swap \u2014 O(n\u00B2)", false, "Bubble sort"), ("C", "Select minimum, place at front \u2014 O(n\u00B2)", false, "Selection sort"), ("D", "Insert each in sorted position \u2014 O(n\u00B2)", false, "Insertion sort"));

        yield return Q("Explain polymorphism in OOP", "Computer Science", "OOP", "4 Units", 2, 0.4f, new[] { "oop" }, "en", "authored",
            ("A", "Same method name, different behavior based on object type", true, null), ("B", "Hiding internal state from external access", false, "That's encapsulation"), ("C", "Creating new classes from existing ones", false, "That's inheritance"), ("D", "Breaking code into separate modules", false, "That's modularity"));

        yield return Q("Convert: NOT(A AND B) to logic gates", "Computer Science", "Boolean Logic", "4 Units", 3, 0.5f, new[] { "boolean-logic" }, "en", "ingested",
            ("A", "NAND gate (single gate)", true, null), ("B", "Two NOT gates + OR gate", false, "De Morgan's but more complex"), ("C", "NOR gate", false, "That's NOT(A OR B)"), ("D", "XOR gate", false, "Different function"));

        yield return Q("SQL: Find students with grade > 90 in both Math and Physics", "Computer Science", "Databases", "4 Units", 3, 0.5f, new[] { "databases" }, "en", "authored",
            ("A", "SELECT s.* FROM students s WHERE s.id IN (SELECT student_id FROM grades WHERE subject='Math' AND grade>90) AND s.id IN (SELECT student_id FROM grades WHERE subject='Physics' AND grade>90)", true, null), ("B", "SELECT * FROM students WHERE grade > 90", false, "Missing subject filter"), ("C", "SELECT * FROM grades WHERE grade > 90 AND subject IN ('Math','Physics')", false, "Doesn't require both"), ("D", "SELECT * FROM students HAVING grade > 90", false, "HAVING without GROUP BY"));

        yield return Q("Trace BFS starting from vertex A: A-B, A-C, B-D, C-D, D-E", "Computer Science", "Graphs", "5 Units", 3, 0.6f, new[] { "graphs" }, "en", "authored",
            ("A", "A, B, C, D, E", true, null), ("B", "A, B, D, E, C", false, "DFS-like"), ("C", "A, C, B, D, E", false, "Wrong order"), ("D", "A, B, C, E, D", false, "E before D"));

        yield return Q("Difference between ArrayList and LinkedList", "Computer Science", "Data Structures", "4 Units", 4, 0.5f, new[] { "data-structures" }, "en", "ai-generated",
            ("A", "ArrayList: O(1) random access, O(n) insert; LinkedList: O(n) access, O(1) insert at known position", true, null), ("B", "ArrayList is always faster", false, "Not for insertions"), ("C", "LinkedList uses less memory", false, "Pointer overhead"), ("D", "Both have same performance", false, "Very different"));

        yield return Q("What design pattern separates object construction from representation?", "Computer Science", "OOP", "5 Units", 2, 0.4f, new[] { "oop" }, "en", "authored",
            ("A", "Builder", true, null), ("B", "Factory", false, "Creates but doesn't separate steps"), ("C", "Singleton", false, "Restricts instances"), ("D", "Observer", false, "Handles events"));

        yield return Q("Design a recursive Fibonacci function. Base cases?", "Computer Science", "Recursion", "4 Units", 3, 0.4f, new[] { "recursion" }, "en", "authored",
            ("A", "fib(0)=0, fib(1)=1", true, null), ("B", "fib(1)=1 only", false, "Missing fib(0)"), ("C", "No base case needed", false, "Infinite recursion"), ("D", "fib(0)=1, fib(1)=1", false, "fib(0) should be 0"));

        yield return Q("Explain dynamic programming with knapsack problem", "Computer Science", "Dynamic Programming", "5 Units", 4, 0.8f, new[] { "dynamic-programming" }, "en", "ai-generated",
            ("A", "Build table of subproblems, each cell optimal for given weight/items", true, null), ("B", "Always pick the most valuable item first", false, "That's greedy, not optimal"), ("C", "Try all combinations exhaustively", false, "That's brute force"), ("D", "Sort items by weight, pick lightest first", false, "Not optimal either"));

        yield return Q("Write SQL to count students per subject with average grade", "Computer Science", "Databases", "4 Units", 3, 0.5f, new[] { "databases" }, "en", "ingested",
            ("A", "SELECT subject, COUNT(*), AVG(grade) FROM grades GROUP BY subject", true, null), ("B", "SELECT subject, COUNT(*) FROM grades", false, "Missing GROUP BY"), ("C", "SELECT * FROM grades ORDER BY subject", false, "No aggregation"), ("D", "SELECT DISTINCT subject FROM grades", false, "No counts"));

        yield return Q("Time complexity of inserting into a balanced BST?", "Computer Science", "Data Structures", "5 Units", 2, 0.4f, new[] { "data-structures" }, "en", "authored",
            ("A", "O(log n)", true, null), ("B", "O(n)", false, "Unbalanced BST"), ("C", "O(1)", false, "Hash table"), ("D", "O(n log n)", false, "Sorting"));

        yield return Q("Explain array vs linked list memory allocation", "Computer Science", "Data Structures", "4 Units", 4, 0.5f, new[] { "data-structures" }, "en", "authored",
            ("A", "Arrays: contiguous memory; linked lists: scattered nodes with pointers", true, null), ("B", "Arrays are always faster", false, "Not for insertions"), ("C", "Linked lists use less total memory", false, "Pointer overhead"), ("D", "Both use same layout", false, "Fundamentally different"));
    }

    // ── ENGLISH (10 questions) ───────────────────────────────────────────

    private static IEnumerable<SeedQuestion> EnglishQuestions()
    {
        yield return Q("Identify the literary device: 'The wind whispered through the trees'", "English", "Literature", "3 Units", 2, 0.3f, new[] { "literature" }, "en", "authored",
            ("A", "Personification", true, null), ("B", "Simile", false, "No 'like' or 'as'"), ("C", "Hyperbole", false, "No exaggeration"), ("D", "Alliteration", false, "Not about repeated sounds"));

        yield return Q("Choose correct form: If she ___ harder, she would have passed.", "English", "Grammar", "4 Units", 3, 0.4f, new[] { "grammar" }, "en", "authored",
            ("A", "had studied", true, null), ("B", "studied", false, "Wrong tense"), ("C", "would study", false, "Mixes conditionals"), ("D", "has studied", false, "Present perfect"));

        yield return Q("Rewrite in passive: The students completed the assignment on time.", "English", "Grammar", "3 Units", 3, 0.3f, new[] { "grammar" }, "en", "authored",
            ("A", "The assignment was completed on time by the students", true, null), ("B", "The assignment completed by the students", false, "Missing auxiliary"), ("C", "On time the students completed the assignment", false, "Still active"), ("D", "The assignment is completed on time", false, "Wrong tense"));

        yield return Q("Identify tone and purpose of a newspaper editorial", "English", "Reading Comprehension", "4 Units", 4, 0.6f, new[] { "reading-comprehension" }, "en", "ai-generated",
            ("A", "Persuasive tone; purpose is to influence public opinion", true, null), ("B", "Informative; reports facts neutrally", false, "That's a news article"), ("C", "Narrative; tells a story", false, "That's fiction"), ("D", "Descriptive; paints a picture", false, "That's descriptive writing"));

        yield return Q("Match vocabulary: 'Ubiquitous' most nearly means", "English", "Vocabulary", "4 Units", 1, 0.4f, new[] { "vocabulary" }, "en", "authored",
            ("A", "Found everywhere", true, null), ("B", "Very large", false, "That's 'enormous'"), ("C", "Extremely rare", false, "Opposite"), ("D", "Moving quickly", false, "That's 'swift'"));

        yield return Q("Summarize the main conflict in a typical coming-of-age story", "English", "Literature", "4 Units", 4, 0.5f, new[] { "literature" }, "en", "authored",
            ("A", "Internal conflict between childhood identity and adult responsibility", true, null), ("B", "War between two countries", false, "That's a war novel"), ("C", "Solving a mystery or crime", false, "That's a detective story"), ("D", "Surviving in nature", false, "That's an adventure story"));

        yield return Q("Choose the correct sentence:", "English", "Grammar", "3 Units", 2, 0.3f, new[] { "grammar" }, "en", "ingested",
            ("A", "Neither the teacher nor the students were prepared.", true, null), ("B", "Neither the teacher nor the students was prepared.", false, "Verb agrees with nearest subject (students)"), ("C", "Neither the teacher or the students were prepared.", false, "'Neither' pairs with 'nor'"), ("D", "Neither the teacher nor the students is prepared.", false, "Wrong agreement"));

        yield return Q("What rhetorical device is: 'Ask not what your country can do for you'?", "English", "Rhetoric", "5 Units", 4, 0.6f, new[] { "rhetoric" }, "en", "ai-generated",
            ("A", "Chiasmus (reversed parallel structure)", true, null), ("B", "Metaphor", false, "No comparison"), ("C", "Hyperbole", false, "No exaggeration"), ("D", "Onomatopoeia", false, "No sound words"));

        yield return Q("Analyze symbolism of 'the green light' in The Great Gatsby", "English", "Literature", "5 Units", 5, 0.7f, new[] { "literature", "poetry-analysis" }, "en", "authored",
            ("A", "Represents Gatsby's unattainable dreams and the American Dream", true, null), ("B", "Simply indicates a traffic signal", false, "Misses symbolic meaning"), ("C", "Symbolizes money and greed only", false, "Too narrow"), ("D", "Has no symbolic meaning", false, "Central symbol of the novel"));

        yield return Q("Identify the error: 'The team have decided to postpone their meeting.'", "English", "Grammar", "3 Units", 3, 0.4f, new[] { "grammar" }, "en", "authored",
            ("A", "No error in British English; in American English, 'has' is preferred", true, null), ("B", "'Their' should be 'its'", false, "Both acceptable"), ("C", "'Postpone' is wrong tense", false, "Correct infinitive"), ("D", "'Meeting' should be 'meet'", false, "Meeting is correct noun"));
    }

    // ── Aggregator ──

    private static IEnumerable<SeedQuestion> GetSeedQuestions()
    {
        // Phase 1: hand-crafted questions (~100)
        foreach (var q in MathQuestions()) yield return q;
        foreach (var q in PhysicsQuestions()) yield return q;
        foreach (var q in ChemistryQuestions()) yield return q;
        foreach (var q in BiologyQuestions()) yield return q;
        foreach (var q in CsQuestions()) yield return q;
        foreach (var q in EnglishQuestions()) yield return q;

        // Phase 2: programmatically generated questions (~900) to reach 1,000 total
        foreach (var q in GenerateQuestions()) yield return q;
    }

    // ── Programmatic Question Generator ─────────────────────────────────

    private const int TargetTotal = 1000;
    private const int Seed = 42;

    /// <summary>
    /// Normally distributed random using Box-Muller transform (seeded).
    /// </summary>
    private static double NextGaussian(Random rng, double mean, double stddev)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stddev * normal;
    }

    private static int ClampInt(double value, int min, int max) =>
        Math.Max(min, Math.Min(max, (int)Math.Round(value)));

    private static float ClampFloat(double value, float min, float max) =>
        Math.Max(min, Math.Min(max, (float)value));

    private static IEnumerable<SeedQuestion> GenerateQuestions()
    {
        // Count hand-crafted questions to know where to start
        var handCrafted = MathQuestions().Count()
                        + PhysicsQuestions().Count()
                        + ChemistryQuestions().Count()
                        + BiologyQuestions().Count()
                        + CsQuestions().Count()
                        + EnglishQuestions().Count();

        var remaining = TargetTotal - handCrafted;
        if (remaining <= 0) yield break;

        var rng = new Random(Seed);

        // Subject distribution targets (excluding hand-crafted counts)
        var handCraftedPerSubject = new Dictionary<string, int>
        {
            ["Math"] = MathQuestions().Count(),
            ["Physics"] = PhysicsQuestions().Count(),
            ["Chemistry"] = ChemistryQuestions().Count(),
            ["Biology"] = BiologyQuestions().Count(),
            ["Computer Science"] = CsQuestions().Count(),
            ["English"] = EnglishQuestions().Count()
        };

        var targetPerSubject = new Dictionary<string, int>
        {
            ["Math"] = 350,
            ["Physics"] = 200,
            ["Chemistry"] = 150,
            ["Biology"] = 100,
            ["Computer Science"] = 100,
            ["English"] = 100
        };

        // Build generation queue per subject
        var queue = new List<string>();
        foreach (var (subject, target) in targetPerSubject)
        {
            var need = target - handCraftedPerSubject.GetValueOrDefault(subject, 0);
            for (int i = 0; i < need; i++)
                queue.Add(subject);
        }

        // Shuffle for natural interleaving
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }

        foreach (var subject in queue)
        {
            var templates = SubjectTemplates[subject];
            var template = templates[rng.Next(templates.Length)];

            var bloom = ClampInt(NextGaussian(rng, 3.0, 1.2), 1, 6);
            var difficulty = ClampFloat(NextGaussian(rng, 0.5, 0.18), 0.0f, 1.0f);

            var grades = new[] { "9", "10", "11" };
            var grade = grades[rng.Next(grades.Length)];

            // Source type: 60% ai-generated, 25% ingested, 15% authored
            var sourceRoll = rng.NextDouble();
            var source = sourceRoll < 0.60 ? "ai-generated"
                       : sourceRoll < 0.85 ? "ingested"
                       : "authored";

            // Language: 60% he, 20% ar, 20% en
            var langRoll = rng.NextDouble();
            var language = langRoll < 0.60 ? "he"
                         : langRoll < 0.80 ? "ar"
                         : "en";

            // Status: 80% published, 15% approved, 5% draft
            // (stored as part of the question seed but used for audit doc)

            var concepts = template.Concepts;
            var stem = BuildStem(template, rng, language);

            var options = BuildOptions(template, rng, language);

            yield return new SeedQuestion(
                stem, subject, template.Topic, grade,
                bloom, difficulty, concepts, language, source,
                options[0], options[1], options[2], options[3]);
        }
    }

    private static string BuildStem(QuestionTemplate t, Random rng, string lang)
    {
        var variant = t.StemVariants[rng.Next(t.StemVariants.Length)];
        // Replace placeholders with random values
        var stem = variant
            .Replace("{n1}", rng.Next(2, 50).ToString())
            .Replace("{n2}", rng.Next(2, 30).ToString())
            .Replace("{n3}", rng.Next(1, 20).ToString())
            .Replace("{coeff}", rng.Next(2, 10).ToString())
            .Replace("{exp}", rng.Next(2, 5).ToString());

        if (lang == "he" && t.HebrewPrefix is not null)
            stem = t.HebrewPrefix + ": " + stem;
        else if (lang == "ar" && t.ArabicPrefix is not null)
            stem = t.ArabicPrefix + ": " + stem;

        return stem;
    }

    private static SeedOption[] BuildOptions(QuestionTemplate t, Random rng, string lang)
    {
        var set = t.OptionSets[rng.Next(t.OptionSets.Length)];
        return set;
    }

    // ── Question Templates per Subject ──────────────────────────────────

    private sealed record QuestionTemplate(
        string Topic,
        string[] Concepts,
        string[] StemVariants,
        string? HebrewPrefix,
        string? ArabicPrefix,
        SeedOption[][] OptionSets);

    private static readonly Dictionary<string, QuestionTemplate[]> SubjectTemplates = new()
    {
        ["Math"] = new QuestionTemplate[]
        {
            new("Linear Equations", new[] { "linear-equations" },
                new[]
                {
                    "Solve for x: {coeff}x + {n1} = {n2}",
                    "Find x if {coeff}x - {n1} = {n2}",
                    "Determine the solution of {n1} - {coeff}x = {n2}",
                    "What is x when {coeff}x = {n1} + {n2}?",
                },
                "\u05E4\u05EA\u05D5\u05E8 \u05D0\u05EA \u05D4\u05DE\u05E9\u05D5\u05D5\u05D0\u05D4",
                "\u062D\u0644 \u0627\u0644\u0645\u0639\u0627\u062F\u0644\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "x = 4", true, null), new SeedOption("B", "x = 8", false, "Arithmetic error"), new SeedOption("C", "x = -2", false, "Sign error"), new SeedOption("D", "x = 0", false, "Incorrect simplification") },
                    new[] { new SeedOption("A", "x = 7", true, null), new SeedOption("B", "x = 14", false, "Forgot to divide"), new SeedOption("C", "x = 3", false, "Subtraction error"), new SeedOption("D", "x = -7", false, "Sign error") },
                    new[] { new SeedOption("A", "x = 5", true, null), new SeedOption("B", "x = 10", false, "Doubled"), new SeedOption("C", "x = 1", false, "Off by one"), new SeedOption("D", "x = -5", false, "Negated") },
                }),
            new("Quadratic Equations", new[] { "quadratic-equations" },
                new[]
                {
                    "Solve: x\u00B2 - {n1}x + {n2} = 0",
                    "Find the roots of x\u00B2 + {n1}x - {n2} = 0",
                    "Factor and solve: x\u00B2 - {n2} = 0",
                    "Using the quadratic formula, solve {coeff}x\u00B2 - {n1}x + {n3} = 0",
                },
                "\u05E4\u05EA\u05D5\u05E8 \u05D0\u05EA \u05D4\u05DE\u05E9\u05D5\u05D5\u05D0\u05D4 \u05D4\u05E8\u05D9\u05D1\u05D5\u05E2\u05D9\u05EA",
                "\u062D\u0644 \u0627\u0644\u0645\u0639\u0627\u062F\u0644\u0629 \u0627\u0644\u062A\u0631\u0628\u064A\u0639\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "x = 2, x = 3", true, null), new SeedOption("B", "x = -2, x = -3", false, "Sign error"), new SeedOption("C", "x = 1, x = 6", false, "Wrong factoring"), new SeedOption("D", "x = 0, x = 5", false, "Lost constant") },
                    new[] { new SeedOption("A", "x = \u00B13", true, null), new SeedOption("B", "x = 3 only", false, "Missing negative root"), new SeedOption("C", "x = 9", false, "Forgot square root"), new SeedOption("D", "x = -9", false, "Squared instead") },
                }),
            new("Derivatives", new[] { "derivatives" },
                new[]
                {
                    "Find the derivative of f(x) = {coeff}x\u00B3 - {n1}x\u00B2 + {n2}x",
                    "Differentiate: f(x) = {n1}x\u2074 + {coeff}x",
                    "What is d/dx of ({coeff}x\u00B2 + {n1})?",
                    "Find f'(x) for f(x) = {n1}sin(x) + {coeff}x\u00B2",
                },
                "\u05DE\u05E6\u05D0 \u05D0\u05EA \u05D4\u05E0\u05D2\u05D6\u05E8\u05EA",
                "\u0623\u0648\u062C\u062F \u0627\u0644\u0645\u0634\u062A\u0642\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "3x\u00B2 - 6x + 2", true, null), new SeedOption("B", "3x\u00B2 - 6x", false, "Dropped constant term"), new SeedOption("C", "x\u00B2 - 3x + 2", false, "Wrong power rule"), new SeedOption("D", "6x - 6", false, "Differentiated twice") },
                    new[] { new SeedOption("A", "2x", true, null), new SeedOption("B", "x\u00B2", false, "Didn't differentiate"), new SeedOption("C", "2", false, "Second derivative"), new SeedOption("D", "x + 1", false, "Added instead") },
                }),
            new("Integrals", new[] { "integrals" },
                new[]
                {
                    "Evaluate \u222B ({coeff}x + {n1}) dx",
                    "Find the definite integral of {n1}x\u00B2 from 0 to {n2}",
                    "Compute \u222B\u2080\u00B9 ({coeff}x\u00B3) dx",
                    "What is \u222B (sin(x) + {coeff}) dx?",
                },
                "\u05D7\u05E9\u05D1 \u05D0\u05EA \u05D4\u05D0\u05D9\u05E0\u05D8\u05D2\u05E8\u05DC",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u062A\u0643\u0627\u0645\u0644",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "x\u00B2 + C", true, null), new SeedOption("B", "2x + C", false, "Differentiated instead"), new SeedOption("C", "x\u00B3/3 + C", false, "Wrong power"), new SeedOption("D", "x + C", false, "Forgot power rule") },
                    new[] { new SeedOption("A", "10", true, null), new SeedOption("B", "8", false, "Calculation error"), new SeedOption("C", "12", false, "Wrong bounds"), new SeedOption("D", "5", false, "Halved result") },
                }),
            new("Probability", new[] { "probability" },
                new[]
                {
                    "A bag has {n1} red and {n2} blue balls. P(red)?",
                    "Two events: P(A)=0.{n3}, P(B)=0.{n2}. If independent, find P(A\u2229B).",
                    "In how many ways can {n3} items be arranged in a line?",
                    "What is the probability of drawing 2 aces from a standard deck?",
                },
                "\u05D7\u05E9\u05D1 \u05D4\u05E1\u05EA\u05D1\u05E8\u05D5\u05EA",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0627\u062D\u062A\u0645\u0627\u0644",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "0.6", true, null), new SeedOption("B", "0.4", false, "Complement"), new SeedOption("C", "0.5", false, "Assumes equal"), new SeedOption("D", "1.0", false, "Sum of all") },
                    new[] { new SeedOption("A", "120", true, null), new SeedOption("B", "60", false, "Divided by 2"), new SeedOption("C", "24", false, "Off by one factorial"), new SeedOption("D", "720", false, "Wrong n") },
                }),
            new("Sequences", new[] { "sequences" },
                new[]
                {
                    "Find the {n1}th term of arithmetic sequence a\u2081={n3}, d={coeff}",
                    "Sum of first {n1} terms: a\u2081={n3}, d={coeff}",
                    "Is the sequence {n1}, {n2}, ... arithmetic or geometric?",
                    "Find the common ratio if a\u2081={n1} and a\u2083={n2}",
                },
                "\u05DE\u05E6\u05D0 \u05D0\u05EA \u05D4\u05D0\u05D9\u05D1\u05E8 \u05D1\u05E1\u05D3\u05E8\u05D4",
                "\u0623\u0648\u062C\u062F \u0627\u0644\u062D\u062F \u0641\u064A \u0627\u0644\u0645\u062A\u062A\u0627\u0644\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "47", true, null), new SeedOption("B", "50", false, "Off by one"), new SeedOption("C", "44", false, "Wrong d"), new SeedOption("D", "23", false, "Halved") },
                    new[] { new SeedOption("A", "820", true, null), new SeedOption("B", "410", false, "Missing n/2"), new SeedOption("C", "1640", false, "Doubled"), new SeedOption("D", "80", false, "Only two terms") },
                }),
            new("Geometry", new[] { "analytic-geometry" },
                new[]
                {
                    "Find the area of a triangle with base {n1}cm and height {n2}cm",
                    "Calculate the circumference of a circle with radius {n1}cm",
                    "What is the area of a rectangle with sides {n1}cm and {n2}cm?",
                    "Find the hypotenuse of a right triangle with legs {n1} and {n2}",
                },
                "\u05D7\u05E9\u05D1 \u05D0\u05EA \u05D4\u05E9\u05D8\u05D7",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0645\u0633\u0627\u062D\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "20 cm\u00B2", true, null), new SeedOption("B", "40 cm\u00B2", false, "Forgot \u00F72"), new SeedOption("C", "10 cm\u00B2", false, "Wrong formula"), new SeedOption("D", "80 cm\u00B2", false, "Doubled base") },
                    new[] { new SeedOption("A", "31.4 cm", true, null), new SeedOption("B", "15.7 cm", false, "Used \u03C0r"), new SeedOption("C", "62.8 cm", false, "Doubled"), new SeedOption("D", "10 cm", false, "No \u03C0") },
                }),
            new("Logarithms", new[] { "logarithms" },
                new[]
                {
                    "Solve: log\u2082(x) = {n3}",
                    "Simplify: log({n1}) + log({n2})",
                    "Find x: ln(x) = {coeff}",
                    "Evaluate log\u2081\u2080({n1}\u00D7{n2})",
                },
                "\u05E4\u05EA\u05D5\u05E8 \u05D0\u05EA \u05D4\u05DC\u05D5\u05D2\u05E8\u05D9\u05EA\u05DD",
                "\u062D\u0644 \u0627\u0644\u0644\u0648\u063A\u0627\u0631\u064A\u062A\u0645",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "32", true, null), new SeedOption("B", "10", false, "Confused with log\u2081\u2080"), new SeedOption("C", "25", false, "Multiplied base by exp"), new SeedOption("D", "8", false, "Wrong base") },
                    new[] { new SeedOption("A", "log(ab)", true, null), new SeedOption("B", "log(a+b)", false, "Added instead"), new SeedOption("C", "log(a)/log(b)", false, "Divided"), new SeedOption("D", "2log(a)", false, "Assumed equal") },
                }),
            new("Statistics", new[] { "statistics" },
                new[]
                {
                    "Find the mean of: {n1}, {n2}, {n3}, {coeff}, {exp}",
                    "Calculate the median of {n1} data points with given values",
                    "What is the standard deviation of: {n1}, {n2}, {n3}?",
                    "In a dataset, the mean is {n1} and std dev is {coeff}. Find z-score of {n2}.",
                },
                "\u05D7\u05E9\u05D1 \u05D0\u05EA \u05D4\u05DE\u05DE\u05D5\u05E6\u05E2",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0645\u062A\u0648\u0633\u0637",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "12.4", true, null), new SeedOption("B", "62", false, "Sum not mean"), new SeedOption("C", "10", false, "Median not mean"), new SeedOption("D", "15", false, "Rounded up") },
                    new[] { new SeedOption("A", "2.5", true, null), new SeedOption("B", "5", false, "Variance not stddev"), new SeedOption("C", "1.25", false, "Halved"), new SeedOption("D", "0", false, "All same") },
                }),
            new("Trigonometry", new[] { "trigonometry" },
                new[]
                {
                    "Find sin({n1}\u00B0) given the unit circle",
                    "Solve: {coeff}sin(x) = {n3} for 0 \u2264 x \u2264 2\u03C0",
                    "Simplify: sin\u00B2(\u03B8) + cos\u00B2(\u03B8)",
                    "Find the period of f(x) = sin({coeff}x)",
                },
                "\u05D7\u05E9\u05D1 \u05D8\u05E8\u05D9\u05D2\u05D5\u05E0\u05D5\u05DE\u05D8\u05E8\u05D9\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0645\u062B\u0644\u062B\u0627\u062A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "1", true, null), new SeedOption("B", "0", false, "Wrong angle"), new SeedOption("C", "0.5", false, "30\u00B0 value"), new SeedOption("D", "-1", false, "180\u00B0 value") },
                    new[] { new SeedOption("A", "2\u03C0/3", true, null), new SeedOption("B", "\u03C0", false, "Wrong coefficient"), new SeedOption("C", "\u03C0/3", false, "Halved"), new SeedOption("D", "6\u03C0", false, "Multiplied") },
                }),
            new("Complex Numbers", new[] { "complex-numbers" },
                new[]
                {
                    "Express z = {n1}+{n2}i in polar form",
                    "Find |z| for z = {n1} - {n2}i",
                    "Multiply: ({n1}+{n3}i)({n2}-{coeff}i)",
                    "Find the conjugate of z = {coeff} + {n1}i",
                },
                "\u05DE\u05E6\u05D0 \u05D0\u05EA \u05D4\u05E6\u05D5\u05E8\u05D4 \u05D4\u05E7\u05D5\u05D8\u05D1\u05D9\u05EA",
                "\u0623\u0648\u062C\u062F \u0627\u0644\u0634\u0643\u0644 \u0627\u0644\u0642\u0637\u0628\u064A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "5(cos 53\u00B0 + i sin 53\u00B0)", true, null), new SeedOption("B", "7(cos 45\u00B0 + i sin 45\u00B0)", false, "Wrong modulus"), new SeedOption("C", "25(cos 53\u00B0 + i sin 53\u00B0)", false, "Used r\u00B2"), new SeedOption("D", "5", false, "Lost angle") },
                    new[] { new SeedOption("A", "13", true, null), new SeedOption("B", "7", false, "Added components"), new SeedOption("C", "1", false, "Subtracted"), new SeedOption("D", "169", false, "Forgot square root") },
                }),
            new("Combinatorics", new[] { "combinatorics" },
                new[]
                {
                    "How many ways to choose {n3} from {n1} items?",
                    "Calculate P({n1},{n3}) — permutations",
                    "How many {n3}-digit codes from digits 0-9 (repetition allowed)?",
                    "In how many ways can {n1} people sit around a circular table?",
                },
                "\u05D7\u05E9\u05D1 \u05E7\u05D5\u05DE\u05D1\u05D9\u05E0\u05D8\u05D5\u05E8\u05D9\u05E7\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u062A\u0648\u0627\u0641\u064A\u0642",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "10", true, null), new SeedOption("B", "20", false, "Permutation not combination"), new SeedOption("C", "5", false, "Divided by 2"), new SeedOption("D", "1", false, "Wrong formula") },
                    new[] { new SeedOption("A", "1000", true, null), new SeedOption("B", "720", false, "No repetition"), new SeedOption("C", "30", false, "Added digits"), new SeedOption("D", "100", false, "Two digits") },
                }),
            new("Vectors", new[] { "vectors" },
                new[]
                {
                    "Find the dot product of ({n1},{n2}) and ({n3},{coeff})",
                    "Calculate the magnitude of vector ({n1},{n2},{n3})",
                    "Are vectors ({n1},{n2}) and ({n3},{coeff}) perpendicular?",
                    "Find the cross product of ({n1},0,{n2}) and (0,{n3},{coeff})",
                },
                "\u05D7\u05E9\u05D1 \u05DE\u05DB\u05E4\u05DC\u05EA \u05E1\u05E7\u05DC\u05E8\u05D9\u05EA",
                "\u0623\u0648\u062C\u062F \u0627\u0644\u062C\u062F\u0627\u0621 \u0627\u0644\u0633\u0644\u0645\u064A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "23", true, null), new SeedOption("B", "46", false, "Doubled"), new SeedOption("C", "-23", false, "Sign error"), new SeedOption("D", "0", false, "Perpendicular assumption") },
                    new[] { new SeedOption("A", "7.07", true, null), new SeedOption("B", "10", false, "Added components"), new SeedOption("C", "50", false, "Didn't take root"), new SeedOption("D", "3.5", false, "Halved") },
                }),
        },
        ["Physics"] = new QuestionTemplate[]
        {
            new("Kinematics", new[] { "kinematics" },
                new[]
                {
                    "An object accelerates at {coeff} m/s\u00B2 for {n1} seconds from rest. Find final velocity.",
                    "A ball is dropped from {n1}m. Time to reach ground?",
                    "Projectile at {n1}\u00B0 with v\u2080={n2} m/s. Find max height.",
                    "Two cars start together; one at {n1} m/s, other at {coeff} m/s\u00B2. When do they meet?",
                },
                "\u05D7\u05E9\u05D1 \u05E7\u05D9\u05E0\u05DE\u05D8\u05D9\u05E7\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u062D\u0631\u0643\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "20 m/s", true, null), new SeedOption("B", "10 m/s", false, "Halved"), new SeedOption("C", "40 m/s", false, "Doubled"), new SeedOption("D", "5 m/s", false, "Divided by time") },
                    new[] { new SeedOption("A", "2.0 s", true, null), new SeedOption("B", "4.0 s", false, "Used wrong g"), new SeedOption("C", "1.0 s", false, "Missing factor"), new SeedOption("D", "3.0 s", false, "Calculation error") },
                }),
            new("Dynamics", new[] { "dynamics" },
                new[]
                {
                    "A {n1}kg box on frictionless surface. F={n2}N. Find acceleration.",
                    "Find tension in rope pulling {n1}kg at {coeff}m/s\u00B2 upward.",
                    "Object slides down {n1}\u00B0 incline. Find acceleration (\u03BC=0.{n3}).",
                    "Two masses {n1}kg and {n2}kg connected by string over pulley. Find acceleration.",
                },
                "\u05D7\u05E9\u05D1 \u05D3\u05D9\u05E0\u05DE\u05D9\u05E7\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u0627",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "4 m/s\u00B2", true, null), new SeedOption("B", "8 m/s\u00B2", false, "Doubled"), new SeedOption("C", "2 m/s\u00B2", false, "Halved"), new SeedOption("D", "0 m/s\u00B2", false, "Forgot force") },
                    new[] { new SeedOption("A", "150 N", true, null), new SeedOption("B", "100 N", false, "Only weight"), new SeedOption("C", "50 N", false, "Only ma"), new SeedOption("D", "200 N", false, "Added twice") },
                }),
            new("Energy & Work", new[] { "energy" },
                new[]
                {
                    "Calculate KE of {n1}kg object at {n2}m/s",
                    "Work done by F={n1}N over {n2}m at {n3}\u00B0 angle",
                    "PE of {n1}kg at height {n2}m (g=10)",
                    "Spring k={n1}N/m compressed {n3}cm. Elastic PE?",
                },
                "\u05D7\u05E9\u05D1 \u05D0\u05E0\u05E8\u05D2\u05D9\u05D4 \u05D5\u05E2\u05D1\u05D5\u05D3\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0637\u0627\u0642\u0629 \u0648\u0627\u0644\u0634\u063A\u0644",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "200 J", true, null), new SeedOption("B", "100 J", false, "Forgot \u00BD"), new SeedOption("C", "400 J", false, "Forgot v\u00B2"), new SeedOption("D", "50 J", false, "Wrong formula") },
                    new[] { new SeedOption("A", "300 J", true, null), new SeedOption("B", "150 J", false, "Halved"), new SeedOption("C", "600 J", false, "Doubled"), new SeedOption("D", "30 J", false, "Wrong units") },
                }),
            new("Electricity", new[] { "electricity" },
                new[]
                {
                    "Current through {n1}\u03A9 resistor with {n2}V applied",
                    "Equivalent resistance: {n1}\u03A9 and {n2}\u03A9 in parallel",
                    "Power dissipated by {n1}\u03A9 resistor at {coeff}A",
                    "Charge flowing through circuit in {n1}s at {coeff}A",
                },
                "\u05D7\u05E9\u05D1 \u05D7\u05E9\u05DE\u05DC",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0643\u0647\u0631\u0628\u0627\u0621",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "2 A", true, null), new SeedOption("B", "4 A", false, "Used V\u00D7R"), new SeedOption("C", "0.5 A", false, "Inverted"), new SeedOption("D", "10 A", false, "Wrong calculation") },
                    new[] { new SeedOption("A", "3.33 \u03A9", true, null), new SeedOption("B", "15 \u03A9", false, "Series not parallel"), new SeedOption("C", "7.5 \u03A9", false, "Average"), new SeedOption("D", "0.3 \u03A9", false, "Inverted sum") },
                }),
            new("Waves", new[] { "waves" },
                new[]
                {
                    "Wave: f={n1}Hz, \u03BB={n2}m. Speed?",
                    "Calculate frequency: speed {n1}m/s, wavelength {n2}m",
                    "Standing wave on {n1}m string. Wavelength of 3rd harmonic?",
                    "Sound speed 340m/s. Wavelength at {n1}Hz?",
                },
                "\u05D7\u05E9\u05D1 \u05D2\u05DC\u05D9\u05DD",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0645\u0648\u062C\u0627\u062A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "340 m/s", true, null), new SeedOption("B", "170 m/s", false, "Halved"), new SeedOption("C", "680 m/s", false, "Doubled"), new SeedOption("D", "34 m/s", false, "Wrong order") },
                    new[] { new SeedOption("A", "0.68 m", true, null), new SeedOption("B", "1.36 m", false, "Doubled"), new SeedOption("C", "0.34 m", false, "Halved"), new SeedOption("D", "170 m", false, "Inverted") },
                }),
            new("Thermodynamics", new[] { "thermodynamics" },
                new[]
                {
                    "Heat to raise {n1}g water by {n2}\u00B0C (c=4.18 J/g\u00B0C)",
                    "Efficiency of engine: Q_in={n1}J, W={n2}J",
                    "Final temp: {n1}g water at {n2}\u00B0C mixed with {n1}g at {n3}\u00B0C",
                    "Latent heat: {n1}g ice at 0\u00B0C to water (L=334 J/g)",
                },
                "\u05D7\u05E9\u05D1 \u05EA\u05E8\u05DE\u05D5\u05D3\u05D9\u05E0\u05DE\u05D9\u05E7\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u0627 \u0627\u0644\u062D\u0631\u0627\u0631\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "4180 J", true, null), new SeedOption("B", "2090 J", false, "Halved"), new SeedOption("C", "8360 J", false, "Doubled"), new SeedOption("D", "418 J", false, "Wrong mass") },
                    new[] { new SeedOption("A", "40%", true, null), new SeedOption("B", "60%", false, "Used Q_out"), new SeedOption("C", "80%", false, "Doubled"), new SeedOption("D", "20%", false, "Halved") },
                }),
            new("Magnetism", new[] { "magnetism" },
                new[]
                {
                    "Force on {n1}m wire carrying {coeff}A in {n3}T field at 90\u00B0",
                    "Magnetic field at center of solenoid: n={n1}, I={coeff}A, L={n2}m",
                    "EMF induced in {n1}-turn coil as flux changes by {n2}Wb in {n3}s",
                    "Lorentz force on charge {coeff}C at {n1}m/s in {n3}T field",
                },
                "\u05D7\u05E9\u05D1 \u05DE\u05D2\u05E0\u05D8\u05D9\u05D5\u05EA",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0645\u063A\u0646\u0627\u0637\u064A\u0633\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "3 N", true, null), new SeedOption("B", "6 N", false, "Doubled"), new SeedOption("C", "1.5 N", false, "Halved"), new SeedOption("D", "0.3 N", false, "Decimal error") },
                    new[] { new SeedOption("A", "10 V", true, null), new SeedOption("B", "5 V", false, "Missing turns"), new SeedOption("C", "20 V", false, "Doubled"), new SeedOption("D", "1 V", false, "Single turn") },
                }),
            new("Optics", new[] { "optics" },
                new[]
                {
                    "Image distance for object at {n1}cm from f={n2}cm convex lens",
                    "Snell's law: n\u2081={n3}, \u03B8\u2081={n1}\u00B0, n\u2082={coeff}. Find \u03B8\u2082.",
                    "Critical angle: n\u2081={coeff}, n\u2082=1",
                    "Magnification of mirror: object {n1}cm, image {n2}cm from mirror",
                },
                "\u05D7\u05E9\u05D1 \u05D0\u05D5\u05E4\u05D8\u05D9\u05E7\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0628\u0635\u0631\u064A\u0627\u062A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "30 cm", true, null), new SeedOption("B", "15 cm", false, "Used 1/f only"), new SeedOption("C", "60 cm", false, "Doubled"), new SeedOption("D", "10 cm", false, "Wrong formula") },
                    new[] { new SeedOption("A", "42\u00B0", true, null), new SeedOption("B", "30\u00B0", false, "Wrong ratio"), new SeedOption("C", "90\u00B0", false, "Total internal"), new SeedOption("D", "60\u00B0", false, "Swapped n values") },
                }),
        },
        ["Chemistry"] = new QuestionTemplate[]
        {
            new("Acids & Bases", new[] { "acids-bases" },
                new[]
                {
                    "Calculate pH of 0.{n3}M HCl solution",
                    "What is the pOH of a solution with pH={n3}?",
                    "Buffer solution: {n1}mL of 0.1M acetic acid + {n2}mL of 0.1M NaOH. Find pH.",
                    "Identify: is NH\u2084Cl acidic, basic, or neutral in water?",
                },
                "\u05D7\u05E9\u05D1 \u05D7\u05D5\u05DE\u05E6\u05D5\u05EA \u05D5\u05D1\u05E1\u05D9\u05E1\u05D9\u05DD",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0623\u062D\u0645\u0627\u0636 \u0648\u0627\u0644\u0642\u0648\u0627\u0639\u062F",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "2", true, null), new SeedOption("B", "12", false, "pOH not pH"), new SeedOption("C", "7", false, "Neutral"), new SeedOption("D", "0.01", false, "Concentration") },
                    new[] { new SeedOption("A", "Acidic", true, null), new SeedOption("B", "Basic", false, "Conjugate acid of weak base"), new SeedOption("C", "Neutral", false, "Not neutral"), new SeedOption("D", "Amphoteric", false, "Not amphoteric") },
                }),
            new("Stoichiometry", new[] { "stoichiometry" },
                new[]
                {
                    "How many moles in {n1}g of NaCl (M=58.5)?",
                    "Mass of {coeff} moles of H\u2082O (M=18)",
                    "Volume of {n1}mol gas at STP",
                    "Limiting reagent: {n1}mol A + {n2}mol B \u2192 products (1:2 ratio)",
                },
                "\u05D7\u05E9\u05D1 \u05E1\u05D8\u05D5\u05D9\u05DB\u05D9\u05D5\u05DE\u05D8\u05E8\u05D9\u05D4",
                "\u0627\u062D\u0633\u0628 \u0627\u0644\u0633\u062A\u0648\u064A\u0643\u064A\u0648\u0645\u062A\u0631\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "2 mol", true, null), new SeedOption("B", "0.5 mol", false, "Inverted"), new SeedOption("C", "117 mol", false, "Didn't divide"), new SeedOption("D", "58.5 mol", false, "Used M directly") },
                    new[] { new SeedOption("A", "22.4 L", true, null), new SeedOption("B", "11.2 L", false, "Halved"), new SeedOption("C", "44.8 L", false, "Doubled"), new SeedOption("D", "1 L", false, "Wrong assumption") },
                }),
            new("Chemical Bonding", new[] { "chemical-bonding" },
                new[]
                {
                    "What type of bond forms between Na and Cl?",
                    "Predict bond angle in H\u2082O",
                    "Which molecule is nonpolar: CO\u2082, H\u2082O, NH\u2083, CH\u2084?",
                    "Explain why diamond is harder than graphite",
                },
                "\u05E7\u05E9\u05E8\u05D9\u05DD \u05DB\u05D9\u05DE\u05D9\u05D9\u05DD",
                "\u0627\u0644\u0631\u0648\u0627\u0628\u0637 \u0627\u0644\u0643\u064A\u0645\u064A\u0627\u0626\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Ionic bond", true, null), new SeedOption("B", "Covalent bond", false, "Nonmetal-nonmetal"), new SeedOption("C", "Metallic bond", false, "Metal-metal"), new SeedOption("D", "Van der Waals", false, "Intermolecular") },
                    new[] { new SeedOption("A", "CO\u2082 and CH\u2084", true, null), new SeedOption("B", "H\u2082O", false, "Bent = polar"), new SeedOption("C", "NH\u2083", false, "Pyramidal = polar"), new SeedOption("D", "All are nonpolar", false, "Incorrect") },
                }),
            new("Redox", new[] { "redox" },
                new[]
                {
                    "Assign oxidation states in K\u2082Cr\u2082O\u2087",
                    "Balance redox: Fe\u00B2\u207A \u2192 Fe\u00B3\u207A (in acidic solution)",
                    "Identify reducing agent: Zn + Cu\u00B2\u207A \u2192 Zn\u00B2\u207A + Cu",
                    "What is oxidized in: 2Mg + O\u2082 \u2192 2MgO?",
                },
                "\u05D7\u05DE\u05E6\u05D5\u05DF-\u05D7\u05D9\u05D6\u05D5\u05E8",
                "\u0627\u0644\u0623\u0643\u0633\u062F\u0629 \u0648\u0627\u0644\u0627\u062E\u062A\u0632\u0627\u0644",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Zn is the reducing agent", true, null), new SeedOption("B", "Cu\u00B2\u207A", false, "That's the oxidizing agent"), new SeedOption("C", "Zn\u00B2\u207A", false, "Product"), new SeedOption("D", "Cu", false, "Product") },
                    new[] { new SeedOption("A", "Mg is oxidized", true, null), new SeedOption("B", "O\u2082 is oxidized", false, "O\u2082 is reduced"), new SeedOption("C", "MgO is oxidized", false, "Product"), new SeedOption("D", "Nothing is oxidized", false, "Redox occurs") },
                }),
            new("Organic Chemistry", new[] { "organic-chemistry" },
                new[]
                {
                    "Name the functional group: -OH",
                    "IUPAC name for CH\u2083CH\u2082CH\u2083",
                    "Product of ethanol + acetic acid (with catalyst)",
                    "Type of isomerism in CH\u2083CH\u2082OH vs CH\u2083OCH\u2083",
                },
                "\u05DB\u05D9\u05DE\u05D9\u05D4 \u05D0\u05D5\u05E8\u05D2\u05E0\u05D9\u05EA",
                "\u0627\u0644\u0643\u064A\u0645\u064A\u0627\u0621 \u0627\u0644\u0639\u0636\u0648\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Hydroxyl (alcohol)", true, null), new SeedOption("B", "Carboxyl", false, "That's -COOH"), new SeedOption("C", "Aldehyde", false, "That's -CHO"), new SeedOption("D", "Ether", false, "That's R-O-R") },
                    new[] { new SeedOption("A", "Ethyl ethanoate (ester)", true, null), new SeedOption("B", "Ethanol", false, "Reactant"), new SeedOption("C", "Ethane", false, "No functional group"), new SeedOption("D", "Acetic anhydride", false, "Different reaction") },
                }),
            new("Electrochemistry", new[] { "electrochemistry" },
                new[]
                {
                    "Calculate cell voltage: E\u00B0(Cu)=+0.34V, E\u00B0(Zn)=-0.76V",
                    "Which electrode is the anode in a Zn-Cu cell?",
                    "Faraday's law: charge to deposit {n1}g of Cu (M=63.5, n=2)",
                    "Predict if reaction is spontaneous: E\u00B0cell={coeff}.{n3}V",
                },
                "\u05D0\u05DC\u05E7\u05D8\u05E8\u05D5\u05DB\u05D9\u05DE\u05D9\u05D4",
                "\u0627\u0644\u0643\u064A\u0645\u064A\u0627\u0621 \u0627\u0644\u0643\u0647\u0631\u0628\u0627\u0626\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "1.10 V", true, null), new SeedOption("B", "0.34 V", false, "Only cathode"), new SeedOption("C", "-1.10 V", false, "Wrong sign"), new SeedOption("D", "0.42 V", false, "Subtracted wrong way") },
                    new[] { new SeedOption("A", "Zn is the anode", true, null), new SeedOption("B", "Cu is the anode", false, "Cu is cathode"), new SeedOption("C", "Both are anodes", false, "One each"), new SeedOption("D", "Neither", false, "Must have both") },
                }),
            new("Atomic Structure", new[] { "atomic-structure" },
                new[]
                {
                    "How many electrons in the 3rd shell of element Z={n1}?",
                    "Write electron configuration for element with Z={n1}",
                    "Identify the element: 1s\u00B2 2s\u00B2 2p\u2076 3s\u00B2 3p\u2074",
                    "How many unpaired electrons in nitrogen (Z=7)?",
                },
                "\u05DE\u05D1\u05E0\u05D4 \u05D0\u05D8\u05D5\u05DE\u05D9",
                "\u0627\u0644\u0628\u0646\u064A\u0629 \u0627\u0644\u0630\u0631\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Sulfur (S)", true, null), new SeedOption("B", "Silicon (Si)", false, "Z=14"), new SeedOption("C", "Phosphorus (P)", false, "Z=15"), new SeedOption("D", "Chlorine (Cl)", false, "Z=17") },
                    new[] { new SeedOption("A", "3 unpaired electrons", true, null), new SeedOption("B", "1 unpaired", false, "Wrong filling"), new SeedOption("C", "5 unpaired", false, "All unpaired"), new SeedOption("D", "0 unpaired", false, "All paired") },
                }),
            new("Solutions", new[] { "solutions" },
                new[]
                {
                    "Molarity of {n1}g NaCl in {n2}mL solution (M=58.5)",
                    "Dilution: {coeff}mL of {n1}M to {n2}mL. Final concentration?",
                    "Boiling point elevation: {coeff}m solution, Kb=0.512\u00B0C/m",
                    "Osmotic pressure of {coeff}M solution at {n1}\u00B0C",
                },
                "\u05EA\u05DE\u05D9\u05E1\u05D5\u05EA",
                "\u0627\u0644\u0645\u062D\u0627\u0644\u064A\u0644",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "0.5 M", true, null), new SeedOption("B", "1.0 M", false, "Wrong volume"), new SeedOption("C", "2.0 M", false, "Inverted"), new SeedOption("D", "0.25 M", false, "Halved") },
                    new[] { new SeedOption("A", "0.512\u00B0C", true, null), new SeedOption("B", "1.024\u00B0C", false, "Doubled"), new SeedOption("C", "0.256\u00B0C", false, "Halved"), new SeedOption("D", "5.12\u00B0C", false, "Wrong magnitude") },
                }),
        },
        ["Biology"] = new QuestionTemplate[]
        {
            new("Cell Biology", new[] { "cell-biology" },
                new[]
                {
                    "Compare structure of prokaryotic and eukaryotic cells",
                    "What is the function of the endoplasmic reticulum?",
                    "Describe the fluid mosaic model of cell membrane",
                    "Explain active transport vs passive transport",
                },
                "\u05D1\u05D9\u05D5\u05DC\u05D5\u05D2\u05D9\u05D4 \u05EA\u05D0\u05D9\u05EA",
                "\u0628\u064A\u0648\u0644\u0648\u062C\u064A\u0627 \u0627\u0644\u062E\u0644\u064A\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Eukaryotes have membrane-bound nucleus; prokaryotes don't", true, null), new SeedOption("B", "Prokaryotes are larger", false, "Opposite"), new SeedOption("C", "Both have nucleus", false, "Only eukaryotes"), new SeedOption("D", "No structural differences", false, "Major differences exist") },
                    new[] { new SeedOption("A", "Active uses ATP; passive doesn't", true, null), new SeedOption("B", "Both use ATP", false, "Only active"), new SeedOption("C", "Passive is faster", false, "Not necessarily"), new SeedOption("D", "They are the same", false, "Different mechanisms") },
                }),
            new("Genetics", new[] { "genetics" },
                new[]
                {
                    "Predict offspring ratios for Aa x aa cross",
                    "Explain codominance with an example",
                    "How does X-linked inheritance differ from autosomal?",
                    "Describe the relationship between genotype and phenotype",
                },
                "\u05D2\u05E0\u05D8\u05D9\u05E7\u05D4",
                "\u0627\u0644\u0648\u0631\u0627\u062B\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "1:1 ratio (Aa:aa)", true, null), new SeedOption("B", "3:1 ratio", false, "That's Aa x Aa"), new SeedOption("C", "All Aa", false, "That's AA x aa"), new SeedOption("D", "All aa", false, "Wrong cross") },
                    new[] { new SeedOption("A", "Both alleles fully expressed (e.g., AB blood type)", true, null), new SeedOption("B", "One allele masks the other", false, "That's dominance"), new SeedOption("C", "Blending of traits", false, "That's incomplete dominance"), new SeedOption("D", "Multiple genes control one trait", false, "That's polygenic") },
                }),
            new("Photosynthesis", new[] { "photosynthesis" },
                new[]
                {
                    "Compare C3, C4, and CAM photosynthesis pathways",
                    "Where do light reactions occur in the chloroplast?",
                    "What is the role of NADPH in the Calvin cycle?",
                    "How does CO\u2082 concentration affect photosynthesis rate?",
                },
                "\u05E4\u05D5\u05D8\u05D5\u05E1\u05D9\u05E0\u05EA\u05D6\u05D4",
                "\u0627\u0644\u062A\u0645\u062B\u064A\u0644 \u0627\u0644\u0636\u0648\u0626\u064A",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Thylakoid membranes", true, null), new SeedOption("B", "Stroma", false, "Calvin cycle location"), new SeedOption("C", "Cytoplasm", false, "Not in chloroplast"), new SeedOption("D", "Inner membrane", false, "Wrong membrane") },
                    new[] { new SeedOption("A", "Provides reducing power to fix CO\u2082", true, null), new SeedOption("B", "Splits water", false, "That's photolysis"), new SeedOption("C", "Produces O\u2082", false, "Byproduct of light reactions"), new SeedOption("D", "Generates ATP", false, "That's ATP synthase") },
                }),
            new("Evolution", new[] { "evolution" },
                new[]
                {
                    "Distinguish between homologous and analogous structures",
                    "Explain genetic drift and its effect on small populations",
                    "What evidence supports the theory of evolution?",
                    "Describe speciation through geographic isolation",
                },
                "\u05D0\u05D1\u05D5\u05DC\u05D5\u05E6\u05D9\u05D4",
                "\u0627\u0644\u062A\u0637\u0648\u0631",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Homologous: same origin, different function; Analogous: different origin, same function", true, null), new SeedOption("B", "They are the same concept", false, "Different concepts"), new SeedOption("C", "Homologous means identical", false, "Same origin not identical"), new SeedOption("D", "Analogous means related species", false, "Convergent evolution") },
                    new[] { new SeedOption("A", "Random changes in allele frequency, stronger in small populations", true, null), new SeedOption("B", "Directed changes toward fitness", false, "That's natural selection"), new SeedOption("C", "Only affects large populations", false, "Opposite"), new SeedOption("D", "Same as mutation", false, "Different mechanism") },
                }),
            new("Ecology", new[] { "ecology" },
                new[]
                {
                    "Describe energy flow through a food chain",
                    "Explain the nitrogen cycle",
                    "What is the difference between a community and an ecosystem?",
                    "How does biodiversity affect ecosystem stability?",
                },
                "\u05D0\u05E7\u05D5\u05DC\u05D5\u05D2\u05D9\u05D4",
                "\u0627\u0644\u0628\u064A\u0626\u0629",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "~10% energy transferred between trophic levels", true, null), new SeedOption("B", "100% energy transferred", false, "Energy lost as heat"), new SeedOption("C", "Energy increases up the chain", false, "Decreases"), new SeedOption("D", "No energy is lost", false, "2nd law of thermodynamics") },
                    new[] { new SeedOption("A", "Community is living organisms; ecosystem includes abiotic factors", true, null), new SeedOption("B", "They are identical", false, "Different levels"), new SeedOption("C", "Ecosystem is smaller", false, "Ecosystem is broader"), new SeedOption("D", "Community includes soil and water", false, "Those are abiotic") },
                }),
            new("Human Physiology", new[] { "human-physiology" },
                new[]
                {
                    "Describe the path of blood through the heart",
                    "How do nephrons filter blood in the kidney?",
                    "Explain the role of hemoglobin in oxygen transport",
                    "Compare sympathetic and parasympathetic nervous systems",
                },
                "\u05E4\u05D9\u05D6\u05D9\u05D5\u05DC\u05D5\u05D2\u05D9\u05D4 \u05D0\u05E0\u05D5\u05E9\u05D9\u05EA",
                "\u0641\u0633\u064A\u0648\u0644\u0648\u062C\u064A\u0627 \u0627\u0644\u0625\u0646\u0633\u0627\u0646",
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Right atrium \u2192 right ventricle \u2192 lungs \u2192 left atrium \u2192 left ventricle \u2192 body", true, null), new SeedOption("B", "Left to right only", false, "Both sides involved"), new SeedOption("C", "Blood doesn't pass through lungs", false, "Pulmonary circulation"), new SeedOption("D", "Heart has 2 chambers", false, "4 chambers") },
                    new[] { new SeedOption("A", "Sympathetic: fight-or-flight; Parasympathetic: rest-and-digest", true, null), new SeedOption("B", "Both are the same", false, "Opposing functions"), new SeedOption("C", "Sympathetic slows heart", false, "Speeds up"), new SeedOption("D", "Parasympathetic isn't automatic", false, "Both are autonomic") },
                }),
        },
        ["Computer Science"] = new QuestionTemplate[]
        {
            new("Algorithms", new[] { "algorithms" },
                new[]
                {
                    "What is the time complexity of searching in a hash table (average case)?",
                    "Describe the greedy algorithm approach and give an example",
                    "Compare BFS and DFS traversal strategies",
                    "What is the complexity of finding the shortest path with Dijkstra's algorithm?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "O(1) average case", true, null), new SeedOption("B", "O(n)", false, "Worst case"), new SeedOption("C", "O(log n)", false, "Binary search"), new SeedOption("D", "O(n\u00B2)", false, "Nested loop") },
                    new[] { new SeedOption("A", "BFS uses queue, explores level by level; DFS uses stack, goes deep first", true, null), new SeedOption("B", "Both are the same", false, "Different strategies"), new SeedOption("C", "BFS always faster", false, "Depends on problem"), new SeedOption("D", "DFS finds shortest path", false, "BFS does in unweighted") },
                }),
            new("Data Structures", new[] { "data-structures" },
                new[]
                {
                    "When would you use a heap over a sorted array?",
                    "Explain how a hash map handles collisions",
                    "Compare array-based vs pointer-based stack implementations",
                    "What is a balanced binary search tree and why is it important?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "When you need efficient insert and extract-min/max: O(log n)", true, null), new SeedOption("B", "When you need random access", false, "Arrays better"), new SeedOption("C", "When memory is limited", false, "Similar memory"), new SeedOption("D", "Never, sorted array is always better", false, "Heap has faster insert") },
                    new[] { new SeedOption("A", "Chaining (linked lists) or open addressing (probing)", true, null), new SeedOption("B", "It doesn't handle collisions", false, "Must handle them"), new SeedOption("C", "Deletes the old value", false, "Both kept"), new SeedOption("D", "Resizes immediately", false, "Only at load factor threshold") },
                }),
            new("OOP", new[] { "oop" },
                new[]
                {
                    "Explain the SOLID principles in object-oriented design",
                    "What is the difference between abstract class and interface?",
                    "Describe the Observer design pattern",
                    "How does encapsulation improve code maintainability?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Abstract class can have implementation; interface only defines contract", true, null), new SeedOption("B", "They are identical", false, "Different capabilities"), new SeedOption("C", "Interface can have fields", false, "Only methods/properties"), new SeedOption("D", "Abstract class can be instantiated", false, "Cannot be instantiated") },
                    new[] { new SeedOption("A", "Hides internal state, exposes only necessary interface", true, null), new SeedOption("B", "Makes code run faster", false, "About design not speed"), new SeedOption("C", "Prevents all bugs", false, "Reduces coupling"), new SeedOption("D", "Only relevant in Java", false, "Universal OOP principle") },
                }),
            new("Databases", new[] { "databases" },
                new[]
                {
                    "Explain normalization and its forms (1NF, 2NF, 3NF)",
                    "Write SQL: find the second highest salary from employees table",
                    "What is the difference between INNER JOIN and LEFT JOIN?",
                    "Explain ACID properties in database transactions",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "INNER JOIN returns matching rows only; LEFT JOIN includes all left table rows", true, null), new SeedOption("B", "They return the same results", false, "Different row sets"), new SeedOption("C", "LEFT JOIN is always slower", false, "Depends on data"), new SeedOption("D", "INNER JOIN includes NULLs", false, "LEFT JOIN does") },
                    new[] { new SeedOption("A", "Atomicity, Consistency, Isolation, Durability", true, null), new SeedOption("B", "Availability, Consistency, Isolation, Distribution", false, "That's CAP-like"), new SeedOption("C", "Only relevant for NoSQL", false, "Core RDBMS concept"), new SeedOption("D", "Optional transaction properties", false, "Fundamental guarantees") },
                }),
            new("Boolean Logic", new[] { "boolean-logic" },
                new[]
                {
                    "Simplify: A AND (A OR B)",
                    "Truth table for XOR gate with inputs A, B",
                    "Apply De Morgan's law to: NOT(A OR B)",
                    "Convert to NAND-only: A OR B",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "A (absorption law)", true, null), new SeedOption("B", "A OR B", false, "Not simplified"), new SeedOption("C", "A AND B", false, "Wrong law"), new SeedOption("D", "B", false, "Lost A") },
                    new[] { new SeedOption("A", "NOT A AND NOT B", true, null), new SeedOption("B", "NOT A OR NOT B", false, "That's NOT(A AND B)"), new SeedOption("C", "A AND B", false, "No negation"), new SeedOption("D", "A OR B", false, "Original without NOT") },
                }),
            new("Recursion", new[] { "recursion" },
                new[]
                {
                    "Write a recursive function to calculate factorial of n",
                    "What is the base case for recursive binary search?",
                    "Explain tail recursion and its optimization benefit",
                    "Trace the recursive calls for fib(5)",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "if n <= 1 return 1; else return n * factorial(n-1)", true, null), new SeedOption("B", "No base case needed", false, "Stack overflow"), new SeedOption("C", "return n + factorial(n-1)", false, "Addition not multiplication"), new SeedOption("D", "return factorial(n) * n", false, "Infinite recursion") },
                    new[] { new SeedOption("A", "When low > high (element not found) or element found", true, null), new SeedOption("B", "When array is empty", false, "Partial answer"), new SeedOption("C", "No base case needed", false, "Essential for termination"), new SeedOption("D", "When mid = 0", false, "Too specific") },
                }),
        },
        ["English"] = new QuestionTemplate[]
        {
            new("Grammar", new[] { "grammar" },
                new[]
                {
                    "Choose correct: She ___ to the store yesterday. (go/went/gone/going)",
                    "Identify the error: 'Me and him went to the park.'",
                    "Rewrite using reported speech: 'I will finish tomorrow,' she said.",
                    "Choose the correct relative pronoun: The book ___ I read was excellent.",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "went", true, null), new SeedOption("B", "go", false, "Present tense"), new SeedOption("C", "gone", false, "Past participle needs auxiliary"), new SeedOption("D", "going", false, "Present participle") },
                    new[] { new SeedOption("A", "'He and I went to the park'", true, null), new SeedOption("B", "No error", false, "Pronoun case error"), new SeedOption("C", "'Me and he went'", false, "Still wrong case"), new SeedOption("D", "'Him and I went'", false, "Mixed cases") },
                }),
            new("Literature", new[] { "literature" },
                new[]
                {
                    "What literary device is used: 'Life is a journey'?",
                    "Identify the theme in a story about overcoming adversity",
                    "Compare the narrator types: first person vs third person omniscient",
                    "What is the purpose of foreshadowing in literature?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Metaphor", true, null), new SeedOption("B", "Simile", false, "No 'like' or 'as'"), new SeedOption("C", "Hyperbole", false, "Not exaggeration"), new SeedOption("D", "Irony", false, "Not contradictory") },
                    new[] { new SeedOption("A", "To hint at future events and build suspense", true, null), new SeedOption("B", "To confuse the reader", false, "Adds depth"), new SeedOption("C", "To summarize the plot", false, "That's an epilogue"), new SeedOption("D", "To introduce new characters", false, "Not its purpose") },
                }),
            new("Reading Comprehension", new[] { "reading-comprehension" },
                new[]
                {
                    "What is the main idea of a passage about climate change?",
                    "Infer the author's attitude toward technology from the passage",
                    "Identify cause and effect in the given paragraph",
                    "What evidence supports the claim in paragraph 3?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "The author presents a balanced view with evidence for both sides", true, null), new SeedOption("B", "The author only discusses benefits", false, "Too one-sided"), new SeedOption("C", "No main idea is presented", false, "Every passage has one"), new SeedOption("D", "The passage is purely fictional", false, "Informational text") },
                    new[] { new SeedOption("A", "Evidence from the text directly supports the claim", true, null), new SeedOption("B", "No evidence is needed", false, "Claims need support"), new SeedOption("C", "Personal opinion suffices", false, "Text-based evidence"), new SeedOption("D", "The claim contradicts the passage", false, "Must align") },
                }),
            new("Vocabulary", new[] { "vocabulary" },
                new[]
                {
                    "Choose the word that best fits: The scientist made a ___ discovery. (mundane/groundbreaking/trivial/ordinary)",
                    "What is the antonym of 'benevolent'?",
                    "Use context clues: 'The parsimonious man refused to donate.'",
                    "Which word means 'to make something less severe'?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "groundbreaking", true, null), new SeedOption("B", "mundane", false, "Means ordinary"), new SeedOption("C", "trivial", false, "Means unimportant"), new SeedOption("D", "ordinary", false, "Not notable") },
                    new[] { new SeedOption("A", "Stingy/miserly (parsimonious means extremely frugal)", true, null), new SeedOption("B", "Generous", false, "Opposite meaning"), new SeedOption("C", "Wealthy", false, "Not about wealth"), new SeedOption("D", "Angry", false, "Unrelated") },
                }),
            new("Rhetoric", new[] { "rhetoric" },
                new[]
                {
                    "Identify the rhetorical appeal: 'Studies show 90% of experts agree...'",
                    "What persuasive technique is used in emotional advertisements?",
                    "Explain ethos, pathos, and logos with examples",
                    "How does repetition function as a rhetorical device?",
                },
                null, null,
                new SeedOption[][]
                {
                    new[] { new SeedOption("A", "Logos (appeal to logic/evidence)", true, null), new SeedOption("B", "Pathos", false, "Appeal to emotion"), new SeedOption("C", "Ethos", false, "Appeal to credibility"), new SeedOption("D", "Kairos", false, "Appeal to timing") },
                    new[] { new SeedOption("A", "Emphasizes key ideas and creates rhythm/memorability", true, null), new SeedOption("B", "Fills space in a speech", false, "Strategic purpose"), new SeedOption("C", "Shows lack of vocabulary", false, "Intentional technique"), new SeedOption("D", "Only used in poetry", false, "Used across all forms") },
                }),
        },
    };

    // ── Helpers ──

    private static SeedQuestion Q(string stem, string subject, string topic, string grade,
        int bloom, float difficulty, string[] concepts, string language, string source,
        (string l, string t, bool c, string? r) a, (string l, string t, bool c, string? r) b,
        (string l, string t, bool c, string? r) c2, (string l, string t, bool c, string? r) d) =>
        new(stem, subject, topic, grade, bloom, difficulty, concepts, language, source,
            new SeedOption(a.l, a.t, a.c, a.r), new SeedOption(b.l, b.t, b.c, b.r),
            new SeedOption(c2.l, c2.t, c2.c, c2.r), new SeedOption(d.l, d.t, d.c, d.r));

    /// <summary>
    /// Create ModerationAuditDocument for all existing questions that don't have one.
    /// This handles the case where questions were seeded before the moderation system existed.
    /// </summary>
    private static async Task SeedModerationAuditDocsAsync(IDocumentStore store, ILogger logger)
    {
        await using var readSession = store.QuerySession();
        var questions = await readSession.Query<QuestionReadModel>().ToListAsync();

        await using var writeSession = store.LightweightSession();
        var now = DateTimeOffset.UtcNow;
        int created = 0;

        foreach (var q in questions)
        {
            // Check if audit doc already exists
            var existing = await readSession.LoadAsync<ModerationAuditDocument>(q.Id);
            if (existing is not null) continue;

            var auditDoc = new ModerationAuditDocument
            {
                Id = q.Id,
                QuestionId = q.Id,
                Status = ModerationItemStatus.Pending,
                SourceType = q.SourceType ?? "authored",
                AiQualityScore = q.QualityScore,
                StemPreview = q.StemPreview.Length > 120 ? q.StemPreview[..120] + "..." : q.StemPreview,
                Subject = q.Subject,
                Grade = q.Grade ?? "",
                Language = q.Language ?? "he",
                CreatedBy = SeedAuthor(created),
                SubmittedAt = q.CreatedAt != default ? q.CreatedAt : now.AddDays(-Random.Shared.Next(0, 7)),
                UpdatedAt = now,
            };
            writeSession.Store(auditDoc);
            created++;
        }

        if (created > 0)
        {
            await writeSession.SaveChangesAsync();
            logger.LogInformation("Created {Count} moderation audit docs for existing questions", created);
        }
    }

    private static string SeedAuthor(int index) => (index % 5) switch
    {
        0 => "Dr. Cohen",
        1 => "Prof. Levi",
        2 => "Sarah A.",
        3 => "Ahmed K.",
        _ => "System"
    };

    private sealed record SeedQuestion(
        string Stem, string Subject, string Topic, string Grade,
        int Bloom, float Difficulty, string[] Concepts,
        string Language, string Source, params SeedOption[] Options)
    {
        public string? Explanation { get; init; }
    };

    private sealed record SeedOption(string Label, string Text, bool IsCorrect, string? Rationale);
}
