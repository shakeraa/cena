// =============================================================================
// Cena Platform — Social Seed Data (HARDEN SocialEndpoints)
// Idempotent seeding for class feed items, peer solutions, friendships, study rooms.
// Safe to re-run on every startup — uses upsert semantics.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seeds social feature data: class feed items, peer solutions, friendships, study rooms.
/// All seeds are idempotent (upsert by ID) — safe to re-run.
/// </summary>
public static class SocialSeedData
{
    /// <summary>
    /// Seed all social data. Call from DatabaseSeeder.SeedAllAsync additionalSeeds.
    /// </summary>
    public static async Task SeedSocialDataAsync(
        IDocumentStore store,
        ILogger logger)
    {
        logger.LogInformation("Seeding social data (feed items, solutions, friendships, study rooms)...");

        await using var session = store.LightweightSession();

        // Seed in dependency order
        await SeedClassFeedItemsAsync(session, logger);
        await SeedPeerSolutionsAsync(session, logger);
        await SeedFriendshipsAsync(session, logger);
        await SeedStudyRoomsAsync(session, logger);

        await session.SaveChangesAsync();

        logger.LogInformation("Social data seeding complete.");
    }

    /// <summary>
    /// Seed 10 ClassFeedItemDocument for dev experience.
    /// </summary>
    private static async Task SeedClassFeedItemsAsync(
        IDocumentSession session,
        ILogger logger)
    {
        var feedItems = new[]
        {
            new ClassFeedItemDocument
            {
                Id = "seed:feed:001",
                FeedItemId = "feed_001",
                Kind = "achievement",
                AuthorStudentId = "seed:student:001",
                AuthorDisplayName = "Alex",
                Title = "Earned 'Week Streak' Badge",
                Body = "Maintained a 7-day learning streak!",
                PostedAt = DateTime.UtcNow.AddHours(-2),
                ReactionCount = 12,
                CommentCount = 3
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:002",
                FeedItemId = "feed_002",
                Kind = "milestone",
                AuthorStudentId = "seed:student:002",
                AuthorDisplayName = "Jordan",
                Title = "Completed 100 Questions",
                Body = "Just hit 100 questions answered correctly!",
                PostedAt = DateTime.UtcNow.AddHours(-4),
                ReactionCount = 8,
                CommentCount = 1
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:003",
                FeedItemId = "feed_003",
                Kind = "question",
                AuthorStudentId = "seed:student:003",
                AuthorDisplayName = "Taylor",
                Title = "Help with Quadratic Equations",
                Body = "Can someone explain factoring when a ≠ 1?",
                PostedAt = DateTime.UtcNow.AddHours(-5),
                ReactionCount = 5,
                CommentCount = 7
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:004",
                FeedItemId = "feed_004",
                Kind = "announcement",
                AuthorStudentId = "seed:teacher:001",
                AuthorDisplayName = "Ms. Johnson",
                Title = "Quiz Tomorrow",
                Body = "Remember: Chapter 5 quiz tomorrow during class!",
                PostedAt = DateTime.UtcNow.AddHours(-6),
                ReactionCount = 24,
                CommentCount = 5
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:005",
                FeedItemId = "feed_005",
                Kind = "achievement",
                AuthorStudentId = "seed:student:004",
                AuthorDisplayName = "Morgan",
                Title = "Mastered Algebra",
                Body = "Completed all Algebra Fundamentals cards!",
                PostedAt = DateTime.UtcNow.AddHours(-8),
                ReactionCount = 15,
                CommentCount = 4
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:006",
                FeedItemId = "feed_006",
                Kind = "milestone",
                AuthorStudentId = "seed:student:005",
                AuthorDisplayName = "Casey",
                Title = "Reached Level 10",
                Body = "Congratulations on reaching Level 10!",
                PostedAt = DateTime.UtcNow.AddHours(-10),
                ReactionCount = 20,
                CommentCount = 6
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:007",
                FeedItemId = "feed_007",
                Kind = "question",
                AuthorStudentId = "seed:student:006",
                AuthorDisplayName = "Riley",
                Title = "Physics Problem Help",
                Body = "Stuck on conservation of momentum problem #12",
                PostedAt = DateTime.UtcNow.AddHours(-12),
                ReactionCount = 3,
                CommentCount = 4
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:008",
                FeedItemId = "feed_008",
                Kind = "achievement",
                AuthorStudentId = "seed:student:007",
                AuthorDisplayName = "Quinn",
                Title = "Speed Demon Unlocked",
                Body = "Completed a session in under 10 minutes!",
                PostedAt = DateTime.UtcNow.AddHours(-14),
                ReactionCount = 9,
                CommentCount = 2
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:009",
                FeedItemId = "feed_009",
                Kind = "announcement",
                AuthorStudentId = "seed:teacher:002",
                AuthorDisplayName = "Mr. Chen",
                Title = "Extra Credit Opportunity",
                Body = "Submit a solution to the bonus problem for +5 points!",
                PostedAt = DateTime.UtcNow.AddHours(-16),
                ReactionCount = 31,
                CommentCount = 12
            },
            new ClassFeedItemDocument
            {
                Id = "seed:feed:010",
                FeedItemId = "feed_010",
                Kind = "milestone",
                AuthorStudentId = "seed:student:008",
                AuthorDisplayName = "Avery",
                Title = "30-Day Streak!",
                Body = "One month of continuous learning!",
                PostedAt = DateTime.UtcNow.AddHours(-18),
                ReactionCount = 18,
                CommentCount = 8
            }
        };

        foreach (var item in feedItems)
        {
            session.Store(item);
        }

        logger.LogInformation("Seeded {Count} class feed items", feedItems.Length);
    }

