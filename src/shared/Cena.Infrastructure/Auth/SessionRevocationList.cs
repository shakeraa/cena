// =============================================================================
// Cena Platform — Session JWT Revocation List (prr-011)
//
// In-memory revocation list for server-minted session JWTs (NOT Firebase ID
// tokens — those are handled by TokenRevocationMiddleware + Redis). Backs
// the POST /api/auth/session/logout endpoint: when a student signs out, the
// server marks the current session JWT's `jti` as revoked so a copy of the
// cookie exfiltrated beforehand is no longer accepted even if its signature
// validates.
//
// Design notes:
//   • Keyed on jti (unique per session JWT). The jti is generated with a 128-
//     bit RNG at mint time, so collision is bounded by birthday-paradox math
//     well beyond the 24-hour cookie Max-Age.
//   • Each entry carries the JWT's expiry so the background sweep can evict
//     entries once they would no longer validate anyway — bounding memory
//     growth under sustained logout volume.
//   • Thread-safe via ConcurrentDictionary. Reads on the hot path are
//     O(1) and lock-free; writes are rare (one per logout).
//   • In-memory is correct for pilot (single host) and incorrect for the
//     horizontally-scaled pod topology of production. The production port to
//     Redis is tracked as follow-up in the prr-011 task body — the interface
//     stays identical so the swap is a one-line DI change.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// In-memory revocation list for server-minted session JWTs. Callers add a
/// jti+expiry when a session is terminated (logout, kicked) and the auth
/// middleware asks <see cref="IsRevoked"/> on every request.
/// </summary>
public sealed class SessionRevocationList
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();
    private readonly ILogger<SessionRevocationList>? _logger;

    public SessionRevocationList(ILogger<SessionRevocationList>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Mark a session JWT as revoked until its natural expiry. If the jti is
    /// already present with a later expiry, the later expiry wins (defensive
    /// against racing logouts on a stretched-out session).
    /// </summary>
    public void Add(string jti, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("jti is required", nameof(jti));

        _revoked.AddOrUpdate(
            jti,
            _ => expiresAt,
            (_, existing) => expiresAt > existing ? expiresAt : existing);

        _logger?.LogInformation(
            "[SESSION_REVOKED] jti={JtiPrefix}... expiresAt={ExpiresAt}",
            jti.Length > 8 ? jti[..8] : jti,
            expiresAt);
    }

    /// <summary>
    /// Returns true if the jti is on the revocation list and the supplied
    /// <paramref name="now"/> is before the revocation expiry. Revoked-but-
    /// expired entries return false so cleanup is eventually consistent
    /// even if the background sweep falls behind.
    /// </summary>
    public bool IsRevoked(string jti, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        if (!_revoked.TryGetValue(jti, out var expiresAt))
            return false;

        return now < expiresAt;
    }

    /// <summary>
    /// Remove entries whose expiry has passed. Called by
    /// <see cref="SessionRevocationListCleanupService"/> on a timer; exposed
    /// internal for unit-testing the eviction behaviour.
    /// </summary>
    internal int Sweep(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var kvp in _revoked)
        {
            if (kvp.Value <= now && _revoked.TryRemove(kvp.Key, out _))
                removed++;
        }

        if (removed > 0)
        {
            _logger?.LogDebug(
                "[SESSION_REVOKED_SWEEP] removed {Removed} expired entries, remaining={Remaining}",
                removed, _revoked.Count);
        }

        return removed;
    }

    /// <summary>Current size of the revocation set. Exposed for metrics/tests.</summary>
    public int Count => _revoked.Count;
}

/// <summary>
/// Background service that sweeps expired revocation entries every 5 minutes.
/// Registered alongside <see cref="SessionRevocationList"/> as a singleton
/// hosted service.
/// </summary>
public sealed class SessionRevocationListCleanupService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly SessionRevocationList _list;
    private readonly ILogger<SessionRevocationListCleanupService> _logger;
    private readonly TimeProvider _clock;

    public SessionRevocationListCleanupService(
        SessionRevocationList list,
        ILogger<SessionRevocationListCleanupService> logger,
        TimeProvider? clock = null)
    {
        _list = list;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[SESSION_REVOKED_SWEEP] starting, interval={Interval}",
            SweepInterval);

        using var timer = new PeriodicTimer(SweepInterval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    _list.Sweep(_clock.GetUtcNow());
                }
                catch (Exception ex)
                {
                    // Never let a sweep exception kill the background loop —
                    // revocation is a correctness feature, and a transient
                    // dictionary contention should retry on the next tick.
                    _logger.LogError(ex, "[SESSION_REVOKED_SWEEP] sweep failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
    }
}
