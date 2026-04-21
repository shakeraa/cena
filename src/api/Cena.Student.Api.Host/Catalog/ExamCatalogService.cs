// =============================================================================
// Cena Platform — Exam Catalog Service (prr-220, ADR-0050)
//
// Loads the YAML catalog under contracts/exam-catalog/, keeps an atomic
// immutable snapshot in memory, and serves locale-resolved + tenant-
// overlay-filtered views to the endpoints layer. Admin rebuild replaces
// the snapshot atomically.
//
// Monotonicity: the service refuses to publish a new snapshot whose
// catalog_version is not strictly greater than the previous one (per
// ADR-0050 + task DoD). Rebuild returns a structured warning so the
// admin UI can explain why the rebuild was refused, rather than
// silently poisoning the runtime.
//
// Tenant overlay: served from ITenantCatalogOverlayStore. For Launch
// the default store returns an empty overlay for every tenant (global
// menu visible everywhere), but the shape is future-proof per ADR-0050
// "overlay is empty but present at Launch".
//
// Offline fallback: the service exposes RenderFallbackJson so a build
// step or the SPA dev server can bake contracts/exam-catalog/ into
// src/student/full-version/public/catalog-fallback.json.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Api.Contracts.Catalog;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Catalog;

public interface IExamCatalogService
{
    /// <summary>Current atomic snapshot — never null after startup.</summary>
    CatalogSnapshot Current { get; }

    /// <summary>Reload catalog from disk. Enforces monotonicity.</summary>
    Task<CatalogRebuildOutcome> RebuildAsync(CancellationToken ct = default);

    /// <summary>Serve the grouped catalog for one tenant + locale.</summary>
    ExamTargetCatalogDto GetForTenant(
        string? tenantId,
        string locale,
        CatalogTenantOverlay overlay);

    /// <summary>Serve topics for a single target + locale. Null = unknown target.</summary>
    ExamTargetTopicsDto? GetTopics(string examCode, string locale);

    /// <summary>Render the full global catalog as offline-fallback JSON.</summary>
    string RenderFallbackJson();
}

public sealed record CatalogRebuildOutcome(
    string PreviousVersion,
    string CurrentVersion,
    int TargetsLoaded,
    IReadOnlyList<string> Warnings,
    bool Accepted);

public interface ITenantCatalogOverlayStore
{
    /// <summary>Resolve overlay for a tenant. Empty for unknown tenants.</summary>
    CatalogTenantOverlay Resolve(string? tenantId);
}

/// <summary>Default overlay store — empty for every tenant (Launch posture).</summary>
public sealed class NullTenantCatalogOverlayStore : ITenantCatalogOverlayStore
{
    public CatalogTenantOverlay Resolve(string? tenantId) =>
        tenantId is null
            ? CatalogTenantOverlay.Empty
            : new CatalogTenantOverlay(tenantId, null, Array.Empty<string>());
}

public sealed class ExamCatalogService : IExamCatalogService
{
    public const string DefaultLocale = "en";
    private static readonly string[] SupportedLocales = { "en", "he", "ar" };

    private readonly string _catalogDir;
    private readonly ITenantCatalogOverlayStore _overlayStore;
    private readonly ILogger<ExamCatalogService> _logger;
    private readonly TimeProvider _time;
    private readonly object _sync = new();

    private CatalogSnapshot _snapshot;

    public ExamCatalogService(
        string catalogDir,
        ITenantCatalogOverlayStore overlayStore,
        ILogger<ExamCatalogService> logger,
        TimeProvider? time = null)
    {
        _catalogDir = catalogDir
            ?? throw new ArgumentNullException(nameof(catalogDir));
        _overlayStore = overlayStore ?? new NullTenantCatalogOverlayStore();
        _logger = logger;
        _time = time ?? TimeProvider.System;

        _snapshot = ExamCatalogYamlLoader.Load(catalogDir, _time.GetUtcNow());
        _logger.LogInformation(
            "[CATALOG] initial load: version={Version} targets={Count} dir={Dir}",
            _snapshot.CatalogVersion, _snapshot.TargetsByCode.Count, catalogDir);
    }

    public CatalogSnapshot Current => Volatile.Read(ref _snapshot!);

    public Task<CatalogRebuildOutcome> RebuildAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        CatalogSnapshot previous = Current;
        CatalogSnapshot candidate;

        try
        {
            candidate = ExamCatalogYamlLoader.Load(_catalogDir, _time.GetUtcNow());
        }
        catch (ExamCatalogLoadException ex)
        {
            _logger.LogError(ex, "[CATALOG] rebuild parse failure");
            return Task.FromResult(new CatalogRebuildOutcome(
                PreviousVersion: previous.CatalogVersion,
                CurrentVersion: previous.CatalogVersion,
                TargetsLoaded: previous.TargetsByCode.Count,
                Warnings: new[] { $"parse_failure: {ex.Message}" },
                Accepted: false));
        }

