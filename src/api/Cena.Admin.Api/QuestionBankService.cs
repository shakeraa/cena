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
            new[] { "Math", "Physics", "Chemistry", "Biology", "Computer Science", "English" },
            new[]
            {
                new ConceptFilter("linear-equations", "Linear Equations", "Math"),
                new ConceptFilter("quadratic-equations", "Quadratic Equations", "Math"),
                new ConceptFilter("derivatives", "Derivatives", "Math"),
                new ConceptFilter("integrals", "Integrals", "Math"),
                new ConceptFilter("trigonometry", "Trigonometry", "Math"),
                new ConceptFilter("probability", "Probability", "Math"),
                new ConceptFilter("kinematics", "Kinematics", "Physics"),
                new ConceptFilter("dynamics", "Dynamics", "Physics"),
                new ConceptFilter("electricity", "Electricity", "Physics"),
                new ConceptFilter("waves", "Waves", "Physics"),
                new ConceptFilter("stoichiometry", "Stoichiometry", "Chemistry"),
                new ConceptFilter("acids-bases", "Acids & Bases", "Chemistry"),
                new ConceptFilter("organic-chemistry", "Organic Chemistry", "Chemistry"),
                new ConceptFilter("genetics", "Genetics", "Biology"),
                new ConceptFilter("cell-biology", "Cell Biology", "Biology"),
                new ConceptFilter("evolution", "Evolution", "Biology"),
                new ConceptFilter("algorithms", "Algorithms", "Computer Science"),
                new ConceptFilter("data-structures", "Data Structures", "Computer Science"),
                new ConceptFilter("reading-comprehension", "Reading Comprehension", "English"),
                new ConceptFilter("grammar", "Grammar", "English")
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
            ("linear-equations", "Linear Equations", "Math"),
            ("quadratic-equations", "Quadratic Equations", "Math"),
            ("functions", "Functions", "Math"),
            ("derivatives", "Derivatives", "Math"),
            ("integrals", "Integrals", "Math"),
            ("trigonometry", "Trigonometry", "Math"),
            ("probability", "Probability", "Math"),
            ("statistics", "Statistics", "Math"),
            ("sequences", "Sequences & Series", "Math"),
            ("vectors", "Vectors", "Math"),
            ("complex-numbers", "Complex Numbers", "Math"),
            ("kinematics", "Kinematics", "Physics"),
            ("dynamics", "Dynamics", "Physics"),
            ("energy", "Energy & Work", "Physics"),
            ("momentum", "Momentum", "Physics"),
            ("waves", "Waves", "Physics"),
            ("electricity", "Electricity", "Physics"),
            ("magnetism", "Magnetism", "Physics"),
            ("optics", "Optics", "Physics"),
            ("stoichiometry", "Stoichiometry", "Chemistry"),
            ("acids-bases", "Acids & Bases", "Chemistry"),
            ("organic-chemistry", "Organic Chemistry", "Chemistry"),
            ("equilibrium", "Chemical Equilibrium", "Chemistry"),
            ("electrochemistry", "Electrochemistry", "Chemistry"),
            ("genetics", "Genetics", "Biology"),
            ("cell-biology", "Cell Biology", "Biology"),
            ("evolution", "Evolution", "Biology"),
            ("ecology", "Ecology", "Biology"),
            ("photosynthesis", "Photosynthesis", "Biology"),
            ("algorithms", "Algorithms", "Computer Science"),
            ("data-structures", "Data Structures", "Computer Science"),
            ("complexity", "Computational Complexity", "Computer Science"),
            ("reading-comprehension", "Reading Comprehension", "English"),
            ("grammar", "Grammar", "English"),
            ("literature", "Literature Analysis", "English")
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

        // Israeli Bagrut-aligned subjects and topics
        var subjectTopics = new Dictionary<string, (string[] concepts, string[] stems)>
        {
            ["Math"] = (
                new[] { "linear-equations", "quadratic-equations", "functions", "derivatives", "integrals",
                         "trigonometry", "probability", "statistics", "sequences", "vectors",
                         "complex-numbers", "analytic-geometry", "logarithms", "inequalities", "combinatorics" },
                new[]
                {
                    "Solve for x: 2x² + 5x - 3 = 0",
                    "Find the derivative of f(x) = x³ - 3x² + 2x - 1",
                    "Solve the integral ∫(3x² + 2x)dx",
                    "Find equation of line passing through points (2,3) and (4,7)",
                    "Simplify: (x² - 9)/(x² + 6x + 9)",
                    "Calculate P(A∪B) given P(A)=0.4, P(B)=0.3, P(A∩B)=0.1",
                    "Find the sum of arithmetic sequence: a₁=3, d=4, n=20",
                    "Solve: log₂(x) + log₂(x-2) = 3",
                    "Find the area enclosed by f(x)=x² and g(x)=2x",
                    "Determine if f(x) = x³-3x has local extrema",
                    "Express z = 3+4i in polar form",
                    "Find the equation of a circle passing through A(1,2), B(3,4), C(5,0)",
                    "How many ways can 5 students be arranged in a line?",
                    "Solve the system: { 2x + 3y = 7, x - y = 1 }",
                    "Find the angle between vectors u=(1,2) and v=(3,-1)",
                    "Calculate the standard deviation of: 4, 7, 2, 9, 5, 8",
                    "Prove by induction: 1+2+...+n = n(n+1)/2",
                    "Find all x satisfying |2x-3| < 5",
                    "Evaluate lim(x→0) sin(3x)/x",
                    "Find the tangent line to y=eˣ at x=1"
                }),
            ["Physics"] = (
                new[] { "kinematics", "dynamics", "energy", "momentum", "waves",
                         "optics", "electricity", "magnetism", "thermodynamics", "modern-physics",
                         "circular-motion", "gravitation", "fluid-mechanics", "oscillations" },
                new[]
                {
                    "A ball is thrown upward with initial velocity 20 m/s. Calculate maximum height.",
                    "Calculate the electric field at distance r from point charge q",
                    "What is the momentum of a 5kg object moving at 10m/s?",
                    "Calculate work done by force F=10N over distance d=5m at 30° angle",
                    "A wave has frequency 440Hz and wavelength 0.77m. Find wave speed.",
                    "Calculate the focal length of a converging lens with R₁=20cm, R₂=-30cm",
                    "Find the equivalent resistance of three 6Ω resistors in parallel",
                    "A car accelerates from rest at 3 m/s². Find distance covered in 8 seconds.",
                    "Calculate the gravitational force between two 100kg masses separated by 2m",
                    "What is the kinetic energy of a 2kg ball moving at 15 m/s?",
                    "A projectile is launched at 45° with v₀=30 m/s. Find the range.",
                    "Calculate the period of a simple pendulum of length 1.5m",
                    "Find the current through a 12V battery with internal resistance 0.5Ω and external 5.5Ω",
                    "A spring (k=200 N/m) is compressed 0.1m. Calculate stored potential energy.",
                    "Calculate the de Broglie wavelength of an electron at 1.5×10⁶ m/s",
                    "Find the magnetic force on a 2m wire carrying 5A in a 0.3T field at 90°",
                    "Calculate heat needed to raise 500g of water from 20°C to 80°C",
                    "A satellite orbits Earth at 400km altitude. Find orbital velocity."
                }),
            ["Chemistry"] = (
                new[] { "atomic-structure", "chemical-bonding", "stoichiometry", "thermochemistry",
                         "equilibrium", "acids-bases", "electrochemistry", "organic-chemistry",
                         "reaction-rates", "periodic-trends", "solutions", "redox" },
                new[]
                {
                    "Balance: Fe₂O₃ + CO → Fe + CO₂",
                    "Calculate the pH of a 0.01M HCl solution",
                    "How many moles of NaCl are in 58.5g?",
                    "Draw the Lewis structure for CO₂",
                    "Calculate the enthalpy change for: 2H₂ + O₂ → 2H₂O",
                    "What is the electron configuration of Fe²⁺?",
                    "Calculate the molarity of 4g NaOH in 500mL solution",
                    "Predict the products: CH₃COOH + NaOH →",
                    "Calculate the equilibrium constant Kc given concentrations",
                    "Identify the oxidizing agent in: Zn + CuSO₄ → ZnSO₄ + Cu",
                    "Calculate the voltage of a Zn/Cu galvanic cell",
                    "Name the IUPAC name for CH₃CH₂CH(CH₃)CH₂OH",
                    "Calculate the rate of reaction given concentration change over time",
                    "Explain why NaCl dissolves in water but not in hexane",
                    "Calculate the boiling point elevation of 1m glucose solution"
                }),
            ["Biology"] = (
                new[] { "cell-biology", "genetics", "evolution", "ecology", "human-physiology",
                         "molecular-biology", "photosynthesis", "respiration", "immune-system",
                         "nervous-system", "plant-biology", "biotechnology" },
                new[]
                {
                    "Describe the stages of mitosis and their significance",
                    "Explain Mendel's law of independent assortment with an example",
                    "Compare and contrast DNA replication in prokaryotes and eukaryotes",
                    "Calculate the expected genotype ratio from a dihybrid cross (AaBb × AaBb)",
                    "Explain the role of ATP synthase in oxidative phosphorylation",
                    "Describe the light-dependent reactions of photosynthesis",
                    "Compare Type I and Type II diabetes mellitus",
                    "Explain how a nerve impulse is transmitted across a synapse",
                    "Describe the structure and function of antibodies",
                    "Explain the process of PCR and its applications",
                    "Compare r-selected and K-selected species with examples",
                    "Describe the feedback mechanisms regulating blood glucose levels",
                    "Explain Hardy-Weinberg equilibrium and conditions for deviation",
                    "Describe the structure of a chloroplast and its role in photosynthesis"
                }),
            ["Computer Science"] = (
                new[] { "algorithms", "data-structures", "complexity", "recursion", "sorting",
                         "graphs", "dynamic-programming", "oop", "boolean-logic", "databases" },
                new[]
                {
                    "What is the time complexity of binary search?",
                    "Write pseudocode for merge sort and analyze its complexity",
                    "Explain the difference between stack and queue with examples",
                    "Design a recursive function to calculate Fibonacci(n)",
                    "Explain polymorphism in OOP with a Java/C# example",
                    "Convert the boolean expression: NOT(A AND B) to NOR gates",
                    "Write SQL to find students with grade > 90 in both Math and Physics",
                    "Trace BFS on a given graph starting from vertex A",
                    "Explain the difference between ArrayList and LinkedList",
                    "Design a class hierarchy for a school management system",
                    "What is the worst-case complexity of quicksort and when does it occur?",
                    "Explain dynamic programming with the knapsack problem"
                }),
            ["English"] = (
                new[] { "reading-comprehension", "grammar", "vocabulary", "writing", "literature",
                         "unseen-text", "rhetoric", "poetry-analysis" },
                new[]
                {
                    "Read the passage and identify the author's main argument",
                    "Choose the correct form: If I ___ (know) earlier, I would have helped.",
                    "Identify the literary device: 'The wind whispered through the trees'",
                    "Write a paragraph arguing for or against school uniforms",
                    "Analyze the theme of identity in the given poem",
                    "Match the vocabulary words to their definitions (context-based)",
                    "Rewrite the sentence in passive voice: 'The committee approved the proposal'",
                    "Summarize the main conflict in the given short story excerpt",
                    "Identify the tone and purpose of the newspaper editorial",
                    "Compare the use of symbolism in two given poems"
                })
        };

        var random = new Random(42);
        var statuses = Enum.GetValues<QuestionStatus>();
        var languages = new[] { "he", "he", "he", "he", "ar", "en" }; // 67% Hebrew, 17% Arabic, 17% English

        int id = 0;
        foreach (var (subject, (concepts, stems)) in subjectTopics)
        {
            // Generate 250 questions per subject (1500 total)
            int count = subject switch { "Math" => 400, "Physics" => 350, "Chemistry" => 250, "Biology" => 250, _ => 150 };
            for (int i = 0; i < count; i++)
            {
                id++;
                var concept1 = concepts[random.Next(concepts.Length)];
                var concept2 = concepts[random.Next(concepts.Length)];
                while (concept2 == concept1) concept2 = concepts[random.Next(concepts.Length)];

                var difficulty = random.NextSingle();
                var qualityBase = subject switch { "Math" => 70, "Physics" => 68, _ => 65 };

                questions.Add(new QuestionListItem(
                    Id: $"q-{id:0000}",
                    StemPreview: stems[random.Next(stems.Length)],
                    Subject: subject,
                    Concepts: new[] { concept1, concept2 },
                    BloomsLevel: random.Next(1, 7),
                    Difficulty: difficulty,
                    Status: statuses[random.Next(statuses.Length)],
                    QualityScore: random.Next(qualityBase, 99),
                    UsageCount: random.Next(0, 8000),
                    SuccessRate: random.NextSingle() > 0.15f ? 0.2f + random.NextSingle() * 0.7f : null));
            }
        }

        return questions;
    }
}
