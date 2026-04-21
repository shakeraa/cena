// =============================================================================
// Cena Platform — StudentPlanMigrationService tests (prr-219)
//
// Tests the migration safety net:
//   1. Happy path upcast produces ExamTargetAdded_V1 + StudentPlanMigrated_V1.
//   2. Idempotency: re-running the same MigrationSourceId is a no-op.
//   3. Feature flag off → UpcastRowOutcome.Skipped.
//   4. Dry run → UpcastRowOutcome.WouldMigrate, no events written.
//   5. Missing catalog sitting → target created then archived
//      (ArchiveReason.CatalogRetired) so retention sweeps apply.
//   6. Cross-tenant snapshot in batch drain → UpcastRowOutcome.Failed
//      + StudentPlanMigrationFailed_V1 recorded, batch continues.
//   7. Batch-level aggregation: total/migrated/failed/skipped counters.
//   8. Per-row errors do not throw from UpcastTenantAsync.
//   9. Reversibility: archiving migrated targets restores "no active"
//      state for the student, without deleting the event trail.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Cena.Actors.StudentPlan.Migration;

namespace Cena.Actors.Tests.StudentPlan.Migration;

public sealed class StudentPlanMigrationServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
    private const string Tenant = "inst-001";
    private const string StudentA = "stu-a";
    private const string StudentB = "stu-b";

    private sealed class FakeFlag(bool globalDefault, params string[] blockedStudents) : IMigrationFeatureFlag
    {
        public bool IsEnabledForTenant(string tenantId) => globalDefault;
        public bool IsEnabledForStudent(string tenantId, string studentAnonId)
            => globalDefault && !blockedStudents.Contains(studentAnonId);
    }

    private static SittingCode SampleSitting => new(
        "תשפ״ו", SittingSeason.Summer, SittingMoed.A);

    private static LegacyStudentPlanSnapshot Snap(
        string sourceId = "legacy-1",
        string studentId = StudentA,
        string tenantId = Tenant,
        bool withSitting = true,
        double weeklyHours = 5)
        => new(
            MigrationSourceId: sourceId,
            StudentAnonId: studentId,
            TenantId: tenantId,
            LegacyDeadlineUtc: DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
            LegacyWeeklyBudget: TimeSpan.FromHours(weeklyHours),
            InferredExamCode: new ExamCode("BAGRUT_MATH_5U"),
            InferredTrack: new TrackCode("5U"),
            InferredSitting: withSitting ? SampleSitting : null);

    private static (StudentPlanMigrationService service, InMemoryStudentPlanAggregateStore store, StudentPlanCommandHandler handler) Build(bool flagOn = true, params string[] blockedStudents)
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store, () => FixedNow);
        var flag = new FakeFlag(flagOn, blockedStudents);
        var svc = new StudentPlanMigrationService(store, handler, flag, () => FixedNow);
        return (svc, store, handler);
    }

    // ── Happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task Upcast_writes_added_and_migrated_events()
    {
        var (svc, store, _) = Build();

        var r = await svc.UpcastAsync(Snap(), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Migrated, r.Outcome);
        Assert.NotNull(r.TargetId);

        var events = store.GetRawEvents(StudentA);
        Assert.Contains(events, e => e is StudentPlanInitialized_V1);
        Assert.Contains(events, e => e is ExamTargetAdded_V1);
        Assert.Contains(events, e => e is StudentPlanMigrated_V1);
    }

    [Fact]
    public async Task Upcast_new_target_is_Source_Migration()
    {
        var (svc, store, _) = Build();

        await svc.UpcastAsync(Snap(), dryRun: false);

        var added = store.GetRawEvents(StudentA).OfType<ExamTargetAdded_V1>().Single();
        Assert.Equal(ExamTargetSource.Migration, added.Target.Source);
        Assert.Equal(StudentPlanMigrationService.SystemMigrationAssignedById, added.Target.AssignedById.Value);
    }

    // ── Idempotency ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upcast_twice_with_same_source_id_is_no_op()
    {
        var (svc, store, _) = Build();
        var first = await svc.UpcastAsync(Snap(), dryRun: false);
        Assert.Equal(UpcastRowOutcome.Migrated, first.Outcome);

        var second = await svc.UpcastAsync(Snap(), dryRun: false);

        Assert.Equal(UpcastRowOutcome.AlreadyMigrated, second.Outcome);
        // No duplicate events.
        Assert.Single(store.GetRawEvents(StudentA).OfType<StudentPlanMigrated_V1>());
        Assert.Single(store.GetRawEvents(StudentA).OfType<ExamTargetAdded_V1>());
    }

    [Fact]
    public async Task IsMigratedAsync_returns_true_after_successful_upcast()
    {
        var (svc, _, _) = Build();
        await svc.UpcastAsync(Snap(sourceId: "legacy-xyz"), dryRun: false);

        var isMigrated = await svc.IsMigratedAsync(StudentA, "legacy-xyz");

        Assert.True(isMigrated);
    }

    [Fact]
    public async Task IsMigratedAsync_returns_false_for_unknown_student()
    {
        var (svc, _, _) = Build();

        var isMigrated = await svc.IsMigratedAsync("never", "legacy-xyz");

        Assert.False(isMigrated);
    }

    // ── Feature flag gate ─────────────────────────────────────────────────

    [Fact]
    public async Task Upcast_flag_off_returns_skipped()
    {
        var (svc, store, _) = Build(flagOn: false);

        var r = await svc.UpcastAsync(Snap(), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Skipped, r.Outcome);
        Assert.Empty(store.GetRawEvents(StudentA));
    }

    [Fact]
    public async Task Upcast_blocked_student_returns_skipped()
    {
        var (svc, store, _) = Build(flagOn: true, blockedStudents: new[] { StudentA });

        var r = await svc.UpcastAsync(Snap(), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Skipped, r.Outcome);
        Assert.Empty(store.GetRawEvents(StudentA));
    }

    // ── Dry run ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upcast_dry_run_reports_would_migrate_without_writing()
    {
        var (svc, store, _) = Build();

        var r = await svc.UpcastAsync(Snap(), dryRun: true);

        Assert.Equal(UpcastRowOutcome.WouldMigrate, r.Outcome);
        Assert.Empty(store.GetRawEvents(StudentA));
    }

    // ── Missing catalog sitting ──────────────────────────────────────────

    [Fact]
    public async Task Upcast_without_inferred_sitting_archives_target_immediately()
    {
        var (svc, store, _) = Build();

        var r = await svc.UpcastAsync(Snap(withSitting: false), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Migrated, r.Outcome);

        var events = store.GetRawEvents(StudentA);
        Assert.Single(events.OfType<ExamTargetAdded_V1>());
        Assert.Single(events.OfType<ExamTargetArchived_V1>());
        Assert.Equal(
            ArchiveReason.CatalogRetired,
            events.OfType<ExamTargetArchived_V1>().Single().Reason);

        // No active targets after the upcast.
        var aggregate = await store.LoadAsync(StudentA);
        Assert.Empty(aggregate.State.ActiveTargets);
    }

    // ── Batch drain ─────────────────────────────────────────────────────

    [Fact]
    public async Task Batch_drains_multiple_rows_for_same_tenant()
    {
        var (svc, store, _) = Build();
        var snapshots = new[]
        {
            Snap(sourceId: "l-a", studentId: StudentA),
            Snap(sourceId: "l-b", studentId: StudentB),
        };

        var result = await svc.UpcastTenantAsync(Tenant, snapshots, dryRun: false);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Migrated);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(Tenant, result.TenantId);
    }

    [Fact]
    public async Task Batch_dry_run_reports_counts_without_writing()
    {
        var (svc, store, _) = Build();
        var snapshots = new[] { Snap(sourceId: "l-a", studentId: StudentA) };

        var result = await svc.UpcastTenantAsync(Tenant, snapshots, dryRun: true);

        Assert.Equal(1, result.WouldMigrate);
        Assert.Empty(store.GetRawEvents(StudentA));
    }

    [Fact]
    public async Task Batch_cross_tenant_snapshot_is_recorded_as_failure_without_halting_batch()
    {
        var (svc, store, _) = Build();
        var snapshots = new[]
        {
            Snap(sourceId: "l-a", studentId: StudentA, tenantId: "OTHER_TENANT"),
            Snap(sourceId: "l-b", studentId: StudentB, tenantId: Tenant),
        };

        var result = await svc.UpcastTenantAsync(Tenant, snapshots, dryRun: false);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Migrated);
        Assert.Contains(store.GetRawEvents(StudentA),
            e => e is StudentPlanMigrationFailed_V1);
    }

    [Fact]
    public async Task Batch_flag_off_skips_all_rows()
    {
        var (svc, store, _) = Build(flagOn: false);
        var snapshots = new[] { Snap() };

        var result = await svc.UpcastTenantAsync(Tenant, snapshots, dryRun: false);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Migrated);
    }

    // ── Reversibility ───────────────────────────────────────────────────

    [Fact]
    public async Task Migrated_target_can_be_archived_with_MigrationRollback_reason()
    {
        var (svc, store, handler) = Build();
        var r = await svc.UpcastAsync(Snap(), dryRun: false);
        Assert.Equal(UpcastRowOutcome.Migrated, r.Outcome);

        var archived = await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentA, r.TargetId!.Value, ArchiveReason.MigrationRollback));

        Assert.True(archived.Success);
        var aggregate = await store.LoadAsync(StudentA);
        Assert.Empty(aggregate.State.ActiveTargets);
        // Event trail preserved.
        Assert.Contains(store.GetRawEvents(StudentA),
            e => e is ExamTargetArchived_V1 x && x.Reason == ArchiveReason.MigrationRollback);
    }

    // ── Invariant-violating legacy data ─────────────────────────────────

    [Fact]
    public async Task Upcast_clamps_weekly_hours_above_cap_to_40()
    {
        var (svc, store, _) = Build();

        var r = await svc.UpcastAsync(Snap(weeklyHours: 9999), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Migrated, r.Outcome);
        var added = store.GetRawEvents(StudentA).OfType<ExamTargetAdded_V1>().Single();
        Assert.Equal(40, added.Target.WeeklyHours);
    }

    [Fact]
    public async Task Upcast_clamps_weekly_hours_below_min_to_1()
    {
        var (svc, store, _) = Build();

        var r = await svc.UpcastAsync(Snap(weeklyHours: 0.1), dryRun: false);

        Assert.Equal(UpcastRowOutcome.Migrated, r.Outcome);
        var added = store.GetRawEvents(StudentA).OfType<ExamTargetAdded_V1>().Single();
        Assert.Equal(1, added.Target.WeeklyHours);
    }
}

