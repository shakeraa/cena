// =============================================================================
// Cena Platform — Admin Coverage Heatmap Service (prr-209)
//
// Builds the heatmap DTO by joining:
//   - ICoverageCellVariantCounter.Snapshot()  (live in-process counts)
//   - ICoverageTargetManifestProvider.Current (per-cell N contract)
//   - ICoverageRungDrilldownSource (variant-level detail for drilldown)
//
// Status derivation (task body prr-209):
//   variantCount == requiredCount        → green  + solid
//   variantCount  < requiredCount        → red    + dots       (deficit)
//   variantCount  > requiredCount        → amber  + diagonal   (over-coverage
//                                                               is wasted
//                                                               effort per
//                                                               the spec)
//
// Tenant scoping (ADR-0001):
//   - SUPER_ADMIN may omit the institute param or pass any id.
//   - ADMIN/MODERATOR must pass an institute matching their institute_id
//     claim (or the first binding from Tenancy.TenantScope.GetInstituteFilter).
//     Mismatches raise ForbiddenException which the middleware renders 403.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Tenancy;

namespace Cena.Admin.Api.Coverage;

public interface ICoverageHeatmapService
{
    /// <summary>
    /// Produce the full heatmap for <paramref name="track"/>. Returns null
    /// when the track literal is not a known <see cref="TemplateTrack"/>
    /// (the endpoint converts null → 404 per prr-209 DoD).
    /// </summary>
    CoverageHeatmapResponse? BuildHeatmap(string track, string instituteId, ClaimsPrincipal user);

    /// <summary>
    /// Build the drilldown payload for a single cell. Returns null when the
    /// cell is not tracked anywhere (snapshot + manifest both silent). The
    /// endpoint maps null → 404.
    /// </summary>
    CoverageRungDrilldownResponse? BuildRungDrilldown(
        string topic,
        string difficulty,
        string methodology,
        string track,
        string questionType,
        string language,
        string instituteId,
        ClaimsPrincipal user);
}

/// <summary>
/// Optional per-cell detail provider for the drilldown. Phase 1 ships an
/// empty implementation so the endpoint returns a structurally-correct
/// response; a prr-201 follow-up will wire a Marten-backed source.
/// </summary>
public interface ICoverageRungDrilldownSource
{
    /// <summary>
    /// Variant-level detail for a cell. Empty list = "no per-variant detail
    /// wired yet" (Phase 1 of prr-209).
    /// </summary>
    IReadOnlyList<CoverageRungVariantSummary> GetVariants(CoverageCell cell);

    /// <summary>
    /// Count of curator-queue items open against this cell. Zero when no
    /// curator queue is wired or no items exist.
    /// </summary>
    int GetCuratorQueuedCount(CoverageCell cell);
}

/// <summary>
/// Default empty implementation — registered by
/// <c>CenaAdminServiceRegistration</c> as a safe fallback when the prr-201
/// projection is not yet wired.
/// </summary>
public sealed class EmptyCoverageRungDrilldownSource : ICoverageRungDrilldownSource
{
    public IReadOnlyList<CoverageRungVariantSummary> GetVariants(CoverageCell cell)
        => Array.Empty<CoverageRungVariantSummary>();

    public int GetCuratorQueuedCount(CoverageCell cell) => 0;
}

public sealed class CoverageHeatmapService : ICoverageHeatmapService
{
    private readonly ICoverageCellVariantCounter _counter;
    private readonly ICoverageTargetManifestProvider _manifest;
    private readonly ICoverageRungDrilldownSource _drilldown;
    private readonly TimeProvider _clock;

