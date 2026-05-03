// =============================================================================
// Cena Platform — Marten registration for the Social bounded context (PRR-304)
//
// Extracted from MartenConfiguration.cs as part of the PRR-304 LOC drain.
// Mirrors the per-bounded-context pattern established by
// MockExamRunMartenRegistration.RegisterMockExamRunContext: each bounded
// context owns the schema/event wiring for its own documents, and the
// cross-cutting MartenConfiguration.cs stays under the 500-LOC ratchet.
//
// Documents registered here all live in Cena.Infrastructure.Documents.SocialDocuments:
//   - CommentDocument            (post comments)
//   - FriendRequestDocument      (pending friend invites)
//   - FriendshipDocument         (accepted friendships)
//   - StudyRoomDocument          (study-room metadata)
//   - StudyRoomMembershipDocument(student ↔ room link)
//   - ClassFeedItemDocument      (classroom feed posts)
//   - PeerSolutionDocument       (peer-shared solution snippets)
//
// Behaviour-preserving extract: indexes + Identity expressions are
// identical to the pre-extract definitions in MartenConfiguration.cs.
// No projection registration changes; tests stay green.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Configuration;

/// <summary>
/// Marten registrations for the Social bounded context (friend requests,
/// friendships, study rooms, class feed, peer solutions, comments).
/// Called from <see cref="MartenConfiguration.ConfigureCenaEventStore"/>.
/// </summary>
public static class MartenSocialRegistration
{
    /// <summary>
    /// Register all Social-context documents with Marten. Idempotent;
    /// safe to call multiple times during host startup composition.
    /// </summary>
    public static void RegisterSocialContext(this StoreOptions opts)
    {
        // ── Social Documents (STB-06b) ──
        opts.Schema.For<CommentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ItemId)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);

        opts.Schema.For<FriendRequestDocument>()
            .Identity(x => x.Id)
            .Index(x => x.FromStudentId)
            .Index(x => x.ToStudentId)
            .Index(x => x.Status);

        opts.Schema.For<FriendshipDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentAId)
            .Index(x => x.StudentBId);

        opts.Schema.For<StudyRoomDocument>()
            .Identity(x => x.Id)
            .Index(x => x.HostStudentId)
            .Index(x => x.IsPublic);

        opts.Schema.For<StudyRoomMembershipDocument>()
            .Identity(x => x.Id)
            .Index(x => x.RoomId)
            .Index(x => x.StudentId)
            .Index(x => x.IsActive);

        // ── Class Feed Documents (HARDEN SocialEndpoints) ──
        opts.Schema.For<ClassFeedItemDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ClassroomId)
            .Index(x => x.Kind)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);

        opts.Schema.For<PeerSolutionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.QuestionId)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);
    }
}
