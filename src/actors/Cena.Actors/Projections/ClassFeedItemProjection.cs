// =============================================================================
// Cena Platform — Class Feed Item Projection
// Multi-stream projection that creates social feed items from various events.
// Registered as Async for eventual consistency.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Marten;
using Marten.Events.Projections;

namespace Cena.Actors.Projections;

/// <summary>
/// Projection that transforms student activity events into class feed items.
/// Listens for: BadgeEarned, LearningSessionEnded (highlights), and future events.
/// </summary>
public class ClassFeedItemProjection : EventProjection
{
    public ClassFeedItemProjection()
    {
        // EventProjection routes via reflection on Project(...) overloads.
    }

    /// <summary>
    /// Create a feed item when a student earns a badge.
    /// FIND-data-001: Never use DateTime.UtcNow in projections — it breaks deterministic replay.
    /// Use event.Timestamp as deterministic fallback when AwardedAt is default.
    /// </summary>
    public void Project(BadgeEarned_V1 e, IDocumentOperations ops)
    {
        // Deterministic timestamp selection: AwardedAt > Timestamp > default
        // All values come from the event itself, never from wall clock.
        var postedAt = e.AwardedAt != default ? e.AwardedAt :
                       e.Timestamp != default ? e.Timestamp :
                       DateTimeOffset.MinValue;

        var doc = new ClassFeedItemDocument
        {
            Id = $"feed:badge:{e.StudentId}:{e.BadgeId}",
            FeedItemId = $"feed:badge:{e.StudentId}:{e.BadgeId}",
            Kind = "achievement",
            AuthorStudentId = e.StudentId,
            AuthorDisplayName = "", // Will be populated by seeder or query join
            Title = $"Earned '{e.BadgeName}' Badge",
            Body = $"Unlocked the {e.BadgeName} badge!",
            PostedAt = postedAt.UtcDateTime,
            ReactionCount = 0,
            CommentCount = 0
        };

        ops.Store(doc);
    }
}
