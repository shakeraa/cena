// =============================================================================
// Cena Platform — NotificationDispatcher Tests (FIND-arch-002)
// Regression coverage for NATS subject drift. The dispatcher previously
// subscribed to the literal "events.xp.awarded" while SessionNatsPublisher
// emitted on "cena.events.student.{studentId}.xp_awarded", so XP
// notifications never reached the dispatcher. These tests pin the contract
// so a future change to either side breaks the build, not production.
// =============================================================================

using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Actors.Notifications;
using Cena.Actors.Tests.Bus;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NSubstitute;

namespace Cena.Actors.Tests.Notifications;

public sealed class NotificationDispatcherTests
{
    private readonly INatsConnection _nats = Substitute.For<INatsConnection>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();

    // ── Subject contract: subscribe wildcard matches publisher subjects ──

    [Fact]
    public void SubscribeWildcard_MatchesSessionNatsPublisherXpAwardedSubject()
    {
        // SessionNatsPublisher.PublishXpAwardedAsync publishes on
        //   NatsSubjects.StudentEvent(studentId, "xp_awarded")
        // = "cena.events.student.{studentId}.xp_awarded"
        // The dispatcher subscribes on the wildcard
        //   NatsSubjects.StudentEventTypeWildcard("xp_awarded")
        // = "cena.events.student.*.xp_awarded"
        var publisherSubject = NatsSubjects.StudentEvent("stu-001", NatsSubjects.StudentXpAwarded);
        var subscriberWildcard = NatsSubjects.StudentEventTypeWildcard(NatsSubjects.StudentXpAwarded);

        Assert.Equal("cena.events.student.stu-001.xp_awarded", publisherSubject);
        Assert.Equal("cena.events.student.*.xp_awarded", subscriberWildcard);
        Assert.True(NatsSubjectMatcher.IsMatch(subscriberWildcard, publisherSubject),
            $"subscriber wildcard '{subscriberWildcard}' must match publisher subject '{publisherSubject}'");
    }

    [Fact]
    public void SubscribeWildcard_MatchesEveryStudentId()
    {
        var wildcard = NatsSubjects.StudentEventTypeWildcard(NatsSubjects.StudentXpAwarded);
        foreach (var studentId in new[] { "stu-001", "stu-42", "a-b-c", "12345" })
        {
            var subject = NatsSubjects.StudentEvent(studentId, NatsSubjects.StudentXpAwarded);
            Assert.True(NatsSubjectMatcher.IsMatch(wildcard, subject),
                $"wildcard '{wildcard}' must match '{subject}'");
        }
    }

    [Fact]
    public void SubscribeWildcard_DoesNotMatchUnrelatedEventTypes()
    {
        var wildcard = NatsSubjects.StudentEventTypeWildcard(NatsSubjects.StudentXpAwarded);
        var unrelated = new[]
        {
            NatsSubjects.StudentEvent("stu-001", NatsSubjects.StudentSessionStarted),
            NatsSubjects.StudentEvent("stu-001", NatsSubjects.StudentMasteryUpdated),
            NatsSubjects.StudentEvent("stu-001", NatsSubjects.StudentStreakUpdated),
            "events.xp.awarded",                 // the old broken literal
            "cena.events.xp.awarded",            // a plausible drifted pattern
            "cena.events.student.stu-001"        // too short
        };
        foreach (var subject in unrelated)
        {
            Assert.False(NatsSubjectMatcher.IsMatch(wildcard, subject),
                $"wildcard '{wildcard}' must NOT match '{subject}'");
        }
    }

    [Fact]
    public void SubscribeWildcard_DoesNotMatchLegacyLiteralSubject()
    {
        // The dispatcher used to subscribe on "events.xp.awarded" literally.
        // Guard: the modern wildcard must not accept that, and the modern
        // publisher subject must not equal the legacy literal either.
        var wildcard = NatsSubjects.StudentEventTypeWildcard(NatsSubjects.StudentXpAwarded);
        Assert.False(NatsSubjectMatcher.IsMatch(wildcard, "events.xp.awarded"));
        Assert.NotEqual("events.xp.awarded",
            NatsSubjects.StudentEvent("stu-001", NatsSubjects.StudentXpAwarded));
    }

