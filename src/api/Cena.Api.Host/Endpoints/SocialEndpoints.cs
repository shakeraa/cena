// =============================================================================
// Cena Platform — Social REST Endpoints (HARDEN: production-grade)
// Class feed, peer solutions, friends, and study rooms (real Marten queries)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Social;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
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

        group.MapGet("/class-feed", GetClassFeed).WithName("GetClassFeed");
        group.MapGet("/peers/solutions", GetPeerSolutions).WithName("GetPeerSolutions");
        group.MapGet("/friends", GetFriends).WithName("GetFriends");
        group.MapGet("/study-rooms", GetStudyRooms).WithName("GetStudyRooms");

        // STB-06b: Write endpoints
        group.MapPost("/reactions", AddReaction).WithName("AddReaction");
        group.MapPost("/comments", AddComment).WithName("AddComment");
        group.MapPost("/friends/request", SendFriendRequest).WithName("SendFriendRequest");
        group.MapPost("/friends/{id}/accept", AcceptFriendRequest).WithName("AcceptFriendRequest");
        group.MapPost("/study-rooms", CreateStudyRoom).WithName("CreateStudyRoom");
        group.MapPost("/study-rooms/{id}/join", JoinStudyRoom).WithName("JoinStudyRoom");
        group.MapPost("/study-rooms/{id}/leave", LeaveStudyRoom).WithName("LeaveStudyRoom");

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
    private static async Task<IResult> GetFriends(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Query friendships from Marten
        var friendships = await session.Query<FriendshipDocument>()
            .Where(f => f.StudentAId == studentId || f.StudentBId == studentId)
            .ToListAsync();

        // Build friend list with profile data
        var friends = new List<Friend>();
        foreach (var f in friendships)
        {
            var friendId = f.StudentAId == studentId ? f.StudentBId : f.StudentAId;
            var friendProfile = await session.LoadAsync<StudentProfileSnapshot>(friendId);
            var level = Math.Max(1, (friendProfile?.TotalXp ?? 0) / 100);
            
            friends.Add(new Friend(
                StudentId: friendId,
                DisplayName: friendProfile?.DisplayName ?? friendProfile?.FullName ?? "Student",
                AvatarUrl: null, // Future: add AvatarUrl to StudentProfileSnapshot
                Level: level,
                StreakDays: 0, // Future: compute from streak projection
                IsOnline: false)); // Future: check presence service
        }

        // Query pending friend requests to this student
        var pendingDocs = await session.Query<FriendRequestDocument>()
            .Where(r => r.ToStudentId == studentId && r.Status == "pending")
            .ToListAsync();

        var pendingRequests = new List<FriendRequest>();
        foreach (var r in pendingDocs)
        {
            var fromProfile = await session.LoadAsync<StudentProfileSnapshot>(r.FromStudentId);
            pendingRequests.Add(new FriendRequest(
                RequestId: r.RequestId,
                FromStudentId: r.FromStudentId,
                FromDisplayName: fromProfile?.DisplayName ?? fromProfile?.FullName ?? "Student",
                FromAvatarUrl: null, // Future: add AvatarUrl to StudentProfileSnapshot
                RequestedAt: r.RequestedAt));
        }

        var dto = new FriendsListDto(
            Friends: friends.ToArray(),
            PendingRequests: pendingRequests.ToArray());

        return Results.Ok(dto);
    }

    // GET /api/social/study-rooms — returns study rooms
    private static async Task<IResult> GetStudyRooms(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();

        // Get rooms that are either public or the student is a member of
        var memberships = await session.Query<StudyRoomMembershipDocument>()
            .Where(m => m.StudentId == studentId && m.IsActive)
            .Select(m => m.RoomId)
            .ToListAsync();

        var rooms = await session.Query<StudyRoomDocument>()
            .Where(r => r.IsActive && (r.IsPublic || memberships.Contains(r.RoomId)))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        // Build room list with member counts
        var roomDtos = new List<StudyRoom>();
        foreach (var r in rooms)
        {
            var memberCount = await session.Query<StudyRoomMembershipDocument>()
                .CountAsync(m => m.RoomId == r.RoomId && m.IsActive);

            var hostProfile = await session.LoadAsync<StudentProfileSnapshot>(r.HostStudentId);

            roomDtos.Add(new StudyRoom(
                RoomId: r.RoomId,
                Name: r.Name,
                Subject: r.Subject,
                HostStudentId: r.HostStudentId,
                HostDisplayName: hostProfile?.DisplayName ?? hostProfile?.FullName ?? "Student",
                MemberCount: memberCount,
                MaxCapacity: r.MaxCapacity,
                IsPublic: r.IsPublic,
                CreatedAt: r.CreatedAt));
        }

        var dto = new StudyRoomListDto(Rooms: roomDtos.ToArray());
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
