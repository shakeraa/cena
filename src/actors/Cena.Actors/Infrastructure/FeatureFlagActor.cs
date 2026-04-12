// =============================================================================
// Cena Platform -- FeatureFlagActor (FIND-arch-024)
// Layer: Infrastructure | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Singleton actor providing system-wide feature flags with rollout percentages.
// FIND-arch-024: Now uses event-sourced persistence with Marten projection.
// Origin: Fortnite's "Lightswitch" service for system-wide feature control.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Marten;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Infrastructure;

// =============================================================================
// MESSAGES
// =============================================================================

public sealed record GetFlag(string FlagName);
public sealed record SetFlag(string FlagName, bool Enabled, double RolloutPercent = 100.0, string? Reason = null);
public sealed record GetAllFlags;
public sealed record CheckFlagForStudent(string FlagName, string StudentId);
public sealed record InitializeDefaultFlags; // Internal message for bootstrapping

// ── Responses ──

public sealed record FlagResponse(string FlagName, bool Enabled, double RolloutPercent);
public sealed record AllFlagsResponse(IReadOnlyDictionary<string, FeatureFlagState> Flags);
public sealed record FlagCheckResponse(string FlagName, bool EnabledForStudent);

// =============================================================================
// STATE MODEL
// =============================================================================

public sealed record FeatureFlagState(
    string Name,
    bool Enabled,
    double RolloutPercent,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

// =============================================================================
// ACTOR
// =============================================================================

public sealed class FeatureFlagActor : IActor
{
    private readonly ILogger<FeatureFlagActor> _logger;
    private readonly IDocumentStore _store;
    private readonly string _systemUserId = "system";
    private Dictionary<string, FeatureFlagState> _flags = new(StringComparer.OrdinalIgnoreCase);

    public FeatureFlagActor(
        ILogger<FeatureFlagActor> logger,
        IDocumentStore store)
    {
        _logger = logger;
        _store = store;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started => OnStarted(),
            InitializeDefaultFlags => OnInitializeDefaults(),
            GetFlag q => HandleGetFlag(context, q),
            SetFlag cmd => HandleSetFlag(context, cmd),
            GetAllFlags => HandleGetAll(context),
            CheckFlagForStudent q => HandleCheckForStudent(context, q),
            _ => Task.CompletedTask
        };
    }

    private async Task OnStarted()
    {
        _logger.LogInformation("FeatureFlagActor starting — loading persisted flags");

        // FIND-arch-024: Load persisted flags from projection
        await using var session = _store.QuerySession();
        var docs = await session.Query<FeatureFlagDocument>()
            .Where(f => !f.IsDeleted)
            .ToListAsync();

        foreach (var doc in docs)
        {
            _flags[doc.Name] = new FeatureFlagState(
                doc.Name,
                doc.Enabled,
                doc.RolloutPercent,
                doc.UpdatedAt,
                doc.UpdatedBy);
        }

        _logger.LogInformation("FeatureFlagActor loaded {Count} persisted flags", _flags.Count);

        // Initialize defaults if no flags exist
        if (_flags.Count == 0)
        {
            await OnInitializeDefaults();
        }
    }

    private async Task OnInitializeDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        _logger.LogInformation("FeatureFlagActor initializing default flags");

        var defaults = new[]
        {
            ("llm.kimi.enabled", true, 100.0),
            ("llm.sonnet.enabled", true, 100.0),
            ("llm.opus.enabled", true, 100.0),
            ("pedagogy.socratic", true, 100.0),
            ("pedagogy.scaffolding", true, 100.0),
            ("session.max_minutes", true, 100.0),
            ("outreach.enabled", true, 100.0),
            ("experimental.adaptive_difficulty", false, 0.0)
        };

        await using var session = _store.LightweightSession();

        foreach (var (name, enabled, rollout) in defaults)
        {
            // Only set if not already persisted
            var exists = await session.LoadAsync<FeatureFlagDocument>(name);
            if (exists == null)
            {
                var evt = new FeatureFlagSet_V1(
                    name, enabled, rollout, _systemUserId, "Initial default", now);
                session.Events.StartStream<FeatureFlagProjection>(name, evt);

                _flags[name] = new FeatureFlagState(name, enabled, rollout, now, _systemUserId);
            }
        }

        await session.SaveChangesAsync();
        _logger.LogInformation("FeatureFlagActor initialized {Count} default flags", defaults.Length);
    }

    private Task HandleGetFlag(IContext context, GetFlag q)
    {
        if (_flags.TryGetValue(q.FlagName, out var flag))
            context.Respond(new FlagResponse(flag.Name, flag.Enabled, flag.RolloutPercent));
        else
            context.Respond(new FlagResponse(q.FlagName, true, 100.0)); // default: enabled
        return Task.CompletedTask;
    }

    private async Task HandleSetFlag(IContext context, SetFlag cmd)
    {
        var now = DateTimeOffset.UtcNow;

        // FIND-arch-024: Persist to event stream
        await using var session = _store.LightweightSession();
        var evt = new FeatureFlagSet_V1(
            cmd.FlagName,
            cmd.Enabled,
            cmd.RolloutPercent,
            "admin", // TODO: Get from context.User when called via API
            cmd.Reason,
            now);

        var existing = await session.LoadAsync<FeatureFlagDocument>(cmd.FlagName);
        if (existing != null)
        {
            session.Events.Append(cmd.FlagName, evt);
        }
        else
        {
            session.Events.StartStream<FeatureFlagProjection>(cmd.FlagName, evt);
        }

        await session.SaveChangesAsync();

        // Update in-memory cache
        _flags[cmd.FlagName] = new FeatureFlagState(
            cmd.FlagName, cmd.Enabled, cmd.RolloutPercent, now, "admin");

        _logger.LogInformation("Feature flag updated: {Flag} Enabled={Enabled} Rollout={Rollout}%",
            cmd.FlagName, cmd.Enabled, cmd.RolloutPercent);

        context.Respond(new FlagResponse(cmd.FlagName, cmd.Enabled, cmd.RolloutPercent));
    }

    private Task HandleGetAll(IContext context)
    {
        var dict = new Dictionary<string, FeatureFlagState>(_flags, StringComparer.OrdinalIgnoreCase);
        context.Respond(new AllFlagsResponse(dict));
        return Task.CompletedTask;
    }

    private Task HandleCheckForStudent(IContext context, CheckFlagForStudent q)
    {
        if (!_flags.TryGetValue(q.FlagName, out var flag) || !flag.Enabled)
        {
            context.Respond(new FlagCheckResponse(q.FlagName, flag?.Enabled ?? true));
            return Task.CompletedTask;
        }

        // Rollout: deterministic hash of studentId + flagName → 0-100
        bool inRollout = flag.RolloutPercent >= 100.0 ||
                         GetRolloutBucket(q.StudentId, q.FlagName) < flag.RolloutPercent;
        context.Respond(new FlagCheckResponse(q.FlagName, inRollout));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deterministic rollout bucket (0-100) based on student + flag name.
    /// Same student always gets same bucket for a given flag.
    /// </summary>
    internal static double GetRolloutBucket(string studentId, string flagName)
    {
        var input = Encoding.UTF8.GetBytes($"{studentId}:{flagName}");
        var hash = SHA256.HashData(input);
        uint val = BitConverter.ToUInt32(hash, 0);
        return (val % 10000) / 100.0;
    }
}
