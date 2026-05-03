using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessagingRedisKeysTests
{
    [Fact]
    public void ThreadStreamKey_FormatsCorrectly()
    {
        Assert.Equal("cena:thread:t-123", MessagingRedisKeys.ThreadStream("t-123"));
    }

    [Fact]
    public void ThreadMetaKey_FormatsCorrectly()
    {
        Assert.Equal("cena:thread:t-123:meta", MessagingRedisKeys.ThreadMeta("t-123"));
    }

    [Fact]
    public void UnreadKey_IncludesUserIdAndThreadId()
    {
        Assert.Equal("cena:thread:t-123:unread:u-456",
            MessagingRedisKeys.Unread("t-123", "u-456"));
    }

    [Fact]
    public void UserThreadsKey_FormatsCorrectly()
    {
        Assert.Equal("cena:user:u-456:threads",
            MessagingRedisKeys.UserThreads("u-456"));
    }

    [Fact]
    public void WebhookDedupKey_IncludesSourceAndId()
    {
        Assert.Equal("cena:webhook:dedup:whatsapp:SM123",
            MessagingRedisKeys.WebhookDedup("whatsapp", "SM123"));
    }

    [Fact]
    public void MessageTtl_Is30Days()
    {
        Assert.Equal(TimeSpan.FromDays(30), MessagingRedisKeys.MessageTtl);
    }

    [Fact]
    public void DeduplicationTtl_Is5Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MessagingRedisKeys.DeduplicationTtl);
    }

    [Fact]
    public void MaxStreamLength_Is10000()
    {
        Assert.Equal(10_000, MessagingRedisKeys.MaxStreamLength);
    }
}
