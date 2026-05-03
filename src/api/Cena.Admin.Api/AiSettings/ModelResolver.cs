// =============================================================================
// Cena Platform — IModelResolver / ModelResolver
//
// Single seam every Anthropic call site goes through to learn which model id
// to invoke for a given task. The resolution chain is:
//
//   1. AiSettingsDocument.ModelOverridesByTask[<task>]
//        → curator-configured per-task override (admin SPA dropdown)
//   2. RoutingConfigTaskDefaults.TryResolveDefault(<task>)
//        → routing-config.yaml § default_model_by_task[<task>]
//   3. RoutingConfigTaskDefaults.GlobalDefaultModelId
//        → routing-config.yaml § global_default_model_id
//   4. throw ModelNotConfiguredException
//        → no fallthrough, no silent-default-to-Sonnet ambush
//
// 60-second hot cache mirrors the AiGenerationService doc cache — the
// admin-side write paths invalidate (Invalidate()) on every mutation, so
// a curator's flip from Haiku → Sonnet is visible to in-flight call sites
// within the next 60 seconds at worst, sub-second on the same admin-api
// instance because the write site evicts before SaveChangesAsync returns.
//
// Singleton DI lifetime so the cache is shared across requests. Per the
// task DoD: fail-loud on unknown task names, mirroring BagrutTaxonomyCatalog
// closed-set discipline.
//
// Why this seam exists at all (instead of every caller doing its own
// AiSettingsDocument lookup + routing-config-yaml parse):
//
//   • Closed-set discipline: a bug in caller A (e.g. concept_extraction)
//     accidentally consulting ai_generation's override would silently
//     route A's traffic to whatever curator picked for the OTHER task.
//     ModelResolver makes the task name a typed parameter, not a magic
//     string parsed N times.
//
//   • Cost-meter accuracy: every caller emits LlmCallPricing alongside
//     the legacy meter. A model swap that goes through ModelResolver
//     pairs with AnthropicSupportedModels.ResolvePricingFor — pricing
//     stays accurate without each caller hard-coding "if Haiku then 1/5"
//     match-style logic.
//
//   • Cache invalidation: one cache, one eviction site (the PUT
//     endpoint), one observable cache-hit metric. Distributed caches
//     across N callers would fan out the eviction and risk the
//     "everyone sees the new model except concept_extraction" race.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Thrown when <see cref="ModelResolver.ResolveModelForTaskAsync"/> exhausts
/// the resolution chain. Indicates a configuration bug, not a runtime
/// transient — should fail-loud to the caller.
/// </summary>
public sealed class ModelNotConfiguredException : InvalidOperationException
{
    public ModelNotConfiguredException(string message) : base(message) { }
}

/// <summary>
/// Single seam every admin-side Anthropic caller goes through to learn the
/// model id for a given task. Resolution chain documented at the top of
/// <see cref="ModelResolver"/>.
/// </summary>
public interface IModelResolver
{
    /// <summary>
    /// Resolve the Anthropic model id for <paramref name="taskName"/>. Walks
    /// the chain (override → routing-config-task → routing-config-global)
    /// and throws <see cref="ModelNotConfiguredException"/> when no row is
    /// configured at any tier. Closed-set discipline: an unknown task name
    /// (one not in <c>RoutingConfigTaskDefaults.KnownTaskNames</c> AND with
    /// no override AND no global fallback) fails loud rather than silently
    /// routing to Sonnet.
    /// </summary>
    Task<string> ResolveModelForTaskAsync(string taskName, CancellationToken ct = default);

    /// <summary>
    /// Drop the cached <see cref="AiSettingsDocument"/>. Called by the PUT
    /// endpoint immediately after SaveChangesAsync so the next resolve
    /// reflects the curator's change. Idempotent.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Diagnostic projection of the current resolution state for every task
    /// the resolver knows about. Backs the admin GET endpoint.
    /// </summary>
    Task<IReadOnlyList<TaskModelResolution>> SnapshotAsync(CancellationToken ct = default);
}

/// <summary>
/// One row per known task in the resolver snapshot — task name, currently
/// resolved model id, and which tier of the chain it came from. The admin
/// SPA renders this as one row per task in the per-task model overrides
/// panel.
/// </summary>
/// <param name="Task">Canonical task name (e.g. <c>concept_extraction</c>).</param>
/// <param name="CurrentModelId">The model id that ResolveModelForTaskAsync would return RIGHT NOW.</param>
/// <param name="Source">Which tier produced <see cref="CurrentModelId"/>: <c>override</c>, <c>routing-config-task-default</c>, <c>routing-config-global-default</c>.</param>
/// <param name="IsOverridden">True iff the curator's override map has this task; false means routing-config tier.</param>
/// <param name="OverrideModelId">The override (if any) before falling through to routing-config; null when no override.</param>
public sealed record TaskModelResolution(
    string Task,
    string CurrentModelId,
    string Source,
    bool IsOverridden,
    string? OverrideModelId);

