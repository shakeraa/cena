// =============================================================================
// Regression tests for FIND-sec-001 — Leaderboard SQL Injection parameterization
// =============================================================================
//
// Background:
//   Before the fix, src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
//   used C# verbatim string interpolation ($@"...") to embed classroom.SchoolId,
//   friend ids, student ids, student XP, and result limits directly into raw
//   SQL that was then executed against Postgres via NpgsqlCommand.CommandText.
//   Any of those values reaching the service from a live request was a SQL
//   injection vector. Audit trail: docs/reviews/agent-2-security-findings.md
//
//   After the fix, every raw SQL string comes from LeaderboardService.SqlBuilders,
//   which returns a (string sql, NpgsqlParameter[] parameters) tuple using
//   positional parameters ($1, $2, ...). Values are BOUND, not interpolated.
//
// What these tests prove:
//   1. Every query string from SqlBuilders contains NO occurrence of any
//      user-supplied value — not even one that happens to contain a SQL payload
//      like `'; DROP TABLE ... --`. This is the gold-standard anti-SQLi invariant.
//   2. Every query string contains the expected positional parameter markers
//      ($1, $2, ...). This proves the placeholder model is in place.
//   3. The NpgsqlParameter[] returned by SqlBuilders carries the user-supplied
//      values as-is. If any sanitizer had mangled them this would fail.
//   4. NpgsqlDbType is set correctly (Integer for limits/xp, Text for ids,
//      Array|Text for friend lists). Wrong types would cause runtime errors
//      and silently re-introduce string coercion.
//
// These tests are intentionally DB-free. No Postgres container is required.
// If a future regression re-introduces string interpolation the malicious
// payload will reappear inside the SQL string and every test in this file will
// fail loudly on the `DoesNotContain` assertion. That is the point.
// =============================================================================

using Cena.Infrastructure.Gamification;
using NpgsqlTypes;

namespace Cena.Infrastructure.Tests.Gamification;

public class LeaderboardServiceSqliSafetyTests
{
    // The classic SQL injection payload: single quote to break out of a quoted
    // literal, then a DROP TABLE against the exact real table name, then a
    // comment to neutralise the rest of the statement. Every test below throws
    // this at a different SqlBuilders method.
    private const string SqliPayload = "'; DROP TABLE cena.mt_doc_studentprofilesnapshot; --";

    // Another payload flavour — boolean-OR-true tautology with closing quote.
    private const string OrTruePayload = "' OR '1'='1";

    // ─── GetGlobalLeaderboard ────────────────────────────────────────────────

    [Fact]
    public void BuildGlobalLeaderboardQuery_DoesNotInlineLimit()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildGlobalLeaderboardQuery(100);

