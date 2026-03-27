// =============================================================================
// Cena Platform -- Content Moderation Service
// ADM-005: Moderation queue management and review workflows
// =============================================================================

using System.Text.Json;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Api.Admin;

public interface IContentModerationService
{
    Task<ModerationQueueResponse> GetQueueAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        string sortBy,
        string orderBy);

    Task<QuestionDetailResponse?> GetItemDetailAsync(string id);
    Task<bool> ClaimItemAsync(string id, string moderatorId);
    Task<bool> ApproveItemAsync(string id, string moderatorId);
    Task<bool> RejectItemAsync(string id, string moderatorId, string reason);
    Task<bool> FlagItemAsync(string id, string moderatorId, string reason);
    Task<bool> RequestChangesAsync(string id, string moderatorId, string feedback);
    Task<bool> AddCommentAsync(string id, string moderatorId, string text);
    Task<bool> BulkActionAsync(string action, IReadOnlyList<string> itemIds, string moderatorId);
    Task<ModerationStatsResponse> GetStatsAsync();
    Task<QueueSummary> GetQueueSummaryAsync();
}

public sealed class ContentModerationService : IContentModerationService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ContentModerationService> _logger;
    private static readonly List<ModerationQueueItem> _mockItems = GenerateMockItems();
    private static readonly Dictionary<string, QuestionDetailResponse> _mockDetails = GenerateMockDetails();
    private static readonly List<ModerationComment> _mockComments = new();
    private static readonly List<ModerationHistory> _mockHistory = new();

    public ContentModerationService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<ContentModerationService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ModerationQueueResponse> GetQueueAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        string sortBy,
        string orderBy)
    {
        var items = _mockItems.AsEnumerable();

        // Apply status filter
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (Enum.TryParse<ModerationStatus>(status, true, out var statusEnum))
            {
                items = items.Where(i => i.Status == statusEnum);
            }
        }

        // Apply search filter
        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLowerInvariant();
            items = items.Where(i =>
                i.QuestionText.ToLowerInvariant().Contains(searchLower) ||
                i.Subject.ToLowerInvariant().Contains(searchLower) ||
                i.Author.ToLowerInvariant().Contains(searchLower));
        }

        // Apply sorting
        items = sortBy?.ToLowerInvariant() switch
        {
            "submittedat" => orderBy?.ToLowerInvariant() == "desc"
                ? items.OrderByDescending(i => i.SubmittedAt)
                : items.OrderBy(i => i.SubmittedAt),
            "subject" => orderBy?.ToLowerInvariant() == "desc"
                ? items.OrderByDescending(i => i.Subject)
                : items.OrderBy(i => i.Subject),
            "grade" => orderBy?.ToLowerInvariant() == "desc"
                ? items.OrderByDescending(i => i.Grade)
                : items.OrderBy(i => i.Grade),
            _ => orderBy?.ToLowerInvariant() == "desc"
                ? items.OrderByDescending(i => i.SubmittedAt)
                : items.OrderBy(i => i.SubmittedAt)
        };

        // Priority: flagged items first, then oldest first
        items = items
            .OrderByDescending(i => i.Status == ModerationStatus.Flagged)
            .ThenBy(i => i.SubmittedAt);

        var total = items.Count();
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ModerationQueueResponse(pagedItems, total, page, pageSize);
    }

    public async Task<QuestionDetailResponse?> GetItemDetailAsync(string id)
    {
        if (_mockDetails.TryGetValue(id, out var detail))
        {
            // Add dynamic comments and history
            var comments = _mockComments.Where(c => c.Id.StartsWith(id)).ToList();
            var history = _mockHistory.Where(h => h.User.Contains(id) || h.Action.Contains(id)).ToList();

            return detail with
            {
                Comments = comments,
                History = history
            };
        }

        return null;
    }

    public async Task<bool> ClaimItemAsync(string id, string moderatorId)
    {
        var item = _mockItems.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        // Check if already claimed by someone else
        if (!string.IsNullOrEmpty(item.AssignedTo) && item.AssignedTo != moderatorId)
            return false;

        var index = _mockItems.IndexOf(item);
        _mockItems[index] = item with
        {
            Status = ModerationStatus.InReview,
            AssignedTo = moderatorId
        };

        _mockHistory.Add(new ModerationHistory(
            DateTimeOffset.UtcNow,
            moderatorId,
            "claim",
            null));

        return true;
    }

    public async Task<bool> ApproveItemAsync(string id, string moderatorId)
    {
        var item = _mockItems.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        var index = _mockItems.IndexOf(item);
        _mockItems[index] = item with
        {
            Status = ModerationStatus.Approved,
            AssignedTo = moderatorId
        };

        _mockHistory.Add(new ModerationHistory(
            DateTimeOffset.UtcNow,
            moderatorId,
            "approve",
            null));

        return true;
    }

    public async Task<bool> RejectItemAsync(string id, string moderatorId, string reason)
    {
        var item = _mockItems.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        var index = _mockItems.IndexOf(item);
        _mockItems[index] = item with
        {
            Status = ModerationStatus.Rejected,
            AssignedTo = moderatorId
        };

        _mockHistory.Add(new ModerationHistory(
            DateTimeOffset.UtcNow,
            moderatorId,
            "reject",
            reason));

        return true;
    }

    public async Task<bool> FlagItemAsync(string id, string moderatorId, string reason)
    {
        var item = _mockItems.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        var index = _mockItems.IndexOf(item);
        _mockItems[index] = item with
        {
            Status = ModerationStatus.Flagged,
            AssignedTo = moderatorId
        };

        _mockHistory.Add(new ModerationHistory(
            DateTimeOffset.UtcNow,
            moderatorId,
            "flag",
            reason));

        return true;
    }

    public async Task<bool> RequestChangesAsync(string id, string moderatorId, string feedback)
    {
        _mockHistory.Add(new ModerationHistory(
            DateTimeOffset.UtcNow,
            moderatorId,
            "request-changes",
            feedback));

        return true;
    }

    public async Task<bool> AddCommentAsync(string id, string moderatorId, string text)
    {
        _mockComments.Add(new ModerationComment(
            $"{id}-{_mockComments.Count}",
            moderatorId,
            text,
            DateTimeOffset.UtcNow));

        return true;
    }

    public async Task<bool> BulkActionAsync(string action, IReadOnlyList<string> itemIds, string moderatorId)
    {
        foreach (var id in itemIds)
        {
            switch (action.ToLowerInvariant())
            {
                case "approve":
                    await ApproveItemAsync(id, moderatorId);
                    break;
                case "reject":
                    await RejectItemAsync(id, moderatorId, "Bulk rejection");
                    break;
                case "flag":
                    await FlagItemAsync(id, moderatorId, "Bulk flag");
                    break;
            }
        }

        return true;
    }

    public async Task<ModerationStatsResponse> GetStatsAsync()
    {
        var today = DateTimeOffset.UtcNow.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);

        var reviewedToday = _mockHistory.Count(h =>
            h.Timestamp.Date == today &&
            (h.Action == "approve" || h.Action == "reject"));

        var reviewedThisWeek = _mockHistory.Count(h =>
            h.Timestamp >= startOfWeek &&
            (h.Action == "approve" || h.Action == "reject"));

        var totalDecisions = _mockHistory.Count(h => h.Action == "approve" || h.Action == "reject");
        var approvedCount = _mockHistory.Count(h => h.Action == "approve");
        var approvalRate = totalDecisions > 0 ? (float)approvedCount / totalDecisions * 100 : 0f;

        // Per-moderator stats
        var moderatorStats = _mockHistory
            .Where(h => h.Action == "approve" || h.Action == "reject")
            .GroupBy(h => h.User)
            .Select(g => new PerModeratorStats(
                g.Key,
                g.Key,
                g.Count(),
                g.Count(h => h.Action == "approve"),
                g.Count(h => h.Action == "reject"),
                5.2f)) // Mock average review time
            .ToList();

        // Daily trend (last 7 days)
        var trend = new List<DailyModerationStats>();
        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            trend.Add(new DailyModerationStats(
                date.ToString("yyyy-MM-dd"),
                _mockItems.Count(x => x.SubmittedAt.Date == date),
                _mockHistory.Count(h => h.Timestamp.Date == date && (h.Action == "approve" || h.Action == "reject")),
                _mockHistory.Count(h => h.Timestamp.Date == date && h.Action == "approve"),
                _mockHistory.Count(h => h.Timestamp.Date == date && h.Action == "reject")));
        }

        return new ModerationStatsResponse(
            reviewedToday,
            reviewedThisWeek,
            approvalRate,
            5.2f,
            moderatorStats,
            trend);
    }

    public async Task<QueueSummary> GetQueueSummaryAsync()
    {
        var pending = _mockItems.Where(i => i.Status == ModerationStatus.Pending).ToList();
        var inReview = _mockItems.Count(i => i.Status == ModerationStatus.InReview);
        var flagged = _mockItems.Count(i => i.Status == ModerationStatus.Flagged);

        var oldest = pending.OrderBy(i => i.SubmittedAt).FirstOrDefault();
        var oldestHours = oldest != null
            ? (int)(DateTimeOffset.UtcNow - oldest.SubmittedAt).TotalHours
            : 0;

        return new QueueSummary(pending.Count, inReview, flagged, oldestHours);
    }

    // ---- Mock Data Generation ----

    private static List<ModerationQueueItem> GenerateMockItems()
    {
        var items = new List<ModerationQueueItem>();
        var subjects = new[] { "Math", "Physics" };
        var grades = new[] { "3 Units", "4 Units", "5 Units" };
        var authors = new[] { "Dr. Cohen", "Prof. Levi", "Sarah A.", "Ahmed K.", "System" };
        var statuses = new[] { ModerationStatus.Pending, ModerationStatus.InReview, ModerationStatus.Flagged };

        var sampleQuestions = new[]
        {
            "Solve for x: 2x² + 5x - 3 = 0",
            "Find the derivative of f(x) = x³ - 3x² + 2x - 1",
            "A ball is thrown upward with initial velocity 20 m/s. Calculate the maximum height reached.",
            "Prove that the sum of angles in a triangle equals 180°",
            "Calculate the electric field at a distance r from a point charge q",
            "Solve the integral ∫(3x² + 2x)dx",
            "Find the equation of the line passing through points (2,3) and (4,7)",
            "What is the momentum of a 5kg object moving at 10m/s?",
            "Simplify: (x² - 9)/(x² + 6x + 9)",
            "Calculate the work done by a force F = 10N over distance d = 5m at 30° angle",
            "Find the domain of f(x) = √(x² - 4)",
            "A wave has frequency 50Hz and wavelength 2m. What is its velocity?",
        };

        var random = new Random(123);
        for (int i = 0; i < 45; i++)
        {
            var submittedAt = DateTimeOffset.UtcNow.AddHours(-random.Next(1, 168));
            items.Add(new ModerationQueueItem(
                Id: $"q-{i + 1:0000}",
                QuestionText: sampleQuestions[i % sampleQuestions.Length],
                Subject: subjects[i % subjects.Length],
                Grade: grades[i % grades.Length],
                Author: authors[i % authors.Length],
                SubmittedAt: submittedAt,
                Status: i < 15 ? statuses[i % statuses.Length] : (i < 35 ? ModerationStatus.Pending : ModerationStatus.InReview),
                AssignedTo: i >= 35 ? $"moderator-{i % 3 + 1}" : null,
                AiQualityScore: random.Next(65, 95),
                SourceType: i % 3 == 0 ? "ingested" : "authored"
            ));
        }

        return items;
    }

    private static Dictionary<string, QuestionDetailResponse> GenerateMockDetails()
    {
        var details = new Dictionary<string, QuestionDetailResponse>();

        foreach (var item in _mockItems)
        {
            var options = new List<AnswerOption>
            {
                new("A", "First answer option", item.Id.EndsWith("1") || item.Id.EndsWith("5")),
                new("B", "Second answer option", item.Id.EndsWith("2") || item.Id.EndsWith("6")),
                new("C", "Third answer option", item.Id.EndsWith("3") || item.Id.EndsWith("7")),
                new("D", "Fourth answer option", item.Id.EndsWith("4") || item.Id.EndsWith("8") || item.Id.EndsWith("9") || item.Id.EndsWith("0"))
            };

            var correct = options.First(o => o.IsCorrect);

            details[item.Id] = new QuestionDetailResponse(
                Id: item.Id,
                QuestionText: item.QuestionText,
                Options: options,
                CorrectAnswer: correct.Label,
                Subject: item.Subject,
                Topic: $"{item.Subject} Fundamentals",
                Grade: item.Grade,
                Difficulty: "Medium",
                ConceptTags: new[] { "algebra", "equations", "solving" },
                AiQualityScore: item.AiQualityScore,
                QualityScores: new QualityBreakdown(
                    MathCorrectness: item.AiQualityScore + 3,
                    LanguageQuality: item.AiQualityScore - 2,
                    PedagogicalQuality: item.AiQualityScore,
                    PlagiarismScore: 5),
                OriginalSource: item.SourceType == "ingested" ? "Imported from exam database" : null,
                NormalizedVersion: item.SourceType == "ingested" ? item.QuestionText : null,
                Status: item.Status,
                AssignedTo: item.AssignedTo,
                SubmittedAt: item.SubmittedAt,
                Comments: new List<ModerationComment>(),
                History: new List<ModerationHistory>()
            );
        }

        return details;
    }
}