    public CoverageHeatmapService(
        ICoverageCellVariantCounter counter,
        ICoverageTargetManifestProvider manifest,
        ICoverageRungDrilldownSource drilldown,
        TimeProvider? clock = null)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _drilldown = drilldown ?? throw new ArgumentNullException(nameof(drilldown));
        _clock = clock ?? TimeProvider.System;
    }

    public CoverageHeatmapResponse? BuildHeatmap(string track, string instituteId, ClaimsPrincipal user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);
        ArgumentNullException.ThrowIfNull(user);

        if (!TryParseTrack(track, out var parsedTrack))
        {
            // Unknown track literal → endpoint returns 404 per DoD (e).
            return null;
        }

        AssertInstituteAccess(user, instituteId);

        var manifest = _manifest.Current;
        var snapshot = _counter.Snapshot();

        // Union: every live snapshot row for this track + every declared
        // manifest cell for this track. A declared cell with no live count
        // still appears (as zero), which is exactly what admins need so they
        // can see "no one is filling this rung".
        var liveForTrack = snapshot
            .Where(s => s.Cell.Track == parsedTrack)
            .ToDictionary(s => CoverageTargetManifest.MatchKeyFor(s.Cell), s => s,
                StringComparer.OrdinalIgnoreCase);

        var declaredForTrack = manifest.Cells
            .Where(c => string.Equals(c.Track, parsedTrack.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var cells = new List<CoverageHeatmapCellDto>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Emit one cell per declared target — authoritative "should exist".
        foreach (var target in declaredForTrack)
        {
            var key = target.MatchKey;
            seenKeys.Add(key);
            liveForTrack.TryGetValue(key, out var liveRecord);

            var variantCount = liveRecord?.VariantCount ?? 0;
            var required = manifest.ResolveRequiredN(target);
            var belowSlo = liveRecord?.BelowSlo ?? (variantCount < required);

            cells.Add(BuildCellDto(
                topic: target.Topic,
                difficulty: target.Difficulty,
                methodology: target.Methodology,
                trackStr: target.Track,
                questionType: target.QuestionType,
                language: target.Language,
                variantCount: variantCount,
                required: required,
                belowSlo: belowSlo,
                declared: true,
                active: target.Active,
                updatedAt: liveRecord?.UpdatedAt,
                notes: target.Notes));
        }

        // 2. Any live snapshot rows for this track NOT declared → surface as
        //    undeclared drift so admins can chase "who's filling a cell we
        //    never graded?". Required = GlobalMin so they still get a status.
        foreach (var (key, rec) in liveForTrack)
        {
            if (seenKeys.Contains(key)) continue;

            var required = manifest.GlobalMin;
            cells.Add(BuildCellDto(
                topic: rec.Cell.Topic,
                difficulty: rec.Cell.Difficulty.ToString(),
                methodology: rec.Cell.Methodology.ToString(),
                trackStr: rec.Cell.Track.ToString(),
                questionType: rec.Cell.QuestionType,
                language: rec.Cell.Language ?? "en",
                variantCount: rec.VariantCount,
                required: required,
                belowSlo: rec.BelowSlo,
                declared: false,
                active: false,
                updatedAt: rec.UpdatedAt,
                notes: null));
        }

        var summary = SummariseCells(cells);
        return new CoverageHeatmapResponse(
            Track: parsedTrack.ToString(),
            InstituteId: instituteId,
            GeneratedAt: _clock.GetUtcNow(),
            Summary: summary,
            Cells: cells);
    }

    public CoverageRungDrilldownResponse? BuildRungDrilldown(
        string topic,
        string difficulty,
        string methodology,
        string track,
        string questionType,
        string language,
        string instituteId,
        ClaimsPrincipal user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(difficulty);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodology);
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);
        ArgumentNullException.ThrowIfNull(user);

        if (!TryParseTrack(track, out var parsedTrack)) return null;
        if (!Enum.TryParse<TemplateDifficulty>(difficulty, true, out var parsedDifficulty)) return null;
        if (!Enum.TryParse<TemplateMethodology>(methodology, true, out var parsedMethodology)) return null;

        AssertInstituteAccess(user, instituteId);

        var manifest = _manifest.Current;
        var resolvedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language;

        // Build the synthetic CoverageCell we're looking up. Subject falls
        // back to the first dotted segment of the topic since the public
        // contract key does not carry subject.
        var subject = DeriveSubject(topic);
        var lookupCell = new CoverageCell
        {
            Track = parsedTrack,
            Subject = subject,
            Topic = topic,
            Difficulty = parsedDifficulty,
            Methodology = parsedMethodology,
            QuestionType = questionType,
            Language = resolvedLanguage,
        };

        var matchKey = CoverageTargetManifest.MatchKeyFor(lookupCell);
        var target = manifest.FindByMatchKey(matchKey);

        // Find the live record by composite match key (subject-agnostic) so
        // a snapshot entry authored with a different subject value still
        // joins if the 6 non-subject axes match.
        var liveRecord = _counter.Snapshot()
            .FirstOrDefault(s => string.Equals(
                CoverageTargetManifest.MatchKeyFor(s.Cell),
                matchKey,
                StringComparison.OrdinalIgnoreCase));

        if (target is null && liveRecord is null)
        {
            // Cell is neither declared nor tracked → 404.
            return null;
        }

        var variantCount = liveRecord?.VariantCount ?? 0;
        var required = target is not null
            ? manifest.ResolveRequiredN(target)
            : manifest.GlobalMin;
        var belowSlo = liveRecord?.BelowSlo ?? (variantCount < required);

        var cellDto = BuildCellDto(
            topic: topic,
            difficulty: parsedDifficulty.ToString(),
            methodology: parsedMethodology.ToString(),
            trackStr: parsedTrack.ToString(),
            questionType: questionType,
            language: resolvedLanguage,
            variantCount: variantCount,
            required: required,
            belowSlo: belowSlo,
            declared: target is not null,
            active: target?.Active ?? false,
            updatedAt: liveRecord?.UpdatedAt,
            notes: target?.Notes);

        // Build the cell we hand to the drilldown source so it can query the
        // prr-201 projection by a subject-aware address. Using the live
        // record's cell when available preserves the subject the orchestrator
        // emitted; otherwise we fall back to the derived subject.
        var drilldownCell = liveRecord?.Cell ?? lookupCell;
        var variants = _drilldown.GetVariants(drilldownCell) ?? Array.Empty<CoverageRungVariantSummary>();
        var curatorQueued = _drilldown.GetCuratorQueuedCount(drilldownCell);

        return new CoverageRungDrilldownResponse(
            InstituteId: instituteId,
            GeneratedAt: _clock.GetUtcNow(),
            Cell: cellDto,
            CuratorQueuedCount: curatorQueued,
            LastUpdated: liveRecord?.UpdatedAt,
            Variants: variants);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private CoverageHeatmapCellDto BuildCellDto(
        string topic,
        string difficulty,
        string methodology,
        string trackStr,
        string questionType,
        string language,
        int variantCount,
        int required,
        bool belowSlo,
        bool declared,
        bool active,
        DateTimeOffset? updatedAt,
        string? notes)
    {
        var deficit = Math.Max(0, required - variantCount);
        var surplus = Math.Max(0, variantCount - required);
        var (status, pattern) = DeriveStatusAndPattern(variantCount, required);

        return new CoverageHeatmapCellDto(
            Topic: topic,
            Difficulty: difficulty,
            Methodology: methodology,
            Track: trackStr,
            QuestionType: questionType,
            Language: language,
            VariantCount: variantCount,
            RequiredCount: required,
            Deficit: deficit,
            Surplus: surplus,
            BelowSlo: belowSlo,
            Active: active,
            Declared: declared,
            UpdatedAt: updatedAt,
            Status: status,
            PatternKey: pattern,
            MatchKey: string.Join('/', topic, difficulty, methodology, trackStr, questionType, language),
            Notes: notes);
    }

    /// <summary>
    /// Status + pattern derivation per prr-209 DoD:
    ///   variantCount == required         → green  + solid    (at target)
    ///   variantCount  < required         → red    + dots     (deficit)
    ///   variantCount  > required         → amber  + diagonal (over-coverage =
    ///                                                          wasted effort)
    /// </summary>
    internal static (string Status, string PatternKey) DeriveStatusAndPattern(
        int variantCount, int required)
    {
        if (variantCount < required)
            return (CoverageHeatmapStatus.Red, CoverageHeatmapPatternKey.Dots);
        if (variantCount > required)
            return (CoverageHeatmapStatus.Amber, CoverageHeatmapPatternKey.Diagonal);
        return (CoverageHeatmapStatus.Green, CoverageHeatmapPatternKey.Solid);
    }

    private static CoverageHeatmapSummary SummariseCells(IReadOnlyList<CoverageHeatmapCellDto> cells)
    {
        int green = 0, amber = 0, red = 0, deficit = 0, surplus = 0;
        foreach (var c in cells)
        {
            switch (c.Status)
            {
                case CoverageHeatmapStatus.Green: green++; break;
                case CoverageHeatmapStatus.Amber: amber++; break;
                case CoverageHeatmapStatus.Red: red++; break;
            }
            deficit += c.Deficit;
            surplus += c.Surplus;
        }
        return new CoverageHeatmapSummary(
            TotalCells: cells.Count,
            GreenCount: green,
            AmberCount: amber,
            RedCount: red,
            DeficitTotal: deficit,
            SurplusTotal: surplus);
    }

    private static bool TryParseTrack(string track, out TemplateTrack parsed)
        => Enum.TryParse<TemplateTrack>(track, ignoreCase: true, out parsed);

    /// <summary>
    /// Enforce tenant scoping per ADR-0001. SUPER_ADMIN bypasses (null
    /// filter); every other role must have an institute binding that
    /// includes <paramref name="requestedInstituteId"/>.
    /// </summary>
    private static void AssertInstituteAccess(ClaimsPrincipal user, string requestedInstituteId)
    {
        var role = user.FindFirstValue(ClaimTypes.Role)
                ?? user.FindFirstValue("role");
        if (role == "SUPER_ADMIN") return;

        var allowed = TenantScope.GetInstituteFilter(user, defaultInstituteId: null);
        if (allowed.Count == 0)
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_INSUFFICIENT_ROLE,
                "Caller has no institute binding; coverage heatmap access denied.");
        }

        var matched = false;
        foreach (var id in allowed)
        {
            if (string.Equals(id, requestedInstituteId, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "Caller institute does not match requested institute; coverage heatmap access denied.");
        }
    }

    private static string DeriveSubject(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return "math";
        var dot = topic.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? topic[..dot] : topic;
    }
}
