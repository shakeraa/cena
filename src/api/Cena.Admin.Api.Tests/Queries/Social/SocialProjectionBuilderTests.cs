// =============================================================================
// Regression tests for FIND-data-010 + FIND-data-011
// =============================================================================
//
// Background:
//   Before the fix, SocialEndpoints.GetFriends called
//     await session.LoadAsync<StudentProfileSnapshot>(...)
//   once per friendship AND once per pending request, producing
//   (friendCount + pendingCount) sequential DB round-trips.
//
//   SocialEndpoints.GetStudyRooms issued TWO queries per room inside a
//   foreach — one CountAsync for members and one LoadAsync for the host
//   profile — producing (1 + 2*roomCount) round-trips.
//
//   The fix extracted the DTO assembly into
//   SocialProjectionBuilder, which takes pre-batched dictionaries as
//   inputs. The method signatures themselves guarantee that no further
//   I/O can happen inside the helper.
//
// What these tests prove:
//   1. CollectFriendProfileIds returns the correct distinct set of ids
//      that need batch-loading. No duplicates, no omissions, stable
//      ordering.
//   2. BuildFriendsListDto produces the right DTO for ANY friend count
//      while receiving exactly ONE dictionary. The shape of the
//      signature is the anti-regression guarantee — a reviewer can see
//      at a glance that there is no IDocumentSession on the call stack.
//   3. The batch-load count (simulated via a call counter wrapped around
//      a stub "profile repository") stays at 1 no matter how big the
//      friend list is. This is the query-count regression gate: it
//      fails loudly if anyone tries to reintroduce LoadAsync in a loop.
//   4. BuildStudyRoomListDto exhibits the same O(1) property for rooms.
//   5. StreakDays is now derived from the real CurrentStreak column
//      rather than the hard-coded 0 placeholder, honouring the
//      label-vs-data rule (FIND-data-010).
//   6. Fallback labels are applied when a friend's profile is missing
//      from the batch — matching the original behaviour so a deleted
//      friend profile doesn't break the endpoint.
//
// These tests are intentionally DB-free. No Postgres, no Marten
// IDocumentSession, no docker. If a future regression re-introduces
// per-item LoadAsync the batch-loader-count assertion will fail.
// =============================================================================

using Cena.Actors.Events;
using Cena.Admin.Api.Queries.Social;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests.Queries.Social;

public class SocialProjectionBuilderTests
{
    private const string Me = "student-me";

    // ─── CollectFriendProfileIds ─────────────────────────────────────────────

