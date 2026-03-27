// =============================================================================
// Cena Platform -- Question Bank Service
// ADM-010: Question bank browser and management
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IQuestionBankService
{
    Task<QuestionListResponse> GetQuestionsAsync(
        string? subject,
        int? bloomsLevel,
        float? minDifficulty,
        float? maxDifficulty,
        string? status,
        string? language,
        string? conceptId,
        string? search,
        int page, int pageSize, string sortBy, string orderBy);

    Task<QuestionBankDetailResponse?> GetQuestionAsync(string id);
    Task<QuestionBankDetailResponse?> UpdateQuestionAsync(string id, UpdateBankQuestionRequest request);
    Task<bool> DeprecateQuestionAsync(string id, DeprecateBankQuestionRequest request);
    Task<QuestionFiltersResponse> GetFiltersAsync();
    Task<ConceptAutocompleteResponse> AutocompleteConceptsAsync(string query);
    Task<QuestionStats?> GetPerformanceAsync(string id);
    Task<bool> ApproveAsync(string id);
}

public sealed class QuestionBankService : IQuestionBankService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<QuestionBankService> _logger;
    private static readonly List<QuestionListItem> _mockQuestions = GenerateMockQuestions();

    public QuestionBankService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<QuestionBankService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<QuestionListResponse> GetQuestionsAsync(
        string? subject,
        int? bloomsLevel,
        float? minDifficulty,
        float? maxDifficulty,
        string? status,
        string? language,
        string? conceptId,
        string? search,
        int page, int pageSize, string sortBy, string orderBy)
    {
        var questions = _mockQuestions.AsEnumerable();

        if (!string.IsNullOrEmpty(subject))
            questions = questions.Where(q => q.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase));

        if (bloomsLevel.HasValue)
            questions = questions.Where(q => q.BloomsLevel == bloomsLevel.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<QuestionStatus>(status, true, out var statusEnum))
            questions = questions.Where(q => q.Status == statusEnum);

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLowerInvariant();
            questions = questions.Where(q =>
                q.StemPreview.ToLowerInvariant().Contains(searchLower) ||
                q.Subject.ToLowerInvariant().Contains(searchLower));
        }

        // Apply sorting
        questions = sortBy?.ToLowerInvariant() switch
        {
            "difficulty" => orderBy?.ToLowerInvariant() == "desc"
                ? questions.OrderByDescending(q => q.Difficulty)
                : questions.OrderBy(q => q.Difficulty),
            "qualityscore" => orderBy?.ToLowerInvariant() == "desc"
                ? questions.OrderByDescending(q => q.QualityScore)
                : questions.OrderBy(q => q.QualityScore),
            _ => questions.OrderByDescending(q => q.QualityScore)
        };

        var total = questions.Count();
        var paged = questions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new QuestionListResponse(paged, total, page, pageSize);
    }

    public async Task<QuestionBankDetailResponse?> GetQuestionAsync(string id)
    {
        var mockItem = _mockQuestions.FirstOrDefault(q => q.Id == id);
        if (mockItem == null) return null;

        var random = new Random(id.GetHashCode());

        return new QuestionBankDetailResponse(
            Id: id,
            Stem: mockItem.StemPreview,
            StemHtml: $"<p>{mockItem.StemPreview}</p>",
            Options: new List<AnswerOptionDetail>
            {
                new("a", "A", "Option A text", "<p>Option A</p>", true, null),
                new("b", "B", "Option B text", "<p>Option B</p>", false, "Common misconception about order of operations"),
                new("c", "C", "Option C text", "<p>Option C</p>", false, "Incorrectly applies formula"),
                new("d", "D", "Option D text", "<p>Option D</p>", false, "Calculation error")
            },
            CorrectAnswers: new[] { "A" },
            Subject: mockItem.Subject,
            Topic: $"{mockItem.Subject} Fundamentals",
            Grade: "4 Units",
            BloomsLevel: mockItem.BloomsLevel,
            Difficulty: mockItem.Difficulty,
            ConceptIds: mockItem.Concepts.ToList(),
            ConceptNames: mockItem.Concepts.Select(c => c.Replace("-", " ")).ToList(),
            Status: mockItem.Status,
            QualityScore: mockItem.QualityScore,
            SourceType: random.NextSingle() > 0.5 ? "authored" : "ingested",
            SourceItemId: random.NextSingle() > 0.5 ? $"src-{random.Next(1000)}" : null,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-random.Next(1, 365)),
            UpdatedAt: DateTimeOffset.UtcNow.AddDays(-random.Next(1, 30)),
            CreatedBy: $"author-{random.Next(1, 10)}",
            Performance: new QuestionStats(
                TimesServed: random.Next(100, 5000),
                AccuracyRate: 0.4f + random.NextSingle() * 0.5f,
                AvgTimeSeconds: random.Next(30, 300),
                DiscriminationIndex: 0.3f + random.NextSingle() * 0.4f,
                new List<PerformanceByDifficulty>
                {
                    new("Easy", random.Next(50, 200), 0.7f + random.NextSingle() * 0.25f),
                    new("Medium", random.Next(100, 500), 0.5f + random.NextSingle() * 0.3f),
                    new("Hard", random.Next(20, 100), 0.3f + random.NextSingle() * 0.3f)
                }),
            Provenance: random.NextSingle() > 0.7 ? new QuestionProvenance(
                $"src-{random.Next(1000)}",
                "https://example.com/source",
                DateTimeOffset.UtcNow.AddDays(-random.Next(30, 365)),
                $"importer-{random.Next(1, 5)}",
                "Original text from source...") : null);
    }

    public async Task<QuestionBankDetailResponse?> UpdateQuestionAsync(string id, UpdateBankQuestionRequest request)
    {
        _logger.LogInformation("Updating question {QuestionId}", id);
        return await GetQuestionAsync(id);
    }

    public async Task<bool> DeprecateQuestionAsync(string id, DeprecateBankQuestionRequest request)
    {
        _logger.LogInformation("Deprecating question {QuestionId}: {Reason}", id, request.Reason);
        return true;
    }

    public async Task<QuestionFiltersResponse> GetFiltersAsync()
    {
        return new QuestionFiltersResponse(
            new[] { "Math", "Physics" },
            new[]
            {
                new ConceptFilter("algebra-linear", "Linear Equations", "Math"),
                new ConceptFilter("algebra-quadratic", "Quadratic Equations", "Math"),
                new ConceptFilter("calc-derivatives", "Derivatives", "Math"),
                new ConceptFilter("physics-kinematics", "Kinematics", "Physics"),
                new ConceptFilter("physics-dynamics", "Dynamics", "Physics")
            },
            new[] { "3 Units", "4 Units", "5 Units" });
    }

    public async Task<QuestionStats?> GetPerformanceAsync(string id)
    {
        var detail = await GetQuestionAsync(id);
        return detail?.Performance;
    }

    public async Task<bool> ApproveAsync(string id)
    {
        var exists = _mockQuestions.Any(q => q.Id == id);
        if (exists)
            _logger.LogInformation("Approving question {QuestionId}", id);
        return exists;
    }

    public async Task<ConceptAutocompleteResponse> AutocompleteConceptsAsync(string query)
    {
        var allConcepts = new[]
        {
            ("algebra-linear", "Linear Equations", "Math"),
            ("algebra-quadratic", "Quadratic Equations", "Math"),
            ("calc-derivatives", "Derivatives", "Math"),
            ("calc-integrals", "Integrals", "Math"),
            ("physics-kinematics", "Kinematics", "Physics"),
            ("physics-dynamics", "Dynamics", "Physics"),
            ("physics-waves", "Waves", "Physics")
        };

        var queryLower = query.ToLowerInvariant();
        var matches = allConcepts
            .Where(c => c.Item2.ToLowerInvariant().Contains(queryLower))
            .Select(c => new ConceptMatch(c.Item1, c.Item2, c.Item3, new Random().Next(10, 500)))
            .ToList();

        return new ConceptAutocompleteResponse(matches);
    }

    private static List<QuestionListItem> GenerateMockQuestions()
    {
        var questions = new List<QuestionListItem>();
        var subjects = new[] { "Math", "Physics" };
        var sampleStems = new[]
        {
            "Solve for x: 2x² + 5x - 3 = 0",
            "Find the derivative of f(x) = x³ - 3x² + 2x - 1",
            "A ball is thrown upward with initial velocity 20 m/s. Calculate maximum height.",
            "Prove that the sum of angles in a triangle equals 180°",
            "Calculate the electric field at distance r from point charge q",
            "Solve the integral ∫(3x² + 2x)dx",
            "Find equation of line passing through points (2,3) and (4,7)",
            "What is momentum of 5kg object moving at 10m/s?",
            "Simplify: (x² - 9)/(x² + 6x + 9)",
            "Calculate work done by force F = 10N over distance d = 5m at 30° angle"
        };

        var random = new Random(42);
        var statuses = Enum.GetValues<QuestionStatus>();

        for (int i = 0; i < 200; i++)
        {
            questions.Add(new QuestionListItem(
                Id: $"q-{i + 1:0000}",
                StemPreview: sampleStems[i % sampleStems.Length],
                Subject: subjects[i % subjects.Length],
                Concepts: new[] { $"concept-{i % 10}", $"concept-{(i + 1) % 10}" },
                BloomsLevel: random.Next(1, 7),
                Difficulty: random.NextSingle(),
                Status: statuses[random.Next(statuses.Length)],
                QualityScore: random.Next(60, 98),
                UsageCount: random.Next(0, 5000),
                SuccessRate: random.NextSingle() > 0.2f ? random.NextSingle() : null));
        }

        return questions;
    }
}
