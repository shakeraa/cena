// =============================================================================
// Cena Platform -- FeatureFlagActor (RES-010)
// Layer: Infrastructure | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Singleton actor providing system-wide feature flags with rollout percentages.
// Origin: Fortnite's "Lightswitch" service for system-wide feature control.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Infrastructure;

// =============================================================================
// MESSAGES
// =============================================================================

public sealed record GetFlag(string FlagName);
public sealed record SetFlag(string FlagName, bool Enabled, double RolloutPercent = 100.0);
public sealed record GetAllFlags;

/// <summary>
/// Check if a flag is enabled for a specific student (respects rollout %).
/// </summary>
public sealed record CheckFlagForStudent(string FlagName, string StudentId);

// ── Responses ──

public sealed record FlagResponse(string FlagName, bool Enabled, double RolloutPercent);
public sealed record AllFlagsResponse(IReadOnlyDictionary<string, FeatureFlag> Flags);
public sealed record FlagCheckResponse(string FlagName, bool EnabledForStudent);

// =============================================================================
// FLAG MODEL
// =============================================================================

public sealed record FeatureFlag(
    string Name,
    bool Enabled,
    double RolloutPercent,
    DateTimeOffset UpdatedAt);

// =============================================================================
// ACTOR
// =============================================================================

public sealed class FeatureFlagActor : IActor
{
    private readonly ILogger<FeatureFlagActor> _logger;
    private readonly Dictionary<string, FeatureFlag> _flags = new(StringComparer.OrdinalIgnoreCase);

    public FeatureFlagActor(ILogger<FeatureFlagActor> logger)
    {
        _logger = logger;
        InitializeDefaults();
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started                  => OnStarted(),
            GetFlag q                => HandleGetFlag(context, q),
            SetFlag cmd              => HandleSetFlag(context, cmd),
            GetAllFlags              => HandleGetAll(context),
            CheckFlagForStudent q    => HandleCheckForStudent(context, q),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted()
    {
        _logger.LogInformation("FeatureFlagActor started with {Count} default flags", _flags.Count);
        return Task.CompletedTask;
    }

    private void InitializeDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        SetDefault("llm.kimi.enabled", true, 100.0, now);
        SetDefault("llm.sonnet.enabled", true, 100.0, now);
        SetDefault("llm.opus.enabled", true, 100.0, now);
        SetDefault("pedagogy.socratic", true, 100.0, now);
        SetDefault("pedagogy.scaffolding", true, 100.0, now);
        SetDefault("session.max_minutes", true, 100.0, now); // true = 45min default
        SetDefault("outreach.enabled", true, 100.0, now);
        SetDefault("experimental.adaptive_difficulty", false, 0.0, now);
    }

    private void SetDefault(string name, bool enabled, double rollout, DateTimeOffset ts) =>
        _flags[name] = new FeatureFlag(name, enabled, rollout, ts);

    private Task HandleGetFlag(IContext context, GetFlag q)
    {
        if (_flags.TryGetValue(q.FlagName, out var flag))
            context.Respond(new FlagResponse(flag.Name, flag.Enabled, flag.RolloutPercent));
        else
            context.Respond(new FlagResponse(q.FlagName, true, 100.0)); // default: enabled
        return Task.CompletedTask;
    }

    private Task HandleSetFlag(IContext context, SetFlag cmd)
    {
        var flag = new FeatureFlag(cmd.FlagName, cmd.Enabled, cmd.RolloutPercent, DateTimeOffset.UtcNow);
        _flags[cmd.FlagName] = flag;
        _logger.LogInformation("Feature flag updated: {Flag} Enabled={Enabled} Rollout={Rollout}%",
            cmd.FlagName, cmd.Enabled, cmd.RolloutPercent);
        return Task.CompletedTask;
    }

    private Task HandleGetAll(IContext context)
    {
        context.Respond(new AllFlagsResponse(
            new Dictionary<string, FeatureFlag>(_flags, StringComparer.OrdinalIgnoreCase)));
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
        bool inRollout = flag.RolloutPercent >= 100.0 || GetRolloutBucket(q.StudentId, q.FlagName) < flag.RolloutPercent;
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
        // Take first 4 bytes as uint, mod 10000, divide by 100 for 2-decimal precision
        uint val = BitConverter.ToUInt32(hash, 0);
        return (val % 10000) / 100.0;
    }
}
