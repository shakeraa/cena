// =============================================================================
// Cena Platform -- Request Integrity Middleware (SEC-006)
// Validates X-Request-Timestamp, X-Request-Nonce, and X-Request-Signature
// headers to prevent replay attacks, request forgery, and answer tampering.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cena.Infrastructure.Correlation;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Security;

/// <summary>
/// Middleware that enforces request-level tamper detection via HMAC-SHA256
/// signatures, timestamp-based replay protection, and nonce deduplication.
///
/// Required request headers:
/// <list type="bullet">
///   <item><c>X-Request-Timestamp</c> — Unix epoch seconds (UTC).</item>
///   <item><c>X-Request-Nonce</c> — Unique per-request token (UUID recommended).</item>
///   <item><c>X-Request-Signature</c> — HMAC-SHA256 hex digest of
///     <c>{method}:{path}:{timestamp}:{nonce}</c>.</item>
/// </list>
///
/// Skips validation for:
/// <list type="bullet">
///   <item>OPTIONS requests (CORS preflight)</item>
///   <item>Health check endpoints (<c>/health</c>)</item>
///   <item>Prometheus metrics endpoint (<c>/metrics</c>)</item>
/// </list>
///
/// SEC-006
/// </summary>
public sealed class RequestIntegrityMiddleware
{
    private const string TimestampHeader = "X-Request-Timestamp";
    private const string NonceHeader = "X-Request-Nonce";
    private const string SignatureHeader = "X-Request-Signature";

    private const int MaxNonceLength = 128;
    private const int NonceTtlMinutes = 5;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestIntegrityMiddleware> _logger;
    private readonly TamperDetectionOptions _options;
    private readonly MemoryCache _nonceCache;
    private readonly byte[] _secretKeyBytes;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RequestIntegrityMiddleware(
        RequestDelegate next,
        ILogger<RequestIntegrityMiddleware> logger,
        IOptions<TamperDetectionOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;

        _nonceCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.NonceMaxEntries
        });

        _secretKeyBytes = Encoding.UTF8.GetBytes(_options.SharedSecret);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        if (ShouldSkip(context))
        {
            await _next(context);
            return;
        }

        // --- 1. Validate timestamp (replay protection) ---
        if (!context.Request.Headers.TryGetValue(TimestampHeader, out var timestampValues)
            || !long.TryParse(timestampValues.FirstOrDefault(), out var timestampEpoch))
        {
            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_TIMESTAMP,
                "Missing or invalid X-Request-Timestamp header.");
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var drift = Math.Abs(now - timestampEpoch);
        if (drift > _options.MaxClockSkewSeconds)
        {
            _logger.LogWarning(
                "SEC-006: Request rejected — timestamp drift {DriftSeconds}s exceeds max {MaxSkew}s. " +
                "Path={Path} Method={Method}",
                drift, _options.MaxClockSkewSeconds,
                context.Request.Path, context.Request.Method);

            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_TIMESTAMP,
                "Request timestamp is too old or too far in the future.");
            return;
        }

        // --- 2. Validate nonce (deduplication) ---
        var nonce = context.Request.Headers[NonceHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > MaxNonceLength)
        {
            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_NONCE,
                "Missing or invalid X-Request-Nonce header.");
            return;
        }

        if (_nonceCache.TryGetValue(nonce, out _))
        {
            _logger.LogWarning(
                "SEC-006: Replay detected — nonce already used. Nonce={Nonce} Path={Path}",
                nonce, context.Request.Path);

            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_NONCE,
                "Request nonce has already been used (possible replay attack).");
            return;
        }

        // Record the nonce with a sliding 5-minute TTL
        _nonceCache.Set(nonce, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(NonceTtlMinutes),
            Size = 1
        });

        // --- 3. Validate HMAC signature ---
        var expectedSignature = context.Request.Headers[SignatureHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expectedSignature))
        {
            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_SIGNATURE,
                "Missing X-Request-Signature header.");
            return;
        }

        var payload = $"{context.Request.Method}:{context.Request.Path}:{timestampEpoch}:{nonce}";
        var computedSignature = ComputeHmacSha256(payload);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(expectedSignature)))
        {
            _logger.LogWarning(
                "SEC-006: Signature mismatch. Path={Path} Method={Method}",
                context.Request.Path, context.Request.Method);

            await RejectAsync(context, ErrorCodes.CENA_SEC_TAMPER_SIGNATURE,
                "Request signature is invalid.");
            return;
        }

        await _next(context);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool ShouldSkip(HttpContext context)
    {
        // CORS preflight — no body, no auth, no tamper headers
        if (HttpMethods.IsOptions(context.Request.Method))
            return true;

        var path = context.Request.Path;

        // Health check and metrics endpoints
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private string ComputeHmacSha256(string payload)
    {
        using var hmac = new HMACSHA256(_secretKeyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static async Task RejectAsync(HttpContext context, string errorCode, string message)
    {
        var correlationId = CorrelationContext.Current ?? Guid.NewGuid().ToString();

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var error = new CenaError(
            errorCode,
            message,
            ErrorCategory.Authorization,
            Details: null,
            CorrelationId: correlationId);

        var response = new ErrorResponse(error);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