        Assert.Contains("LIMIT $1", sql);
        // The literal 100 must NOT be inlined — only the $1 placeholder.
        Assert.DoesNotContain("LIMIT 100", sql);
        Assert.Single(parameters);
        Assert.Equal(NpgsqlDbType.Integer, parameters[0].NpgsqlDbType);
        Assert.Equal(100, parameters[0].Value);
    }

    // ─── GetClassLeaderboard ─────────────────────────────────────────────────

    [Fact]
    public void BuildClassLeaderboardQuery_SanitizesSqlInjectionPayload_InSchoolId()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildClassLeaderboardQuery(
            SqliPayload, 50);

        // The raw payload must not appear anywhere inside the SQL string.
        Assert.DoesNotContain(SqliPayload, sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("--", sql);

        // Expected positional parameters.
        Assert.Contains("data->>'SchoolId' = $1", sql);
        Assert.Contains("LIMIT $2", sql);
        Assert.Equal(2, parameters.Length);

        // The payload is carried as a BOUND string parameter, not a SQL fragment.
        Assert.Equal(NpgsqlDbType.Text, parameters[0].NpgsqlDbType);
        Assert.Equal(SqliPayload, parameters[0].Value);

        Assert.Equal(NpgsqlDbType.Integer, parameters[1].NpgsqlDbType);
        Assert.Equal(50, parameters[1].Value);
    }

    [Fact]
    public void BuildClassLeaderboardQuery_SanitizesOrTruePayload()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildClassLeaderboardQuery(
            OrTruePayload, 25);

        Assert.DoesNotContain(OrTruePayload, sql);
        Assert.DoesNotContain("OR '1'='1", sql);
        Assert.Equal(OrTruePayload, parameters[0].Value);
    }

    [Fact]
    public void BuildClassLeaderboardQuery_DoesNotInlineLimit()
    {
        var (sql, _) = LeaderboardService.SqlBuilders.BuildClassLeaderboardQuery("school-x", 77);
        Assert.DoesNotContain("LIMIT 77", sql);
        Assert.Contains("LIMIT $2", sql);
    }

    // ─── GetFriendsLeaderboard ───────────────────────────────────────────────

    [Fact]
    public void BuildFriendsLeaderboardQuery_SanitizesSqlInjectionInFriendIds()
    {
        var friendIds = new[]
        {
            "student-alice",
            SqliPayload,              // malicious friend id
            "student-bob"
        };

        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildFriendsLeaderboardQuery(
            friendIds, 50);

        // No payload anywhere in the SQL text.
        Assert.DoesNotContain(SqliPayload, sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        // And crucially, no string-join IN clause — we use = ANY($1::text[])
        Assert.DoesNotContain("IN ('", sql);
        Assert.Contains("= ANY($1::text[])", sql);
        Assert.Contains("LIMIT $2", sql);

        // Parameters bound as a Postgres text[] array.
        Assert.Equal(2, parameters.Length);
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameters[0].NpgsqlDbType);

        var boundArray = Assert.IsType<string[]>(parameters[0].Value);
        Assert.Equal(3, boundArray.Length);
        Assert.Contains(SqliPayload, boundArray);
        Assert.Contains("student-alice", boundArray);
        Assert.Contains("student-bob", boundArray);

        Assert.Equal(NpgsqlDbType.Integer, parameters[1].NpgsqlDbType);
        Assert.Equal(50, parameters[1].Value);
    }

    [Fact]
    public void BuildFriendsLeaderboardQuery_HandlesEmptyList()
    {
        // Even an empty list must produce a parameterised query — no fallback
        // to a string-join that could be bypassed later.
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildFriendsLeaderboardQuery(
            Array.Empty<string>(), 10);

        Assert.Contains("= ANY($1::text[])", sql);
        Assert.DoesNotContain("IN (", sql);

        var boundArray = Assert.IsType<string[]>(parameters[0].Value);
        Assert.Empty(boundArray);
    }

    // ─── GetStudentRanks (4 sub-queries) ─────────────────────────────────────

    [Fact]
    public void BuildStudentXpLookupQuery_SanitizesStudentIdPayload()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildStudentXpLookupQuery(
            SqliPayload);

        Assert.DoesNotContain(SqliPayload, sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("data->>'StudentId' = $1", sql);

        Assert.Single(parameters);
        Assert.Equal(NpgsqlDbType.Text, parameters[0].NpgsqlDbType);
        Assert.Equal(SqliPayload, parameters[0].Value);
    }

    [Fact]
    public void BuildStudentXpLookupQuery_HandlesSingleQuoteInId()
    {
        const string weird = "student'id";
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildStudentXpLookupQuery(weird);

        Assert.DoesNotContain(weird, sql);
        Assert.Equal(weird, parameters[0].Value);
    }

    [Fact]
    public void BuildGlobalRankQuery_DoesNotInlineStudentXp()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildGlobalRankQuery(9999);

        Assert.DoesNotContain("9999", sql);
        Assert.Contains("> $1", sql);

        Assert.Single(parameters);
        Assert.Equal(NpgsqlDbType.Integer, parameters[0].NpgsqlDbType);
        Assert.Equal(9999, parameters[0].Value);
    }

    [Fact]
    public void BuildClassRankQuery_SanitizesSchoolIdPayload()
    {
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildClassRankQuery(
            SqliPayload, 500);

        Assert.DoesNotContain(SqliPayload, sql);
        Assert.DoesNotContain("500", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("data->>'SchoolId' = $1", sql);
        Assert.Contains("> $2", sql);

        Assert.Equal(2, parameters.Length);
        Assert.Equal(NpgsqlDbType.Text, parameters[0].NpgsqlDbType);
        Assert.Equal(SqliPayload, parameters[0].Value);
        Assert.Equal(NpgsqlDbType.Integer, parameters[1].NpgsqlDbType);
        Assert.Equal(500, parameters[1].Value);
    }

    [Fact]
    public void BuildFriendsRankQuery_SanitizesFriendListAndStudentXp()
    {
        var friendIds = new[] { "alice", SqliPayload, "bob" };
        var (sql, parameters) = LeaderboardService.SqlBuilders.BuildFriendsRankQuery(
            friendIds, 1234);

        Assert.DoesNotContain(SqliPayload, sql);
        Assert.DoesNotContain("1234", sql);
        Assert.DoesNotContain("IN ('", sql);
        Assert.Contains("= ANY($1::text[])", sql);
        Assert.Contains("> $2", sql);

        Assert.Equal(2, parameters.Length);
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, parameters[0].NpgsqlDbType);
        var arr = Assert.IsType<string[]>(parameters[0].Value);
        Assert.Contains(SqliPayload, arr);

        Assert.Equal(NpgsqlDbType.Integer, parameters[1].NpgsqlDbType);
        Assert.Equal(1234, parameters[1].Value);
    }

    // ─── Structural sweep: every builder must use $N placeholders ────────────

    [Fact]
    public void AllBuilders_UsePositionalPlaceholdersAndNeverInterpolation()
    {
        // Call each builder with benign inputs and assert that the returned SQL
        // contains at least one $N placeholder and never a C# interpolation
        // leftover like {variable}.

        var sqls = new[]
        {
            LeaderboardService.SqlBuilders.BuildGlobalLeaderboardQuery(10).sql,
            LeaderboardService.SqlBuilders.BuildClassLeaderboardQuery("school", 10).sql,
            LeaderboardService.SqlBuilders.BuildFriendsLeaderboardQuery(new[] { "a" }, 10).sql,
            LeaderboardService.SqlBuilders.BuildStudentXpLookupQuery("s").sql,
            LeaderboardService.SqlBuilders.BuildGlobalRankQuery(10).sql,
            LeaderboardService.SqlBuilders.BuildClassRankQuery("school", 10).sql,
            LeaderboardService.SqlBuilders.BuildFriendsRankQuery(new[] { "a" }, 10).sql,
        };

        foreach (var sql in sqls)
        {
            Assert.Contains("$1", sql);
            Assert.DoesNotContain("{0}", sql);
            Assert.DoesNotContain("{1}", sql);
            // No single-quote-wrapped user values (the smoking gun of interpolation).
            // A raw quote is still allowed in the SELECT projection for jsonb keys
            // like data->>'StudentId' — those are SCHEMA constants, not user input.
            // We only forbid quoted user values that would look like WHERE col = 'x'.
            Assert.DoesNotContain("= 'school'", sql);
            Assert.DoesNotContain("= 's'", sql);
        }
    }
}