public sealed class ModelResolver : IModelResolver
{
    /// <summary>
    /// Cache TTL — 60 seconds per the task DoD. Short enough that a curator
    /// who changes overrides on host A sees the change on host B within a
    /// minute (federated admin) without admin paths drowning Marten on
    /// every call.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDocumentStore _documentStore;
    private readonly RoutingConfigTaskDefaults _yamlDefaults;
    private readonly ILogger<ModelResolver> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private AiSettingsDocument? _cachedDoc;
    private DateTimeOffset _cachedAt;

    public ModelResolver(
        IDocumentStore documentStore,
        RoutingConfigTaskDefaults yamlDefaults,
        ILogger<ModelResolver> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(yamlDefaults);
        ArgumentNullException.ThrowIfNull(logger);
        _documentStore = documentStore;
        _yamlDefaults = yamlDefaults;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> ResolveModelForTaskAsync(
        string taskName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);

        var doc = await LoadDocAsync(ct).ConfigureAwait(false);

        // 1. Curator override (validated at the write site against the
        //    closed-set; a stale row that pre-dates a retirement still
        //    rides because a retired model is still usually accepted by
        //    Anthropic — we surface the warning but resolve the value).
        if (doc.ModelOverridesByTask.TryGetValue(taskName, out var overrideModelId)
            && !string.IsNullOrWhiteSpace(overrideModelId))
        {
            if (!AnthropicSupportedModels.IsSupported(overrideModelId))
            {
                _logger.LogWarning(
                    "ModelResolver: override for task='{Task}' references unsupported model_id='{Model}' " +
                    "— resolving anyway because Anthropic may still accept it; recommend the curator pick " +
                    "a value in AnthropicSupportedModels.All.",
                    taskName, overrideModelId);
            }
            return overrideModelId!;
        }

        // 2. routing-config-task-default → 3. routing-config-global-default.
        if (_yamlDefaults.TryResolveDefault(taskName, out var yamlModelId, out _))
        {
            return yamlModelId;
        }

        // 4. fail-loud.
        throw new ModelNotConfiguredException(
            $"No Anthropic model configured for task='{taskName}'. Add a row under " +
            "contracts/llm/routing-config.yaml § default_model_by_task: or set a per-task override " +
            "via PUT /api/admin/ai/settings/model-overrides/{taskName}. " +
            $"Known tasks: [{string.Join(", ", _yamlDefaults.KnownTaskNames)}]; " +
            $"global default: {_yamlDefaults.GlobalDefaultModelId ?? "(none)"}");
    }

    public void Invalidate()
    {
        // Best-effort — under extreme contention a concurrent resolver may
        // refresh just before this fires; the resolver re-reads on the next
        // call after the TTL expires anyway, so the worst-case window is
        // still bounded by CacheTtl.
        _cacheLock.Wait();
        try
        {
            _cachedDoc = null;
            _cachedAt = default;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<TaskModelResolution>> SnapshotAsync(
        CancellationToken ct = default)
    {
        var doc = await LoadDocAsync(ct).ConfigureAwait(false);

        // Surface every task the YAML knows about plus any task that has
        // an override (so curator-set rows for tasks NOT in YAML are
        // visible — though the validator on the PUT endpoint will refuse
        // such a write, so this branch is defensive only).
        var taskNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _yamlDefaults.KnownTaskNames) taskNames.Add(name);
        foreach (var name in doc.ModelOverridesByTask.Keys) taskNames.Add(name);

        var results = new List<TaskModelResolution>(taskNames.Count);
        foreach (var task in taskNames)
        {
            doc.ModelOverridesByTask.TryGetValue(task, out var overrideModelId);
            var hasOverride = !string.IsNullOrWhiteSpace(overrideModelId);

            string current;
            string source;
            if (hasOverride)
            {
                current = overrideModelId!;
                source = "override";
            }
            else if (_yamlDefaults.TryResolveDefault(task, out var yamlModelId, out var yamlSource))
            {
                current = yamlModelId;
                source = yamlSource;
            }
            else
            {
                // Should not happen for tasks in KnownTaskNames; defensive.
                current = "";
                source = "unresolved";
            }

            results.Add(new TaskModelResolution(
                Task: task,
                CurrentModelId: current,
                Source: source,
                IsOverridden: hasOverride,
                OverrideModelId: hasOverride ? overrideModelId : null));
        }
        return results;
    }

    /// <summary>
    /// Load the singleton settings doc, hitting Marten at most once per
    /// <see cref="CacheTtl"/>. Returns a fresh defaulted document when
    /// none exists yet (first run) — never returns null.
    /// </summary>
    private async Task<AiSettingsDocument> LoadDocAsync(CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_cachedDoc is not null && now - _cachedAt < CacheTtl)
                return _cachedDoc;

            AiSettingsDocument? doc = null;
            try
            {
                await using var session = _documentStore.QuerySession();
                doc = await session.LoadAsync<AiSettingsDocument>(
                    AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
            }
            catch (Marten.Exceptions.MartenCommandException ex)
                when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
            {
                // First-run cold start: settings table not yet auto-created.
                // Defer to defaults (no overrides).
                _logger.LogInformation(
                    "ModelResolver: AiSettingsDocument table not yet created — using routing-config defaults");
                doc = null;
            }

            _cachedDoc = doc ?? new AiSettingsDocument();
            _cachedAt = now;
            return _cachedDoc;
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
