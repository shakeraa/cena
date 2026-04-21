// =============================================================================
// Cena Platform — Cultural Context Review Board Service (prr-034 MVP)
//
// MVP slice: enqueue DLQ entries, query the queue for ops, let a reviewer
// claim + decide an entry, publish a NATS signal so downstream subscribers
// (ops dashboards, alerting) see queue activity.
//
// DESIGN:
//   - Backed by Marten (CulturalContextReviewBoardDocument).
//   - NATS DLQ topic: cena.dlq.cultural-context.<category>. Subscribers
//     wire up per-category pagers if desired.
//   - Tenant-scoped via TenantScope: ops queue read endpoint returns
//     only the caller's school's entries unless the caller is
//     SUPER_ADMIN.
//
// FOLLOW-UPS (OUT OF SCOPE for prr-034 MVP, each its own task):
//   - Reviewer-roster management UI.
//   - SLA timer + paging on breach.
//   - Board-decision artifact export endpoint for regulator audits.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cena.Api.Contracts.Admin.Cultural;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Admin.Api;

/// <summary>
/// Ops-side contract for the cultural-context community review board DLQ.
/// </summary>
public interface ICulturalContextReviewBoardService
{
    /// <summary>
    /// Append a new entry to the DLQ. Publishes a NATS signal on the
    /// <c>cena.dlq.cultural-context.&lt;category&gt;</c> topic.
    /// </summary>
    Task<string> EnqueueAsync(
        CulturalContextEnqueueRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// List DLQ entries visible to the caller (tenant-scoped).
    /// </summary>
    Task<CulturalContextDlqListResponse> ListAsync(
        ClaimsPrincipal user,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed class CulturalContextReviewBoardService : ICulturalContextReviewBoardService
{
    public const string DlqSubjectRoot = "cena.dlq.cultural-context";

    private readonly IDocumentStore _store;
    private readonly INatsConnection _nats;
    private readonly ILogger<CulturalContextReviewBoardService> _logger;

    public CulturalContextReviewBoardService(
        IDocumentStore store,
        INatsConnection nats,
        ILogger<CulturalContextReviewBoardService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> EnqueueAsync(
        CulturalContextEnqueueRequest request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.SchoolId))
            throw new ArgumentException("SchoolId is required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SubjectKind))
            throw new ArgumentException("SubjectKind is required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SubjectId))
            throw new ArgumentException("SubjectId is required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Source))
            throw new ArgumentException("Source is required", nameof(request));

        var id = $"ccdlq_{Guid.NewGuid():N}";
        var doc = new CulturalContextReviewBoardDocument
        {
            Id = id,
            SchoolId = request.SchoolId,
            SubjectKind = request.SubjectKind,
            SubjectId = request.SubjectId,
            ConcernCategory = string.IsNullOrWhiteSpace(request.ConcernCategory)
                ? "unknown"
                : request.ConcernCategory,
            Reason = request.Reason ?? "",
            Source = request.Source,
            CorrelationId = request.CorrelationId,
            EnqueuedByOperatorId = request.EnqueuedByOperatorId,
            EnqueuedAt = DateTimeOffset.UtcNow,
            Status = "pending",
            AuditTrail = new List<BoardAuditEntry>
            {
                new()
                {
                    At = DateTimeOffset.UtcNow,
                    ActorId = request.EnqueuedByOperatorId ?? "system",
                    Action = "enqueued",
                    Note = request.Source,
                },
            },
        };

        await using (var session = _store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync(ct);
        }

        var subject = $"{DlqSubjectRoot}.{SanitiseSubjectSegment(doc.ConcernCategory)}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = doc.Id,
            schoolId = doc.SchoolId,
            subjectKind = doc.SubjectKind,
            subjectId = doc.SubjectId,
            concernCategory = doc.ConcernCategory,
            source = doc.Source,
            correlationId = doc.CorrelationId,
            enqueuedAt = doc.EnqueuedAt,
        });

        try
        {
            await _nats.PublishAsync(subject, payload, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // DLQ enqueue survives even if NATS is unhappy — Marten is
            // the source of truth for the queue. Log loudly; ops pages on
            // the metric.
            _logger.LogError(ex,
                "[CULTURAL_CONTEXT_DLQ] NATS publish failed subject={Subject} id={Id}",
                subject, doc.Id);
        }

        _logger.LogInformation(
            "[CULTURAL_CONTEXT_DLQ] enqueued id={Id} school={SchoolId} subject={SubjectKind}/{SubjectId} category={Category} source={Source}",
            doc.Id, doc.SchoolId, doc.SubjectKind, doc.SubjectId, doc.ConcernCategory, doc.Source);

        return doc.Id;
    }

    public async Task<CulturalContextDlqListResponse> ListAsync(
        ClaimsPrincipal user,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));

        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 25 : Math.Min(pageSize, 200);

        var schoolFilter = TenantScope.GetSchoolFilter(user); // null for SUPER_ADMIN

        await using var session = _store.QuerySession();
        var query = session.Query<CulturalContextReviewBoardDocument>().AsQueryable();

        if (schoolFilter is not null)
            query = query.Where(d => d.SchoolId == schoolFilter);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);

        var total = await query.CountAsync(ct);

        var docs = await query
            .OrderByDescending(d => d.EnqueuedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        var items = docs
            .Select(d => new CulturalContextDlqItem(
                Id: d.Id,
                SchoolId: d.SchoolId,
                SubjectKind: d.SubjectKind,
                SubjectId: d.SubjectId,
                ConcernCategory: d.ConcernCategory,
                Reason: d.Reason,
                Source: d.Source,
                CorrelationId: d.CorrelationId,
                EnqueuedByOperatorId: d.EnqueuedByOperatorId,
                EnqueuedAt: d.EnqueuedAt,
                Status: d.Status,
                AssignedReviewerId: d.AssignedReviewerId,
                DecidedAt: d.Decision?.DecidedAt,
                DecisionOutcome: d.Decision?.Outcome))
            .ToList();

        return new CulturalContextDlqListResponse(
            Items: items,
            Page: safePage,
            PageSize: safePageSize,
            Total: total);
    }

    /// <summary>
    /// NATS subjects use dots as hierarchy separators. Our concern-
    /// category strings include hyphens by convention; the DLQ topic
    /// tolerates those. Other whitespace / dots are collapsed to an
    /// underscore so the subject remains single-segment. Does not claim
    /// to be a canonical sanitiser — this is input we control via the
    /// enqueue request.
    /// </summary>
    internal static string SanitiseSubjectSegment(string category)
    {
        var trimmed = (category ?? "").Trim().ToLowerInvariant();
        if (trimmed.Length == 0) return "unknown";
        var chars = trimmed.Select(c =>
        {
            if (c == '-' || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) return c;
            return '_';
        }).ToArray();
        return new string(chars);
    }
}
