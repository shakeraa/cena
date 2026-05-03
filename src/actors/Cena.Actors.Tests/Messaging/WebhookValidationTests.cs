using Cena.Actors.Messaging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging;

public sealed class TwilioSignatureValidatorTests
{
    private const string AuthToken = "test-auth-token-32chars-exactly!";
    private readonly TwilioSignatureValidator _validator = new(AuthToken);

    [Fact]
    public void ValidSignature_ReturnsTrue()
    {
        var url = "https://example.com/api/webhooks/whatsapp";
        var formParams = new Dictionary<string, string>
        {
            ["Body"] = "42",
            ["From"] = "+972501234567",
            ["MessageSid"] = "SM001"
        };

        var signature = ComputeExpectedSignature(url, formParams);

        Assert.True(_validator.Validate(url, formParams, signature));
    }

    [Fact]
    public void InvalidSignature_ReturnsFalse()
    {
        var url = "https://example.com/api/webhooks/whatsapp";
        var formParams = new Dictionary<string, string>
        {
            ["Body"] = "42",
            ["From"] = "+972501234567"
        };

        Assert.False(_validator.Validate(url, formParams, "invalid-garbage"));
    }

    [Fact]
    public void EmptySignature_ReturnsFalse()
    {
        Assert.False(_validator.Validate("https://example.com", new Dictionary<string, string>(), ""));
    }

    [Fact]
    public void NullSignature_ReturnsFalse()
    {
        Assert.False(_validator.Validate("https://example.com", new Dictionary<string, string>(), null!));
    }

    [Fact]
    public void TamperedBody_ReturnsFalse()
    {
        var url = "https://example.com/api/webhooks/whatsapp";
        var formParams = new Dictionary<string, string>
        {
            ["Body"] = "42",
            ["From"] = "+972501234567"
        };
        var signature = ComputeExpectedSignature(url, formParams);

        // Tamper with the body
        formParams["Body"] = "99";
        Assert.False(_validator.Validate(url, formParams, signature));
    }

    private string ComputeExpectedSignature(string url, Dictionary<string, string> formParams)
    {
        var sb = new System.Text.StringBuilder(url);
        foreach (var kv in formParams.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append(kv.Value);
        }

        using var hmac = new System.Security.Cryptography.HMACSHA1(
            System.Text.Encoding.UTF8.GetBytes(AuthToken));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash);
    }
}

public sealed class TelegramTokenValidatorTests
{
    private const string SecretToken = "my-telegram-bot-secret";
    private readonly TelegramTokenValidator _validator = new(SecretToken);

    [Fact]
    public void ValidToken_ReturnsTrue()
    {
        Assert.True(_validator.Validate("my-telegram-bot-secret"));
    }

    [Fact]
    public void InvalidToken_ReturnsFalse()
    {
        Assert.False(_validator.Validate("wrong-token"));
    }

    [Fact]
    public void NullToken_ReturnsFalse()
    {
        Assert.False(_validator.Validate(null));
    }

    [Fact]
    public void EmptyToken_ReturnsFalse()
    {
        Assert.False(_validator.Validate(""));
    }
}

public sealed class RedisWebhookDeduplicatorTests
{
    [Fact]
    public void KeyFormat_IsCorrect()
    {
        // Verify the key format used by the deduplicator
        var key = MessagingRedisKeys.WebhookDedup("whatsapp", "SM001");
        Assert.Equal("cena:webhook:dedup:whatsapp:SM001", key);
    }

    [Fact]
    public void TtlForDedup_Is5Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MessagingRedisKeys.DeduplicationTtl);
    }

    [Fact]
    public void DifferentSources_DifferentKeys()
    {
        var k1 = MessagingRedisKeys.WebhookDedup("whatsapp", "SM001");
        var k2 = MessagingRedisKeys.WebhookDedup("telegram", "SM001");
        Assert.NotEqual(k1, k2);
    }
}

public sealed class PiiHasherTests
{
    [Fact]
    public void Hash_ReturnsDeterministicResult()
    {
        var hash1 = PiiHasher.Hash("+972501234567");
        var hash2 = PiiHasher.Hash("+972501234567");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_Returns16CharHex()
    {
        var hash = PiiHasher.Hash("+972501234567");
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9a-f]{16}$", hash);
    }

    [Fact]
    public void Hash_DifferentInputs_DifferentOutputs()
    {
        var hash1 = PiiHasher.Hash("+972501234567");
        var hash2 = PiiHasher.Hash("+972509999999");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_DoesNotContainOriginalInput()
    {
        var phone = "+972501234567";
        var hash = PiiHasher.Hash(phone);
        Assert.DoesNotContain("972", hash);
    }
}
