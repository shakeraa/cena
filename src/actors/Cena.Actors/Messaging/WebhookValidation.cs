// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Webhook Signature Validation
// Layer: Infrastructure | Runtime: .NET 9
// Twilio HMAC-SHA1 and Telegram Bot API secret token validators.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.Messaging;

/// <summary>
/// Validates Twilio webhook signatures using HMAC-SHA1.
/// </summary>
public interface ITwilioSignatureValidator
{
    bool Validate(string url, IDictionary<string, string> formParams, string signature);
}

public sealed class TwilioSignatureValidator : ITwilioSignatureValidator
{
    private readonly byte[] _authTokenBytes;

    public TwilioSignatureValidator(string authToken)
    {
        _authTokenBytes = Encoding.UTF8.GetBytes(authToken);
    }

    public bool Validate(string url, IDictionary<string, string> formParams, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Build the data string: URL + sorted params concatenated as key+value
        var sb = new StringBuilder(url);
        foreach (var kv in formParams.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append(kv.Value);
        }

        using var hmac = new HMACSHA1(_authTokenBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var computed = Convert.ToBase64String(hash);

        return string.Equals(computed, signature, StringComparison.Ordinal);
    }
}

/// <summary>
/// Validates Telegram Bot API secret token header.
/// </summary>
public interface ITelegramTokenValidator
{
    bool Validate(string? headerToken);
}

public sealed class TelegramTokenValidator : ITelegramTokenValidator
{
    private readonly string _secretToken;

    public TelegramTokenValidator(string secretToken)
    {
        _secretToken = secretToken;
    }

    public bool Validate(string? headerToken)
    {
        if (string.IsNullOrEmpty(headerToken))
            return false;

        return string.Equals(_secretToken, headerToken, StringComparison.Ordinal);
    }
}

/// <summary>
/// Webhook deduplication service using Redis SET NX with TTL.
/// </summary>
public interface IWebhookDeduplicator
{
    /// <summary>
    /// Returns true if this is the first time seeing this message (not a duplicate).
    /// </summary>
    Task<bool> TryAcquireAsync(string source, string externalId);
}

public sealed class RedisWebhookDeduplicator : IWebhookDeduplicator
{
    private readonly StackExchange.Redis.IConnectionMultiplexer _redis;

    public RedisWebhookDeduplicator(StackExchange.Redis.IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryAcquireAsync(string source, string externalId)
    {
        var db = _redis.GetDatabase();
        var key = MessagingRedisKeys.WebhookDedup(source, externalId);

        // SET key 1 NX EX 300 — returns true only if key didn't exist
        return await db.StringSetAsync(key, "1",
            MessagingRedisKeys.DeduplicationTtl,
            when: StackExchange.Redis.When.NotExists);
    }
}

/// <summary>
/// Hashes PII for safe logging. Never log phone numbers or
/// Telegram IDs in plaintext.
/// </summary>
public static class PiiHasher
{
    public static string Hash(string pii)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pii));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
