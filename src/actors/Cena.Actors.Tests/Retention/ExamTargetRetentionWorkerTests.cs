// =============================================================================
// Cena Platform — ExamTargetRetentionWorker tests (prr-229)
//
// Covers:
//   - Worker shreds targets past the 24-month window.
//   - Worker skips targets within the window.
//   - Worker respects the 60-month opt-in extension.
//   - Failure on one row does not abort the sweep.
//   - RetentionPolicy horizon math is correct (month-arithmetic, not 30-day).
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Infrastructure;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Cena.Actors.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cena.Actors.Tests.Retention;

/// <summary>
/// Minimal test double for <see cref="Cena.Actors.Infrastructure.IClock"/>.
/// Avoids depending on Microsoft.Extensions.TimeProvider.Testing so the
/// test project keeps its current package set.
/// </summary>
internal sealed class FrozenClock : Cena.Actors.Infrastructure.IClock
{
    private DateTimeOffset _now;
    public FrozenClock(DateTimeOffset now) { _now = now; }
    public DateTimeOffset UtcNow => _now;
    public DateTime UtcDateTime => _now.UtcDateTime;
    public DateTime LocalDateTime => _now.LocalDateTime;
    public string FormatUtc(string format) => _now.UtcDateTime.ToString(format);
    public void Set(DateTimeOffset at) { _now = at; }
}

public sealed class ExamTargetRetentionWorkerTests
{
    // ── Policy arithmetic ────────────────────────────────────────────────

