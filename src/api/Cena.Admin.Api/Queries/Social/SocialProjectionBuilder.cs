// =============================================================================
// Cena Platform — Social Projection Builder (FIND-data-010 / FIND-data-011)
// =============================================================================
//
// Pure, side-effect-free helpers that assemble Social API DTOs from
// pre-loaded Marten document collections + batch-loaded profile / count
// dictionaries.
//
// Why this lives here (and not inline in the endpoint file):
//   - The old GetFriends / GetStudyRooms handlers issued O(N) Marten
//     LoadAsync calls inside a foreach, producing N+1 query storms.
//   - The fix requires the endpoint to batch-load profiles, member counts
//     and host profiles into dictionaries BEFORE assembling the DTO.
//   - Once the endpoint has dictionaries, the assembly is pure and
//     deterministic — no I/O. That's exactly what a unit test needs.
//   - By expressing the assembly as a static method that takes the
//     dictionaries as inputs, the compiler itself guarantees zero extra
//     queries: there is no IDocumentSession on the method signature.
//     The query-count regression is enforced by the type system.
//
// Callers:
//   - src/api/Cena.Api.Host/Endpoints/SocialEndpoints.cs
//   - src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
//
// Tests:
//   - src/api/Cena.Admin.Api.Tests/Queries/Social/SocialProjectionBuilderTests.cs
//
// References:
//   - docs/reviews/agent-3-data-findings.md (FIND-data-010, FIND-data-011)
// =============================================================================

using Cena.Actors.Events;
using Cena.Api.Contracts.Social;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Queries.Social;