        lock (_sync)
        {
            if (!IsStrictlyGreater(candidate.CatalogVersion, previous.CatalogVersion))
            {
                _logger.LogWarning(
                    "[CATALOG] rebuild refused — version {New} is not strictly greater than {Old}",
                    candidate.CatalogVersion, previous.CatalogVersion);

                return Task.FromResult(new CatalogRebuildOutcome(
                    PreviousVersion: previous.CatalogVersion,
                    CurrentVersion: previous.CatalogVersion,
                    TargetsLoaded: previous.TargetsByCode.Count,
                    Warnings: new[]
                    {
                        $"non_monotonic_version: new={candidate.CatalogVersion} old={previous.CatalogVersion}",
                    },
                    Accepted: false));
            }

            Volatile.Write(ref _snapshot!, candidate);
        }

        _logger.LogInformation(
            "[CATALOG] rebuilt: {Old} → {New}, targets={Count}",
            previous.CatalogVersion, candidate.CatalogVersion, candidate.TargetsByCode.Count);

        return Task.FromResult(new CatalogRebuildOutcome(
            PreviousVersion: previous.CatalogVersion,
            CurrentVersion: candidate.CatalogVersion,
            TargetsLoaded: candidate.TargetsByCode.Count,
            Warnings: Array.Empty<string>(),
            Accepted: true));
    }

    public ExamTargetCatalogDto GetForTenant(
        string? tenantId,
        string locale,
        CatalogTenantOverlay overlay)
    {
        var snap = Current;
        var (served, fallbackUsed) = ResolveLocale(locale);

        var visibleCodes = VisibleCodes(snap, overlay);

        var groups = new List<ExamTargetGroupDto>();
        foreach (var family in snap.FamilyOrder)
        {
            if (!snap.Families.TryGetValue(family, out var codesInFamily))
                continue;

            var targets = new List<ExamTargetDto>();
            foreach (var code in codesInFamily)
            {
                if (!visibleCodes.Contains(code)) continue;
                if (!snap.TargetsByCode.TryGetValue(code, out var t)) continue;
                targets.Add(ProjectTarget(t, served, includeTopics: true));
            }

            if (targets.Count > 0)
                groups.Add(new ExamTargetGroupDto(family, targets));
        }

        return new ExamTargetCatalogDto(
            CatalogVersion: snap.CatalogVersion,
            Locale: served,
            LocaleFallbackUsed: fallbackUsed ? locale : served,
            FamilyOrder: snap.FamilyOrder,
            Groups: groups,
            Overlay: new TenantCatalogOverlayDto(
                TenantId: overlay.TenantId ?? tenantId,
                EnabledExamCodes: overlay.EnabledExamCodes,
                DisabledExamCodes: overlay.DisabledExamCodes));
    }

    public ExamTargetTopicsDto? GetTopics(string examCode, string locale)
    {
        var snap = Current;
        if (!snap.TargetsByCode.TryGetValue(examCode, out var target))
            return null;

        var (served, _) = ResolveLocale(locale);
        var display = DisplayForLocale(target.Display, served);
        var topics = target.Topics
            .Select(t => new ExamTopicDto(
                t.TopicId,
                DisplayForLocale(t.Display, served)))
            .ToArray();

        return new ExamTargetTopicsDto(
            ExamCode: target.ExamCode,
            CatalogVersion: snap.CatalogVersion,
            Locale: served,
            Display: display,
            Topics: topics);
    }

    public string RenderFallbackJson()
    {
        var snap = Current;

        // Bake the full global catalog (all visible codes, all locales) so
        // the SPA can fall back without network. Tenant overlay is always
        // empty in the fallback per the task DoD.
        var groups = new List<ExamTargetGroupDto>();
        foreach (var family in snap.FamilyOrder)
        {
            if (!snap.Families.TryGetValue(family, out var codesInFamily)) continue;

            var targets = new List<ExamTargetDto>();
            foreach (var code in codesInFamily)
            {
                if (!snap.TargetsByCode.TryGetValue(code, out var t)) continue;
                // Bake with the English projection — the SPA swaps locales
                // client-side after load. Keeping fallback single-locale
                // keeps the bundle small; for multi-locale SPAs this method
                // can be called per locale.
                targets.Add(ProjectTarget(t, DefaultLocale, includeTopics: true));
            }
            if (targets.Count > 0)
                groups.Add(new ExamTargetGroupDto(family, targets));
        }

        var bundle = new ExamTargetCatalogDto(
            CatalogVersion: snap.CatalogVersion,
            Locale: DefaultLocale,
            LocaleFallbackUsed: DefaultLocale,
            FamilyOrder: snap.FamilyOrder,
            Groups: groups,
            Overlay: new TenantCatalogOverlayDto(null, null, Array.Empty<string>()));

        return JsonSerializer.Serialize(bundle, FallbackJsonOptions);
    }

    // ---------- helpers ----------

    internal static readonly JsonSerializerOptions FallbackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static (string served, bool fallbackUsed) ResolveLocale(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return (DefaultLocale, false);

        var normalized = requested.Trim().ToLowerInvariant();
        // Support both "he" and "he-IL" — strip the region hint.
        var primary = normalized.Split('-', '_')[0];
        if (SupportedLocales.Contains(primary, StringComparer.OrdinalIgnoreCase))
            return (primary, false);

        return (DefaultLocale, true);
    }

    private static LocalizedDisplayDto DisplayForLocale(
        IReadOnlyDictionary<string, LocalizedEntry> displayMap,
        string locale)
    {
        if (displayMap.TryGetValue(locale, out var hit))
            return new LocalizedDisplayDto(hit.Name, hit.ShortDescription);
        if (displayMap.TryGetValue(DefaultLocale, out var fallback))
            return new LocalizedDisplayDto(fallback.Name, fallback.ShortDescription);
        // Last-ditch — take whatever is first.
        var first = displayMap.Values.FirstOrDefault()
            ?? new LocalizedEntry("(unnamed)", null);
        return new LocalizedDisplayDto(first.Name, first.ShortDescription);
    }

    private static ExamTargetDto ProjectTarget(
        CatalogTarget t,
        string locale,
        bool includeTopics)
    {
        var display = DisplayForLocale(t.Display, locale);
        var sittings = t.Sittings.Select(s => new ExamSittingDto(
            s.Code, s.AcademicYear, s.Season, s.Moed, s.CanonicalDate)).ToArray();

        IReadOnlyList<ExamTopicDto> topics = includeTopics
            ? t.Topics.Select(tp => new ExamTopicDto(
                tp.TopicId, DisplayForLocale(tp.Display, locale))).ToArray()
            : Array.Empty<ExamTopicDto>();

        return new ExamTargetDto(
            ExamCode: t.ExamCode,
            Family: t.Family,
            Region: t.Region,
            Track: t.Track,
            Units: t.Units,
            Regulator: t.Regulator,
            MinistrySubjectCode: t.MinistrySubjectCode,
            MinistryQuestionPaperCodes: t.MinistryQuestionPaperCodes,
            Availability: t.Availability,
            ItemBankStatus: t.ItemBankStatus,
            PassbackEligible: t.PassbackEligible,
            DefaultLeadDays: t.DefaultLeadDays,
            Sittings: sittings,
            Display: display,
            Topics: topics);
    }

    private static HashSet<string> VisibleCodes(
        CatalogSnapshot snap,
        CatalogTenantOverlay overlay)
    {
        // Start from the global set.
        var set = new HashSet<string>(snap.TargetsByCode.Keys, StringComparer.Ordinal);

        // If overlay has an explicit allow-list, intersect.
        if (overlay.EnabledExamCodes is { Count: > 0 } enabled)
            set.IntersectWith(enabled);

        // Subtract disabled.
        if (overlay.DisabledExamCodes.Count > 0)
            foreach (var d in overlay.DisabledExamCodes) set.Remove(d);

        return set;
    }

    /// <summary>
    /// Strict lexicographic comparison on the version string. Works for the
    /// YYYY.MM.DD-NN convention we use. Accepts "2026.04.21-02" &gt; "2026.04.21-01".
    /// Returns false for equal versions — rebuild must bump the stamp.
    /// </summary>
    internal static bool IsStrictlyGreater(string candidate, string baseline)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.IsNullOrWhiteSpace(baseline)) return true;
        return string.CompareOrdinal(candidate.Trim(), baseline.Trim()) > 0;
    }

    /// <summary>
    /// Test-only factory so tests can construct the service with a preloaded
    /// snapshot and no disk access. Never invoked from production code.
    /// </summary>
    internal static ExamCatalogService ForTests(
        CatalogSnapshot snapshot,
        ITenantCatalogOverlayStore? overlayStore = null,
        ILogger<ExamCatalogService>? logger = null)
    {
        var svc = (ExamCatalogService)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(ExamCatalogService));
        typeof(ExamCatalogService).GetField("_catalogDir",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, string.Empty);
        typeof(ExamCatalogService).GetField("_overlayStore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, overlayStore ?? new NullTenantCatalogOverlayStore());
        typeof(ExamCatalogService).GetField("_logger",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ExamCatalogService>.Instance);
        typeof(ExamCatalogService).GetField("_time",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, TimeProvider.System);
        typeof(ExamCatalogService).GetField("_snapshot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, snapshot);
        return svc;
    }
}
