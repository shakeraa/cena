// =============================================================================
// Cena Platform — Parametric Template Authoring Service (prr-202)
//
// Owns the CRUD + preview orchestration for parametric templates. Every
// mutation:
//
//   1. Loads prior state (if any) into ParametricTemplateDocument.
//   2. Applies the request (create/update/delete) via TemplateAuthoringMapper.
//   3. Revalidates against ParametricTemplate.Validate() to match dry-run
//      error messages from TemplateGenerateEndpoint.
//   4. Computes a state hash, stores the document, appends a versioned event
//      to the per-template Marten stream.
//   5. Writes an AuditEventDocument for the admin dashboard.
//
// Tenant scoping:
//   Templates are *global content* (any school can author/consume) but every
//   event carries the acting user's school_id. This matches the QuestionDocument
//   pattern and is asserted by TemplateAuthoringTenantScopedTest.
//
// Preview:
//   Orchestrates ParametricCompiler.CompileAsync with <sampleCount> renderings.
//   Each variant is CAS-verified by SymPyParametricRenderer (the compiler's
//   injected IParametricRenderer). A preview that produces zero accepted
//   samples returns a 422-appropriate error but is NOT a mutation — no
//   document update, only a ParametricTemplatePreviewExecuted_V1 event for
//   audit/latency metrics.
//
// NO STUBS. Every path is production-grade. No "TODO: real impl later".
// =============================================================================

using System.Security.Claims;
using Cena.Actors.QuestionBank.Templates;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Templates;

public interface IParametricTemplateAuthoringService
{
    Task<TemplateListResponseDto> ListAsync(
        TemplateListFilterDto filter, ClaimsPrincipal user, CancellationToken ct = default);

    Task<TemplateDetailDto?> GetAsync(
        string templateId, ClaimsPrincipal user, CancellationToken ct = default);

    Task<TemplateDetailDto> CreateAsync(
        TemplateCreateRequestDto request, ClaimsPrincipal user, CancellationToken ct = default);

