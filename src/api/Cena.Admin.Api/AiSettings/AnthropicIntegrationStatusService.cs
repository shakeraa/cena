// =============================================================================
// Cena Platform — AnthropicIntegrationStatusService (2026-05-03)
//
// Surfaces "is the Anthropic API key configured AND is the LLM tier actually
// reachable?" so the admin SPA can render a top-of-layout banner instead of
// the previous silent log-and-degrade behaviour.
//
// Design constraints:
//   - The plaintext API key MUST NEVER leave this process. We only report
//     whether a key is present + which source it came from + the most-recent
//     call outcome (success/failure with reason category, no SDK details that
//     could leak prompts).
//   - Fast: the GET endpoint is polled by every admin user every 60s. The
//     "is configured" check is a fast Marten lookup + a configuration read,
//     no Anthropic round-trip. Reachability is reported from the
//     last-N-calls sliding window kept in memory by HybridConceptExtractor /
//     LlmBagrutQuestionSegmenter / AiGenerationService et al.
//   - Per CLAUDE.md "no stubs" — the service is real on day one. The
//     reachability sliding window starts empty (Unknown) until the first LLM
//     call lands and recorders feed it.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Where the API key was found. The SPA badge text differs slightly between
/// "you set it via the UI" (Marten) and "operator set it via env" (Config) —
/// Marten edits are curator-driven and roundtrip through this service; env
/// edits require a restart and the message tells the curator that.
/// </summary>
public enum AnthropicApiKeySource
{
    /// <summary>No source resolved a usable key. UI banner: red.</summary>
    None,
    /// <summary>AiSettingsDocument has a non-empty cipher blob.</summary>
    Marten,
    /// <summary>IConfiguration["Anthropic:ApiKey"] is non-empty.</summary>
    Configuration,
}

/// <summary>
/// Reachability summary derived from the last-N call recorders. Unknown means
/// no call has been attempted since process boot — the SPA shows a neutral
/// "no calls yet" state, NOT a red banner.
/// </summary>
public enum AnthropicReachability
{
    Unknown,
    Healthy,
    Degraded,
    Down,
}

/// <summary>
/// Reason an LLM call failed. Surfaced verbatim in the SPA so the curator
/// understands "auth-failure means rotate the key" vs "transport means we
/// retry automatically."
/// </summary>
public enum AnthropicCallFailureKind
{
    AuthFailure,
    Transport,
    CircuitOpen,
    InvalidResponse,
    Other,
}

public sealed record AnthropicIntegrationStatus(
    bool ApiKeyConfigured,
    AnthropicApiKeySource KeySource,
    AnthropicReachability Reachability,
    int RecentSuccessCount,
    int RecentFailureCount,
    string? LastFailureCategory,
    string? LastFailureMessage,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset CheckedAt);

public interface IAnthropicIntegrationStatusService
{
    Task<AnthropicIntegrationStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Recorders called from every Anthropic call site (HybridConceptExtractor,
    /// LlmBagrutQuestionSegmenter, AiGenerationService, OcrTextEnhancer,
    /// QualityGateService) so the integration banner reflects live state.
    /// </summary>
    void RecordSuccess(string callerTask);
    void RecordFailure(string callerTask, AnthropicCallFailureKind kind, string message);
}

public sealed class AnthropicIntegrationStatusService : IAnthropicIntegrationStatusService
{
    // 50-call sliding window, in-memory. The admin SPA polls every 60s and
    // each per-task caller emits a few calls per minute under normal use,
    // so 50 is roughly the last 5-10 minutes of activity. Per-process; no
    // cross-instance state — that's deliberate (each replica reports its
    // own integration health for its own caller pool).
    private const int SlidingWindowSize = 50;

    private readonly IDocumentStore? _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnthropicIntegrationStatusService> _logger;

    private readonly object _windowLock = new();
    private readonly LinkedList<CallOutcome> _window = new();
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastFailureAt;
    private string? _lastFailureMessage;
    private AnthropicCallFailureKind? _lastFailureKind;

    public AnthropicIntegrationStatusService(
        IConfiguration configuration,
        ILogger<AnthropicIntegrationStatusService> logger,
        IDocumentStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AnthropicIntegrationStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (configured, source) = await ProbeKeyAsync(ct).ConfigureAwait(false);

        int success;
        int failure;
        string? lastFailMsg;
        AnthropicCallFailureKind? lastFailKind;
        DateTimeOffset? lastSuccess;
        DateTimeOffset? lastFail;

        lock (_windowLock)
        {
            success = 0;
            failure = 0;
            foreach (var c in _window)
            {
                if (c.Success) success++;
                else failure++;
            }
            lastFailMsg = _lastFailureMessage;
            lastFailKind = _lastFailureKind;
            lastSuccess = _lastSuccessAt;
            lastFail = _lastFailureAt;
        }

        var reachability = (configured, success, failure) switch
        {
            (false, _, _) => AnthropicReachability.Down,
            (true, 0, 0) => AnthropicReachability.Unknown,
            (true, var s, var f) when f > s => AnthropicReachability.Down,
            (true, var s, var f) when f > 0 && s > 0 => AnthropicReachability.Degraded,
            _ => AnthropicReachability.Healthy,
        };

        return new AnthropicIntegrationStatus(
            ApiKeyConfigured: configured,
            KeySource: source,
            Reachability: reachability,
            RecentSuccessCount: success,
            RecentFailureCount: failure,
            LastFailureCategory: lastFailKind?.ToString(),
            LastFailureMessage: lastFailMsg,
            LastSuccessAt: lastSuccess,
            LastFailureAt: lastFail,
            CheckedAt: DateTimeOffset.UtcNow);
    }

    public void RecordSuccess(string callerTask)
    {
        lock (_windowLock)
        {
            AppendToWindow(success: true);
            _lastSuccessAt = DateTimeOffset.UtcNow;
        }
    }

    public void RecordFailure(string callerTask, AnthropicCallFailureKind kind, string message)
    {
        lock (_windowLock)
        {
            AppendToWindow(success: false);
            _lastFailureAt = DateTimeOffset.UtcNow;
            _lastFailureKind = kind;
            // Trim the message to a sane length so a giant SDK exception
            // message doesn't blow up the JSON response. The full thing is
            // already in the structured logs via the original LogWarning at
            // each call site; this is just the short label for the banner.
            _lastFailureMessage = message.Length > 240 ? message[..240] + "…" : message;
        }
    }

    private async Task<(bool Configured, AnthropicApiKeySource Source)> ProbeKeyAsync(CancellationToken ct)
    {
        if (_store is not null)
        {
            try
            {
                await using var session = _store.QuerySession();
                var doc = await session.LoadAsync<AiSettingsDocument>(
                    AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
                if (doc is not null && !string.IsNullOrEmpty(doc.AnthropicApiKeyCipher))
                    return (true, AnthropicApiKeySource.Marten);
            }
            catch (Marten.Exceptions.MartenCommandException ex)
                when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
            {
                _logger.LogDebug(
                    "AiSettingsDocument table not yet created — falling back to IConfiguration probe");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AnthropicIntegrationStatusService: AiSettingsDocument probe failed — "
                    + "falling back to IConfiguration");
            }
        }

        var fromConfig = _configuration["Anthropic:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return (true, AnthropicApiKeySource.Configuration);

        return (false, AnthropicApiKeySource.None);
    }

    private void AppendToWindow(bool success)
    {
        // Caller holds _windowLock.
        _window.AddLast(new CallOutcome(success));
        while (_window.Count > SlidingWindowSize)
            _window.RemoveFirst();
    }

    private readonly record struct CallOutcome(bool Success);
}
