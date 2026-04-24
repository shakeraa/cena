// =============================================================================
// Cena Platform — Session Question Seed Data (HARDEN SessionEndpoints)
// Idempotent seeding of questions for learning sessions
// Safe to re-run on every startup — uses upsert semantics.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seeds question bank with initial questions for learning sessions.
/// All seeds are idempotent (upsert by ID) — safe to re-run.
/// </summary>
public static class SessionQuestionSeedData
{
    /// <summary>
    /// Seed session question data. Call from DatabaseSeeder.SeedAllAsync.
    /// </summary>
    public static async Task SeedSessionQuestionsAsync(
        IDocumentStore store,
        ILogger logger)
    {
        logger.LogInformation("Seeding session question bank...");

        await using var session = store.LightweightSession();

        // Mathematics questions
        var mathQuestions = new[]
        {
            new QuestionDocument
            {
                Id = "seed:question:math:001",
                QuestionId = "q_math_001",
                Subject = "Mathematics",
                Topic = "Multiplication",
                Difficulty = "easy",
                ConceptId = "concept:math:multiplication",
                Prompt = "What is 12 × 8?",
                QuestionType = "multiple-choice",
                Choices = new[] { "92", "96", "104", "108" },
                CorrectAnswer = "96",
                Explanation = "12 × 8 = (10 × 8) + (2 × 8) = 80 + 16 = 96",
                Grade = 5,
                IsActive = true,
                // FIND-pedagogy-008: explicit learning objective link
                LearningObjectiveId = "lo-math-alg-linear-001",
                // RDY-003: foundational concept — no prerequisites
                Prerequisites = new List<string>(),
            },
            new QuestionDocument
            {
                Id = "seed:question:math:002",
                QuestionId = "q_math_002",
                Subject = "Mathematics",
                Topic = "Algebra",
                Difficulty = "medium",
                ConceptId = "concept:math:linear-equations",
                Prompt = "Solve for x: 2x + 5 = 15",
                QuestionType = "multiple-choice",
                Choices = new[] { "5", "10", "15", "20" },
                CorrectAnswer = "5",
                Explanation = "2x + 5 = 15 → 2x = 10 → x = 5",
                Grade = 7,
                IsActive = true,
                LearningObjectiveId = "lo-math-alg-linear-001",
                // RDY-003: linear equations require number properties
                Prerequisites = new List<string> { "ALG-001" },
            },
            new QuestionDocument
            {
                Id = "seed:question:math:003",
                QuestionId = "q_math_003",
                Subject = "Mathematics",
                Topic = "Calculus",
                Difficulty = "hard",
                ConceptId = "concept:math:derivatives",
                Prompt = "What is the derivative of x²?",
                QuestionType = "multiple-choice",
                Choices = new[] { "x", "2x", "x²", "2" },
                CorrectAnswer = "2x",
                Explanation = "Using the power rule: d/dx(x^n) = n·x^(n-1). So d/dx(x²) = 2x.",
                Grade = 11,
                IsActive = true,
                LearningObjectiveId = "lo-math-calc-derivatives-001",
                // RDY-003: derivatives require limits + derivative definition
                Prerequisites = new List<string> { "CAL-001", "CAL-002" },
            }
        };

        // Science questions
        var scienceQuestions = new[]
        {
            new QuestionDocument
            {
                Id = "seed:question:chem:001",
                QuestionId = "q_chem_001",
                Subject = "Chemistry",
                Topic = "Chemical Formulas",
                Difficulty = "easy",
                ConceptId = "concept:chem:water",
                Prompt = "What is the chemical symbol for water?",
                QuestionType = "multiple-choice",
                Choices = new[] { "H2O", "CO2", "O2", "NaCl" },
                CorrectAnswer = "H2O",
                Explanation = "Water consists of two hydrogen atoms and one oxygen atom, giving the formula H2O.",
                Grade = 6,
                IsActive = true,
                LearningObjectiveId = "lo-chem-acids-bases-001",
            },
            new QuestionDocument
            {
                Id = "seed:question:phys:001",
                QuestionId = "q_phys_001",
                Subject = "Physics",
                Topic = "Light",
                Difficulty = "medium",
                ConceptId = "concept:phys:speed-of-light",
                Prompt = "What is the speed of light approximately?",
                QuestionType = "multiple-choice",
                Choices = new[] { "300,000 km/s", "150,000 km/s", "1,000,000 km/s", "100,000 km/s" },
                CorrectAnswer = "300,000 km/s",
                Explanation = "The speed of light in a vacuum is approximately 299,792 km/s, often rounded to 300,000 km/s.",
                Grade = 9,
                IsActive = true,
                LearningObjectiveId = "lo-physics-mech-kinematics-001",
            }
        };

        // Store all questions
        foreach (var question in mathQuestions.Concat(scienceQuestions))
        {
            session.Store(question);
        }

        await session.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} questions to session question bank", 
            mathQuestions.Length + scienceQuestions.Length);
    }
}