    [Fact]
    public void Policy_horizon_is_24_months_default()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var horizon = ExamTargetRetentionPolicy.ComputeHorizon(
            archived, extendedRetention: false);
        Assert.Equal(DateTimeOffset.Parse("2028-04-21T09:00:00Z"), horizon);
    }

    [Fact]
    public void Policy_horizon_is_60_months_when_extended()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var horizon = ExamTargetRetentionPolicy.ComputeHorizon(
            archived, extendedRetention: true);
        Assert.Equal(DateTimeOffset.Parse("2031-04-21T09:00:00Z"), horizon);
    }

    [Fact]
    public void Policy_not_beyond_retention_just_before_horizon()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var justBefore = archived.AddMonths(24).AddSeconds(-1);

        Assert.False(ExamTargetRetentionPolicy.IsBeyondRetention(
            archived, extendedRetention: false, nowUtc: justBefore));
    }

    [Fact]
    public void Policy_beyond_retention_at_and_after_horizon()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var at = archived.AddMonths(24);
        var after = archived.AddMonths(24).AddDays(1);

        Assert.True(ExamTargetRetentionPolicy.IsBeyondRetention(
            archived, extendedRetention: false, nowUtc: at));
        Assert.True(ExamTargetRetentionPolicy.IsBeyondRetention(
            archived, extendedRetention: false, nowUtc: after));
    }

    [Fact]
    public void Policy_expiring_soon_within_60_days()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var expiringSoon = archived.AddMonths(24).AddDays(-30);

        Assert.True(ExamTargetRetentionPolicy.IsExpiringSoon(
            archived, extendedRetention: false, nowUtc: expiringSoon));
    }

    [Fact]
    public void Policy_not_expiring_soon_more_than_60_days_out()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var farOut = archived.AddMonths(24).AddDays(-120);

        Assert.False(ExamTargetRetentionPolicy.IsExpiringSoon(
            archived, extendedRetention: false, nowUtc: farOut));
    }

    // ── Worker sweep ─────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_shreds_targets_past_24_months()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(25);
        var harness = NewHarness(now);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            archived,
            TenantId: null));
        await harness.Mastery.UpsertAsync(new SkillKeyedMasteryRow(
            new MasteryKey(
                "stu-1",
                ExamTargetCode.Parse("bagrut-math-5yu"),
                SkillCode.Parse("math.a.b")),
            0.4f, 1, archived, MasteryEventSource.Native));

        var result = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.RowsShredded);
        Assert.Equal(0, result.RowsFailed);
        Assert.Empty(await harness.Mastery.ListByStudentAsync("stu-1"));
    }

    [Fact]
    public async Task Worker_skips_targets_within_retention_window()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(12); // well within 24m
        var harness = NewHarness(now);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            archived,
            TenantId: null));
        await harness.Mastery.UpsertAsync(new SkillKeyedMasteryRow(
            new MasteryKey(
                "stu-1",
                ExamTargetCode.Parse("bagrut-math-5yu"),
                SkillCode.Parse("math.a.b")),
            0.4f, 1, archived, MasteryEventSource.Native));

        var result = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.RowsShredded);
        Assert.Single(await harness.Mastery.ListByStudentAsync("stu-1"));
    }

    [Fact]
    public async Task Worker_respects_60_month_opt_in()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(30); // past 24m but within 60m
        var harness = NewHarness(now);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            archived,
            TenantId: null));
        await harness.Extension.SetAsync(new ExamTargetRetentionExtension(
            "stu-1", archived, archived.AddMonths(60)));
        await harness.Mastery.UpsertAsync(new SkillKeyedMasteryRow(
            new MasteryKey(
                "stu-1",
                ExamTargetCode.Parse("bagrut-math-5yu"),
                SkillCode.Parse("math.a.b")),
            0.4f, 1, archived, MasteryEventSource.Native));

        var result = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.RowsShredded);
        Assert.Equal(1, result.RowsSkippedExtended);
        Assert.Single(await harness.Mastery.ListByStudentAsync("stu-1"));
    }

    [Fact]
    public async Task Worker_notifies_student_on_shred()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(25);
        var notifier = new CollectingNotifier();
        var harness = NewHarness(now, notifier);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-1",
            ExamTargetCode.Parse("sat-math"),
            archived,
            TenantId: null));

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Single(notifier.Calls);
        Assert.Equal("stu-1", notifier.Calls[0].StudentAnonId);
        Assert.Equal("sat-math", notifier.Calls[0].ExamTargetCode.Value);
    }

    [Fact]
    public async Task Worker_removes_row_from_source_after_shred_so_next_sweep_is_noop()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(25);
        var harness = NewHarness(now);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-1",
            ExamTargetCode.Parse("pet-quant"),
            archived,
            TenantId: null));

        var first = await harness.Worker.RunOnceAsync(CancellationToken.None);
        var second = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, first.RowsShredded);
        Assert.Equal(0, second.RowsInspected);
    }

    [Fact]
    public async Task Worker_failure_on_one_row_does_not_abort_others()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(25);
        var faultyMastery = new FaultyMasteryStore();
        var harness = NewHarnessWithCustomMastery(now, faultyMastery);

        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-ok",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            archived,
            TenantId: null));
        harness.Source.Append(new ArchivedExamTargetRow(
            "stu-faulty",
            ExamTargetCode.Parse("sat-math"),
            archived,
            TenantId: null));
        faultyMastery.FailFor = "stu-faulty";

        var result = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.RowsShredded);
        Assert.Equal(1, result.RowsFailed);
    }

    [Fact]
    public async Task Worker_respects_max_shreds_per_run_cap()
    {
        var archived = DateTimeOffset.Parse("2026-04-21T09:00:00Z");
        var now = archived.AddMonths(25);
        var harness = NewHarness(now, maxShredsPerRun: 2);

        for (var i = 0; i < 5; i++)
        {
            harness.Source.Append(new ArchivedExamTargetRow(
                $"stu-{i}",
                ExamTargetCode.Parse("bagrut-math-5yu"),
                archived,
                TenantId: null));
        }

        var result = await harness.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, result.RowsShredded);
        // Remaining rows deferred to next sweep.
    }

    // ── Harness helpers ──────────────────────────────────────────────────

    private sealed class Harness
    {
        public required InMemoryArchivedExamTargetSource Source { get; init; }
        public required InMemoryExamTargetRetentionExtensionStore Extension { get; init; }
        public required ISkillKeyedMasteryStore Mastery { get; init; }
        public required ExamTargetRetentionWorker Worker { get; init; }
    }

    private sealed class CollectingNotifier : IRetentionShredNotifier
    {
        public List<(string StudentAnonId, ExamTargetCode ExamTargetCode)> Calls
            { get; } = new();
        public Task NotifyShreddedAsync(
            string studentAnonId,
            ExamTargetCode examTargetCode,
            DateTimeOffset shreddedAtUtc,
            CancellationToken ct = default)
        {
            Calls.Add((studentAnonId, examTargetCode));
            return Task.CompletedTask;
        }
    }

    private sealed class FaultyMasteryStore : ISkillKeyedMasteryStore
    {
        private readonly InMemorySkillKeyedMasteryStore _inner = new();
        public string? FailFor { get; set; }

        public Task<SkillKeyedMasteryRow?> TryGetAsync(
            MasteryKey key, CancellationToken ct = default)
            => _inner.TryGetAsync(key, ct);
        public Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByStudentAsync(
            string studentAnonId, CancellationToken ct = default)
            => _inner.ListByStudentAsync(studentAnonId, ct);
        public Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByTargetAsync(
            string studentAnonId,
            ExamTargetCode examTargetCode,
            CancellationToken ct = default)
            => _inner.ListByTargetAsync(studentAnonId, examTargetCode, ct);
        public Task UpsertAsync(
            SkillKeyedMasteryRow row, CancellationToken ct = default)
            => _inner.UpsertAsync(row, ct);
        public Task<int> DeleteByStudentAsync(
            string studentAnonId, CancellationToken ct = default)
            => _inner.DeleteByStudentAsync(studentAnonId, ct);
        public Task<int> DeleteByTargetAsync(
            string studentAnonId,
            ExamTargetCode examTargetCode,
            CancellationToken ct = default)
        {
            if (FailFor is not null
                && string.Equals(FailFor, studentAnonId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated failure");
            }
            return _inner.DeleteByTargetAsync(studentAnonId, examTargetCode, ct);
        }
    }

    private static Harness NewHarness(
        DateTimeOffset now,
        IRetentionShredNotifier? notifier = null,
        int maxShredsPerRun = 10_000)
    {
        var source = new InMemoryArchivedExamTargetSource();
        var extension = new InMemoryExamTargetRetentionExtensionStore();
        var mastery = new InMemorySkillKeyedMasteryStore();
        var clock = new FrozenClock(now);
        var opts = Options.Create(new ExamTargetRetentionWorkerOptions
        {
            MaxShredsPerRun = maxShredsPerRun,
        });
        var worker = new ExamTargetRetentionWorker(
            source,
            extension,
            mastery,
            notifier ?? new NoopRetentionShredNotifier(),
            clock,
            NullLogger<ExamTargetRetentionWorker>.Instance,
            opts,
            new ExamTargetRetentionMetrics());
        return new Harness
        {
            Source = source,
            Extension = extension,
            Mastery = mastery,
            Worker = worker,
        };
    }

    private static Harness NewHarnessWithCustomMastery(
        DateTimeOffset now,
        ISkillKeyedMasteryStore mastery)
    {
        var source = new InMemoryArchivedExamTargetSource();
        var extension = new InMemoryExamTargetRetentionExtensionStore();
        var clock = new FrozenClock(now);
        var opts = Options.Create(new ExamTargetRetentionWorkerOptions());
        var worker = new ExamTargetRetentionWorker(
            source,
            extension,
            mastery,
            new NoopRetentionShredNotifier(),
            clock,
            NullLogger<ExamTargetRetentionWorker>.Instance,
            opts,
            new ExamTargetRetentionMetrics());
        return new Harness
        {
            Source = source,
            Extension = extension,
            Mastery = mastery,
            Worker = worker,
        };
    }
}