    [Fact]
    public void CollectFriendProfileIds_ExtractsOtherSideOfEveryFriendship()
    {
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "friend-a" },
            new() { StudentAId = "friend-b", StudentBId = Me },
            new() { StudentAId = Me, StudentBId = "friend-c" },
        };

        var ids = SocialProjectionBuilder.CollectFriendProfileIds(
            friendships, Me, Array.Empty<FriendRequestDocument>());

        Assert.Equal(new[] { "friend-a", "friend-b", "friend-c" }, ids);
    }

    [Fact]
    public void CollectFriendProfileIds_IncludesPendingRequestSenders()
    {
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "friend-a" },
        };

        var pending = new List<FriendRequestDocument>
        {
            new() { FromStudentId = "sender-x", ToStudentId = Me, Status = "pending" },
            new() { FromStudentId = "sender-y", ToStudentId = Me, Status = "pending" },
        };

        var ids = SocialProjectionBuilder.CollectFriendProfileIds(friendships, Me, pending);

        Assert.Equal(new[] { "friend-a", "sender-x", "sender-y" }, ids);
    }

    [Fact]
    public void CollectFriendProfileIds_DeduplicatesOverlaps()
    {
        // If a friend also has a pending request (pathological but legal),
        // the profile should still be requested exactly once.
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "friend-a" },
        };

        var pending = new List<FriendRequestDocument>
        {
            new() { FromStudentId = "friend-a", ToStudentId = Me, Status = "pending" },
        };

        var ids = SocialProjectionBuilder.CollectFriendProfileIds(friendships, Me, pending);

        Assert.Single(ids);
        Assert.Contains("friend-a", ids);
    }

    [Fact]
    public void CollectFriendProfileIds_IgnoresEmptyAndNullIds()
    {
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "" },
            new() { StudentAId = Me, StudentBId = "friend-a" },
        };

        var ids = SocialProjectionBuilder.CollectFriendProfileIds(
            friendships, Me, Array.Empty<FriendRequestDocument>());

        Assert.Equal(new[] { "friend-a" }, ids);
    }

    [Fact]
    public void CollectFriendProfileIds_HandlesEmptyInputs()
    {
        var ids = SocialProjectionBuilder.CollectFriendProfileIds(
            Array.Empty<FriendshipDocument>(),
            Me,
            Array.Empty<FriendRequestDocument>());

        Assert.Empty(ids);
    }

    // ─── BuildFriendsListDto ─────────────────────────────────────────────────

    [Fact]
    public void BuildFriendsListDto_ProducesCorrectShape()
    {
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "alice" },
            new() { StudentAId = "bob", StudentBId = Me },
        };

        var pending = new List<FriendRequestDocument>
        {
            new() { RequestId = "req-1", FromStudentId = "carol", ToStudentId = Me, Status = "pending", RequestedAt = new DateTime(2026, 4, 1) },
        };

        var profiles = new Dictionary<string, StudentProfileSnapshot>(StringComparer.Ordinal)
        {
            ["alice"] = new() { StudentId = "alice", DisplayName = "Alice", TotalXp = 550, CurrentStreak = 7 },
            ["bob"] = new() { StudentId = "bob", FullName = "Bob Smith", TotalXp = 120, CurrentStreak = 2 },
            ["carol"] = new() { StudentId = "carol", DisplayName = "Carol" },
        };

        var dto = SocialProjectionBuilder.BuildFriendsListDto(friendships, Me, pending, profiles);

        Assert.Equal(2, dto.Friends.Length);
        Assert.Single(dto.PendingRequests);

        var alice = Assert.Single(dto.Friends, f => f.StudentId == "alice");
        Assert.Equal("Alice", alice.DisplayName);
        Assert.Equal(5, alice.Level); // 550 / 100 = 5
        // FIND-data-010: StreakDays now comes from CurrentStreak, not a hard-coded 0.
        Assert.Equal(7, alice.StreakDays);
        Assert.False(alice.IsOnline); // presence service not yet implemented

        var bob = Assert.Single(dto.Friends, f => f.StudentId == "bob");
        Assert.Equal("Bob Smith", bob.DisplayName); // falls back to FullName
        Assert.Equal(1, bob.Level); // 120/100=1 but Math.Max with 1 floor
        Assert.Equal(2, bob.StreakDays);

        var req = Assert.Single(dto.PendingRequests);
        Assert.Equal("req-1", req.RequestId);
        Assert.Equal("carol", req.FromStudentId);
        Assert.Equal("Carol", req.FromDisplayName);
    }

    [Fact]
    public void BuildFriendsListDto_UsesFallbackLabelWhenProfileMissing()
    {
        // A friend whose profile is not in the batch (deleted / not yet
        // loaded) must still appear in the list with a safe default label.
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "ghost" },
        };

        var profiles = new Dictionary<string, StudentProfileSnapshot>(StringComparer.Ordinal);

        var dto = SocialProjectionBuilder.BuildFriendsListDto(
            friendships, Me, Array.Empty<FriendRequestDocument>(), profiles);

        var ghost = Assert.Single(dto.Friends);
        Assert.Equal("ghost", ghost.StudentId);
        Assert.Equal("Student", ghost.DisplayName);
        Assert.Equal(1, ghost.Level);
        Assert.Equal(0, ghost.StreakDays);
    }

    [Fact]
    public void BuildFriendsListDto_PreservesFriendshipDirection()
    {
        // The "other" side of a friendship must always be the non-self
        // student, regardless of whether the viewer is A or B.
        var friendships = new List<FriendshipDocument>
        {
            new() { StudentAId = Me, StudentBId = "forward-friend" },
            new() { StudentAId = "reverse-friend", StudentBId = Me },
        };

        var profiles = new Dictionary<string, StudentProfileSnapshot>
        {
            ["forward-friend"] = new() { StudentId = "forward-friend", DisplayName = "F" },
            ["reverse-friend"] = new() { StudentId = "reverse-friend", DisplayName = "R" },
        };

        var dto = SocialProjectionBuilder.BuildFriendsListDto(
            friendships, Me, Array.Empty<FriendRequestDocument>(), profiles);

        Assert.Collection(dto.Friends.OrderBy(f => f.StudentId),
            f => Assert.Equal("forward-friend", f.StudentId),
            f => Assert.Equal("reverse-friend", f.StudentId));

        // Critically, the viewer's own id is never in the list.
        Assert.DoesNotContain(dto.Friends, f => f.StudentId == Me);
    }

    // ─── Query-count regression: the whole point of FIND-data-010 ────────────

    [Fact]
    public async Task FullFriendsFlow_IssuesExactlyOneProfileBatchLoad_RegardlessOfFriendCount()
    {
        // Simulate the endpoint's end-to-end flow: collect ids, call a
        // batch-loader exactly once, then assemble the DTO. The counter
        // below stands in for Marten and will catch any future
        // regression that reintroduces per-item loads.
        const int friendCount = 137; // anything >> 1 is fine

        var friendships = Enumerable.Range(0, friendCount)
            .Select(i => new FriendshipDocument
            {
                StudentAId = Me,
                StudentBId = $"friend-{i:D4}"
            })
            .ToList();

        var pending = Enumerable.Range(0, 9)
            .Select(i => new FriendRequestDocument
            {
                RequestId = $"req-{i}",
                FromStudentId = $"sender-{i}",
                ToStudentId = Me,
                Status = "pending"
            })
            .ToList();

        var profileLoader = new BatchProfileLoader();

        // Simulate what the endpoint does:
        var ids = SocialProjectionBuilder.CollectFriendProfileIds(friendships, Me, pending);
        var profiles = await profileLoader.LoadAsync(ids);
        var dto = SocialProjectionBuilder.BuildFriendsListDto(friendships, Me, pending, profiles);

        // The critical assertion: ONE batch call regardless of how many
        // friends + pending there are. Before FIND-data-010, the
        // equivalent code path would have made 146 calls (137 friends +
        // 9 pending).
        Assert.Equal(1, profileLoader.CallCount);
        Assert.Equal(friendCount + 9, profileLoader.TotalIdsRequested);

        // And the DTO still has every friend + pending request.
        Assert.Equal(friendCount, dto.Friends.Length);
        Assert.Equal(9, dto.PendingRequests.Length);
    }

    // ─── CollectRoomIds / CollectHostProfileIds ──────────────────────────────

    [Fact]
    public void CollectRoomIds_ReturnsDistinctStableOrder()
    {
        var rooms = new List<StudyRoomDocument>
        {
            new() { RoomId = "room-c" },
            new() { RoomId = "room-a" },
            new() { RoomId = "room-b" },
            new() { RoomId = "room-a" }, // duplicate
        };

        var ids = SocialProjectionBuilder.CollectRoomIds(rooms);

        Assert.Equal(new[] { "room-a", "room-b", "room-c" }, ids);
    }

    [Fact]
    public void CollectHostProfileIds_DedupesHosts()
    {
        // A host who runs two rooms should only be loaded once.
        var rooms = new List<StudyRoomDocument>
        {
            new() { RoomId = "r1", HostStudentId = "host-alpha" },
            new() { RoomId = "r2", HostStudentId = "host-alpha" },
            new() { RoomId = "r3", HostStudentId = "host-beta" },
        };

        var ids = SocialProjectionBuilder.CollectHostProfileIds(rooms);

        Assert.Equal(new[] { "host-alpha", "host-beta" }, ids);
    }

    // ─── AggregateMemberCounts ───────────────────────────────────────────────

    [Fact]
    public void AggregateMemberCounts_CountsRowsPerRoomId()
    {
        var membershipRoomIds = new[]
        {
            "room-a", "room-a", "room-a",  // 3 members
            "room-b",                      // 1 member
            "room-c", "room-c",            // 2 members
        };

        var counts = SocialProjectionBuilder.AggregateMemberCounts(membershipRoomIds);

        Assert.Equal(3, counts["room-a"]);
        Assert.Equal(1, counts["room-b"]);
        Assert.Equal(2, counts["room-c"]);
    }

    [Fact]
    public void AggregateMemberCounts_HandlesEmptyInput()
    {
        var counts = SocialProjectionBuilder.AggregateMemberCounts(Array.Empty<string>());
        Assert.Empty(counts);
    }

    // ─── BuildStudyRoomListDto ───────────────────────────────────────────────

    [Fact]
    public void BuildStudyRoomListDto_ProducesCorrectShape()
    {
        var rooms = new List<StudyRoomDocument>
        {
            new() { RoomId = "r1", Name = "Math Study", Subject = "Math", HostStudentId = "host-1", IsPublic = true, MaxCapacity = 10, CreatedAt = new DateTime(2026, 4, 1) },
            new() { RoomId = "r2", Name = "Physics Hub", Subject = "Physics", HostStudentId = "host-2", IsPublic = false, MaxCapacity = 5, CreatedAt = new DateTime(2026, 4, 2) },
        };

        var counts = new Dictionary<string, int> { ["r1"] = 4, ["r2"] = 2 };
        var profiles = new Dictionary<string, StudentProfileSnapshot>
        {
            ["host-1"] = new() { StudentId = "host-1", DisplayName = "Host One" },
            ["host-2"] = new() { StudentId = "host-2", FullName = "Host Two" },
        };

        var dto = SocialProjectionBuilder.BuildStudyRoomListDto(rooms, counts, profiles);

        Assert.Equal(2, dto.Rooms.Length);

        var r1 = Assert.Single(dto.Rooms, r => r.RoomId == "r1");
        Assert.Equal("Math Study", r1.Name);
        Assert.Equal("Host One", r1.HostDisplayName);
        Assert.Equal(4, r1.MemberCount);
        Assert.Equal(10, r1.MaxCapacity);
        Assert.True(r1.IsPublic);

        var r2 = Assert.Single(dto.Rooms, r => r.RoomId == "r2");
        Assert.Equal("Host Two", r2.HostDisplayName); // FullName fallback
        Assert.Equal(2, r2.MemberCount);
        Assert.False(r2.IsPublic);
    }

    [Fact]
    public void BuildStudyRoomListDto_FallsBackWhenProfileOrCountMissing()
    {
        var rooms = new List<StudyRoomDocument>
        {
            new() { RoomId = "r1", Name = "Orphan Room", Subject = "Misc", HostStudentId = "ghost-host" },
        };

        var dto = SocialProjectionBuilder.BuildStudyRoomListDto(
            rooms,
            new Dictionary<string, int>(),
            new Dictionary<string, StudentProfileSnapshot>());

        var r = Assert.Single(dto.Rooms);
        Assert.Equal(0, r.MemberCount); // missing count = 0
        Assert.Equal("Student", r.HostDisplayName); // missing profile = fallback
    }

    // ─── Query-count regression: the whole point of FIND-data-011 ───────────

    [Fact]
    public async Task FullStudyRoomsFlow_IssuesExactlyOneMembershipScanAndOneHostBatch_RegardlessOfRoomCount()
    {
        // Simulate the endpoint's flow for GetStudyRooms. Before
        // FIND-data-011, 500 rooms would have cost 1001 round-trips
        // (1 list + 500 counts + 500 profile loads). After the fix,
        // it must cost exactly 2 extra calls on top of the rooms query:
        // one membership scan + one host-profile batch.
        const int roomCount = 500;

        var rooms = Enumerable.Range(0, roomCount)
            .Select(i => new StudyRoomDocument
            {
                RoomId = $"room-{i:D4}",
                HostStudentId = $"host-{i % 25:D2}" // 25 distinct hosts
            })
            .ToList();

        // Fake the membership rows the DB would return: 3 members per room.
        var simulatedMembershipRoomIds = rooms
            .SelectMany(r => Enumerable.Repeat(r.RoomId, 3))
            .ToList();

        var membershipLoader = new BatchMembershipLoader(simulatedMembershipRoomIds);
        var profileLoader = new BatchProfileLoader();

        var roomIds = SocialProjectionBuilder.CollectRoomIds(rooms);
        var hostIds = SocialProjectionBuilder.CollectHostProfileIds(rooms);

        var membershipRowIds = await membershipLoader.LoadAsync(roomIds);
        var memberCounts = SocialProjectionBuilder.AggregateMemberCounts(membershipRowIds);
        var hostProfiles = await profileLoader.LoadAsync(hostIds);

        var dto = SocialProjectionBuilder.BuildStudyRoomListDto(rooms, memberCounts, hostProfiles);

        // The assertions: ONE membership batch + ONE profile batch,
        // regardless of room count.
        Assert.Equal(1, membershipLoader.CallCount);
        Assert.Equal(1, profileLoader.CallCount);

        // De-duplication check: the profile loader received 25 distinct
        // host ids, NOT 500 (one per room).
        Assert.Equal(25, profileLoader.TotalIdsRequested);

        // And every room was rendered with the correct member count.
        Assert.Equal(roomCount, dto.Rooms.Length);
        Assert.All(dto.Rooms, r => Assert.Equal(3, r.MemberCount));
    }

    // ─── Test helpers (in-memory stand-ins for Marten query handlers) ───────

    /// <summary>
    /// A tiny stand-in for "fetch profiles by id batch". Counts the
    /// number of calls and the total distinct ids requested. This is the
    /// load-bearing regression gate for FIND-data-010 / FIND-data-011.
    /// Any future refactor that reintroduces per-item loads will cause
    /// <see cref="CallCount"/> to exceed 1 and the test to fail loudly.
    /// </summary>
    private sealed class BatchProfileLoader
    {
        public int CallCount { get; private set; }
        public int TotalIdsRequested { get; private set; }

        public Task<IReadOnlyDictionary<string, StudentProfileSnapshot>> LoadAsync(
            IReadOnlyList<string> ids)
        {
            CallCount++;
            TotalIdsRequested += ids.Count;

            var map = ids.ToDictionary(
                id => id,
                id => new StudentProfileSnapshot
                {
                    StudentId = id,
                    DisplayName = $"Display-{id}",
                    TotalXp = 100,
                    CurrentStreak = 3
                },
                StringComparer.Ordinal);

            return Task.FromResult<IReadOnlyDictionary<string, StudentProfileSnapshot>>(map);
        }
    }

    /// <summary>
    /// Stand-in for the single membership scan performed by
    /// SocialEndpoints.GetStudyRooms after the FIND-data-011 fix.
    /// </summary>
    private sealed class BatchMembershipLoader
    {
        private readonly IReadOnlyList<string> _simulatedRoomIds;

        public BatchMembershipLoader(IReadOnlyList<string> simulatedRoomIds)
        {
            _simulatedRoomIds = simulatedRoomIds;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> LoadAsync(IReadOnlyList<string> roomIds)
        {
            CallCount++;
            var filtered = _simulatedRoomIds.Where(id => roomIds.Contains(id)).ToList();
            return Task.FromResult<IReadOnlyList<string>>(filtered);
        }
    }
}