/// <summary>
/// Feature-flag helper tests — stand-alone from the service tests.
/// </summary>
public sealed class MigrationFeatureFlagTests
{
    [Fact]
    public void Off_by_default()
    {
        var flag = new MigrationFeatureFlag(() => MigrationFeatureFlagSnapshot.Off);
        Assert.False(flag.IsEnabledForTenant("any"));
        Assert.False(flag.IsEnabledForStudent("any", "stu"));
    }

    [Fact]
    public void Global_on_enables_all_tenants_except_blocked()
    {
        var snap = new MigrationFeatureFlagSnapshot(
            GlobalDefault: true,
            EnabledTenants: new HashSet<string>(),
            BlockedTenants: new HashSet<string> { "bad" },
            BlockedStudents: new HashSet<string>());
        var flag = new MigrationFeatureFlag(() => snap);

        Assert.True(flag.IsEnabledForTenant("good"));
        Assert.False(flag.IsEnabledForTenant("bad"));
    }

    [Fact]
    public void Tenant_allowlist_off_by_default_except_explicit()
    {
        var snap = new MigrationFeatureFlagSnapshot(
            GlobalDefault: false,
            EnabledTenants: new HashSet<string> { "pilot" },
            BlockedTenants: new HashSet<string>(),
            BlockedStudents: new HashSet<string>());
        var flag = new MigrationFeatureFlag(() => snap);

        Assert.True(flag.IsEnabledForTenant("pilot"));
        Assert.False(flag.IsEnabledForTenant("other"));
    }

    [Fact]
    public void Student_block_overrides_tenant_enable()
    {
        var snap = new MigrationFeatureFlagSnapshot(
            GlobalDefault: true,
            EnabledTenants: new HashSet<string>(),
            BlockedTenants: new HashSet<string>(),
            BlockedStudents: new HashSet<string> { "stu-bad" });
        var flag = new MigrationFeatureFlag(() => snap);

        Assert.True(flag.IsEnabledForStudent("any", "stu-ok"));
        Assert.False(flag.IsEnabledForStudent("any", "stu-bad"));
    }
}