/// <summary>
/// Pure helpers for assembling Social DTOs from pre-loaded documents and
/// batch-fetched lookup dictionaries. No I/O, no Marten session, no side
/// effects — the method signatures themselves guarantee that the N+1
/// query pattern cannot re-appear.
/// </summary>
public static class SocialProjectionBuilder
{
    /// <summary>
    /// Collects the distinct set of student IDs that need profile lookups
    /// in order to render a friends list + pending-requests response.
    /// Callers pass this set to a SINGLE batch query
    /// (<c>session.Query&lt;StudentProfileSnapshot&gt;().Where(p =&gt; ids.Contains(p.Id))</c>)
    /// and then feed the resulting dictionary to
    /// <see cref="BuildFriendsListDto"/>.
    /// </summary>
    /// <param name="friendships">Friendships the current student is part of.</param>
    /// <param name="currentStudentId">The viewer's student id (not included in the output).</param>
    /// <param name="pendingRequests">Incoming pending requests addressed to the current student.</param>
    /// <returns>A distinct, stable-ordered list of student ids to batch-load.</returns>
    public static IReadOnlyList<string> CollectFriendProfileIds(
        IReadOnlyList<FriendshipDocument> friendships,
        string currentStudentId,
        IReadOnlyList<FriendRequestDocument> pendingRequests)
    {
        ArgumentNullException.ThrowIfNull(friendships);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStudentId);

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in friendships)
        {
            // The "other" side of the friendship is the profile we need to render.
            var otherId = f.StudentAId == currentStudentId ? f.StudentBId : f.StudentAId;
            if (!string.IsNullOrEmpty(otherId))
                ids.Add(otherId);
        }

        foreach (var r in pendingRequests)
        {
            if (!string.IsNullOrEmpty(r.FromStudentId))
                ids.Add(r.FromStudentId);
        }

        // Stable ordering helps deterministic tests and cache-friendly SQL.
        return ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Assembles the full friends-list DTO from pre-loaded documents and a
    /// pre-batched profile lookup dictionary.
    /// </summary>
    /// <param name="friendships">Friendships the current student is part of.</param>
    /// <param name="currentStudentId">The viewer's student id.</param>
    /// <param name="pendingRequests">Incoming pending requests addressed to the current student.</param>
    /// <param name="profilesByStudentId">
    /// Dictionary of student id → profile snapshot. Missing ids are tolerated
    /// (the resulting DTO falls back to generic labels); this matches the
    /// original behaviour so that a student with a deleted friend profile
    /// still sees the friendship.
    /// </param>
    public static FriendsListDto BuildFriendsListDto(
        IReadOnlyList<FriendshipDocument> friendships,
        string currentStudentId,
        IReadOnlyList<FriendRequestDocument> pendingRequests,
        IReadOnlyDictionary<string, StudentProfileSnapshot> profilesByStudentId)
    {
        ArgumentNullException.ThrowIfNull(friendships);
        ArgumentNullException.ThrowIfNull(pendingRequests);
        ArgumentNullException.ThrowIfNull(profilesByStudentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStudentId);

        var friends = new List<Friend>(friendships.Count);
        foreach (var f in friendships)
        {
            var friendId = f.StudentAId == currentStudentId ? f.StudentBId : f.StudentAId;
            if (string.IsNullOrEmpty(friendId)) continue;

            profilesByStudentId.TryGetValue(friendId, out var profile);

            friends.Add(new Friend(
                StudentId: friendId,
                DisplayName: profile?.DisplayName ?? profile?.FullName ?? "Student",
                AvatarUrl: null, // Future: add AvatarUrl to StudentProfileSnapshot
                Level: CalculateLevel(profile?.TotalXp ?? 0),
                // FIND-data-010: StreakDays used to be hard-coded to 0. Use the
                // real streak value from the snapshot when we have one.
                StreakDays: profile?.CurrentStreak ?? 0,
                // IsOnline: no presence service yet. Explicitly false until a
                // real presence projection lands. See STB-07/STB-08 backlog.
                IsOnline: false));
        }

        var pending = new List<FriendRequest>(pendingRequests.Count);
        foreach (var r in pendingRequests)
        {
            profilesByStudentId.TryGetValue(r.FromStudentId, out var fromProfile);

            pending.Add(new FriendRequest(
                RequestId: r.RequestId,
                FromStudentId: r.FromStudentId,
                FromDisplayName: fromProfile?.DisplayName ?? fromProfile?.FullName ?? "Student",
                FromAvatarUrl: null,
                RequestedAt: r.RequestedAt));
        }

        return new FriendsListDto(
            Friends: friends.ToArray(),
            PendingRequests: pending.ToArray());
    }

    /// <summary>
    /// Collects the distinct set of host-student ids that need profile
    /// lookups for the study-rooms list.
    /// </summary>
    public static IReadOnlyList<string> CollectHostProfileIds(
        IReadOnlyList<StudyRoomDocument> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rooms)
        {
            if (!string.IsNullOrEmpty(r.HostStudentId))
                ids.Add(r.HostStudentId);
        }

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Collects the distinct set of room ids that need member-count lookups.
    /// </summary>
    public static IReadOnlyList<string> CollectRoomIds(
        IReadOnlyList<StudyRoomDocument> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rooms)
        {
            if (!string.IsNullOrEmpty(r.RoomId))
                ids.Add(r.RoomId);
        }

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Assembles the study-rooms list DTO from pre-loaded rooms plus
    /// pre-batched member-count and host-profile dictionaries.
    /// </summary>
    /// <param name="rooms">Rooms the caller is allowed to see.</param>
    /// <param name="memberCountsByRoomId">Room id → active member count. Missing = 0.</param>
    /// <param name="hostProfilesByStudentId">Student id → snapshot for room hosts. Missing = "Student".</param>
    public static StudyRoomListDto BuildStudyRoomListDto(
        IReadOnlyList<StudyRoomDocument> rooms,
        IReadOnlyDictionary<string, int> memberCountsByRoomId,
        IReadOnlyDictionary<string, StudentProfileSnapshot> hostProfilesByStudentId)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(memberCountsByRoomId);
        ArgumentNullException.ThrowIfNull(hostProfilesByStudentId);

        var roomDtos = new List<StudyRoom>(rooms.Count);
        foreach (var r in rooms)
        {
            memberCountsByRoomId.TryGetValue(r.RoomId, out var memberCount);
            hostProfilesByStudentId.TryGetValue(r.HostStudentId, out var hostProfile);

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

        return new StudyRoomListDto(Rooms: roomDtos.ToArray());
    }

    /// <summary>
    /// Builds the <c>roomId → active-member-count</c> dictionary from a
    /// single projected list of membership rows. The caller is expected to
    /// have fetched ALL active membership rows for the given room ids in
    /// ONE query, then invoked this helper to aggregate — so the total
    /// query count stays at 1 regardless of room count.
    /// </summary>
    public static IReadOnlyDictionary<string, int> AggregateMemberCounts(
        IEnumerable<string> membershipRoomIds)
    {
        ArgumentNullException.ThrowIfNull(membershipRoomIds);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var roomId in membershipRoomIds)
        {
            if (string.IsNullOrEmpty(roomId)) continue;
            counts[roomId] = counts.TryGetValue(roomId, out var c) ? c + 1 : 1;
        }
        return counts;
    }

    private static int CalculateLevel(int totalXp) => Math.Max(1, totalXp / 100);
}
