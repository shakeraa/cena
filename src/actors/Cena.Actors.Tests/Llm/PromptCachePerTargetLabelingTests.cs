// =============================================================================
// Cena Platform — Per-target cache-labeling tests (prr-233)
//
// Coverage (maps to task DoD):
//   (a) Cache hit increments counter with correct exam_target_code + institute_id label.
//   (b) Cache miss likewise.
//   (c) Nested PushScope restores outer frame on dispose.
//   (d) No scope open → both labels emit "unknown".
//   (e) Key-builder emits target segment in expected position.
//   (f) Ambient-context convenience overload composes identically to the
//       four-arg overload.
//   (g) SLO-breach sanity: with a 4:6 hit:miss ratio the observed hit rate
//       is 0.4, which is below the 0.60 SLO floor — the same numerator /
//       denominator the alert rule computes at the Prometheus side.
//
// These tests do not hit a live Redis instance (same discipline as
// PromptCacheIntegrationTests — NSubstitute on IConnectionMultiplexer /
// IDatabase — so the CI container stays Redis-free).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Llm;

public sealed class PromptCachePerTargetLabelingTests : IDisposable
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IPromptCacheKeyContext _keyContext = new AsyncLocalPromptCacheKeyContext();
    private readonly RecordingMeterFactory _meterFactory = new();
    private readonly RedisPromptCache _cache;

    public PromptCachePerTargetLabelingTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        _cache = new RedisPromptCache(
            _redis,
            NullLogger<RedisPromptCache>.Instance,
            _meterFactory,
            _keyContext);
    }

    public void Dispose() => _meterFactory.Dispose();

    // ── (a) hit carries correct target + institute ────────────────────────

    [Fact]
    public async Task Hit_EmitsLabels_FromActiveContextScope()
    {
        using var _ = _keyContext.PushScope(instituteId: "school-42", examTargetCode: "BAGRUT_MATH_5U");
        var key = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", _keyContext);
        _db.StringGetAsync(key, Arg.Any<CommandFlags>()).Returns(new RedisValue("cached-body"));

        var result = await _cache.TryGetAsync(key, "explain", "answer_evaluation", CancellationToken.None);

        Assert.True(result.found);
        var hit = _meterFactory.SingleMeasurement("cena.prompt_cache.hits_total");
        Assert.Equal(1, hit.Value);
        AssertTag(hit, "cache_type", "explain");
        AssertTag(hit, "task", "answer_evaluation");
        AssertTag(hit, "institute_id", "school-42");
        AssertTag(hit, "exam_target_code", "BAGRUT_MATH_5U");
    }

    // ── (b) miss likewise ─────────────────────────────────────────────────

    [Fact]
    public async Task Miss_EmitsLabels_FromActiveContextScope()
    {
        using var _ = _keyContext.PushScope(instituteId: "school-42", examTargetCode: "PET");
        var key = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", _keyContext);
        _db.StringGetAsync(key, Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        var result = await _cache.TryGetAsync(key, "explain", "answer_evaluation", CancellationToken.None);

        Assert.False(result.found);
        var miss = _meterFactory.SingleMeasurement("cena.prompt_cache.misses_total");
        Assert.Equal(1, miss.Value);
        AssertTag(miss, "exam_target_code", "PET");
        AssertTag(miss, "institute_id", "school-42");
    }

    // ── (c) nested scope restores outer on dispose ────────────────────────

    [Fact]
    public async Task NestedScope_RestoresOuterFrame_OnDispose()
    {
        using var outer = _keyContext.PushScope(instituteId: "school-a", examTargetCode: "BAGRUT_MATH_5U");
        Assert.Equal("BAGRUT_MATH_5U", _keyContext.ExamTargetCode);

        using (var inner = _keyContext.PushScope(instituteId: null, examTargetCode: "SAT_MATH"))
        {
            // inner narrows the target, inherits the institute.
            Assert.Equal("school-a", _keyContext.InstituteId);
            Assert.Equal("SAT_MATH", _keyContext.ExamTargetCode);

            var keyInner = "cena:x";
            _db.StringGetAsync(keyInner, Arg.Any<CommandFlags>()).Returns(RedisValue.Null);
            await _cache.TryGetAsync(keyInner, "explain", "answer_evaluation", CancellationToken.None);
        }

        // After inner.Dispose() we are back to the outer frame.
        Assert.Equal("BAGRUT_MATH_5U", _keyContext.ExamTargetCode);
        Assert.Equal("school-a", _keyContext.InstituteId);

        var innerMiss = _meterFactory.Measurements("cena.prompt_cache.misses_total")[0];
        AssertTag(innerMiss, "exam_target_code", "SAT_MATH");
        AssertTag(innerMiss, "institute_id", "school-a");
    }

    // ── (d) no scope → "unknown" ──────────────────────────────────────────

    [Fact]
    public async Task NoScope_EmitsUnknownForBothLabels()
    {
        _db.StringGetAsync("cena:x", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);
        await _cache.TryGetAsync("cena:x", "sys", "system_prompt", CancellationToken.None);

        var miss = _meterFactory.SingleMeasurement("cena.prompt_cache.misses_total");
        AssertTag(miss, "institute_id", RedisPromptCache.UnknownLabel);
        AssertTag(miss, "exam_target_code", RedisPromptCache.UnknownLabel);
    }

    // ── (e) key-builder target segment position ───────────────────────────

    [Fact]
    public void KeyBuilder_TargetSegment_SitsBetweenTenantAndDomain()
    {
        // Per prr-233: cena:t:<tenant>:xt:<target>:explain:<q>:<err>
        Assert.Equal(
            "cena:t:school-42:xt:BAGRUT_MATH_5U:explain:q-123:ProceduralError",
            PromptCacheKeyBuilder.ForExplanation(
                questionId: "q-123",
                errorType: "ProceduralError",
                tenantId: "school-42",
                examTargetCode: "BAGRUT_MATH_5U"));

        // Per prr-233: with only a target (no tenant), the target still anchors
        // immediately after the cena: root so it's a cheap SCAN prefix.
        Assert.Equal(
            "cena:xt:PET:explain:q-1:err",
            PromptCacheKeyBuilder.ForExplanation(
                questionId: "q-1",
                errorType: "err",
                tenantId: null,
                examTargetCode: "PET"));

        // Legacy shape (no target) still resolves so existing keys are untouched.
        Assert.Equal(
            "cena:t:school-42:explain:q-123:ProceduralError",
            PromptCacheKeyBuilder.ForExplanation(
                questionId: "q-123",
                errorType: "ProceduralError",
                tenantId: "school-42",
                examTargetCode: null));
    }

    // ── (f) ambient-context overload == 4-arg overload ────────────────────

    [Fact]
    public void KeyBuilder_AmbientContextOverload_Matches4ArgOverload()
    {
        using var _ = _keyContext.PushScope(instituteId: "school-42", examTargetCode: "BAGRUT_MATH_5U");

        var ambient = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", _keyContext);
        var explicit4 = PromptCacheKeyBuilder.ForExplanation("q-1", "ProceduralError", "school-42", "BAGRUT_MATH_5U");
        Assert.Equal(explicit4, ambient);

        var ctxAmbient = PromptCacheKeyBuilder.ForStudentContext("anon-1", "h", _keyContext);
        var ctxExplicit = PromptCacheKeyBuilder.ForStudentContext("anon-1", "h", "school-42", "BAGRUT_MATH_5U");
        Assert.Equal(ctxExplicit, ctxAmbient);
    }

    // ── (g) SLO-breach shape (simulated <60% 7d-equivalent ratio) ────────

    [Fact]
    public async Task SloBreachSimulation_HitRateBelowSixtyPercent_IsObservable()
    {
        // Fixture: 4 hits + 6 misses = 40% hit rate, below the 60% SLO floor.
        // This is the exact numerator / denominator pair the Prometheus alert
        // computes, so a dashboard panel painting the same expression on this
        // fixture would paint a red cell. We prove the emission is consistent
        // (hits ↔ hits_total, misses ↔ misses_total) so the Prometheus-side
        // expression is operating on the shape we actually emit.
        using var _ = _keyContext.PushScope(instituteId: "school-42", examTargetCode: "BAGRUT_MATH_5U");

        // 4 hits
        _db.StringGetAsync("k-hit", Arg.Any<CommandFlags>()).Returns(new RedisValue("x"));
        for (var i = 0; i < 4; i++)
            await _cache.TryGetAsync("k-hit", "explain", "answer_evaluation", CancellationToken.None);

        // 6 misses
        _db.StringGetAsync("k-miss", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);
        for (var i = 0; i < 6; i++)
            await _cache.TryGetAsync("k-miss", "explain", "answer_evaluation", CancellationToken.None);

        var hits = _meterFactory.Measurements("cena.prompt_cache.hits_total")
            .Where(m => TagValue(m, "exam_target_code") == "BAGRUT_MATH_5U")
            .Sum(m => m.Value);
        var misses = _meterFactory.Measurements("cena.prompt_cache.misses_total")
            .Where(m => TagValue(m, "exam_target_code") == "BAGRUT_MATH_5U")
            .Sum(m => m.Value);

        Assert.Equal(4, hits);
        Assert.Equal(6, misses);

        var hitRate = (double)hits / (hits + misses);
        Assert.True(hitRate < 0.60,
            $"fixture hit rate ({hitRate:F2}) MUST be below the 60% SLO so the " +
            "matching Prometheus alert would fire against this emission shape.");
    }

    // ── Colon safety on scope inputs ──────────────────────────────────────

    [Fact]
    public void PushScope_RejectsColonInInstituteId()
    {
        Assert.Throws<ArgumentException>(
            () => _keyContext.PushScope(instituteId: "school:42", examTargetCode: null));
    }

    [Fact]
    public void PushScope_RejectsColonInExamTargetCode()
    {
        Assert.Throws<ArgumentException>(
            () => _keyContext.PushScope(instituteId: null, examTargetCode: "BAGRUT:MATH"));
    }

    // ── Attribute contract ────────────────────────────────────────────────

    [Fact]
    public void PromptCacheKeyBypassesTargetContextAttribute_RequiresReason()
    {
        Assert.Throws<ArgumentException>(() => new PromptCacheKeyBypassesTargetContextAttribute(""));
        Assert.Throws<ArgumentException>(() => new PromptCacheKeyBypassesTargetContextAttribute("   "));
        var attr = new PromptCacheKeyBypassesTargetContextAttribute("system-prompt is target-independent by contract");
        Assert.Equal("system-prompt is target-independent by contract", attr.Reason);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static void AssertTag(RecordedMeasurement m, string key, string expected)
    {
        Assert.True(m.Tags.TryGetValue(key, out var value),
            $"Measurement missing tag '{key}'. Tags present: {string.Join(",", m.Tags.Keys)}");
        Assert.Equal(expected, value);
    }

    private static string? TagValue(RecordedMeasurement m, string key)
        => m.Tags.TryGetValue(key, out var v) ? v : null;

    // ── minimal recording IMeterFactory so tests can inspect tag sets ─────

    private sealed record RecordedMeasurement(string InstrumentName, long Value, IReadOnlyDictionary<string, string?> Tags);

    private sealed class RecordingMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();
        private readonly MeterListener _listener;
        private readonly List<RecordedMeasurement> _captures = new();
        private readonly object _lock = new();

        public RecordingMeterFactory()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    // We only care about Cena.PromptCache counters here.
                    if (instrument.Meter.Name == "Cena.PromptCache" && instrument is Counter<long>)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var kv in tags)
                {
                    map[kv.Key] = kv.Value?.ToString();
                }
                lock (_lock)
                {
                    _captures.Add(new RecordedMeasurement(instrument.Name, value, map));
                }
            });
            _listener.Start();
        }

        public Meter Create(MeterOptions options)
        {
            var m = new Meter(options);
            _meters.Add(m);
            return m;
        }

        public IReadOnlyList<RecordedMeasurement> Measurements(string instrumentName)
        {
            lock (_lock)
            {
                return _captures
                    .Where(c => c.InstrumentName == instrumentName)
                    .ToList();
            }
        }

        public RecordedMeasurement SingleMeasurement(string instrumentName)
        {
            var xs = Measurements(instrumentName);
            Assert.Single(xs);
            return xs[0];
        }

        public void Dispose()
        {
            _listener.Dispose();
            foreach (var m in _meters) m.Dispose();
            _meters.Clear();
        }
    }
}
