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
// Phase 1B (prr-011) additions:
//   • RecordRotation(oldJti, newJti, expiresAt) stores an old→new successor
//     map so the refresh endpoint can detect rotation-race conditions
//     (ADR-0046 §4, threat-model T4/T5). An attempt to present an old jti
//     that is already on the revocation list AND has a recorded successor
//     is evidence of either an attacker replay or a double-refresh SPA bug;
//     both cases force re-auth and revoke the successor too.
//   • GetSuccessor(oldJti) lets the refresh handler look up the successor
//     without exposing the internal map — the successor is treated as
//     suspect so the handler revokes it explicitly.
//
// Design notes:
//   • Keyed on jti (unique per session JWT). The jti is generated with a 128-
//     bit RNG at mint time, so collision is bounded by birthday-paradox math
//     well beyond the 24-hour cookie Max-Age.
//   • Each entry carries the JWT's expiry so the background sweep can evict
//     entries once they would no longer validate anyway — bounding memory
//     growth under sustained logout volume.
//   • Thread-safe via ConcurrentDictionary. Reads on the hot path are
//     O(1) and lock-free; writes are rare (one per logout/refresh).
//   • In-memory is correct for pilot (single host) and incorrect for the
//     horizontally-scaled pod topology of production. The production port to
//     Redis is tracked as follow-up prr-011h in the threat model — the
//     interface stays identical so the swap is a one-line DI change.
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

    // prr-011 Phase 1B: old-jti → successor-jti map for rotation-race detection.
    // Same lifetime as the revocation entry for the old jti (entries expire
    // together during sweep). A present entry with a still-alive value means
    // the old jti was legitimately rotated; a replay of the old cookie after
    // this point is evidence of either an attacker or a double-refresh race.
    private readonly ConcurrentDictionary<string, string> _successor = new();

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
    /// prr-011 Phase 1B: record a jti rotation. Adds <paramref name="oldJti"/>
    /// to the revocation list at <paramref name="expiresAt"/> AND stores the
    /// <paramref name="newJti"/> as its successor. The refresh handler uses
    /// <see cref="GetSuccessor"/> to detect rotation-race conditions
    /// (ADR-0046 §4).
    /// </summary>
    public void RecordRotation(string oldJti, string newJti, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(oldJti))
            throw new ArgumentException("oldJti is required", nameof(oldJti));
        if (string.IsNullOrWhiteSpace(newJti))
            throw new ArgumentException("newJti is required", nameof(newJti));

        Add(oldJti, expiresAt);
        _successor[oldJti] = newJti;

        _logger?.LogInformation(
            "[SESSION_ROTATED] oldJti={OldPrefix}... → newJti={NewPrefix}...",
            oldJti.Length > 8 ? oldJti[..8] : oldJti,
            newJti.Length > 8 ? newJti[..8] : newJti);
    }

    /// <summary>
    /// Returns the recorded successor jti for <paramref name="oldJti"/>, or
    /// null if no rotation was recorded (or the successor has been swept).
    /// A non-null result indicates the old jti was legitimately rotated —
    /// replaying the old cookie after this point is a rotation-race signal.
    /// </summary>
    public string? GetSuccessor(string oldJti)
    {
        if (string.IsNullOrWhiteSpace(oldJti))
            return null;
        return _successor.TryGetValue(oldJti, out var s) ? s : null;
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
            {
                removed++;
                // Keep the successor map in lock-step with the revocation
                // map — if the old jti entry has expired, the successor
                // record is no longer useful (the successor is either also
                // expired, or has itself been rotated and the new entry
                // lives in a later map slot).
                _successor.TryRemove(kvp.Key, out _);
            }
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

    /// <summary>Current size of the successor map. Exposed for metrics/tests.</summary>
    public int SuccessorCount => _successor.Count;
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
