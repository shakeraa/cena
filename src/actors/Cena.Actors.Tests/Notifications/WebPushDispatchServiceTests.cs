// =============================================================================
// Cena Platform — Web Push Dispatch Service Tests (PWA-BE-002)
// =============================================================================

using Cena.Actors.Notifications;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Notifications;

public sealed class WebPushDispatchServiceTests
{
    private readonly IWebPushClient _webPush;
    private readonly IPushNotificationRateLimiter _rateLimiter;

    public WebPushDispatchServiceTests()
    {
        _webPush = Substitute.For<IWebPushClient>();
        _rateLimiter = Substitute.For<IPushNotificationRateLimiter>();
    }

    private TestableWebPushDispatchService CreateService(
        bool? typeEnabled = null,
        List<WebPushSubscriptionDocument>? subscriptions = null)
    {
        return new TestableWebPushDispatchService(
            _webPush,
            _rateLimiter,
            typeEnabled,
            subscriptions);
    }

    [Fact]
    public async Task DispatchAsync_WebPushNotConfigured_ReturnsFalse()
    {
        _webPush.IsConfigured.Returns(false);
        var service = CreateService();

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchAsync_TypeDisabled_ReturnsFalse()
    {
        _webPush.IsConfigured.Returns(true);
        _rateLimiter.CanSendAsync("student-001").Returns(true);
        var service = CreateService(typeEnabled: false);

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchAsync_RateLimited_ReturnsFalse()
    {
        _webPush.IsConfigured.Returns(true);
        _rateLimiter.CanSendAsync("student-001").Returns(false);
        var service = CreateService(typeEnabled: true);

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchAsync_NoSubscriptions_ReturnsFalse()
    {
        _webPush.IsConfigured.Returns(true);
        _rateLimiter.CanSendAsync("student-001").Returns(true);
        var service = CreateService(typeEnabled: true, subscriptions: new List<WebPushSubscriptionDocument>());

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.False(result);
    }

    [Fact]
    public async Task DispatchAsync_Success_SendsToAllSubscriptions()
    {
        _webPush.IsConfigured.Returns(true);
        _rateLimiter.CanSendAsync("student-001").Returns(true);

        var subs = new List<WebPushSubscriptionDocument>
        {
            new()
            {
                Id = "push/1",
                StudentId = "student-001",
                Endpoint = "https://push.example.com/1",
                P256dh = "p256dh-1",
                Auth = "auth-1"
            },
            new()
            {
                Id = "push/2",
                StudentId = "student-001",
                Endpoint = "https://push.example.com/2",
                P256dh = "p256dh-2",
                Auth = "auth-2"
            }
        };

        var service = CreateService(typeEnabled: true, subscriptions: subs);

        _webPush.SendAsync("https://push.example.com/1", "p256dh-1", "auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebPushSendResult(true));
        _webPush.SendAsync("https://push.example.com/2", "p256dh-2", "auth-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebPushSendResult(true));

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.True(result);
        await _rateLimiter.Received().RecordSentAsync("student-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_ExpiredSubscription_DeletesIt()
    {
        _webPush.IsConfigured.Returns(true);
        _rateLimiter.CanSendAsync("student-001").Returns(true);

        var sub = new WebPushSubscriptionDocument
        {
            Id = "push/1",
            StudentId = "student-001",
            Endpoint = "https://push.example.com/1",
            P256dh = "p256dh-1",
            Auth = "auth-1"
        };
        var subs = new List<WebPushSubscriptionDocument> { sub };

        var service = CreateService(typeEnabled: true, subscriptions: subs);

        _webPush.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WebPushSendResult(false, "SUBSCRIPTION_GONE", "410 Gone"));

        var result = await service.DispatchAsync(
            "student-001", "sessionreminder", "Title", "Body");

        Assert.False(result);
        // The base class calls session.Delete(sub) — our Testable subclass records this.
        Assert.Contains(sub, service.DeletedSubscriptions);
    }

    /// <summary>
    /// Testable subclass that bypasses Marten LINQ by overriding lookup methods
    /// and records deletions so tests can assert cleanup behavior.
    /// </summary>
    private sealed class TestableWebPushDispatchService : WebPushDispatchService
    {
        private readonly bool? _typeEnabled;
        private readonly List<WebPushSubscriptionDocument>? _subscriptions;
        public List<WebPushSubscriptionDocument> DeletedSubscriptions { get; } = new();

        public TestableWebPushDispatchService(
            IWebPushClient webPush,
            IPushNotificationRateLimiter rateLimiter,
            bool? typeEnabled = null,
            List<WebPushSubscriptionDocument>? subscriptions = null)
            : base(
                Substitute.For<Marten.IDocumentStore>(),
                webPush,
                rateLimiter,
                Substitute.For<ILogger<WebPushDispatchService>>())
        {
            _typeEnabled = typeEnabled;
            _subscriptions = subscriptions;
        }

        protected override Task<bool> IsTypeEnabledAsync(string studentId, string notificationType, CancellationToken ct)
        {
            return Task.FromResult(_typeEnabled ?? true);
        }

        protected override Task<IReadOnlyList<WebPushSubscriptionDocument>> GetSubscriptionsAsync(string studentId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<WebPushSubscriptionDocument>>(
                _subscriptions ?? new List<WebPushSubscriptionDocument>());
        }

        protected override void OnSubscriptionDeleted(WebPushSubscriptionDocument sub)
        {
            DeletedSubscriptions.Add(sub);
        }
    }
}
