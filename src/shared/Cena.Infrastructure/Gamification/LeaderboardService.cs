// =============================================================================
// Cena Platform — Leaderboard Service (STB-03c)
// Real-time leaderboard aggregation for global/class/friends scopes
// =============================================================================
//
// FIND-sec-001 (2026-04-11): Every raw SQL in this file used C# verbatim string
// interpolation (dollar-at-quote verbatim interpolation) and was then executed
// via NpgsqlCommand.CommandText with NO parameter binding. Any SchoolId,
// studentId, friend id, studentXp, or limit value that reached the DB was a
// SQL injection vector. All 7 interpolated statements have been replaced with
// positional parameters (numbered $1, $2, ...) bound through
// NpgsqlCommand.Parameters. No behavioural change — same SQL shape, same
// return values — just hard-parameterised.
//
// Reference pattern: src/api/Cena.Admin.Api/EmbeddingAdminService.cs L149-L163
// =============================================================================

using System.Runtime.CompilerServices;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

[assembly: InternalsVisibleTo("Cena.Infrastructure.Tests")]

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

        var (sql, parameters) = SqlBuilders.BuildGlobalLeaderboardQuery(limit);

        var entries = new List<LeaderboardEntry>();
        try
        {
            if (session.Connection is NpgsqlConnection conn)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                ApplyParameters(cmd, parameters);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(ReadLeaderboardEntry(reader));
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

        var (sql, parameters) = SqlBuilders.BuildClassLeaderboardQuery(classroom.SchoolId, limit);

        var entries = new List<LeaderboardEntry>();
        try
        {
            if (session.Connection is NpgsqlConnection conn)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                ApplyParameters(cmd, parameters);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(ReadLeaderboardEntry(reader));
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

        var (sql, parameters) = SqlBuilders.BuildFriendsLeaderboardQuery(friendIds, limit);

        var entries = new List<LeaderboardEntry>();
        try
        {
            if (session.Connection is NpgsqlConnection conn)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                ApplyParameters(cmd, parameters);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(ReadLeaderboardEntry(reader));
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

        int studentXp = 0;
        string? schoolId = null;

        // --- 1. Student XP lookup ---
        try
        {
            if (session.Connection is NpgsqlConnection conn)
            {
                var (sql, parameters) = SqlBuilders.BuildStudentXpLookupQuery(studentId);
                await using var cmd = new NpgsqlCommand(sql, conn);
                ApplyParameters(cmd, parameters);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    studentXp = reader.GetInt32(0);
                    schoolId = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student rank");
        }

        // --- 2. Global rank ---
        int globalRank = 0;
        try
        {
            if (session.Connection is NpgsqlConnection conn)
            {
                var (sql, parameters) = SqlBuilders.BuildGlobalRankQuery(studentXp);
                await using var cmd = new NpgsqlCommand(sql, conn);
                ApplyParameters(cmd, parameters);
                var result = await cmd.ExecuteScalarAsync(ct);
                globalRank = (result != null ? (int)(long)result : 0) + 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting global rank");
        }

        // --- 3. Class rank (by school) ---
        int? classRank = null;
        if (!string.IsNullOrEmpty(schoolId))
        {
            try
            {
                if (session.Connection is NpgsqlConnection conn)
                {
                    var (sql, parameters) = SqlBuilders.BuildClassRankQuery(schoolId, studentXp);
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    ApplyParameters(cmd, parameters);
                    var result = await cmd.ExecuteScalarAsync(ct);
                    classRank = (result != null ? (int)(long)result : 0) + 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class rank");
            }
        }

        // --- 4. Friends rank ---
        var friendIds = await GetFriendIdsAsync(session, studentId, ct);
        friendIds.Add(studentId);

        int? friendsRank = null;
        if (friendIds.Count > 0)
        {
            try
            {
                if (session.Connection is NpgsqlConnection conn)
                {
                    var (sql, parameters) = SqlBuilders.BuildFriendsRankQuery(friendIds, studentXp);
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    ApplyParameters(cmd, parameters);
                    var result = await cmd.ExecuteScalarAsync(ct);
                    friendsRank = (result != null ? (int)(long)result : 0) + 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting friends rank");
            }
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

    /// <summary>
    /// Reads a leaderboard entry row from the shared projection columns:
    /// 0 = StudentId, 1 = DisplayName, 2 = TotalXp, 3 = CurrentStreak, 4 = FullName.
    /// </summary>
    private static LeaderboardEntry ReadLeaderboardEntry(System.Data.Common.DbDataReader reader)
    {
        return new LeaderboardEntry(
            StudentId: reader.GetString(0),
            DisplayName: reader.IsDBNull(1)
                ? (reader.IsDBNull(4) ? "Anonymous" : reader.GetString(4))
                : reader.GetString(1),
            AvatarUrl: null,
            TotalXp: reader.GetInt32(2),
            Level: ComputeLevel(reader.GetInt32(2)),
            CurrentStreak: reader.GetInt32(3),
            WeeklyXp: 0);
    }

    /// <summary>
    /// Copies pre-built parameters onto an NpgsqlCommand. The parameter array is
    /// positional, so the first entry binds to $1, the second to $2, and so on.
    /// </summary>
    private static void ApplyParameters(NpgsqlCommand cmd, NpgsqlParameter[] parameters)
    {
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // SQL Builders — pure functions, independent of any DB connection.
    // Every statement uses positional parameters ($1, $2, ...) so values
    // are BOUND, not interpolated. These are exposed as `internal static`
    // so Cena.Admin.Api.Tests can assert the SQL shape + parameter values
    // without a live Postgres instance.
    // ─────────────────────────────────────────────────────────────────────
    internal static class SqlBuilders
    {
        private const string BaseProjection = @"
            SELECT data->>'StudentId' as StudentId,
                   data->>'DisplayName' as DisplayName,
                   COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   COALESCE((data->>'CurrentStreak')::int, 0) as CurrentStreak,
                   data->>'FullName' as FullName
            FROM cena.mt_doc_studentprofilesnapshot";

        public static (string sql, NpgsqlParameter[] parameters) BuildGlobalLeaderboardQuery(int limit)
        {
            var sql = BaseProjection + @"
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT $1";

            var parameters = new[]
            {
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = limit }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildClassLeaderboardQuery(
            string schoolId, int limit)
        {
            var sql = BaseProjection + @"
            WHERE data->>'SchoolId' = $1
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT $2";

            var parameters = new[]
            {
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = schoolId },
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = limit }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildFriendsLeaderboardQuery(
            IEnumerable<string> friendIds, int limit)
        {
            var sql = BaseProjection + @"
            WHERE data->>'StudentId' = ANY($1::text[])
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT $2";

            var parameters = new[]
            {
                new NpgsqlParameter
                {
                    NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
                    Value = friendIds.ToArray()
                },
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = limit }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildStudentXpLookupQuery(string studentId)
        {
            const string sql = @"
            SELECT COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
                   data->>'SchoolId' as SchoolId
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'StudentId' = $1";

            var parameters = new[]
            {
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = studentId }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildGlobalRankQuery(int studentXp)
        {
            const string sql = @"
            SELECT COUNT(*)
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE COALESCE((data->>'TotalXp')::int, 0) > $1";

            var parameters = new[]
            {
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = studentXp }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildClassRankQuery(
            string schoolId, int studentXp)
        {
            const string sql = @"
            SELECT COUNT(*)
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'SchoolId' = $1
            AND COALESCE((data->>'TotalXp')::int, 0) > $2";

            var parameters = new[]
            {
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = schoolId },
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = studentXp }
            };

            return (sql, parameters);
        }

        public static (string sql, NpgsqlParameter[] parameters) BuildFriendsRankQuery(
            IEnumerable<string> friendIds, int studentXp)
        {
            const string sql = @"
            SELECT COUNT(*)
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'StudentId' = ANY($1::text[])
            AND COALESCE((data->>'TotalXp')::int, 0) > $2";

            var parameters = new[]
            {
                new NpgsqlParameter
                {
                    NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
                    Value = friendIds.ToArray()
                },
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = studentXp }
            };

            return (sql, parameters);
        }
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
