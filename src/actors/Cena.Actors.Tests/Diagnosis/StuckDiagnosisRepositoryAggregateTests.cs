// =============================================================================
// Cena Platform — StuckDiagnosisRepository aggregation tests (RDY-063 Phase 2a+)
//
// In-memory fake tests for GetTopItemsAsync / GetDistributionAsync.
// Real Marten round-trip is covered by the IntegrationTests project
// when pilot data arrives; these tests lock the pure-aggregation logic
// against the fake so we can iterate on the grouping without a DB.
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class StuckDiagnosisRepositoryAggregateTests
{
    private sealed class InMemoryStuckRepo : IStuckDiagnosisRepository
    {
        private readonly List<StuckDiagnosisDocument> _store = new();

        public void Seed(IEnumerable<StuckDiagnosisDocument> docs) => _store.AddRange(docs);

        public Task PersistAsync(string sessionId, string studentAnonId, string questionId,
            StuckDiagnosis diagnosis, int retentionDays, CancellationToken ct = default)
        {
            var at = diagnosis.DiagnosedAt;
            _store.Add(new StuckDiagnosisDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                StudentAnonId = studentAnonId,
                QuestionId = questionId,
                Primary = diagnosis.Primary,
                PrimaryConfidence = diagnosis.PrimaryConfidence,
                Source = diagnosis.Source,
                DiagnosedAt = at,
                DayBucket = new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero),
                ExpiresAt = at.AddDays(retentionDays),
            });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StuckDiagnosisDocument>> GetRecentByQuestionAsync(
            string questionId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StuckDiagnosisDocument>>(
                _store.Where(d => d.QuestionId == questionId)
                      .OrderByDescending(d => d.DiagnosedAt)
                      .Take(limit).ToList());

        public Task<IReadOnlyList<StuckItemAggregate>> GetTopItemsAsync(
            StuckType? filterType, int days, int limit, CancellationToken ct = default)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));
            var q = _store.Where(d => d.DiagnosedAt >= since);
            if (filterType.HasValue && filterType.Value != StuckType.Unknown)
                q = q.Where(d => d.Primary == filterType.Value);
            else
                q = q.Where(d => d.Primary != StuckType.Unknown);

            var result = q.GroupBy(d => new { d.QuestionId, d.Primary })
                .Select(g => new StuckItemAggregate(
                    g.Key.QuestionId, g.Key.Primary,
                    g.Count(),
                    g.Select(x => x.StudentAnonId).Distinct().Count(),
                    g.Average(x => x.PrimaryConfidence),
                    g.Min(x => x.DiagnosedAt),
                    g.Max(x => x.DiagnosedAt)))
                .OrderByDescending(a => a.Count)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<StuckItemAggregate>>(result);
        }

        public Task<IReadOnlyDictionary<StuckType, int>> GetDistributionAsync(
            int days, CancellationToken ct = default)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));
            var dict = _store.Where(d => d.DiagnosedAt >= since)
                .GroupBy(d => d.Primary)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (StuckType t in Enum.GetValues(typeof(StuckType)))
                if (!dict.ContainsKey(t)) dict[t] = 0;
            return Task.FromResult<IReadOnlyDictionary<StuckType, int>>(dict);
        }
    }

    private static StuckDiagnosisDocument Doc(
        string questionId, StuckType primary, string anonId,
        DateTimeOffset at, float conf = 0.8f)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = "s-" + anonId,
            StudentAnonId = anonId,
            QuestionId = questionId,
            Primary = primary,
            PrimaryConfidence = conf,
            DiagnosedAt = at,
            DayBucket = new DateTimeOffset(at.UtcDateTime.Date, TimeSpan.Zero),
            ExpiresAt = at.AddDays(30),
        };

    [Fact]
    public async Task TopItems_GroupsByQuestion_And_OrdersByCount()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = new InMemoryStuckRepo();
        repo.Seed(new[]
        {
            // q-1 has 3 encoding stucks from 3 students
            Doc("q-1", StuckType.Encoding, "a", now.AddHours(-1)),
            Doc("q-1", StuckType.Encoding, "b", now.AddHours(-2)),
            Doc("q-1", StuckType.Encoding, "c", now.AddHours(-3)),
            // q-2 has 1 encoding stuck
            Doc("q-2", StuckType.Encoding, "a", now.AddHours(-4)),
            // q-3 has 2 misconceptions
            Doc("q-3", StuckType.Misconception, "d", now.AddHours(-5)),
            Doc("q-3", StuckType.Misconception, "e", now.AddHours(-6)),
            // Outside window (should be excluded)
            Doc("q-1", StuckType.Encoding, "x", now.AddDays(-100)),
            // Unknown (should be excluded when no filter)
            Doc("q-1", StuckType.Unknown, "y", now.AddHours(-1)),
        });

        var top = await repo.GetTopItemsAsync(null, days: 7, limit: 10, ct: default);

        Assert.Equal(3, top.Count);  // q-1/Encoding, q-3/Misconception, q-2/Encoding
        Assert.Equal("q-1", top[0].QuestionId);
        Assert.Equal(StuckType.Encoding, top[0].Primary);
        Assert.Equal(3, top[0].Count);
        Assert.Equal(3, top[0].DistinctStudentsCount);

        Assert.Equal("q-3", top[1].QuestionId);
        Assert.Equal(2, top[1].Count);
    }

    [Fact]
    public async Task TopItems_FilterByType_RestrictsResults()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = new InMemoryStuckRepo();
        repo.Seed(new[]
        {
            Doc("q-1", StuckType.Encoding, "a", now),
            Doc("q-2", StuckType.Misconception, "b", now),
            Doc("q-3", StuckType.Strategic, "c", now),
        });

        var encoding = await repo.GetTopItemsAsync(StuckType.Encoding, days: 7, limit: 10, ct: default);

        Assert.Single(encoding);
        Assert.Equal("q-1", encoding[0].QuestionId);
    }

    [Fact]
    public async Task Distribution_IncludesAllCategories_EvenWhenZero()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = new InMemoryStuckRepo();
        repo.Seed(new[] { Doc("q-1", StuckType.Encoding, "a", now) });

        var dist = await repo.GetDistributionAsync(days: 7, ct: default);

        // Every StuckType must appear in the distribution, even with 0 count.
        foreach (StuckType t in Enum.GetValues(typeof(StuckType)))
            Assert.True(dist.ContainsKey(t), $"Distribution missing {t}");

        Assert.Equal(1, dist[StuckType.Encoding]);
        Assert.Equal(0, dist[StuckType.Misconception]);
    }

    [Fact]
    public async Task Distribution_ExcludesOutOfWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = new InMemoryStuckRepo();
        repo.Seed(new[]
        {
            Doc("q-1", StuckType.Encoding, "a", now.AddDays(-1)),  // in
            Doc("q-2", StuckType.Encoding, "b", now.AddDays(-100)), // out
        });

        var dist = await repo.GetDistributionAsync(days: 7, ct: default);

        Assert.Equal(1, dist[StuckType.Encoding]);
    }
}
