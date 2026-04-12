// =============================================================================
// Cena Platform — Social REST Endpoints (HARDEN: production-grade)
// Class feed, peer solutions, friends, and study rooms (real Marten queries)
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Queries.Social;
using Cena.Api.Contracts.Social;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class SocialEndpoints
{
    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social")
            .WithTags("Social")
            .RequireAuthorization();

        // Social features require consent
        group.MapGet("/class-feed", GetClassFeed)
            .WithName("GetClassFeed")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        
        // Peer comparison features require consent
        group.MapGet("/peers/solutions", GetPeerSolutions)
            .WithName("GetPeerSolutions")
            .RequireConsent(ProcessingPurpose.PeerComparison);
        
        // Social features require consent
        group.MapGet("/friends", GetFriends)
            .WithName("GetFriends")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        
        // Social features require consent
        group.MapGet("/study-rooms", GetStudyRooms)
            .WithName("GetStudyRooms")
            .RequireConsent(ProcessingPurpose.SocialFeatures);

        // STB-06b: Write endpoints - all require SocialFeatures consent
        group.MapPost("/reactions", AddReaction)
            .WithName("AddReaction")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/comments", AddComment)
            .WithName("AddComment")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/friends/request", SendFriendRequest)
            .WithName("SendFriendRequest")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/friends/{id}/accept", AcceptFriendRequest)
            .WithName("AcceptFriendRequest")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/study-rooms", CreateStudyRoom)
            .WithName("CreateStudyRoom")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/study-rooms/{id}/join", JoinStudyRoom)
            .WithName("JoinStudyRoom")
            .RequireConsent(ProcessingPurpose.SocialFeatures);
        group.MapPost("/study-rooms/{id}/leave", LeaveStudyRoom)
            .WithName("LeaveStudyRoom")
            .RequireConsent(ProcessingPurpose.SocialFeatures);

        return app;
    }

    // GET /api/social/class-feed?page=1 — returns class feed items
    private static async Task<IResult> GetClassFeed(
        HttpContext ctx,
        IDocumentStore store,
        int? page = 1)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var currentPage = Math.Max(1, page ?? 1);
        const int pageSize = 10;

        await using var session = store.QuerySession();

        // Query feed items from Marten, ordered by posted date descending
        var feedItems = await session.Query<ClassFeedItemDocument>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.PostedAt)
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize + 1) // Take one extra to determine if there's more
            .ToListAsync();

        var hasMore = feedItems.Count > pageSize;
        var items = feedItems.Take(pageSize).Select(x => new ClassFeedItem(
            ItemId: x.FeedItemId,
            Kind: x.Kind,
            AuthorStudentId: x.AuthorStudentId,
            AuthorDisplayName: x.AuthorDisplayName,
            AuthorAvatarUrl: x.AuthorAvatarUrl,
            Title: x.Title,
            Body: x.Body,
            PostedAt: x.PostedAt,
            ReactionCount: x.ReactionCount,
            CommentCount: x.CommentCount)).ToArray();

        var dto = new ClassFeedDto(
            Items: items,
            Page: currentPage,
            PageSize: pageSize,
            HasMore: hasMore);

        return Results.Ok(dto);
    }

    // GET /api/social/peers/solutions?questionId=q1 — returns peer solutions
    private static async Task<IResult> GetPeerSolutions(
        HttpContext ctx,
        IDocumentStore store,
        string? questionId = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Query peer solutions from Marten for the given question
        var query = session.Query<PeerSolutionDocument>().Where(x => !x.IsDeleted);
        
        if (!string.IsNullOrEmpty(questionId))
        {
            query = query.Where(x => x.QuestionId == questionId);
        }

        var solutions = await query
            .OrderByDescending(x => x.UpvoteCount - x.DownvoteCount)
            .ThenByDescending(x => x.PostedAt)
            .Select(x => new PeerSolution(
                x.SolutionId,
                x.QuestionId,
                x.AuthorStudentId,
                x.AuthorDisplayName,
                x.Content,
                x.UpvoteCount,
                x.DownvoteCount,
                x.PostedAt))
            .ToListAsync();

        var dto = new PeerSolutionListDto(Solutions: solutions.ToArray());
        return Results.Ok(dto);
    }

    // GET /api/social/friends — returns friends list
    //
    // FIND-data-010 (2026-04-11): this handler used to issue one
    // LoadAsync<StudentProfileSnapshot> per friend AND per pending
    // request inside a foreach, producing (friendCount + pendingCount)
    // extra round-trips. The fix below batch-loads ALL needed profiles
    // in a single Marten LINQ query, then hands the pre-built dictionary
    // to the pure SocialProjectionBuilder helper. Total queries per
    // request: 3 (friendships, pending, profiles) — constant, regardless
    // of friend count. See docs/reviews/agent-3-data-findings.md.
    private static async Task<IResult> GetFriends(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Query 1: friendships involving the current student.
        var friendships = await session.Query<FriendshipDocument>()
            .Where(f => f.StudentAId == studentId || f.StudentBId == studentId)
            .ToListAsync();

        // Query 2: pending incoming friend requests.
        var pendingDocs = await session.Query<FriendRequestDocument>()
            .Where(r => r.ToStudentId == studentId && r.Status == "pending")
            .ToListAsync();

        // Collect every student id whose profile we'll need to render the
        // response. The helper de-dupes across friendships + pending.
        var profileIds = SocialProjectionBuilder.CollectFriendProfileIds(
            friendships.ToList(),
            studentId,
            pendingDocs.ToList());

        // Query 3: ONE batch fetch for every profile we need. This replaces
        // the old (N+M) sequential LoadAsync loop. Short-circuit when there
        // is nothing to fetch to avoid a pointless round-trip.
        var profilesById = profileIds.Count == 0
            ? new Dictionary<string, StudentProfileSnapshot>(StringComparer.Ordinal)
            : (await session.Query<StudentProfileSnapshot>()
                    .Where(p => profileIds.Contains(p.StudentId))
                    .ToListAsync())
                .GroupBy(p => p.StudentId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Pure assembly — no further I/O. See SocialProjectionBuilder for the
        // guarantee that this path cannot re-introduce an N+1 regression.
        var dto = SocialProjectionBuilder.BuildFriendsListDto(
            friendships.ToList(),
            studentId,
            pendingDocs.ToList(),
            profilesById);

        return Results.Ok(dto);
    }

    // GET /api/social/study-rooms — returns study rooms
    //
    // FIND-data-011 (2026-04-11): this handler used to issue TWO queries
    // per room inside a foreach — one CountAsync for members and one
    // LoadAsync for the host profile — producing (1 + 2*N) round-trips.
    // The fix below batch-loads all active memberships for the visible
    // rooms in ONE query, aggregates the counts in memory, and
    // batch-loads all host profiles in ONE more query. Total queries per
    // request: 4 (memberships-for-me, rooms, all-active-memberships,
    // host-profiles) — constant, regardless of room count.
    // See docs/reviews/agent-3-data-findings.md.
    private static async Task<IResult> GetStudyRooms(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Query 1: rooms the current student is a member of (for the
        // visibility filter below).
        var memberships = await session.Query<StudyRoomMembershipDocument>()
            .Where(m => m.StudentId == studentId && m.IsActive)
            .Select(m => m.RoomId)
            .ToListAsync();

        // Query 2: rooms the student is allowed to see (own + public).
        var rooms = await session.Query<StudyRoomDocument>()
            .Where(r => r.IsActive && (r.IsPublic || memberships.Contains(r.RoomId)))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var roomList = rooms.ToList();
        var roomIds = SocialProjectionBuilder.CollectRoomIds(roomList);
        var hostIds = SocialProjectionBuilder.CollectHostProfileIds(roomList);

        // Query 3: ALL active memberships for every visible room in ONE
        // shot. We only project the RoomId column so the response payload
        // is small even for popular rooms. The aggregation happens in
        // memory in the pure helper.
        IReadOnlyDictionary<string, int> memberCountsByRoomId;
        if (roomIds.Count == 0)
        {
            memberCountsByRoomId = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        else
        {
            var activeMembershipRoomIds = await session.Query<StudyRoomMembershipDocument>()
                .Where(m => roomIds.Contains(m.RoomId) && m.IsActive)
                .Select(m => m.RoomId)
                .ToListAsync();

            memberCountsByRoomId = SocialProjectionBuilder.AggregateMemberCounts(
                activeMembershipRoomIds);
        }

        // Query 4: ONE batch fetch for every host profile we need.
        IReadOnlyDictionary<string, StudentProfileSnapshot> hostProfilesById;
        if (hostIds.Count == 0)
        {
            hostProfilesById = new Dictionary<string, StudentProfileSnapshot>(StringComparer.Ordinal);
        }
        else
        {
            hostProfilesById = (await session.Query<StudentProfileSnapshot>()
                    .Where(p => hostIds.Contains(p.StudentId))
                    .ToListAsync())
                .GroupBy(p => p.StudentId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        // Pure assembly — no I/O. The helper's signature guarantees that
        // the N+1 pattern cannot be re-introduced by a future refactor.
        var dto = SocialProjectionBuilder.BuildStudyRoomListDto(
            roomList,
            memberCountsByRoomId,
            hostProfilesById);

        return Results.Ok(dto);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STB-06b: Write Endpoints
    // ═════════════════════════════════════════════════════════════════════════

    // POST /api/social/reactions — add a reaction to an item
    private static async Task<IResult> AddReaction(
        HttpContext ctx,
        IDocumentStore store,
        AddReactionRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.ItemId) || string.IsNullOrWhiteSpace(request.ReactionType))
            return Results.BadRequest(new { error = "ItemId and ReactionType are required" });

        await using var session = store.LightweightSession();

        // Append reaction event
        var reactionEvent = new ReactionAdded_V1(
            StudentId: studentId,
            ItemId: request.ItemId,
            ReactionType: request.ReactionType,
            AddedAt: DateTimeOffset.UtcNow);

        session.Events.Append(studentId, reactionEvent);
        await session.SaveChangesAsync();

        // Get approximate new count (would be projection in real implementation)
        return Results.Ok(new AddReactionResponse(
            Ok: true,
            ItemId: request.ItemId,
            ReactionType: request.ReactionType,
            NewCount: 1));
    }

    // POST /api/social/comments — add a comment to an item
    private static async Task<IResult> AddComment(
        HttpContext ctx,
        IDocumentStore store,
        AddCommentRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.ItemId) || string.IsNullOrWhiteSpace(request.Content))
            return Results.BadRequest(new { error = "ItemId and Content are required" });

        await using var session = store.LightweightSession();

        var commentId = $"comment_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        var comment = new CommentDocument
        {
            Id = commentId,
            CommentId = commentId,
            ItemId = request.ItemId,
            AuthorStudentId = studentId,
            Content = request.Content,
            PostedAt = now
        };

        session.Store(comment);

        // Append event
        var commentEvent = new CommentPosted_V1(
            CommentId: commentId,
            ItemId: request.ItemId,
            AuthorStudentId: studentId,
            Content: request.Content,
            PostedAt: now);

        session.Events.Append(studentId, commentEvent);
        await session.SaveChangesAsync();

        return Results.Ok(new AddCommentResponse(
            CommentId: commentId,
            ItemId: request.ItemId,
            Content: request.Content,
            PostedAt: now));
    }

    // POST /api/social/friends/request — send a friend request
    private static async Task<IResult> SendFriendRequest(
        HttpContext ctx,
        IDocumentStore store,
        SendFriendRequestRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.TargetStudentId))
            return Results.BadRequest(new { error = "TargetStudentId is required" });

        if (request.TargetStudentId == studentId)
            return Results.BadRequest(new { error = "Cannot send friend request to yourself" });

        await using var session = store.LightweightSession();

        // Check if already friends
        var existingFriendship = await session.Query<FriendshipDocument>()
            .FirstOrDefaultAsync(f => 
                (f.StudentAId == studentId && f.StudentBId == request.TargetStudentId) ||
                (f.StudentAId == request.TargetStudentId && f.StudentBId == studentId));

        if (existingFriendship != null)
            return Results.Conflict(new { error = "Already friends with this student" });

        // Check for existing pending request
        var existingRequest = await session.Query<FriendRequestDocument>()
            .FirstOrDefaultAsync(r => 
                r.FromStudentId == studentId && 
                r.ToStudentId == request.TargetStudentId && 
                r.Status == "pending");

        if (existingRequest != null)
            return Results.Ok(new SendFriendRequestResponse(
                RequestId: existingRequest.RequestId,
                Status: "pending"));

        var requestId = $"friend_req_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        var friendRequest = new FriendRequestDocument
        {
            Id = requestId,
            RequestId = requestId,
            FromStudentId = studentId,
            ToStudentId = request.TargetStudentId,
            Status = "pending",
            RequestedAt = now
        };

        session.Store(friendRequest);

        // Append event
        var requestEvent = new FriendRequestSent_V1(
            RequestId: requestId,
            FromStudentId: studentId,
            ToStudentId: request.TargetStudentId,
            RequestedAt: now);

        session.Events.Append(studentId, requestEvent);
        await session.SaveChangesAsync();

        return Results.Ok(new SendFriendRequestResponse(
            RequestId: requestId,
            Status: "pending"));
    }

    // POST /api/social/friends/{id}/accept — accept a friend request
    private static async Task<IResult> AcceptFriendRequest(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var friendRequest = await session.LoadAsync<FriendRequestDocument>(id);
        if (friendRequest == null)
            return Results.NotFound(new { error = "Friend request not found" });

        if (friendRequest.ToStudentId != studentId)
            return Results.Forbid();

        if (friendRequest.Status != "pending")
            return Results.Conflict(new { error = "Friend request is not pending" });

        // Update request status
        friendRequest.Status = "accepted";
        friendRequest.RespondedAt = DateTime.UtcNow;
        session.Store(friendRequest);

        // Create friendship
        var friendshipId = $"friendship_{Guid.NewGuid():N}";
        var friendship = new FriendshipDocument
        {
            Id = friendshipId,
            FriendshipId = friendshipId,
            StudentAId = friendRequest.FromStudentId,
            StudentBId = friendRequest.ToStudentId,
            BecameFriendsAt = DateTime.UtcNow
        };
        session.Store(friendship);

        // Append event
        var acceptedEvent = new FriendRequestAccepted_V1(
            RequestId: id,
            FromStudentId: friendRequest.FromStudentId,
            ToStudentId: studentId,
            AcceptedAt: DateTime.UtcNow);

        session.Events.Append(studentId, acceptedEvent);
        await session.SaveChangesAsync();

        return Results.Ok(new AcceptFriendRequestResponse(
            Ok: true,
            FriendshipId: friendshipId));
    }

    // POST /api/social/study-rooms — create a study room
    private static async Task<IResult> CreateStudyRoom(
        HttpContext ctx,
        IDocumentStore store,
        CreateStudyRoomRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required" });

        await using var session = store.LightweightSession();

        var roomId = $"room_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        var room = new StudyRoomDocument
        {
            Id = roomId,
            RoomId = roomId,
            Name = request.Name,
            Subject = request.Subject ?? "General",
            HostStudentId = studentId,
            IsPublic = request.IsPublic,
            MaxCapacity = request.MaxCapacity > 0 ? request.MaxCapacity : 10,
            CreatedAt = now
        };

        session.Store(room);

        // Create membership for host
        var membershipId = $"membership_{Guid.NewGuid():N}";
        var membership = new StudyRoomMembershipDocument
        {
            Id = membershipId,
            RoomId = roomId,
            StudentId = studentId,
            JoinedAt = now,
            IsActive = true
        };
        session.Store(membership);

        // Append event
        var roomEvent = new StudyRoomCreated_V1(
            RoomId: roomId,
            Name: request.Name,
            Subject: request.Subject ?? "General",
            HostStudentId: studentId,
            IsPublic: request.IsPublic,
            MaxCapacity: request.MaxCapacity > 0 ? request.MaxCapacity : 10,
            CreatedAt: now);

        session.Events.Append(studentId, roomEvent);
        await session.SaveChangesAsync();

        // Get profile for display name
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var displayName = profile?.DisplayName ?? profile?.FullName ?? "Student";

        return Results.Ok(new StudyRoom(
            RoomId: roomId,
            Name: request.Name,
            Subject: request.Subject ?? "General",
            HostStudentId: studentId,
            HostDisplayName: displayName,
            MemberCount: 1,
            MaxCapacity: request.MaxCapacity > 0 ? request.MaxCapacity : 10,
            IsPublic: request.IsPublic,
            CreatedAt: now));
    }

    // POST /api/social/study-rooms/{id}/join — join a study room
    private static async Task<IResult> JoinStudyRoom(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var room = await session.LoadAsync<StudyRoomDocument>(id);
        if (room == null)
            return Results.NotFound(new { error = "Study room not found" });

        if (!room.IsActive)
            return Results.Conflict(new { error = "Study room is not active" });

        // Check if already a member
        var existingMembership = await session.Query<StudyRoomMembershipDocument>()
            .FirstOrDefaultAsync(m => m.RoomId == id && m.StudentId == studentId && m.IsActive);

        if (existingMembership != null)
            return Results.Ok(new JoinStudyRoomResponse(Ok: true, RoomId: id, MemberCount: room.MaxCapacity));

        // Check capacity
        var currentMembers = await session.Query<StudyRoomMembershipDocument>()
            .CountAsync(m => m.RoomId == id && m.IsActive);

        if (currentMembers >= room.MaxCapacity)
            return Results.Conflict(new { error = "Study room is full" });

        var membershipId = $"membership_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        var membership = new StudyRoomMembershipDocument
        {
            Id = membershipId,
            RoomId = id,
            StudentId = studentId,
            JoinedAt = now,
            IsActive = true
        };

        session.Store(membership);

        // Append event
        var joinEvent = new StudyRoomJoined_V1(
            RoomId: id,
            StudentId: studentId,
            JoinedAt: now);

        session.Events.Append(studentId, joinEvent);
        await session.SaveChangesAsync();

        return Results.Ok(new JoinStudyRoomResponse(
            Ok: true,
            RoomId: id,
            MemberCount: currentMembers + 1));
    }

    // POST /api/social/study-rooms/{id}/leave — leave a study room
    private static async Task<IResult> LeaveStudyRoom(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();

        var membership = await session.Query<StudyRoomMembershipDocument>()
            .FirstOrDefaultAsync(m => m.RoomId == id && m.StudentId == studentId && m.IsActive);

        if (membership == null)
            return Results.Ok(new { ok = true }); // Idempotent: already not in room

        membership.IsActive = false;
        membership.LeftAt = DateTime.UtcNow;
        session.Store(membership);

        // Append event
        var leaveEvent = new StudyRoomLeft_V1(
            RoomId: id,
            StudentId: studentId,
            LeftAt: DateTime.UtcNow);

        session.Events.Append(studentId, leaveEvent);
        await session.SaveChangesAsync();

        return Results.Ok(new { ok = true });
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
