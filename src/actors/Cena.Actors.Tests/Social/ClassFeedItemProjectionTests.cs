// =============================================================================
// Cena Platform — Class Feed Item Projection Tests
// Tests for projection that creates feed items from events.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Marten;
using NSubstitute;

namespace Cena.Actors.Tests.Social;

/// <summary>
/// Tests the ClassFeedItemProjection event handlers directly
/// (no Marten infrastructure needed — just method calls).
/// </summary>
public sealed class ClassFeedItemProjectionTests
{
    private readonly ClassFeedItemProjection _projection = new();
    private readonly IDocumentOperations _operations = Substitute.For<IDocumentOperations>();

    [Fact]
    public void Project_BadgeEarned_CreatesAchievementFeedItem()
    {
        var evt = new BadgeEarned_V1(
            StudentId: "student-001",
            BadgeId: "badge-week-streak",
            BadgeName: "Week Streak",
            BadgeDescription: "Maintained a 7-day learning streak",
            AwardedAt: DateTimeOffset.Parse("2026-04-10T10:00:00Z"));

        _projection.Project(evt, _operations);

        _operations.Received(1).Store(Arg.Is<ClassFeedItemDocument>(doc =>
            doc.Id == "feed:badge:student-001:badge-week-streak" &&
            doc.FeedItemId == "feed:badge:student-001:badge-week-streak" &&
            doc.Kind == "achievement" &&
            doc.AuthorStudentId == "student-001" &&
            doc.Title == "Earned 'Week Streak' Badge" &&
            doc.Body == "Unlocked the Week Streak badge!" &&
            doc.ReactionCount == 0 &&
            doc.CommentCount == 0));
    }

    [Fact]
    public void Project_BadgeEarned_SetsCorrectPostedAt()
    {
        var awardedAt = DateTimeOffset.Parse("2026-04-10T15:30:00Z");
        var evt = new BadgeEarned_V1(
            StudentId: "student-002",
            BadgeId: "badge-master",
            BadgeName: "Quiz Master",
            BadgeDescription: "Answered 50 questions correctly",
            AwardedAt: awardedAt);

        _projection.Project(evt, _operations);

        _operations.Received(1).Store(Arg.Is<ClassFeedItemDocument>(doc =>
            doc.PostedAt == awardedAt.DateTime));
    }

    [Fact]
    public void Projection_IncludeType_RegistersBadgeEarnedEvent()
    {
        var includedTypes = _projection.IncludedEventTypes.ToList();
        
        Assert.Contains(typeof(BadgeEarned_V1), includedTypes);
    }

    [Fact]
    public void Project_MultipleBadges_CreatesSeparateFeedItems()
    {
        var evt1 = new BadgeEarned_V1(
            StudentId: "student-001",
            BadgeId: "badge-1",
            BadgeName: "First Steps",
            BadgeDescription: "Completed first session",
            AwardedAt: DateTimeOffset.UtcNow);

        var evt2 = new BadgeEarned_V1(
            StudentId: "student-002",
            BadgeId: "badge-2",
            BadgeName: "Speed Demon",
            BadgeDescription: "Fast completion",
            AwardedAt: DateTimeOffset.UtcNow);

        _projection.Project(evt1, _operations);
        _projection.Project(evt2, _operations);

        _operations.Received(2).Store(Arg.Any<ClassFeedItemDocument>());
    }
}
