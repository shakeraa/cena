// =============================================================================
// Cena Platform — InMemoryHardCapSupportTicketRepository tests (PRR-402)
//
// Locks the repository contract independently of the service layer:
//   - OpenAsync writes, GetAsync reads back
//   - duplicate id rejection
//   - ListOpenAsync filters Status == Open
//   - Resolve/Reject transitions, and the "Open-only" idempotency guard
//   - ListActiveGrantsForStudentAsync sums Resolved grants for the given
//     (student, month) window and ignores other months / Open / Rejected
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class InMemoryHardCapSupportTicketRepositoryTests
{
    private static HardCapSupportTicketDocument Seed(
        string id,
        string student = "stu-a",
        HardCapSupportTicketStatus status = HardCapSupportTicketStatus.Open,
        int granted = 0,
        string monthWindow = "2026-04",
        DateTimeOffset? requested = null)
    {
        return new HardCapSupportTicketDocument
        {
            Id = id,
            StudentSubjectIdHash = student,
            ParentSubjectIdEncrypted = "parent-enc",
            Tier = SubscriptionTier.Premium,
            UploadCountAtRequest = 300,
            MonthlyWindow = monthWindow,
            RequestedAtUtc = requested ?? new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            Reason = "",
            Status = status,
            GrantedExtensionCount = granted,
            GrantedBy = status == HardCapSupportTicketStatus.Open ? "" : "admin-1",
            ResolvedAtUtc = status == HardCapSupportTicketStatus.Open ? null : requested?.AddMinutes(5),
        };
    }

    [Fact]
    public async Task Open_then_get_roundtrips()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        var t = Seed("t-1");
        await repo.OpenAsync(t, default);

        var fetched = await repo.GetAsync("t-1", default);
        Assert.NotNull(fetched);
        Assert.Equal("stu-a", fetched!.StudentSubjectIdHash);
        Assert.Equal(HardCapSupportTicketStatus.Open, fetched.Status);
    }

    [Fact]
    public async Task Open_duplicate_id_throws()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        await repo.OpenAsync(Seed("t-1"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.OpenAsync(Seed("t-1", student: "stu-other"), default));
    }

    [Fact]
    public async Task ListOpen_only_returns_open_tickets_newest_first()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        var base0 = new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero);
        await repo.OpenAsync(Seed("t-older", requested: base0), default);
        await repo.OpenAsync(Seed("t-newer", requested: base0.AddHours(3)), default);
        await repo.OpenAsync(Seed("t-resolved", requested: base0.AddHours(1)), default);
        await repo.ResolveAsync("t-resolved", 20, "admin-1", base0.AddHours(2), default);

        var open = await repo.ListOpenAsync(default);

        Assert.Equal(2, open.Count);
        Assert.Equal("t-newer", open[0].Id);
        Assert.Equal("t-older", open[1].Id);
    }

    [Fact]
    public async Task Resolve_transitions_to_Resolved_with_grant_and_timestamp()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        await repo.OpenAsync(Seed("t-1"), default);
        var resolvedAt = new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero);

        await repo.ResolveAsync("t-1", 50, "admin-1", resolvedAt, default);

        var after = await repo.GetAsync("t-1", default);
        Assert.NotNull(after);
        Assert.Equal(HardCapSupportTicketStatus.Resolved, after!.Status);
        Assert.Equal(50, after.GrantedExtensionCount);
        Assert.Equal("admin-1", after.GrantedBy);
        Assert.Equal(resolvedAt, after.ResolvedAtUtc);
    }

    [Fact]
    public async Task Reject_transitions_to_Rejected_with_zero_grant()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        await repo.OpenAsync(Seed("t-1"), default);

        await repo.RejectAsync("t-1", "admin-1",
            new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), default);

        var after = await repo.GetAsync("t-1", default);
        Assert.NotNull(after);
        Assert.Equal(HardCapSupportTicketStatus.Rejected, after!.Status);
        Assert.Equal(0, after.GrantedExtensionCount);
        Assert.Equal("admin-1", after.GrantedBy);
        Assert.NotNull(after.ResolvedAtUtc);
    }

    [Fact]
    public async Task Resolve_on_already_resolved_ticket_throws()
    {
        // Idempotency guard: once a ticket has left Open, neither Resolve
        // nor Reject may touch it again. Support has to file a new ticket
        // for a second extension.
        var repo = new InMemoryHardCapSupportTicketRepository();
        await repo.OpenAsync(Seed("t-1"), default);
        await repo.ResolveAsync("t-1", 20, "admin-1",
            new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ResolveAsync("t-1", 10, "admin-2",
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), default));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.RejectAsync("t-1", "admin-2",
                new DateTimeOffset(2026, 4, 23, 13, 0, 0, TimeSpan.Zero), default));
    }

    [Fact]
    public async Task Resolve_on_missing_ticket_throws()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ResolveAsync("no-such", 10, "admin-1", DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task ListActiveGrantsForStudent_sums_resolved_grants_in_window()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();

        // Same student, same month — two resolved grants. Should sum.
        await repo.OpenAsync(Seed("t-a"), default);
        await repo.ResolveAsync("t-a", 30, "admin-1",
            new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero), default);

        await repo.OpenAsync(Seed("t-b"), default);
        await repo.ResolveAsync("t-b", 20, "admin-2",
            new DateTimeOffset(2026, 4, 24, 11, 0, 0, TimeSpan.Zero), default);

        // Same student, different month. Must NOT be counted.
        await repo.OpenAsync(Seed("t-mar", monthWindow: "2026-03"), default);
        await repo.ResolveAsync("t-mar", 100, "admin-1",
            new DateTimeOffset(2026, 3, 30, 11, 0, 0, TimeSpan.Zero), default);

        // Different student, same month. Must NOT be counted.
        await repo.OpenAsync(Seed("t-other", student: "stu-b"), default);
        await repo.ResolveAsync("t-other", 40, "admin-1",
            new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero), default);

        // Rejected ticket, same student + same month. Must NOT contribute.
        await repo.OpenAsync(Seed("t-rej"), default);
        await repo.RejectAsync("t-rej", "admin-1",
            new DateTimeOffset(2026, 4, 25, 11, 0, 0, TimeSpan.Zero), default);

        // Open ticket, same student + same month. Must NOT contribute (not yet granted).
        await repo.OpenAsync(Seed("t-open"), default);

        var total = await repo.ListActiveGrantsForStudentAsync("stu-a", "2026-04", default);

        Assert.Equal(50, total);
    }

    [Fact]
    public async Task HasOpenTicketInMonth_detects_and_differentiates()
    {
        var repo = new InMemoryHardCapSupportTicketRepository();
        await repo.OpenAsync(Seed("t-1"), default);

        Assert.True(await repo.HasOpenTicketInMonthAsync("stu-a", "2026-04", default));
        Assert.False(await repo.HasOpenTicketInMonthAsync("stu-a", "2026-05", default));
        Assert.False(await repo.HasOpenTicketInMonthAsync("stu-other", "2026-04", default));

        await repo.ResolveAsync("t-1", 20, "admin-1",
            new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero), default);
        Assert.False(await repo.HasOpenTicketInMonthAsync("stu-a", "2026-04", default));
    }
}
