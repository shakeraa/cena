// =============================================================================
// Cena Platform — InMemoryHardCapSupportTicketRepository (EPIC-PRR-J PRR-402)
//
// Process-local implementation for tests + dev. Production binds the
// Marten-backed impl so tickets survive host restarts. Thread-safe via
// ConcurrentDictionary; list queries snapshot the values collection so
// a concurrent Open or Resolve can't produce a torn read.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class InMemoryHardCapSupportTicketRepository : IHardCapSupportTicketRepository
{
    private readonly ConcurrentDictionary<string, HardCapSupportTicketDocument> _byId = new();

    public Task OpenAsync(HardCapSupportTicketDocument ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(ticket.Id))
            throw new ArgumentException("Ticket Id is required.", nameof(ticket));
        if (string.IsNullOrWhiteSpace(ticket.StudentSubjectIdHash))
            throw new ArgumentException("StudentSubjectIdHash is required.", nameof(ticket));
        if (string.IsNullOrWhiteSpace(ticket.MonthlyWindow))
            throw new ArgumentException("MonthlyWindow is required.", nameof(ticket));
        if (!_byId.TryAdd(ticket.Id, ticket))
            throw new InvalidOperationException($"Duplicate hard-cap ticket id: {ticket.Id}");
        return Task.CompletedTask;
    }

    public Task<HardCapSupportTicketDocument?> GetAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        _byId.TryGetValue(id, out var ticket);
        return Task.FromResult<HardCapSupportTicketDocument?>(ticket);
    }

    public Task<IReadOnlyList<HardCapSupportTicketDocument>> ListOpenAsync(CancellationToken ct)
    {
        var list = _byId.Values
            .Where(t => t.Status == HardCapSupportTicketStatus.Open)
            .OrderByDescending(t => t.RequestedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<HardCapSupportTicketDocument>>(list);
    }

    public Task ResolveAsync(
        string id,
        int grantedExtension,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("resolvedBy is required.", nameof(resolvedBy));
        if (!_byId.TryGetValue(id, out var existing))
            throw new InvalidOperationException($"Hard-cap ticket {id} not found.");
        if (existing.Status != HardCapSupportTicketStatus.Open)
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} is not Open (current: {existing.Status}).");

        var updated = existing with
        {
            Status = HardCapSupportTicketStatus.Resolved,
            GrantedExtensionCount = grantedExtension,
            GrantedBy = resolvedBy,
            ResolvedAtUtc = resolvedAtUtc,
        };
        // ConcurrentDictionary.TryUpdate with the original reference as the
        // compare-value gives us optimistic-concurrency semantics: a second
        // concurrent Resolve/Reject on the same ticket will see the updated
        // row and fail the guard above on retry.
        if (!_byId.TryUpdate(id, updated, existing))
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} was modified concurrently; retry.");
        return Task.CompletedTask;
    }

    public Task RejectAsync(
        string id,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("resolvedBy is required.", nameof(resolvedBy));
        if (!_byId.TryGetValue(id, out var existing))
            throw new InvalidOperationException($"Hard-cap ticket {id} not found.");
        if (existing.Status != HardCapSupportTicketStatus.Open)
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} is not Open (current: {existing.Status}).");

        var updated = existing with
        {
            Status = HardCapSupportTicketStatus.Rejected,
            GrantedExtensionCount = 0,
            GrantedBy = resolvedBy,
            ResolvedAtUtc = resolvedAtUtc,
        };
        if (!_byId.TryUpdate(id, updated, existing))
            throw new InvalidOperationException(
                $"Hard-cap ticket {id} was modified concurrently; retry.");
        return Task.CompletedTask;
    }

    public Task<int> ListActiveGrantsForStudentAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));

        var total = _byId.Values
            .Where(t =>
                t.StudentSubjectIdHash == studentSubjectIdHash
                && t.MonthlyWindow == monthWindow
                && t.Status == HardCapSupportTicketStatus.Resolved)
            .Sum(t => t.GrantedExtensionCount);
        return Task.FromResult(total);
    }

    public Task<bool> HasOpenTicketInMonthAsync(
        string studentSubjectIdHash,
        string monthWindow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));

        var hit = _byId.Values.Any(t =>
            t.StudentSubjectIdHash == studentSubjectIdHash
            && t.MonthlyWindow == monthWindow
            && t.Status == HardCapSupportTicketStatus.Open);
        return Task.FromResult(hit);
    }
}
