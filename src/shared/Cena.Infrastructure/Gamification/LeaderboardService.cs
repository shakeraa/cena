// =============================================================================
// Cena Platform — Leaderboard Service (STB-03c)
// Real-time leaderboard aggregation for global/class/friends scopes
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Gamification;

/// <summary>
/// Service for computing and querying leaderboards.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Get global leaderboard (top students across platform).
    /// </summary>
    Task<LeaderboardView> GetGlobalLeaderboardAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Get class leaderboard for a specific class.
    /// </summary>
    Task<LeaderboardView> GetClassLeaderboardAsync(
        string classId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Get friends leaderboard for a student.
    /// </summary>
    Task<LeaderboardView> GetFriendsLeaderboardAsync(
        string studentId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Get student's rank across all scopes.
    /// </summary>
    Task<StudentRanks> GetStudentRanksAsync(
        string studentId,
        CancellationToken ct = default);

    /// <summary>
    /// Update leaderboard entries (called after XP/stats changes).
    /// </summary>
    Task UpdateLeaderboardAsync(
        string studentId,
        int xpDelta,
        int? sessionCountDelta = null,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation using Marten for persistence.
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<LeaderboardService> _logger;

    public LeaderboardService(
        IDocumentStore store,
        ILogger<LeaderboardService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<LeaderboardView> GetGlobalLeaderboardAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Query using raw SQL to avoid dependency on StudentProfileSnapshot type
        // StudentProfileSnapshot has Id = StudentId
        var sql = $@"
            SELECT data->>'StudentId' as StudentId,
                   data->>'DisplayName' as DisplayName,
                   COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   COALESCE((data->>'CurrentStreak')::int, 0) as CurrentStreak,
                   data->>'FullName' as FullName
            FROM cena.mt_doc_studentprofilesnapshot
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT {limit}";

        var entries = new List<LeaderboardEntry>();
        try
        {
            await using var cmd = session.Connection?.CreateCommand();
            if (cmd != null)
            {
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(new LeaderboardEntry(
                        StudentId: reader.GetString(0),
                        DisplayName: reader.GetString(1) ?? reader.GetString(4) ?? "Anonymous",
                        AvatarUrl: null,
                        TotalXp: reader.GetInt32(2),
                        Level: ComputeLevel(reader.GetInt32(2)),
                        CurrentStreak: reader.GetInt32(3),
                        WeeklyXp: 0));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying global leaderboard");
        }

        return new LeaderboardView(
            Scope: "global",
            ScopeId: null,
            Entries: entries,
            TotalCount: entries.Count,
            GeneratedAt: DateTime.UtcNow);
    }

    public async Task<LeaderboardView> GetClassLeaderboardAsync(
        string classId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Get classroom to find school
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.Id == classId, ct);

        if (classroom == null || string.IsNullOrEmpty(classroom.SchoolId))
        {
            return new LeaderboardView(
                Scope: "class",
                ScopeId: classId,
                Entries: new List<LeaderboardEntry>(),
                TotalCount: 0,
                GeneratedAt: DateTime.UtcNow);
        }

        // Query students from same school
        var sql = $@"
            SELECT data->>'StudentId' as StudentId,
                   data->>'DisplayName' as DisplayName,
                   COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   COALESCE((data->>'CurrentStreak')::int, 0) as CurrentStreak,
                   data->>'FullName' as FullName
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'SchoolId' = '{classroom.SchoolId}'
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT {limit}";

        var entries = new List<LeaderboardEntry>();
        try
        {
            await using var cmd = session.Connection?.CreateCommand();
            if (cmd != null)
            {
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(new LeaderboardEntry(
                        StudentId: reader.GetString(0),
                        DisplayName: reader.GetString(1) ?? reader.GetString(4) ?? "Anonymous",
                        AvatarUrl: null,
                        TotalXp: reader.GetInt32(2),
                        Level: ComputeLevel(reader.GetInt32(2)),
                        CurrentStreak: reader.GetInt32(3),
                        WeeklyXp: 0));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying class leaderboard");
        }

        return new LeaderboardView(
            Scope: "class",
            ScopeId: classId,
            Entries: entries,
            TotalCount: entries.Count,
            GeneratedAt: DateTime.UtcNow);
    }

    public async Task<LeaderboardView> GetFriendsLeaderboardAsync(
        string studentId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Get friend IDs
        var friendIds = await GetFriendIdsAsync(session, studentId, ct);
        friendIds.Add(studentId); // Include self

        if (friendIds.Count == 0)
        {
            return new LeaderboardView(
                Scope: "friends",
                ScopeId: studentId,
                Entries: new List<LeaderboardEntry>(),
                TotalCount: 0,
                GeneratedAt: DateTime.UtcNow);
        }

        // Build IN clause
        var idList = string.Join("','", friendIds);
        var sql = $@"
            SELECT data->>'StudentId' as StudentId,
                   data->>'DisplayName' as DisplayName,
                   COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   COALESCE((data->>'CurrentStreak')::int, 0) as CurrentStreak,
                   data->>'FullName' as FullName
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'StudentId' IN ('{idList}')
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT {limit}";

        var entries = new List<LeaderboardEntry>();
        try
        {
            await using var cmd = session.Connection?.CreateCommand();
            if (cmd != null)
            {
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(new LeaderboardEntry(
                        StudentId: reader.GetString(0),
                        DisplayName: reader.GetString(1) ?? reader.GetString(4) ?? "Anonymous",
                        AvatarUrl: null,
                        TotalXp: reader.GetInt32(2),
                        Level: ComputeLevel(reader.GetInt32(2)),
                        CurrentStreak: reader.GetInt32(3),
                        WeeklyXp: 0));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying friends leaderboard");
        }

        return new LeaderboardView(
            Scope: "friends",
            ScopeId: studentId,
            Entries: entries,
            TotalCount: friendIds.Count,
            GeneratedAt: DateTime.UtcNow);
    }

    public async Task<StudentRanks> GetStudentRanksAsync(
        string studentId,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        // Get student XP using raw query
        var sql = $@"
            SELECT COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   data->>'SchoolId' as SchoolId
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'StudentId' = '{studentId}'";

        int studentXp = 0;
        string? schoolId = null;

        try
        {
            await using var cmd = session.Connection?.CreateCommand();
            if (cmd != null)
            {
                cmd.CommandText = sql;
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result != null)
                {
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        studentXp = reader.GetInt32(0);
                        schoolId = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student rank");
        }

        // Global rank
        var globalRankSql = $@"
            SELECT COUNT(*) 
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE COALESCE((data->>'TotalXp')::int, 0) > {studentXp}";

        int globalRank = 0;
        try
        {
            await using var cmd = session.Connection?.CreateCommand();
            if (cmd != null)
            {
                cmd.CommandText = globalRankSql;
                var result = await cmd.ExecuteScalarAsync(ct);
                globalRank = (result != null ? (int)(long)result : 0) + 1;
            }
        }
        catch { }

        // Class rank (by school)
        int? classRank = null;
        if (!string.IsNullOrEmpty(schoolId))
        {
            var classRankSql = $@"
                SELECT COUNT(*) 
                FROM cena.mt_doc_studentprofilesnapshot
                WHERE data->>'SchoolId' = '{schoolId}'
                AND COALESCE((data->>'TotalXp')::int, 0) > {studentXp}";

            try
            {
                await using var cmd = session.Connection?.CreateCommand();
                if (cmd != null)
                {
                    cmd.CommandText = classRankSql;
                    var result = await cmd.ExecuteScalarAsync(ct);
                    classRank = (result != null ? (int)(long)result : 0) + 1;
                }
            }
            catch { }
        }

        // Friends rank
        var friendIds = await GetFriendIdsAsync(session, studentId, ct);
        friendIds.Add(studentId);

        int? friendsRank = null;
        if (friendIds.Count > 0)
        {
            var idList = string.Join("','", friendIds);
            var friendsRankSql = $@"
                SELECT COUNT(*) 
                FROM cena.mt_doc_studentprofilesnapshot
                WHERE data->>'StudentId' IN ('{idList}')
                AND COALESCE((data->>'TotalXp')::int, 0) > {studentXp}";

            try
            {
                await using var cmd = session.Connection?.CreateCommand();
                if (cmd != null)
                {
                    cmd.CommandText = friendsRankSql;
                    var result = await cmd.ExecuteScalarAsync(ct);
                    friendsRank = (result != null ? (int)(long)result : 0) + 1;
                }
            }
            catch { }
        }

        return new StudentRanks(
            studentId,
            new RankInfo(globalRank, studentXp),
            classRank != null ? new RankInfo(classRank.Value, studentXp) : null,
            friendsRank != null ? new RankInfo(friendsRank.Value, studentXp) : null);
    }

    public async Task UpdateLeaderboardAsync(
        string studentId,
        int xpDelta,
        int? sessionCountDelta = null,
        CancellationToken ct = default)
    {
        // Leaderboard is computed on-demand from StudentProfileSnapshot
        _logger.LogDebug(
            "Leaderboard update triggered for {StudentId}: XP delta {XpDelta}",
            studentId, xpDelta);

        await Task.CompletedTask;
    }

    private async Task<List<string>> GetFriendIdsAsync(
        IQuerySession session,
        string studentId,
        CancellationToken ct)
    {
        var friendships = await session.Query<FriendshipDocument>()
            .Where(f => f.StudentAId == studentId || f.StudentBId == studentId)
            .ToListAsync(ct);

        return friendships
            .Select(f => f.StudentAId == studentId ? f.StudentBId : f.StudentAId)
            .Distinct()
            .ToList();
    }

    private static int ComputeLevel(int totalXp)
    {
        if (totalXp <= 0) return 1;
        var level = (int)((1 + Math.Sqrt(1 + 8 * totalXp / 100.0)) / 2);
        return Math.Max(1, level);
    }
}

/// <summary>
/// Leaderboard view with entries.
/// </summary>
public record LeaderboardView(
    string Scope,
    string? ScopeId,
    IReadOnlyList<LeaderboardEntry> Entries,
    int TotalCount,
    DateTime GeneratedAt);

/// <summary>
/// Single leaderboard entry.
/// </summary>
public record LeaderboardEntry(
    string StudentId,
    string DisplayName,
    string? AvatarUrl,
    int TotalXp,
    int Level,
    int CurrentStreak,
    int WeeklyXp);

/// <summary>
/// Student ranks across all scopes.
/// </summary>
public record StudentRanks(
    string StudentId,
    RankInfo? GlobalRank,
    RankInfo? ClassRank,
    RankInfo? FriendsRank);

/// <summary>
/// Rank info for a specific scope.
/// </summary>
public record RankInfo(
    int Rank,
    int TotalXp);
