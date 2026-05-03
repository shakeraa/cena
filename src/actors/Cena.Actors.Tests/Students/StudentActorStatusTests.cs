// =============================================================================
// Cena Platform -- LCM-001: Account Status Gate Tests
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Students;
using Xunit;

namespace Cena.Actors.Tests.Students;

public class StudentActorStatusTests
{
    // ── StudentState.Apply(AccountStatusChanged_V1) ──

    [Fact]
    public void Apply_AccountStatusChanged_UpdatesStatus()
    {
        var state = new StudentState { StudentId = "s-001", AccountStatus = AccountStatus.Active };
        var evt = new AccountStatusChanged_V1("s-001", "Suspended", "policy violation", "admin-1", DateTimeOffset.UtcNow);
        state.Apply(evt);
        Assert.Equal(AccountStatus.Suspended, state.AccountStatus);
    }

    [Fact]
    public void Apply_AccountStatusChanged_Active_RestoresStatus()
    {
        var state = new StudentState { StudentId = "s-001", AccountStatus = AccountStatus.Suspended };
        var evt = new AccountStatusChanged_V1("s-001", "Active", null, "admin-1", DateTimeOffset.UtcNow);
        state.Apply(evt);
        Assert.Equal(AccountStatus.Active, state.AccountStatus);
    }

    [Theory]
    [InlineData("Locked")]
    [InlineData("Frozen")]
    [InlineData("PendingDelete")]
    [InlineData("Expired")]
    [InlineData("Grace")]
    public void Apply_AccountStatusChanged_AllStatuses(string statusStr)
    {
        var state = new StudentState { StudentId = "s-001" };
        var evt = new AccountStatusChanged_V1("s-001", statusStr, null, "system", DateTimeOffset.UtcNow);
        state.Apply(evt);
        Assert.True(Enum.TryParse<AccountStatus>(statusStr, true, out var expected));
        Assert.Equal(expected, state.AccountStatus);
    }

    [Fact]
    public void Apply_AccountStatusChanged_IncrementsEventVersion()
    {
        var state = new StudentState { StudentId = "s-001", EventVersion = 42 };
        var evt = new AccountStatusChanged_V1("s-001", "Suspended", "test", "admin-1", DateTimeOffset.UtcNow);
        state.Apply(evt);
        Assert.Equal(43, state.EventVersion);
    }

    [Fact]
    public void Apply_AccountStatusChanged_UnknownStatus_DoesNotCrash()
    {
        var state = new StudentState { StudentId = "s-001", AccountStatus = AccountStatus.Active };
        var evt = new AccountStatusChanged_V1("s-001", "UnknownFutureStatus", null, "system", DateTimeOffset.UtcNow);
        state.Apply(evt);
        // Unknown status doesn't change the value (TryParse fails silently)
        Assert.Equal(AccountStatus.Active, state.AccountStatus);
    }

    // ── Snapshot round-trip ──

    [Fact]
    public void Snapshot_Apply_AccountStatusChanged_PersistsStatus()
    {
        var snapshot = new StudentProfileSnapshot { StudentId = "s-001", AccountStatus = "Active" };
        var evt = new AccountStatusChanged_V1("s-001", "Suspended", "test", "admin-1", DateTimeOffset.UtcNow);
        snapshot.Apply(evt);
        Assert.Equal("Suspended", snapshot.AccountStatus);
    }

    [Fact]
    public void Snapshot_DefaultAccountStatus_IsActive()
    {
        var snapshot = new StudentProfileSnapshot();
        Assert.Equal("Active", snapshot.AccountStatus);
    }

    // ── AccountStatus enum coverage ──

    [Fact]
    public void AccountStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<AccountStatus>();
        Assert.Contains(AccountStatus.Active, values);
        Assert.Contains(AccountStatus.Suspended, values);
        Assert.Contains(AccountStatus.Locked, values);
        Assert.Contains(AccountStatus.Frozen, values);
        Assert.Contains(AccountStatus.PendingDelete, values);
        Assert.Contains(AccountStatus.Expired, values);
        Assert.Contains(AccountStatus.Grace, values);
        Assert.Equal(7, values.Length);
    }

    // ── Default state ──

    [Fact]
    public void StudentState_DefaultAccountStatus_IsActive()
    {
        var state = new StudentState();
        Assert.Equal(AccountStatus.Active, state.AccountStatus);
    }
}
