// =============================================================================
// Cena Platform -- Social REST Endpoints (STB-06 Phase 1)
// Class feed, peer solutions, friends, and study rooms (stub data)
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

        // Phase 1: Return 10 hardcoded items per page, max 2 pages
        var currentPage = Math.Max(1, page ?? 1);
        const int pageSize = 10;
        const int maxPages = 2;

        var allItems = new[]
        {
            new ClassFeedItem(
                ItemId: "feed_001",
                Kind: "achievement",
                AuthorStudentId: "student_001",
                AuthorDisplayName: "Alex",
                AuthorAvatarUrl: null,
                Title: "Earned 'Week Streak' Badge",
                Body: "Maintained a 7-day learning streak!",
                PostedAt: DateTime.UtcNow.AddHours(-2),
                ReactionCount: 12,
                CommentCount: 3),
            new ClassFeedItem(
                ItemId: "feed_002",
                Kind: "milestone",
                AuthorStudentId: "student_002",
                AuthorDisplayName: "Jordan",
                AuthorAvatarUrl: null,
                Title: "Completed 100 Questions",
                Body: "Just hit 100 questions answered correctly!",
                PostedAt: DateTime.UtcNow.AddHours(-4),
                ReactionCount: 8,
                CommentCount: 1),
            new ClassFeedItem(
                ItemId: "feed_003",
                Kind: "question",
                AuthorStudentId: "student_003",
                AuthorDisplayName: "Taylor",
                AuthorAvatarUrl: null,
                Title: "Help with Quadratic Equations",
                Body: "Can someone explain factoring when a ≠ 1?",
                PostedAt: DateTime.UtcNow.AddHours(-5),
                ReactionCount: 5,
                CommentCount: 7),
            new ClassFeedItem(
                ItemId: "feed_004",
                Kind: "announcement",
                AuthorStudentId: "teacher_001",
                AuthorDisplayName: "Ms. Johnson",
                AuthorAvatarUrl: null,
                Title: "Quiz Tomorrow",
                Body: "Remember: Chapter 5 quiz tomorrow during class!",
                PostedAt: DateTime.UtcNow.AddHours(-6),
                ReactionCount: 24,
                CommentCount: 5),
            new ClassFeedItem(
                ItemId: "feed_005",
                Kind: "achievement",
                AuthorStudentId: "student_004",
                AuthorDisplayName: "Morgan",
                AuthorAvatarUrl: null,
                Title: "Mastered Algebra",
                Body: "Completed all Algebra Fundamentals cards!",
                PostedAt: DateTime.UtcNow.AddHours(-8),
                ReactionCount: 15,
                CommentCount: 4),
            new ClassFeedItem(
                ItemId: "feed_006",
                Kind: "milestone",
                AuthorStudentId: studentId,
                AuthorDisplayName: "You",
                AuthorAvatarUrl: null,
                Title: "Reached Level 10",
                Body: "Congratulations on reaching Level 10!",
                PostedAt: DateTime.UtcNow.AddHours(-10),
                ReactionCount: 20,
                CommentCount: 6),
            new ClassFeedItem(
                ItemId: "feed_007",
                Kind: "question",
                AuthorStudentId: "student_005",
                AuthorDisplayName: "Casey",
                AuthorAvatarUrl: null,
                Title: "Physics Problem Help",
                Body: "Stuck on conservation of momentum problem #12",
                PostedAt: DateTime.UtcNow.AddHours(-12),
                ReactionCount: 3,
                CommentCount: 4),
            new ClassFeedItem(
                ItemId: "feed_008",
                Kind: "achievement",
                AuthorStudentId: "student_006",
                AuthorDisplayName: "Riley",
                AuthorAvatarUrl: null,
                Title: "Speed Demon Unlocked",
                Body: "Completed a session in under 10 minutes!",
                PostedAt: DateTime.UtcNow.AddHours(-14),
                ReactionCount: 9,
                CommentCount: 2),
            new ClassFeedItem(
                ItemId: "feed_009",
                Kind: "announcement",
                AuthorStudentId: "teacher_002",
                AuthorDisplayName: "Mr. Chen",
                AuthorAvatarUrl: null,
                Title: "Extra Credit Opportunity",
                Body: "Submit a solution to the bonus problem for +5 points!",
                PostedAt: DateTime.UtcNow.AddHours(-16),
                ReactionCount: 31,
                CommentCount: 12),
            new ClassFeedItem(
                ItemId: "feed_010",
                Kind: "milestone",
                AuthorStudentId: "student_007",
                AuthorDisplayName: "Quinn",
                AuthorAvatarUrl: null,
                Title: "30-Day Streak!",
                Body: "One month of continuous learning!",
                PostedAt: DateTime.UtcNow.AddHours(-18),
                ReactionCount: 18,
                CommentCount: 8)
        };

        // Page 2 items (for demonstration)
        var page2Items = new[]
        {
            new ClassFeedItem(
                ItemId: "feed_011",
                Kind: "question",
                AuthorStudentId: "student_008",
                AuthorDisplayName: "Avery",
                AuthorAvatarUrl: null,
                Title: "Geometry Help Needed",
                Body: "How do you find the area of a sector?",
                PostedAt: DateTime.UtcNow.AddHours(-20),
                ReactionCount: 4,
                CommentCount: 3),
            new ClassFeedItem(
                ItemId: "feed_012",
                Kind: "achievement",
                AuthorStudentId: "student_009",
                AuthorDisplayName: "Sam",
                AuthorAvatarUrl: null,
                Title: "Quiz Master Badge",
                Body: "Answered 50 questions correctly!",
                PostedAt: DateTime.UtcNow.AddHours(-22),
                ReactionCount: 7,
                CommentCount: 1)
        };

        ClassFeedItem[] items;
        bool hasMore;

        if (currentPage == 1)
        {
            items = allItems;
            hasMore = true;
        }
        else if (currentPage == 2)
        {
            items = page2Items;
            hasMore = false;
        }
        else
        {
            items = Array.Empty<ClassFeedItem>();
            hasMore = false;
        }

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

        // Phase 1: Return 5 hardcoded stub solutions
        var solutions = new[]
        {
            new PeerSolution(
                SolutionId: "sol_001",
                QuestionId: questionId ?? "q1",
                AuthorStudentId: "student_001",
                AuthorDisplayName: "Alex",
                Content: "To solve this, first identify the variables. Then apply the quadratic formula: x = (-b ± √(b²-4ac)) / 2a. In this case, a=2, b=-4, c=-6.",
                UpvoteCount: 24,
                DownvoteCount: 1,
                PostedAt: DateTime.UtcNow.AddDays(-2)),
            new PeerSolution(
                SolutionId: "sol_002",
                QuestionId: questionId ?? "q1",
                AuthorStudentId: "student_002",
                AuthorDisplayName: "Jordan",
                Content: "I approached this by factoring first. Look for two numbers that multiply to -12 and add to -2. Those numbers are -6 and 2.",
                UpvoteCount: 18,
                DownvoteCount: 0,
                PostedAt: DateTime.UtcNow.AddDays(-3)),
            new PeerSolution(
                SolutionId: "sol_003",
                QuestionId: questionId ?? "q1",
                AuthorStudentId: "student_003",
                AuthorDisplayName: "Taylor",
                Content: "Don't forget to check your answer by substituting back into the original equation! x = 3 and x = -1 both work.",
                UpvoteCount: 15,
                DownvoteCount: 2,
                PostedAt: DateTime.UtcNow.AddDays(-4)),
            new PeerSolution(
                SolutionId: "sol_004",
                QuestionId: questionId ?? "q1",
                AuthorStudentId: studentId,
                AuthorDisplayName: "You",
                Content: "My attempt: I tried completing the square but got stuck after rearranging to x² - 2x = 3.",
                UpvoteCount: 3,
                DownvoteCount: 0,
                PostedAt: DateTime.UtcNow.AddDays(-1)),
            new PeerSolution(
                SolutionId: "sol_005",
                QuestionId: questionId ?? "q1",
                AuthorStudentId: "student_004",
                AuthorDisplayName: "Morgan",
                Content: "Alternative method: Graph both sides as separate functions and find intersection points. y = 2x²-4x-6 and y = 0.",
                UpvoteCount: 8,
                DownvoteCount: 1,
                PostedAt: DateTime.UtcNow.AddDays(-5))
        };

        var dto = new PeerSolutionListDto(Solutions: solutions);
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
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var myLevel = Math.Max(1, (profile?.TotalXp ?? 0) / 100);

        // Phase 1: Return 4 friends, 2 pending
        var friends = new[]
        {
            new Friend(
                StudentId: "student_001",
                DisplayName: "Alex",
                AvatarUrl: null,
                Level: 12,
                StreakDays: 15,
                IsOnline: true),
            new Friend(
                StudentId: "student_002",
                DisplayName: "Jordan",
                AvatarUrl: null,
                Level: 8,
                StreakDays: 7,
                IsOnline: false),
            new Friend(
                StudentId: "student_003",
                DisplayName: "Taylor",
                AvatarUrl: null,
                Level: 15,
                StreakDays: 30,
                IsOnline: true),
            new Friend(
                StudentId: "student_004",
                DisplayName: "Morgan",
                AvatarUrl: null,
                Level: 6,
                StreakDays: 3,
                IsOnline: false)
        };

        var pendingRequests = new[]
        {
            new FriendRequest(
                RequestId: "req_001",
                FromStudentId: "student_009",
                FromDisplayName: "Casey",
                FromAvatarUrl: null,
                RequestedAt: DateTime.UtcNow.AddDays(-2)),
            new FriendRequest(
                RequestId: "req_002",
                FromStudentId: "student_010",
                FromDisplayName: "Riley",
                FromAvatarUrl: null,
                RequestedAt: DateTime.UtcNow.AddDays(-1))
        };

        var dto = new FriendsListDto(
            Friends: friends,
            PendingRequests: pendingRequests);

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

        // Phase 1: Return 3 open rooms
        var rooms = new[]
        {
            new StudyRoom(
                RoomId: "room_001",
                Name: "Algebra Study Group",
                Subject: "Mathematics",
                HostStudentId: "student_001",
                HostDisplayName: "Alex",
                MemberCount: 4,
                MaxCapacity: 8,
                IsPublic: true,
                CreatedAt: DateTime.UtcNow.AddHours(-2)),
            new StudyRoom(
                RoomId: "room_002",
                Name: "Physics Problem Solving",
                Subject: "Physics",
                HostStudentId: "student_003",
                HostDisplayName: "Taylor",
                MemberCount: 6,
                MaxCapacity: 10,
                IsPublic: true,
                CreatedAt: DateTime.UtcNow.AddHours(-4)),
            new StudyRoom(
                RoomId: "room_003",
                Name: "Chemistry Review Session",
                Subject: "Chemistry",
                HostStudentId: "student_005",
                HostDisplayName: "Casey",
                MemberCount: 2,
                MaxCapacity: 6,
                IsPublic: false,
                CreatedAt: DateTime.UtcNow.AddHours(-1))
        };

        var dto = new StudyRoomListDto(Rooms: rooms);
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