    /// <summary>
    /// Seed 5 PeerSolutionDocument for dev experience.
    /// </summary>
    private static async Task SeedPeerSolutionsAsync(
        IDocumentSession session,
        ILogger logger)
    {
        var questionId = "seed:question:001";

        var solutions = new[]
        {
            new PeerSolutionDocument
            {
                Id = "seed:sol:001",
                SolutionId = "sol_001",
                QuestionId = questionId,
                AuthorStudentId = "seed:student:001",
                AuthorDisplayName = "Alex",
                Content = "To solve this, first identify the variables. Then apply the quadratic formula: x = (-b ± √(b²-4ac)) / 2a. In this case, a=2, b=-4, c=-6.",
                UpvoteCount = 24,
                DownvoteCount = 1,
                PostedAt = DateTime.UtcNow.AddDays(-2)
            },
            new PeerSolutionDocument
            {
                Id = "seed:sol:002",
                SolutionId = "sol_002",
                QuestionId = questionId,
                AuthorStudentId = "seed:student:002",
                AuthorDisplayName = "Jordan",
                Content = "I approached this by factoring first. Look for two numbers that multiply to -12 and add to -2. Those numbers are -6 and 2.",
                UpvoteCount = 18,
                DownvoteCount = 0,
                PostedAt = DateTime.UtcNow.AddDays(-3)
            },
            new PeerSolutionDocument
            {
                Id = "seed:sol:003",
                SolutionId = "sol_003",
                QuestionId = questionId,
                AuthorStudentId = "seed:student:003",
                AuthorDisplayName = "Taylor",
                Content = "Don't forget to check your answer by substituting back into the original equation! x = 3 and x = -1 both work.",
                UpvoteCount = 15,
                DownvoteCount = 2,
                PostedAt = DateTime.UtcNow.AddDays(-4)
            },
            new PeerSolutionDocument
            {
                Id = "seed:sol:004",
                SolutionId = "sol_004",
                QuestionId = questionId,
                AuthorStudentId = "seed:student:004",
                AuthorDisplayName = "Morgan",
                Content = "My attempt: I tried completing the square but got stuck after rearranging to x² - 2x = 3.",
                UpvoteCount = 3,
                DownvoteCount = 0,
                PostedAt = DateTime.UtcNow.AddDays(-1)
            },
            new PeerSolutionDocument
            {
                Id = "seed:sol:005",
                SolutionId = "sol_005",
                QuestionId = questionId,
                AuthorStudentId = "seed:student:005",
                AuthorDisplayName = "Casey",
                Content = "Alternative method: Graph both sides as separate functions and find intersection points. y = 2x²-4x-6 and y = 0.",
                UpvoteCount = 8,
                DownvoteCount = 1,
                PostedAt = DateTime.UtcNow.AddDays(-5)
            }
        };

        foreach (var sol in solutions)
        {
            session.Store(sol);
        }

        logger.LogInformation("Seeded {Count} peer solutions", solutions.Length);
    }

