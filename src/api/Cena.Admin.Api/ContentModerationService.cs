// =============================================================================
// Cena Platform -- Content Moderation Service (Production)
// ADM-005: Real event-sourced moderation backed by Marten + Redis.
// Replaces mock data with real queries against ModerationAuditDocument
// and QuestionState event streams.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IContentModerationService
{
    Task<ModerationQueueResponse> GetQueueAsync(
        string? status, string? search, int page, int pageSize, string sortBy, string orderBy);
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
    private readonly INatsConnection _nats;
    private readonly ILogger<ContentModerationService> _logger;

    private const int ClaimTimeoutMinutes = 30;

    public ContentModerationService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        INatsConnection nats,
        ILogger<ContentModerationService> logger)
    {
        _store = store;
        _redis = redis;
        _nats = nats;
        _logger = logger;
    }

    public async Task<ModerationQueueResponse> GetQueueAsync(
        string? status, string? search, int page, int pageSize, string sortBy, string orderBy)
    {
        await using var session = _store.QuerySession();

        var query = session.Query<ModerationAuditDocument>().AsQueryable();

        // Filter by status
        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (Enum.TryParse<ModerationItemStatus>(status, true, out var statusEnum))
            {
                query = query.Where(i => i.Status == statusEnum);
            }
        }

        // Search filter
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLowerInvariant();
            query = query.Where(i =>
                i.StemPreview.Contains(s) ||
                i.Subject.Contains(s) ||
                i.CreatedBy.Contains(s));
        }

        // Get total before pagination
        var total = await query.CountAsync();

        // Sort
        query = (sortBy?.ToLowerInvariant(), orderBy?.ToLowerInvariant()) switch
        {
            ("subject", "desc") => query.OrderByDescending(i => i.Subject),
            ("subject", _) => query.OrderBy(i => i.Subject),
            ("grade", "desc") => query.OrderByDescending(i => i.Grade),
            ("grade", _) => query.OrderBy(i => i.Grade),
            ("quality", "desc") => query.OrderByDescending(i => i.AiQualityScore),
            ("quality", _) => query.OrderBy(i => i.AiQualityScore),
            (_, "desc") => query.OrderByDescending(i => i.SubmittedAt),
            _ => query.OrderBy(i => i.SubmittedAt)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var mapped = items.Select(i => new ModerationQueueItem(
            Id: i.QuestionId,
            QuestionText: i.StemPreview,
            Subject: i.Subject,
            Grade: i.Grade,
            Author: i.CreatedBy,
            SubmittedAt: i.SubmittedAt,
            Status: MapStatus(i.Status),
            AssignedTo: i.AssignedTo,
            AiQualityScore: i.AiQualityScore,
            SourceType: i.SourceType
        )).ToList();

        return new ModerationQueueResponse(mapped, total, page, pageSize);
    }

    public async Task<QuestionDetailResponse?> GetItemDetailAsync(string id)
    {
        await using var session = _store.QuerySession();

        // Load the question aggregate state
        var questionState = await session.Events.AggregateStreamAsync<QuestionState>(id);
        if (questionState is null) return null;

        // Load the moderation audit
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);

        var options = questionState.Options.Select(o =>
            new AnswerOption(o.Label, o.Text, o.IsCorrect)).ToList();

        var correctAnswer = options.FirstOrDefault(o => o.IsCorrect)?.Label ?? "";

        var comments = audit?.Comments.Select(c =>
            new ModerationComment(c.Id, c.AuthorId, c.Text, c.CreatedAt)).ToList()
            ?? new List<ModerationComment>();

        var history = audit?.Actions.Select(a =>
            new ModerationHistory(a.Timestamp, a.ModeratorId, a.Action, a.Reason)).ToList()
            ?? new List<ModerationHistory>();

        var qualityEval = questionState.LastQualityEvaluation;

        return new QuestionDetailResponse(
            Id: questionState.Id,
            QuestionText: questionState.Stem,
            Options: options,
            CorrectAnswer: correctAnswer,
            Subject: questionState.Subject,
            Topic: questionState.Topic,
            Grade: questionState.Grade,
            Difficulty: questionState.Difficulty.ToString("F2"),
            ConceptTags: questionState.ConceptIds,
            AiQualityScore: questionState.QualityScore,
            QualityScores: new QualityBreakdown(
                MathCorrectness: qualityEval?.FactualAccuracy ?? 0,
                LanguageQuality: qualityEval?.LanguageQuality ?? 0,
                PedagogicalQuality: qualityEval?.PedagogicalQuality ?? 0,
                PlagiarismScore: 0),
            OriginalSource: questionState.Provenance?.SourceUrl,
            NormalizedVersion: questionState.Provenance?.OriginalText,
            Status: MapStatus(audit?.Status ?? ModerationItemStatus.Pending),
            AssignedTo: audit?.AssignedTo,
            SubmittedAt: questionState.CreatedAt,
            Comments: comments,
            History: history);
    }

    public async Task<bool> ClaimItemAsync(string id, string moderatorId)
    {
        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        // Check if already claimed by someone else and not expired
        if (!string.IsNullOrEmpty(audit.AssignedTo) &&
            audit.AssignedTo != moderatorId &&
            audit.ClaimExpiresAt.HasValue &&
            audit.ClaimExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Item {Id} already claimed by {Moderator} until {Expires}",
                id, audit.AssignedTo, audit.ClaimExpiresAt);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        audit.Status = ModerationItemStatus.InReview;
        audit.AssignedTo = moderatorId;
        audit.AssignedAt = now;
        audit.ClaimExpiresAt = now.AddMinutes(ClaimTimeoutMinutes);
        audit.UpdatedAt = now;
        audit.Actions.Add(new ModerationActionRecord
        {
            Action = "claim", ModeratorId = moderatorId, Timestamp = now
        });

        session.Store(audit);
        await session.SaveChangesAsync();

        _logger.LogInformation("[AUDIT] Moderation CLAIM: question={Id} moderator={Moderator}", id, moderatorId);
        return true;
    }

    public async Task<bool> ApproveItemAsync(string id, string moderatorId)
    {
        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        var now = DateTimeOffset.UtcNow;
        audit.Status = ModerationItemStatus.Approved;
        audit.UpdatedAt = now;
        audit.Actions.Add(new ModerationActionRecord
        {
            Action = "approve", ModeratorId = moderatorId, Timestamp = now
        });
        session.Store(audit);

        // Emit domain event on the question stream
        session.Events.Append(id, new QuestionApproved_V1(id, moderatorId, now));
        await session.SaveChangesAsync();

        // Publish NATS event for downstream consumers
        await _nats.PublishAsync("cena.review.item.approved",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { questionId = id, moderatorId }));

        _logger.LogInformation("[AUDIT] Moderation APPROVE: question={Id} moderator={Moderator}", id, moderatorId);
        return true;
    }

    public async Task<bool> RejectItemAsync(string id, string moderatorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            _logger.LogWarning("Rejection requires a reason: question={Id}", id);
            return false;
        }

        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        var now = DateTimeOffset.UtcNow;
        audit.Status = ModerationItemStatus.Rejected;
        audit.RejectionReason = reason;
        audit.RejectionCount++;
        audit.UpdatedAt = now;
        audit.Actions.Add(new ModerationActionRecord
        {
            Action = "reject", ModeratorId = moderatorId, Reason = reason, Timestamp = now
        });
        session.Store(audit);

        // Deprecate the question in the event stream
        session.Events.Append(id, new QuestionDeprecated_V1(
            id, $"Rejected by moderator: {reason}", true, moderatorId, now));
        await session.SaveChangesAsync();

        await _nats.PublishAsync("cena.review.item.rejected",
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { questionId = id, moderatorId, reason }));

        _logger.LogInformation("[AUDIT] Moderation REJECT: question={Id} moderator={Moderator} reason={Reason}",
            id, moderatorId, reason);
        return true;
    }

    public async Task<bool> FlagItemAsync(string id, string moderatorId, string reason)
    {
        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        var now = DateTimeOffset.UtcNow;
        audit.Status = ModerationItemStatus.Flagged;
        audit.UpdatedAt = now;
        audit.Actions.Add(new ModerationActionRecord
        {
            Action = "flag", ModeratorId = moderatorId, Reason = reason, Timestamp = now
        });
        session.Store(audit);
        await session.SaveChangesAsync();

        _logger.LogInformation("[AUDIT] Moderation FLAG: question={Id} moderator={Moderator} reason={Reason}",
            id, moderatorId, reason);
        return true;
    }

    public async Task<bool> RequestChangesAsync(string id, string moderatorId, string feedback)
    {
        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        var now = DateTimeOffset.UtcNow;
        audit.Status = ModerationItemStatus.Pending; // Return to pending for re-work
        audit.AssignedTo = null;
        audit.ClaimExpiresAt = null;
        audit.UpdatedAt = now;
        audit.Actions.Add(new ModerationActionRecord
        {
            Action = "request_changes", ModeratorId = moderatorId, Reason = feedback, Timestamp = now
        });
        session.Store(audit);
        await session.SaveChangesAsync();

        _logger.LogInformation("[AUDIT] Moderation REQUEST_CHANGES: question={Id} moderator={Moderator}",
            id, moderatorId);
        return true;
    }

    public async Task<bool> AddCommentAsync(string id, string moderatorId, string text)
    {
        await using var session = _store.LightweightSession();
        var audit = await session.LoadAsync<ModerationAuditDocument>(id);
        if (audit is null) return false;

        audit.Comments.Add(new ModerationCommentRecord
        {
            Id = $"cmt-{Guid.NewGuid():N}",
            AuthorId = moderatorId,
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow
        });
        audit.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(audit);
        await session.SaveChangesAsync();

        return true;
    }

    public async Task<bool> BulkActionAsync(string action, IReadOnlyList<string> itemIds, string moderatorId)
    {
        _logger.LogInformation(
            "[AUDIT] Moderation BULK {Action}: {Count} items by moderator={ModeratorId}",
            action, itemIds.Count, moderatorId);

        foreach (var id in itemIds)
        {
            var success = action.ToLowerInvariant() switch
            {
                "approve" => await ApproveItemAsync(id, moderatorId),
                "reject" => await RejectItemAsync(id, moderatorId, "Bulk rejection"),
                "flag" => await FlagItemAsync(id, moderatorId, "Bulk flag"),
                _ => false
            };

            if (!success)
                _logger.LogWarning("Bulk action {Action} failed for item {Id}", action, id);
        }

        return true;
    }

    public async Task<ModerationStatsResponse> GetStatsAsync()
    {
        await using var session = _store.QuerySession();
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var yesterday = today.AddDays(-1);
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);

        var allAudits = await session.Query<ModerationAuditDocument>()
            .Where(x => x.SubmittedAt >= startOfWeek.AddDays(-7))
            .ToListAsync();

        var pending = allAudits.Count(i => i.Status == ModerationItemStatus.Pending);
        var inReview = allAudits.Count(i => i.Status == ModerationItemStatus.InReview);

        // Today's approvals and rejections
        var approvedToday = allAudits
            .SelectMany(a => a.Actions)
            .Count(a => a.Action == "approve" && a.Timestamp.Date == today);
        var rejectedToday = allAudits
            .SelectMany(a => a.Actions)
            .Count(a => a.Action == "reject" && a.Timestamp.Date == today);

        // Yesterday for % change
        var approvedYesterday = allAudits
            .SelectMany(a => a.Actions)
            .Count(a => a.Action == "approve" && a.Timestamp.Date == yesterday);
        var rejectedYesterday = allAudits
            .SelectMany(a => a.Actions)
            .Count(a => a.Action == "reject" && a.Timestamp.Date == yesterday);

        float PctChange(int current, int prev) =>
            prev == 0 ? (current > 0 ? 100f : 0f) : (float)(current - prev) / prev * 100f;

        // Week aggregate
        var allActions = allAudits.SelectMany(a => a.Actions).ToList();
        var weekActions = allActions.Where(a => a.Timestamp >= startOfWeek).ToList();
        var reviewedThisWeek = weekActions.Count(a => a.Action is "approve" or "reject");

        var totalDecisions = allActions.Count(a => a.Action is "approve" or "reject");
        var totalApproved = allActions.Count(a => a.Action == "approve");
        var approvalRate = totalDecisions > 0 ? (float)totalApproved / totalDecisions * 100f : 0f;

        // Average review time (claim → approve/reject)
        var reviewTimes = new List<double>();
        foreach (var audit in allAudits)
        {
            var claim = audit.Actions.FirstOrDefault(a => a.Action == "claim");
            var decision = audit.Actions.LastOrDefault(a => a.Action is "approve" or "reject");
            if (claim is not null && decision is not null && decision.Timestamp > claim.Timestamp)
            {
                reviewTimes.Add((decision.Timestamp - claim.Timestamp).TotalMinutes);
            }
        }
        var avgReviewTime = reviewTimes.Count > 0 ? (float)reviewTimes.Average() : 0f;

        // Per-moderator stats
        var moderatorStats = allActions
            .Where(a => a.Action is "approve" or "reject")
            .GroupBy(a => a.ModeratorId)
            .Select(g =>
            {
                var modReviewTimes = new List<double>();
                foreach (var audit in allAudits.Where(au => au.Actions.Any(ac => ac.ModeratorId == g.Key)))
                {
                    var claim = audit.Actions.FirstOrDefault(a => a.Action == "claim" && a.ModeratorId == g.Key);
                    var decision = audit.Actions.LastOrDefault(a => a.Action is "approve" or "reject" && a.ModeratorId == g.Key);
                    if (claim is not null && decision is not null && decision.Timestamp > claim.Timestamp)
                        modReviewTimes.Add((decision.Timestamp - claim.Timestamp).TotalMinutes);
                }

                return new PerModeratorStats(
                    g.Key, g.Key,
                    g.Count(),
                    g.Count(a => a.Action == "approve"),
                    g.Count(a => a.Action == "reject"),
                    modReviewTimes.Count > 0 ? (float)modReviewTimes.Average() : 0f);
            }).ToList();

        // Daily trend (last 7 days)
        var trend = Enumerable.Range(0, 7).Select(i =>
        {
            var date = today.AddDays(-6 + i);
            var dayActions = allActions.Where(a => a.Timestamp.Date == date).ToList();
            var dayAudits = allAudits.Where(a => a.SubmittedAt.Date == date).ToList();
            return new DailyModerationStats(
                date.ToString("yyyy-MM-dd"),
                dayAudits.Count,
                dayActions.Count(a => a.Action is "approve" or "reject"),
                dayActions.Count(a => a.Action == "approve"),
                dayActions.Count(a => a.Action == "reject"));
        }).ToList();

        return new ModerationStatsResponse(
            pending, inReview, approvedToday, rejectedToday,
            PctChange(pending, pending),
            0f,
            PctChange(approvedToday, approvedYesterday),
            PctChange(rejectedToday, rejectedYesterday),
            reviewedThisWeek, approvalRate, avgReviewTime,
            moderatorStats, trend);
    }

    public async Task<QueueSummary> GetQueueSummaryAsync()
    {
        await using var session = _store.QuerySession();

        var pending = await session.Query<ModerationAuditDocument>()
            .Where(i => i.Status == ModerationItemStatus.Pending)
            .CountAsync();

        var inReview = await session.Query<ModerationAuditDocument>()
            .Where(i => i.Status == ModerationItemStatus.InReview)
            .CountAsync();

        var flagged = await session.Query<ModerationAuditDocument>()
            .Where(i => i.Status == ModerationItemStatus.Flagged)
            .CountAsync();

        var oldest = await session.Query<ModerationAuditDocument>()
            .Where(i => i.Status == ModerationItemStatus.Pending)
            .OrderBy(i => i.SubmittedAt)
            .FirstOrDefaultAsync();

        var oldestHours = oldest is not null
            ? (int)(DateTimeOffset.UtcNow - oldest.SubmittedAt).TotalHours
            : 0;

        return new QueueSummary(pending, inReview, flagged, oldestHours);
    }

    private static ModerationStatus MapStatus(ModerationItemStatus status) => status switch
    {
        ModerationItemStatus.Pending => ModerationStatus.Pending,
        ModerationItemStatus.InReview => ModerationStatus.InReview,
        ModerationItemStatus.Approved => ModerationStatus.Approved,
        ModerationItemStatus.ApprovedWithEdits => ModerationStatus.Approved,
        ModerationItemStatus.AutoApproved => ModerationStatus.Approved,
        ModerationItemStatus.Rejected => ModerationStatus.Rejected,
        ModerationItemStatus.Flagged => ModerationStatus.Flagged,
        _ => ModerationStatus.Pending
    };
}