    Task<TemplateDetailDto?> UpdateAsync(
        string templateId, TemplateUpdateRequestDto request, ClaimsPrincipal user, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(
        string templateId, string? reason, ClaimsPrincipal user, CancellationToken ct = default);

    Task<TemplatePreviewResponseDto?> PreviewAsync(
        string templateId, TemplatePreviewRequestDto request, ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class ParametricTemplateAuthoringService : IParametricTemplateAuthoringService
{
    internal const int MaxPreviewSamples = 20;
    internal const int DefaultPreviewSamples = 5;

    private readonly IDocumentStore _store;
    private readonly ParametricCompiler _compiler;
    private readonly ILogger<ParametricTemplateAuthoringService> _logger;

    public ParametricTemplateAuthoringService(
        IDocumentStore store,
        ParametricCompiler compiler,
        ILogger<ParametricTemplateAuthoringService> logger)
    {
        _store = store;
        _compiler = compiler;
        _logger = logger;
    }

    // ── LIST ────────────────────────────────────────────────────────────

    public async Task<TemplateListResponseDto> ListAsync(
        TemplateListFilterDto filter, ClaimsPrincipal user, CancellationToken ct = default)
    {
        _ = TenantScope.GetSchoolFilter(user); // authz gate: throws if no school_id for non-SUPER_ADMIN

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize switch { < 1 => 25, > 200 => 200, _ => filter.PageSize };

        await using var session = _store.QuerySession();
        Marten.Linq.IMartenQueryable<ParametricTemplateDocument> q = session.Query<ParametricTemplateDocument>();
        if (!filter.IncludeInactive)
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Active);
        if (!string.IsNullOrWhiteSpace(filter.Subject))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Subject == filter.Subject);
        if (!string.IsNullOrWhiteSpace(filter.Topic))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Topic == filter.Topic);
        if (!string.IsNullOrWhiteSpace(filter.Track))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Track == filter.Track);
        if (!string.IsNullOrWhiteSpace(filter.Difficulty))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Difficulty == filter.Difficulty);
        if (!string.IsNullOrWhiteSpace(filter.Methodology))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Methodology == filter.Methodology);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            q = (Marten.Linq.IMartenQueryable<ParametricTemplateDocument>)q.Where(t => t.Status == filter.Status);

        var total = await q.CountAsync(ct);
        var docs = await q.OrderByDescending(t => t.UpdatedAt)
                         .Skip((page - 1) * pageSize).Take(pageSize)
                         .ToListAsync(ct);

        return new TemplateListResponseDto(
            Items: docs.Select(TemplateAuthoringMapper.ToListItemDto).ToList(),
            Page: page, PageSize: pageSize, Total: total);
    }

    // ── GET ─────────────────────────────────────────────────────────────

    public async Task<TemplateDetailDto?> GetAsync(
        string templateId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        _ = TenantScope.GetSchoolFilter(user);
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<ParametricTemplateDocument>(templateId, ct);
        return doc is null ? null : TemplateAuthoringMapper.ToDetailDto(doc);
    }

    // ── CREATE ──────────────────────────────────────────────────────────

    public async Task<TemplateDetailDto> CreateAsync(
        TemplateCreateRequestDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actorUserId, actorSchoolId) = ResolveActor(user);

        var now = DateTimeOffset.UtcNow;
        var doc = TemplateAuthoringMapper.ApplyCreate(request, actorUserId, actorSchoolId, now);

        // Mirror into domain for a dry-run validation — matches compiler errors.
        var domain = TemplateAuthoringMapper.ToDomain(doc);
        domain.Validate();

        doc.StateHash = TemplateAuthoringMapper.ComputeStateHash(doc);

        await using var session = _store.LightweightSession();

        var existing = await session.LoadAsync<ParametricTemplateDocument>(doc.Id, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Template '{doc.Id}' already exists");

        session.Store(doc);
        session.Events.Append(doc.Id, new ParametricTemplateCreated_V1(
            TemplateId: doc.Id, Version: doc.Version,
            Snapshot: doc, ActorUserId: actorUserId, ActorSchoolId: actorSchoolId,
            OccurredAt: now));

        WriteAuditEvent(session, user, "parametric_template.create", doc.Id,
            $"Created template {doc.Id} v{doc.Version}", success: true, actorSchoolId);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[TEMPLATE_CREATE] id={Tid} v={V} actor={Actor} school={School}",
            doc.Id, doc.Version, actorUserId, actorSchoolId);

        return TemplateAuthoringMapper.ToDetailDto(doc);
    }

    // ── UPDATE ──────────────────────────────────────────────────────────

    public async Task<TemplateDetailDto?> UpdateAsync(
        string templateId, TemplateUpdateRequestDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actorUserId, actorSchoolId) = ResolveActor(user);

        await using var session = _store.LightweightSession();
        var current = await session.LoadAsync<ParametricTemplateDocument>(templateId, ct);
        if (current is null || !current.Active) return null;

        var priorHash = string.IsNullOrEmpty(current.StateHash)
            ? TemplateAuthoringMapper.ComputeStateHash(current)
            : current.StateHash;

        var now = DateTimeOffset.UtcNow;
        var updated = TemplateAuthoringMapper.ApplyUpdate(current, request, actorUserId, actorSchoolId, now);

        var domain = TemplateAuthoringMapper.ToDomain(updated);
        domain.Validate();

        updated.StateHash = TemplateAuthoringMapper.ComputeStateHash(updated);

        var changed = DiffFields(current, updated);

        session.Store(updated);
        session.Events.Append(templateId, new ParametricTemplateUpdated_V1(
            TemplateId: templateId, Version: updated.Version,
            Snapshot: updated, PriorStateHash: priorHash,
            ChangedFields: changed,
            ActorUserId: actorUserId, ActorSchoolId: actorSchoolId, OccurredAt: now));

        WriteAuditEvent(session, user, "parametric_template.update", templateId,
            $"Updated template {templateId} v{current.Version}→v{updated.Version} fields=[{string.Join(",", changed)}]",
            success: true, actorSchoolId);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[TEMPLATE_UPDATE] id={Tid} v={VFrom}→{VTo} fields={Fields} actor={Actor}",
            templateId, current.Version, updated.Version, string.Join(",", changed), actorUserId);

        return TemplateAuthoringMapper.ToDetailDto(updated);
    }

    // ── DELETE (soft) ───────────────────────────────────────────────────

    public async Task<bool> SoftDeleteAsync(
        string templateId, string? reason, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var (actorUserId, actorSchoolId) = ResolveActor(user);

        await using var session = _store.LightweightSession();
        var current = await session.LoadAsync<ParametricTemplateDocument>(templateId, ct);
        if (current is null || !current.Active) return false;

        var priorHash = string.IsNullOrEmpty(current.StateHash)
            ? TemplateAuthoringMapper.ComputeStateHash(current)
            : current.StateHash;
        var priorVersion = current.Version;
        var now = DateTimeOffset.UtcNow;

        current.Active = false;
        current.Version += 1;
        current.Status = "draft";
        current.UpdatedAt = now;
        current.LastMutatedBy = actorUserId;
        current.LastMutatedBySchool = actorSchoolId;
        current.StateHash = TemplateAuthoringMapper.ComputeStateHash(current);

        session.Store(current);
        session.Events.Append(templateId, new ParametricTemplateDeleted_V1(
            TemplateId: templateId, PriorVersion: priorVersion, PriorStateHash: priorHash,
            ActorUserId: actorUserId, ActorSchoolId: actorSchoolId,
            Reason: reason, OccurredAt: now));

        WriteAuditEvent(session, user, "parametric_template.delete", templateId,
            $"Soft-deleted template {templateId} (reason={reason ?? "unspecified"})",
            success: true, actorSchoolId);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[TEMPLATE_DELETE] id={Tid} actor={Actor} school={School} reason={Reason}",
            templateId, actorUserId, actorSchoolId, reason);

        return true;
    }

    // ── PREVIEW ─────────────────────────────────────────────────────────

    public async Task<TemplatePreviewResponseDto?> PreviewAsync(
        string templateId, TemplatePreviewRequestDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actorUserId, actorSchoolId) = ResolveActor(user);

        var sampleCount = request.SampleCount switch
        {
            < 1 => DefaultPreviewSamples,
            > MaxPreviewSamples => MaxPreviewSamples,
            _ => request.SampleCount
        };

        await using var session = _store.LightweightSession();
        var current = await session.LoadAsync<ParametricTemplateDocument>(templateId, ct);
        if (current is null || !current.Active) return null;

        var template = TemplateAuthoringMapper.ToDomain(current);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var samples = new List<TemplatePreviewSampleDto>();
        string? overallError = null;
        int accepted = 0;
        int firstFailureKindOrdinal = -1;
        string? firstFailureDetail = null;

        try
        {
            var report = await _compiler.CompileAsync(template, request.BaseSeed, sampleCount, ct);
            accepted = report.AcceptedCount;
            foreach (var v in report.Variants)
            {
                samples.Add(new TemplatePreviewSampleDto(
                    Seed: v.Seed, Accepted: true,
                    Stem: v.RenderedStem, CanonicalAnswer: v.CanonicalAnswer,
                    Distractors: v.Distractors
                        .Select(d => new TemplatePreviewDistractorDto(d.MisconceptionId, d.Text, d.Rationale))
                        .ToList(),
                    FailureKind: null, FailureDetail: null,
                    LatencyMs: 0));
            }
            foreach (var d in report.Drops)
            {
                if (firstFailureKindOrdinal < 0)
                {
                    firstFailureKindOrdinal = (int)d.Kind;
                    firstFailureDetail = d.Detail;
                }
                samples.Add(new TemplatePreviewSampleDto(
                    Seed: d.Seed, Accepted: false,
                    Stem: d.RenderedStem, CanonicalAnswer: d.AttemptedAnswer,
                    Distractors: Array.Empty<TemplatePreviewDistractorDto>(),
                    FailureKind: d.Kind.ToString(),
                    FailureDetail: d.Detail, LatencyMs: d.LatencyMs));
            }
        }
        catch (InsufficientSlotSpaceException ex)
        {
            overallError = ex.Message;
            firstFailureDetail = ex.Message;
        }
        catch (ArgumentException ex)
        {
            overallError = ex.Message;
            firstFailureDetail = ex.Message;
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        sw.Stop();
        var now = DateTimeOffset.UtcNow;

        session.Events.Append(templateId, new ParametricTemplatePreviewExecuted_V1(
            TemplateId: templateId, TemplateVersion: current.Version,
            SampleCount: sampleCount, AcceptedCount: accepted,
            FirstFailureKind: firstFailureKindOrdinal,
            FirstFailureDetail: firstFailureDetail,
            TotalLatencyMs: sw.Elapsed.TotalMilliseconds,
            ActorUserId: actorUserId, ActorSchoolId: actorSchoolId,
            OccurredAt: now));

        WriteAuditEvent(session, user, "parametric_template.preview", templateId,
            $"Preview templateId={templateId} samples={sampleCount} accepted={accepted}",
            success: overallError is null, actorSchoolId);

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[TEMPLATE_PREVIEW] id={Tid} v={V} samples={S} accepted={A} err={Err} elapsed={Ms}ms",
            templateId, current.Version, sampleCount, accepted, overallError ?? "-",
            sw.Elapsed.TotalMilliseconds);

        return new TemplatePreviewResponseDto(
            TemplateId: templateId, TemplateVersion: current.Version,
            RequestedCount: sampleCount, AcceptedCount: accepted,
            Samples: samples, OverallError: overallError,
            TotalLatencyMs: sw.Elapsed.TotalMilliseconds);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static (string ActorUserId, string ActorSchoolId) ResolveActor(ClaimsPrincipal user)
    {
        var userId = user.FindFirst("user_id")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value
                     ?? "unknown-admin";
        // Tenant scope call is both the authz gate AND the source of ActorSchoolId.
        var schoolId = TenantScope.GetSchoolFilter(user) ?? "global";
        return (userId, schoolId);
    }

    private static void WriteAuditEvent(
        IDocumentSession session, ClaimsPrincipal user, string action, string targetId,
        string description, bool success, string actorSchoolId)
    {
        var audit = new AuditEventDocument
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "parametric_template",
            UserId = user.FindFirst("user_id")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? "unknown",
            UserName = user.Identity?.Name ?? "",
            UserRole = user.FindFirst(ClaimTypes.Role)?.Value
                       ?? user.FindFirst("role")?.Value ?? "",
            TenantId = actorSchoolId,
            Action = action,
            TargetType = "ParametricTemplate",
            TargetId = targetId,
            Description = description,
            IpAddress = "",
            UserAgent = "",
            Success = success,
            ErrorMessage = null,
            MetadataJson = null
        };
        session.Store(audit);
    }

    private static IReadOnlyList<string> DiffFields(
        ParametricTemplateDocument prior, ParametricTemplateDocument next)
    {
        var changed = new List<string>();
        if (prior.Subject != next.Subject) changed.Add(nameof(prior.Subject));
        if (prior.Topic != next.Topic) changed.Add(nameof(prior.Topic));
        if (prior.Track != next.Track) changed.Add(nameof(prior.Track));
        if (prior.Difficulty != next.Difficulty) changed.Add(nameof(prior.Difficulty));
        if (prior.Methodology != next.Methodology) changed.Add(nameof(prior.Methodology));
        if (prior.BloomsLevel != next.BloomsLevel) changed.Add(nameof(prior.BloomsLevel));
        if (prior.Language != next.Language) changed.Add(nameof(prior.Language));
        if (prior.StemTemplate != next.StemTemplate) changed.Add(nameof(prior.StemTemplate));
        if (prior.SolutionExpr != next.SolutionExpr) changed.Add(nameof(prior.SolutionExpr));
        if (prior.VariableName != next.VariableName) changed.Add(nameof(prior.VariableName));
        if (!prior.AcceptShapes.SequenceEqual(next.AcceptShapes)) changed.Add(nameof(prior.AcceptShapes));
        if (prior.Slots.Count != next.Slots.Count
            || !prior.Slots.Select(s => s.Name).SequenceEqual(next.Slots.Select(s => s.Name)))
            changed.Add(nameof(prior.Slots));
        else
        {
            for (var i = 0; i < prior.Slots.Count; i++)
            {
                var a = prior.Slots[i]; var b = next.Slots[i];
                if (a.Kind != b.Kind || a.IntegerMin != b.IntegerMin || a.IntegerMax != b.IntegerMax
                    || a.NumeratorMin != b.NumeratorMin || a.NumeratorMax != b.NumeratorMax
                    || a.DenominatorMin != b.DenominatorMin || a.DenominatorMax != b.DenominatorMax
                    || a.ReduceRational != b.ReduceRational
                    || !a.IntegerExclude.SequenceEqual(b.IntegerExclude)
                    || !a.Choices.SequenceEqual(b.Choices))
                {
                    changed.Add(nameof(prior.Slots));
                    break;
                }
            }
        }
        if (prior.Constraints.Count != next.Constraints.Count
            || !prior.Constraints.Select(c => c.PredicateExpr).SequenceEqual(next.Constraints.Select(c => c.PredicateExpr)))
            changed.Add(nameof(prior.Constraints));
        if (prior.DistractorRules.Count != next.DistractorRules.Count
            || !prior.DistractorRules.Select(d => d.FormulaExpr).SequenceEqual(next.DistractorRules.Select(d => d.FormulaExpr)))
            changed.Add(nameof(prior.DistractorRules));
        if (prior.Status != next.Status) changed.Add(nameof(prior.Status));
        return changed;
    }
}
