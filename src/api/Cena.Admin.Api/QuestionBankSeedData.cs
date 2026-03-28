// =============================================================================
// Cena Platform — Question Bank Seed Data
// 100+ Bagrut-aligned questions across 6 subjects, 3 languages, 3 source types
// Distributed: Math 25, Physics 20, Chemistry 15, Biology 15, CS 15, English 10
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public static class QuestionBankSeedData
{
    public static async Task SeedQuestionsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.QuerySession();
        var existingCount = await session.Query<QuestionReadModel>().CountAsync();

        // If questions exist but moderation audit docs don't, seed just the audit docs
        if (existingCount > 0)
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

        logger.LogInformation("Seeding question bank with Bagrut-aligned questions...");
        await using var writeSession = store.LightweightSession();
        var now = DateTimeOffset.UtcNow;
        int seeded = 0;

        foreach (var q in GetSeedQuestions())
        {
            var id = $"q-{seeded + 1:0000}";
            var options = q.Options.Select(o => new QuestionOptionData(
                o.Label, o.Text, $"<p>{o.Text}</p>", o.IsCorrect, o.Rationale)).ToList();

            object creationEvent = q.Source switch
            {
                "ai-generated" => new QuestionAiGenerated_V1(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    $"Generate a {q.Subject} question about {q.Topic} at Bloom level {q.Bloom}",
                    "claude-sonnet-4-6", 0.7f,
                    $"AI output for: {q.Stem}",
                    "seed-script", now),
                "ingested" => new QuestionIngested_V1(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    $"doc-{seeded}", "https://edu.gov.il/bagrut", "bagrut-2024.pdf",
                    q.Stem, "seed-script", now),
                _ => new QuestionAuthored_V1(
                    id, q.Stem, $"<p>{q.Stem}</p>", options,
                    q.Subject, q.Topic, q.Grade, q.Bloom, q.Difficulty,
                    q.Concepts, q.Language,
                    "seed-script", now)
            };

            writeSession.Events.StartStream<QuestionState>(id, creationEvent);

            // Create matching ModerationAuditDocument for the moderation queue
            var auditDoc = new ModerationAuditDocument
            {
                Id = id,
                QuestionId = id,
                Status = ModerationItemStatus.Pending,
                SourceType = q.Source,
                AiQualityScore = (int)(q.Difficulty * 100),
                StemPreview = q.Stem.Length > 120 ? q.Stem[..120] + "..." : q.Stem,
                Subject = q.Subject,
                Grade = q.Grade,
                Language = q.Language,
                CreatedBy = q.Source == "ai-generated" ? "System" : SeedAuthor(seeded),
                SubmittedAt = now.AddDays(-Random.Shared.Next(0, 7)).AddHours(-Random.Shared.Next(0, 24)),
                UpdatedAt = now,
            };
            writeSession.Store(auditDoc);

            seeded++;
        }

        await writeSession.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} questions + moderation audit docs into event store", seeded);
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
        foreach (var q in MathQuestions()) yield return q;
        foreach (var q in PhysicsQuestions()) yield return q;
        foreach (var q in ChemistryQuestions()) yield return q;
        foreach (var q in BiologyQuestions()) yield return q;
        foreach (var q in CsQuestions()) yield return q;
        foreach (var q in EnglishQuestions()) yield return q;
    }

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
        string Language, string Source, params SeedOption[] Options);

    private sealed record SeedOption(string Label, string Text, bool IsCorrect, string? Rationale);
}
