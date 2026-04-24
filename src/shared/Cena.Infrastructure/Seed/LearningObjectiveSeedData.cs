// =============================================================================
// Cena Platform — Learning Objective Seed Data
// FIND-pedagogy-008: minimum viable LO set covering seeded question subjects.
//
// Strategy:
//   - 10 hand-crafted LOs across Math, Physics, Chemistry, Biology, Computer
//     Science, English, and cross-subject competencies.
//   - Every LO picks exactly one (CognitiveProcess, KnowledgeType) per
//     Anderson &amp; Krathwohl (2001).
//   - Each LO lists the ConceptIds used by the question bank so the
//     downstream QuestionBankSeedData backfill can pick an LO via
//     concept-overlap matching.
//   - StandardsAlignment carries an example Bagrut/CCSS code so exporters
//     can later emit standards reports without a separate lookup.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Idempotent seeder for <see cref="LearningObjectiveDocument"/>. Safe to
/// re-run on every startup — uses upsert by <c>Id</c>.
/// </summary>
public static class LearningObjectiveSeedData
{
    /// <summary>
    /// Core LO set. Exposed as an enumerable so other seeders (question-bank
    /// backfill, admin test harness) can share the same canonical list
    /// without duplicating the literals.
    /// </summary>
    public static IReadOnlyList<LearningObjectiveDocument> GetSeedObjectives()
    {
        var now = DateTimeOffset.UtcNow;

        return new List<LearningObjectiveDocument>
        {
            // ── Math ──────────────────────────────────────────────────────
            new()
            {
                Id = "lo-math-alg-linear-001",
                Code = "MATH-ALG-LINEAR-001",
                Title = "Solve single-variable linear equations",
                Description =
                    "Students will be able to solve single-variable linear equations of the form ax + b = c by applying inverse operations, and verify their solution by substitution.",
                Subject = "Math",
                Grade = "3 Units",
                CognitiveProcess = CognitiveProcess.Apply,
                KnowledgeType = KnowledgeType.Procedural,
                ConceptIds = new List<string> { "linear-equations", "inequalities" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "MATH-3U-ALG-LIN",
                    ["common-core"] = "HSA.REI.B.3",
                },
                CreatedAt = now,
            },
            new()
            {
                Id = "lo-math-alg-quadratic-001",
                Code = "MATH-ALG-QUAD-001",
                Title = "Factor and solve quadratic equations",
                Description =
                    "Students will be able to factor quadratic expressions of the form x² + bx + c and solve quadratic equations by factoring, completing the square, or applying the quadratic formula.",
                Subject = "Math",
                Grade = "4 Units",
                CognitiveProcess = CognitiveProcess.Apply,
                KnowledgeType = KnowledgeType.Procedural,
                ConceptIds = new List<string> { "quadratic-equations" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "MATH-4U-ALG-QUAD",
                    ["common-core"] = "HSA.REI.B.4",
                },
                CreatedAt = now,
            },
            new()
            {
                Id = "lo-math-calc-derivatives-001",
                Code = "MATH-CALC-DERIV-001",
                Title = "Compute derivatives of polynomial and trigonometric functions",
                Description =
                    "Students will apply the power rule, product rule, chain rule, and derivatives of sin/cos to differentiate polynomial and trigonometric functions and interpret the result as a rate of change.",
                Subject = "Math",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Apply,
                KnowledgeType = KnowledgeType.Procedural,
                ConceptIds = new List<string> { "derivatives", "trigonometry", "functions" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "MATH-5U-CALC-DIFF",
                    ["common-core"] = "HSF.IF.B.6",
                },
                CreatedAt = now,
            },
            new()
            {
                Id = "lo-math-calc-integrals-001",
                Code = "MATH-CALC-INTEG-001",
                Title = "Evaluate definite integrals of elementary functions",
                Description =
                    "Students will evaluate definite integrals of polynomial and trigonometric functions using antiderivatives and interpret the result as an area under a curve.",
                Subject = "Math",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Apply,
                KnowledgeType = KnowledgeType.Procedural,
                ConceptIds = new List<string> { "integrals" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "MATH-5U-CALC-INT",
                },
                CreatedAt = now,
            },
            new()
            {
                Id = "lo-math-prob-basic-001",
                Code = "MATH-PROB-BASIC-001",
                Title = "Reason about elementary probability and combinatorics",
                Description =
                    "Students will compute probabilities of independent and mutually exclusive events, and use permutations and combinations to count outcomes in sample spaces.",
                Subject = "Math",
                Grade = "4 Units",
                CognitiveProcess = CognitiveProcess.Analyze,
                KnowledgeType = KnowledgeType.Conceptual,
                ConceptIds = new List<string> { "probability", "combinatorics", "sequences" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "MATH-4U-STAT-PROB",
                    ["common-core"] = "HSS.CP.A.1",
                },
                CreatedAt = now,
            },

            // ── Physics ───────────────────────────────────────────────────
            new()
            {
                Id = "lo-physics-mech-kinematics-001",
                Code = "PHYS-MECH-KIN-001",
                Title = "Analyze motion under constant acceleration",
                Description =
                    "Students will use kinematic equations to analyze one-dimensional motion with constant acceleration, including free-fall, and interpret position-time and velocity-time graphs.",
                Subject = "Physics",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Analyze,
                KnowledgeType = KnowledgeType.Conceptual,
                ConceptIds = new List<string> { "kinematics", "mechanics", "motion" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "PHYS-MECH-1",
                },
                CreatedAt = now,
            },

            // ── Chemistry ─────────────────────────────────────────────────
            new()
            {
                Id = "lo-chem-acids-bases-001",
                Code = "CHEM-AB-001",
                Title = "Explain acid-base behaviour and compute pH",
                Description =
                    "Students will classify substances as acids or bases using Brønsted–Lowry theory and calculate the pH of strong acid / strong base solutions.",
                Subject = "Chemistry",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Understand,
                KnowledgeType = KnowledgeType.Conceptual,
                ConceptIds = new List<string> { "acids-bases" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "CHEM-AB-1",
                },
                CreatedAt = now,
            },

            // ── Biology ───────────────────────────────────────────────────
            new()
            {
                Id = "lo-bio-cells-001",
                Code = "BIO-CELL-001",
                Title = "Describe cell structure and function",
                Description =
                    "Students will identify the major organelles in eukaryotic cells and explain the functional role of each in cellular metabolism and inheritance.",
                Subject = "Biology",
                Grade = "4 Units",
                CognitiveProcess = CognitiveProcess.Understand,
                KnowledgeType = KnowledgeType.Factual,
                ConceptIds = new List<string> { "cells", "biology-basics" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "BIO-CELL-1",
                },
                CreatedAt = now,
            },

            // ── Computer Science ──────────────────────────────────────────
            new()
            {
                Id = "lo-cs-algo-sorting-001",
                Code = "CS-ALGO-SORT-001",
                Title = "Reason about comparison sorting algorithms",
                Description =
                    "Students will implement and analyze the worst-case time complexity of bubble, insertion, merge, and quicksort, and choose an appropriate algorithm for a given problem constraint.",
                Subject = "Computer Science",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Evaluate,
                KnowledgeType = KnowledgeType.Procedural,
                ConceptIds = new List<string> { "sorting", "algorithms", "complexity" },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "CS-ALGO-1",
                },
                CreatedAt = now,
            },

            // ── English ───────────────────────────────────────────────────
            new()
            {
                Id = "lo-english-reading-infer-001",
                Code = "ENG-READ-INFER-001",
                Title = "Infer meaning from unadapted informational text",
                Description =
                    "Students will read unadapted informational text and infer the author's main claim, distinguish stated facts from implicit assumptions, and cite specific textual evidence to support the inference.",
                Subject = "English",
                Grade = "5 Units",
                CognitiveProcess = CognitiveProcess.Analyze,
                KnowledgeType = KnowledgeType.Conceptual,
                ConceptIds = new List<string>
                {
                    "reading-comprehension",
                    "literature",
                    "rhetoric",
                    "vocabulary",
                },
                StandardsAlignment = new Dictionary<string, string>
                {
                    ["bagrut"] = "ENG-READ-1",
                    ["common-core"] = "CCRA.R.1",
                },
                CreatedAt = now,
            },
        };
    }

    /// <summary>
    /// Seed the LO set. Idempotent via upsert-by-Id. Safe to re-run.
    /// </summary>
    public static async Task SeedLearningObjectivesAsync(
        IDocumentStore store,
        ILogger logger)
    {
        var objectives = GetSeedObjectives();

        await using var readSession = store.QuerySession();
        var existing = await readSession.Query<LearningObjectiveDocument>()
            .Select(x => x.Id)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        await using var writeSession = store.LightweightSession();
        int stored = 0;
        foreach (var obj in objectives)
        {
            if (!existingSet.Contains(obj.Id))
            {
                writeSession.Store(obj);
                stored++;
            }
        }

        if (stored > 0)
        {
            await writeSession.SaveChangesAsync();
            logger.LogInformation(
                "Seeded {New} learning objectives (skipped {Existing} already present)",
                stored, existingSet.Count);
        }
        else
        {
            logger.LogInformation(
                "Learning objectives already seeded — {Count} present, skipping",
                existingSet.Count);
        }
    }

    /// <summary>
    /// Pick the best LO for a question given its subject and concept ids.
    /// Used by <c>QuestionBankSeedData</c> to backfill a plausible LO on
    /// every seeded question. Returns <c>null</c> when no LO matches.
    /// </summary>
    public static string? PickBestObjectiveId(
        string subject,
        IEnumerable<string> conceptIds,
        IReadOnlyList<LearningObjectiveDocument>? objectives = null)
    {
        objectives ??= GetSeedObjectives();
        var concepts = conceptIds?.ToHashSet() ?? new HashSet<string>();

        // 1. Prefer an LO in the same subject whose concept overlap is highest.
        var bestSameSubject = objectives
            .Where(o => o.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
            .Select(o => new
            {
                Id = o.Id,
                Score = o.ConceptIds.Count(c => concepts.Contains(c)),
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (bestSameSubject is { Score: > 0 })
            return bestSameSubject.Id;

        // 2. Any LO in the same subject (first match is deterministic).
        var firstSameSubject = objectives
            .FirstOrDefault(o => o.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase));
        if (firstSameSubject != null)
            return firstSameSubject.Id;

        // 3. Any LO whose concept list overlaps.
        var bestCrossSubject = objectives
            .Select(o => new
            {
                Id = o.Id,
                Score = o.ConceptIds.Count(c => concepts.Contains(c)),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        return bestCrossSubject?.Id;
    }
}