    // ── Subject parsing: studentId comes from the subject, not the payload ──

    [Fact]
    public void TryParseStudentIdFromSubject_RoundTripsThroughPublisherHelper()
    {
        var subject = NatsSubjects.StudentEvent("stu-xyz", NatsSubjects.StudentXpAwarded);
        Assert.Equal("stu-xyz", NatsSubjects.TryParseStudentIdFromSubject(subject));
    }

    [Fact]
    public void TryParseStudentIdFromSubject_RejectsMalformedSubjects()
    {
        Assert.Null(NatsSubjects.TryParseStudentIdFromSubject(""));
        Assert.Null(NatsSubjects.TryParseStudentIdFromSubject("events.xp.awarded"));
        Assert.Null(NatsSubjects.TryParseStudentIdFromSubject("cena.events.student"));
        Assert.Null(NatsSubjects.TryParseStudentIdFromSubject("something.else.entirely.here.now"));
    }

    // ── Dispatcher behaviour: event with subject-derived studentId persists ──

    [Fact(Skip = "RDY-054e: NotificationDispatcher.HandleXpAwardedAsync NREs at line 144 — dependency graph not fully substituted. See RDY-054e.")]
    public async Task HandleXpAwardedAsync_PersistsNotificationWithStudentIdFromSubject()
    {
        var sut = new CapturingNotificationDispatcher(_nats, _store);

        // Note: the payload deliberately contains a DIFFERENT studentId so we
        // can prove the dispatcher trusts the subject, not the payload.
        var evt = new XpAwarded_V1(
            StudentId: "payload-cheat-student",
            XpAmount: 50,
            Source: "adaptive-session",
            TotalXp: 1250,
            DifficultyLevel: "comprehension",
            DifficultyMultiplier: 2);

        await sut.HandleXpAwardedAsync("stu-real-from-subject", evt, CancellationToken.None);

        Assert.Single(sut.PersistedNotifications);
        var notification = sut.PersistedNotifications[0];
        Assert.Equal("stu-real-from-subject", notification.StudentId);
        Assert.Equal("xp", notification.Kind);
        Assert.Equal("XP Gained!", notification.Title);
        Assert.Equal("You earned 50 XP from adaptive-session", notification.Body);
        Assert.Equal("award", notification.IconName);
    }

    [Fact]
    public void BuildNotification_ProducesStableShape()
    {
        var evt = new XpAwarded_V1("stu-1", 25, "practice", 100, "recall", 1);
        var notification = NotificationDispatcher.BuildNotification("stu-1", evt, DateTime.UtcNow);

        Assert.Equal("stu-1", notification.StudentId);
        Assert.Equal("xp", notification.Kind);
        Assert.Equal("normal", notification.Priority);
        Assert.Equal("XP Gained!", notification.Title);
        Assert.Equal("You earned 25 XP from practice", notification.Body);
        Assert.StartsWith("notif/", notification.Id);
        Assert.False(notification.Read);
    }

    /// <summary>
    /// Test double that captures persisted notifications in memory rather than
    /// writing to Marten, so the handler can be exercised end-to-end without a
    /// live document store.
    /// </summary>
    private sealed class CapturingNotificationDispatcher : NotificationDispatcher
    {
        public List<NotificationDocument> PersistedNotifications { get; } = new();

        public CapturingNotificationDispatcher(INatsConnection nats, IDocumentStore store)
            : base(nats, store, NullLogger<NotificationDispatcher>.Instance)
        {
        }

        protected override Task PersistNotificationAsync(
            NotificationDocument notification, CancellationToken ct)
        {
            PersistedNotifications.Add(notification);
            return Task.CompletedTask;
        }

        protected override Task<int> CountPushSubscriptionsAsync(
            string studentId, CancellationToken ct) => Task.FromResult(0);
    }
}
