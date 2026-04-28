// =============================================================================
// Cena Platform — TrialAllotmentConfig + InMemoryTrialAllotmentConfigStore tests
// (task t_b89826b8bd60)
//
// Locks in:
//   - Range validation per knob (duration 0..30, turns 0..200, photos 0..50,
//     sessions 0..20). All-zero is valid (no trial offered).
//   - InMemoryStore returns DefaultZero on first read (no row written yet).
//   - Update overwrites the singleton (does NOT append) and persists the
//     audit event with correct PreviousTrialEnabled flag.
//   - Validation failures throw TrialAllotmentValidationException carrying
//     the specific failed field + reason.
//   - Empty admin id rejected with ArgumentException (audit-trail invariant).
//   - TrialEnabled property returns true iff any knob > 0.
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

/// <summary>
/// Local fixed-clock test double. Mirrors the FakeClock pattern used by
/// BankTransferReservationTests so we don't introduce a new test-time
/// dependency just for this fixture.
/// </summary>
file sealed class FixedTrialClock : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTrialClock(DateTimeOffset now) { _now = now; }
    public override DateTimeOffset GetUtcNow() => _now;
}

public class TrialAllotmentConfigTests
{
    private const string AdminId = "enc::admin::op-01";

    // ----- TrialAllotmentValidator ----------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(30, 200, 50, 20)]
    [InlineData(7, 30, 5, 3)]
    public void Validator_accepts_in_range_values(int days, int turns, int photos, int sessions)
    {
        var result = TrialAllotmentValidator.Validate(days, turns, photos, sessions);
        Assert.True(result.IsValid);
        Assert.Null(result.FailedField);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, nameof(TrialAllotmentConfig.TrialDurationDays))]
    [InlineData(31, 0, 0, 0, nameof(TrialAllotmentConfig.TrialDurationDays))]
    [InlineData(0, -1, 0, 0, nameof(TrialAllotmentConfig.TrialTutorTurns))]
    [InlineData(0, 201, 0, 0, nameof(TrialAllotmentConfig.TrialTutorTurns))]
    [InlineData(0, 0, -1, 0, nameof(TrialAllotmentConfig.TrialPhotoDiagnostics))]
    [InlineData(0, 0, 51, 0, nameof(TrialAllotmentConfig.TrialPhotoDiagnostics))]
    [InlineData(0, 0, 0, -1, nameof(TrialAllotmentConfig.TrialPracticeSessions))]
    [InlineData(0, 0, 0, 21, nameof(TrialAllotmentConfig.TrialPracticeSessions))]
    public void Validator_rejects_out_of_range_with_named_field(
        int days, int turns, int photos, int sessions, string expectedField)
    {
        var result = TrialAllotmentValidator.Validate(days, turns, photos, sessions);
        Assert.False(result.IsValid);
        Assert.Equal(expectedField, result.FailedField);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Validator_rejects_first_failed_field_when_multiple_invalid()
    {
        // duration is checked first per the validator order; should report
        // duration even though tutor turns is also invalid.
        var result = TrialAllotmentValidator.Validate(-5, 999, -1, 99);
        Assert.False(result.IsValid);
        Assert.Equal(nameof(TrialAllotmentConfig.TrialDurationDays), result.FailedField);
    }

    // ----- TrialAllotmentConfig.TrialEnabled ------------------------------------

    [Fact]
    public void TrialEnabled_is_false_when_all_zero()
    {
        var config = TrialAllotmentConfig.DefaultZero();
        Assert.False(config.TrialEnabled);
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(7, 30, 5, 3)]
    public void TrialEnabled_is_true_when_any_knob_nonzero(
        int days, int turns, int photos, int sessions)
    {
        var config = new TrialAllotmentConfig
        {
            TrialDurationDays = days,
            TrialTutorTurns = turns,
            TrialPhotoDiagnostics = photos,
            TrialPracticeSessions = sessions,
        };
        Assert.True(config.TrialEnabled);
    }

    // ----- InMemoryTrialAllotmentConfigStore: GetAsync --------------------------

    [Fact]
    public async Task InMemory_GetAsync_returns_default_zero_when_no_row_written()
    {
        var store = new InMemoryTrialAllotmentConfigStore();
        var config = await store.GetAsync(CancellationToken.None);

        Assert.Equal(0, config.TrialDurationDays);
        Assert.Equal(0, config.TrialTutorTurns);
        Assert.Equal(0, config.TrialPhotoDiagnostics);
        Assert.Equal(0, config.TrialPracticeSessions);
        Assert.False(config.TrialEnabled);
        Assert.Equal(string.Empty, config.LastUpdatedByAdminEncrypted);
    }

    [Fact]
    public async Task InMemory_GetAsync_is_defensive_copy_caller_cannot_mutate_store()
    {
        var store = new InMemoryTrialAllotmentConfigStore();
        var first = await store.GetAsync(CancellationToken.None);

        // Mutate the returned object — this MUST NOT affect the store.
        first.TrialDurationDays = 99;
        first.TrialTutorTurns = 999;

        var second = await store.GetAsync(CancellationToken.None);
        Assert.Equal(0, second.TrialDurationDays);
        Assert.Equal(0, second.TrialTutorTurns);
    }

    // ----- InMemoryTrialAllotmentConfigStore: UpdateAsync -----------------------

    [Fact]
    public async Task InMemory_UpdateAsync_persists_values_and_audit_metadata()
    {
        var clock = new FixedTrialClock(DateTimeOffset.Parse("2026-04-28T10:00:00Z"));
        var store = new InMemoryTrialAllotmentConfigStore(clock);

        var updated = await store.UpdateAsync(
            trialDurationDays: 3,
            trialTutorTurns: 10,
            trialPhotoDiagnostics: 3,
            trialPracticeSessions: 1,
            changedByAdminEncrypted: AdminId,
            ct: CancellationToken.None);

        Assert.Equal(3, updated.TrialDurationDays);
        Assert.Equal(10, updated.TrialTutorTurns);
        Assert.Equal(3, updated.TrialPhotoDiagnostics);
        Assert.Equal(1, updated.TrialPracticeSessions);
        Assert.True(updated.TrialEnabled);
        Assert.Equal(AdminId, updated.LastUpdatedByAdminEncrypted);
        Assert.Equal(clock.GetUtcNow(), updated.LastUpdatedAtUtc);

        // Subsequent Get returns the same persisted values.
        var fresh = await store.GetAsync(CancellationToken.None);
        Assert.Equal(3, fresh.TrialDurationDays);
        Assert.Equal(AdminId, fresh.LastUpdatedByAdminEncrypted);
    }

    [Fact]
    public async Task InMemory_UpdateAsync_overwrites_does_not_merge_with_previous()
    {
        var store = new InMemoryTrialAllotmentConfigStore();

        // First update — set duration only.
        await store.UpdateAsync(7, 0, 0, 0, AdminId, CancellationToken.None);

        // Second update — set turns + photos, duration back to zero. Must
        // REPLACE not merge.
        await store.UpdateAsync(0, 30, 5, 0, AdminId, CancellationToken.None);

        var got = await store.GetAsync(CancellationToken.None);
        Assert.Equal(0, got.TrialDurationDays);
        Assert.Equal(30, got.TrialTutorTurns);
        Assert.Equal(5, got.TrialPhotoDiagnostics);
        Assert.Equal(0, got.TrialPracticeSessions);
    }

    [Fact]
    public async Task InMemory_UpdateAsync_emits_audit_event_with_correct_previous_enabled_flag()
    {
        var store = new InMemoryTrialAllotmentConfigStore();

        // First update — turns trial ON (turns=10). Previous state was
        // all-zero, so PreviousTrialEnabled must be false.
        await store.UpdateAsync(0, 10, 0, 0, AdminId, CancellationToken.None);

        // Second update — back to all-zero (turns trial OFF). Previous state
        // had turns=10, so PreviousTrialEnabled must be true.
        await store.UpdateAsync(0, 0, 0, 0, AdminId, CancellationToken.None);

        var events = store.GetAuditEvents();
        Assert.Equal(2, events.Count);
        Assert.False(events[0].PreviousTrialEnabled);   // was off, going on
        Assert.True(events[1].PreviousTrialEnabled);    // was on, going off
        Assert.Equal(10, events[0].TrialTutorTurns);
        Assert.Equal(0, events[1].TrialTutorTurns);
    }

    [Fact]
    public async Task InMemory_UpdateAsync_throws_TrialAllotmentValidationException_on_out_of_range()
    {
        var store = new InMemoryTrialAllotmentConfigStore();
        var ex = await Assert.ThrowsAsync<TrialAllotmentValidationException>(
            () => store.UpdateAsync(
                trialDurationDays: 31,  // > 30 max
                trialTutorTurns: 0,
                trialPhotoDiagnostics: 0,
                trialPracticeSessions: 0,
                changedByAdminEncrypted: AdminId,
                ct: CancellationToken.None));

        Assert.Equal(
            nameof(TrialAllotmentConfig.TrialDurationDays),
            ex.FailedField);
        Assert.Contains("0..30", ex.Reason);
    }

    [Fact]
    public async Task InMemory_UpdateAsync_rejects_empty_admin_id_for_audit_invariant()
    {
        var store = new InMemoryTrialAllotmentConfigStore();
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpdateAsync(
                3, 10, 3, 1,
                changedByAdminEncrypted: "",
                ct: CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpdateAsync(
                3, 10, 3, 1,
                changedByAdminEncrypted: "   ",
                ct: CancellationToken.None));
    }

    [Fact]
    public async Task InMemory_UpdateAsync_idempotent_same_values_emit_separate_events()
    {
        // Re-applying the same values still emits an audit event — the
        // event log records every admin action, not only state-changing ones.
        // This matches the expectation that an audit reader sees a clean
        // record of "admin clicked Save" regardless of whether it changed
        // anything.
        var store = new InMemoryTrialAllotmentConfigStore();

        await store.UpdateAsync(7, 30, 5, 3, AdminId, CancellationToken.None);
        await store.UpdateAsync(7, 30, 5, 3, AdminId, CancellationToken.None);

        var events = store.GetAuditEvents();
        Assert.Equal(2, events.Count);
        // Second event's PreviousTrialEnabled must be true (state was already on).
        Assert.True(events[1].PreviousTrialEnabled);
    }
}
