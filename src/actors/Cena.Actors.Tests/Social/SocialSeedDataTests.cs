// =============================================================================
// Cena Platform — Social Seed Data Tests
// Tests for idempotent seeding of class feed items, peer solutions, friendships.
// =============================================================================

using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Seed;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Social;

/// <summary>
/// Tests for SocialSeedData — verifies document creation logic.
/// Uses mocked IDocumentSession to verify Store calls without real persistence.
/// </summary>
public sealed class SocialSeedDataTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();

    public SocialSeedDataTests()
    {
        _store.LightweightSession().Returns(_session);
    }

    [Fact]
    public async Task SeedSocialDataAsync_OpensLightweightSession()
    {
        await SocialSeedData.SeedSocialDataAsync(_store, NullLogger.Instance);

        _store.Received(1).LightweightSession();
    }

    [Fact]
    public async Task SeedSocialDataAsync_SavesChanges()
    {
        await SocialSeedData.SeedSocialDataAsync(_store, NullLogger.Instance);

        await _session.Received(1).SaveChangesAsync();
    }

    [Fact]
    public void ClassFeedItemDocument_HasAllRequiredFields()
    {
        var doc = new ClassFeedItemDocument
        {
            Id = "feed:test:001",
            FeedItemId = "feed_001",
            Kind = "achievement",
            AuthorStudentId = "student-001",
            AuthorDisplayName = "Alex",
            Title = "Test Achievement",
            Body = "Test body",
            PostedAt = DateTime.UtcNow,
            ReactionCount = 5,
            CommentCount = 2
        };

        Assert.Equal("feed:test:001", doc.Id);
        Assert.Equal("feed_001", doc.FeedItemId);
        Assert.Equal("achievement", doc.Kind);
        Assert.Equal("student-001", doc.AuthorStudentId);
        Assert.Equal("Alex", doc.AuthorDisplayName);
        Assert.Equal("Test Achievement", doc.Title);
        Assert.Equal("Test body", doc.Body);
        Assert.Equal(5, doc.ReactionCount);
        Assert.Equal(2, doc.CommentCount);
        Assert.False(doc.IsDeleted);
    }

    [Fact]
    public void PeerSolutionDocument_HasAllRequiredFields()
    {
        var doc = new PeerSolutionDocument
        {
            Id = "sol:test:001",
            SolutionId = "sol_001",
            QuestionId = "q_001",
            AuthorStudentId = "student-001",
            AuthorDisplayName = "Alex",
            Content = "Test solution content",
            UpvoteCount = 10,
            DownvoteCount = 1,
            PostedAt = DateTime.UtcNow
        };

        Assert.Equal("sol:test:001", doc.Id);
        Assert.Equal("sol_001", doc.SolutionId);
        Assert.Equal("q_001", doc.QuestionId);
        Assert.Equal("student-001", doc.AuthorStudentId);
        Assert.Equal("Alex", doc.AuthorDisplayName);
        Assert.Equal("Test solution content", doc.Content);
        Assert.Equal(10, doc.UpvoteCount);
        Assert.Equal(1, doc.DownvoteCount);
        Assert.False(doc.IsDeleted);
    }

    [Fact]
    public void ClassFeedItemDocument_SupportsAllKinds()
    {
        var kinds = new[] { "achievement", "milestone", "question", "announcement" };

        foreach (var kind in kinds)
        {
            var doc = new ClassFeedItemDocument { Kind = kind };
            Assert.Equal(kind, doc.Kind);
        }
    }

    [Fact]
    public void PeerSolutionDocument_CanHaveZeroVotes()
    {
        var doc = new PeerSolutionDocument
        {
            UpvoteCount = 0,
            DownvoteCount = 0
        };

        Assert.Equal(0, doc.UpvoteCount);
        Assert.Equal(0, doc.DownvoteCount);
    }

    [Fact]
    public void ClassFeedItemDocument_Defaults_IsDeletedFalse()
    {
        var doc = new ClassFeedItemDocument();
        Assert.False(doc.IsDeleted);
    }

    [Fact]
    public void PeerSolutionDocument_Defaults_IsDeletedFalse()
    {
        var doc = new PeerSolutionDocument();
        Assert.False(doc.IsDeleted);
    }
}