    /// <summary>
    /// Seed friendships and friend requests for dev experience.
    /// </summary>
    private static async Task SeedFriendshipsAsync(
        IDocumentSession session,
        ILogger logger)
    {
        // Seed accepted friendships
        var friendships = new[]
        {
            new FriendshipDocument
            {
                Id = "seed:friendship:001",
                FriendshipId = "friendship_001",
                StudentAId = "demo-student-001",
                StudentBId = "seed:student:001",
                BecameFriendsAt = DateTime.UtcNow.AddDays(-30)
            },
            new FriendshipDocument
            {
                Id = "seed:friendship:002",
                FriendshipId = "friendship_002",
                StudentAId = "demo-student-001",
                StudentBId = "seed:student:002",
                BecameFriendsAt = DateTime.UtcNow.AddDays(-20)
            },
            new FriendshipDocument
            {
                Id = "seed:friendship:003",
                FriendshipId = "friendship_003",
                StudentAId = "demo-student-001",
                StudentBId = "seed:student:003",
                BecameFriendsAt = DateTime.UtcNow.AddDays(-15)
            },
            new FriendshipDocument
            {
                Id = "seed:friendship:004",
                FriendshipId = "friendship_004",
                StudentAId = "demo-student-001",
                StudentBId = "seed:student:004",
                BecameFriendsAt = DateTime.UtcNow.AddDays(-10)
            }
        };

        foreach (var f in friendships)
        {
            session.Store(f);
        }

        // Seed pending friend requests
        var pendingRequests = new[]
        {
            new FriendRequestDocument
            {
                Id = "seed:request:001",
                RequestId = "req_001",
                FromStudentId = "seed:student:009",
                ToStudentId = "demo-student-001",
                Status = "pending",
                RequestedAt = DateTime.UtcNow.AddDays(-2)
            },
            new FriendRequestDocument
            {
                Id = "seed:request:002",
                RequestId = "req_002",
                FromStudentId = "seed:student:010",
                ToStudentId = "demo-student-001",
                Status = "pending",
                RequestedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        foreach (var r in pendingRequests)
        {
            session.Store(r);
        }

        logger.LogInformation("Seeded {Count} friendships and {Count} pending requests", 
            friendships.Length, pendingRequests.Length);
    }

    /// <summary>
    /// Seed study rooms for dev experience.
    /// </summary>
    private static async Task SeedStudyRoomsAsync(
        IDocumentSession session,
        ILogger logger)
    {
        var rooms = new[]
        {
            new StudyRoomDocument
            {
                Id = "seed:room:001",
                RoomId = "room_001",
                Name = "Algebra Study Group",
                Subject = "Mathematics",
                HostStudentId = "seed:student:001",
                IsPublic = true,
                MaxCapacity = 8,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                IsActive = true
            },
            new StudyRoomDocument
            {
                Id = "seed:room:002",
                RoomId = "room_002",
                Name = "Physics Problem Solving",
                Subject = "Physics",
                HostStudentId = "seed:student:003",
                IsPublic = true,
                MaxCapacity = 10,
                CreatedAt = DateTime.UtcNow.AddHours(-4),
                IsActive = true
            },
            new StudyRoomDocument
            {
                Id = "seed:room:003",
                RoomId = "room_003",
                Name = "Chemistry Review Session",
                Subject = "Chemistry",
                HostStudentId = "seed:student:005",
                IsPublic = false,
                MaxCapacity = 6,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                IsActive = true
            }
        };

        foreach (var room in rooms)
        {
            session.Store(room);
        }

        // Seed memberships for hosts
        var memberships = new[]
        {
            new StudyRoomMembershipDocument
            {
                Id = "seed:membership:001",
                RoomId = "room_001",
                StudentId = "seed:student:001",
                JoinedAt = DateTime.UtcNow.AddHours(-2),
                IsActive = true
            },
            new StudyRoomMembershipDocument
            {
                Id = "seed:membership:002",
                RoomId = "room_002",
                StudentId = "seed:student:003",
                JoinedAt = DateTime.UtcNow.AddHours(-4),
                IsActive = true
            },
            new StudyRoomMembershipDocument
            {
                Id = "seed:membership:003",
                RoomId = "room_003",
                StudentId = "seed:student:005",
                JoinedAt = DateTime.UtcNow.AddHours(-1),
                IsActive = true
            }
        };

        foreach (var m in memberships)
        {
            session.Store(m);
        }

        logger.LogInformation("Seeded {Count} study rooms with {Count} memberships", 
            rooms.Length, memberships.Length);
    }
}
