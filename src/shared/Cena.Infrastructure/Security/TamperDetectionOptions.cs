// =============================================================================
// Cena Platform -- Tamper Detection Configuration (SEC-006)
// Binds from the "TamperDetection" configuration section.
// =============================================================================

namespace Cena.Infrastructure.Security;

/// <summary>
/// Configuration for the <see cref="RequestIntegrityMiddleware"/>.
/// Bind from <c>TamperDetection</c> section in appsettings.
/// </summary>
public sealed class TamperDetectionOptions
{
    public const string SectionName = "TamperDetection";

    /// <summary>
    /// HMAC-SHA256 shared secret used to verify request signatures.
    /// Must be at least 32 characters in production.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Maximum allowed clock skew in seconds between client and server.
    /// Requests with a timestamp older than this are rejected as potential replays.
    /// Default: 60 seconds.
    /// </summary>
    public int MaxClockSkewSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of nonce entries kept in the in-memory cache.
    /// Each nonce is evicted after 5 minutes. When the cache exceeds this limit,
    /// the oldest entries are evicted first (MemoryCache compact).
    /// Default: 50,000.
    /// </summary>
    public int NonceMaxEntries { get; set; } = 50_000;

    /// <summary>
    /// Master switch. When false, the middleware passes all requests through.
    /// Default: true (enabled). Typically set to false in Development.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
