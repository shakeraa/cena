// =============================================================================
// Cena Platform — AdminGroupManager (RDY-060)
//
// Thread-safe tracker of (connectionId → set of joined group names) for the
// admin SignalR hub. Differs from SignalRGroupManager (student side, 1:1
// studentId↔connection): an admin connection can subscribe to MANY groups
// (system monitor + one or more schools + one or more student-insight
// windows). On disconnect we need to enumerate + clean up every group the
// connection joined.
//
// This class is pure state — group-join authorization (tenant-scope check)
// lives in CenaAdminHub because it depends on ClaimsPrincipal. Tests for
// the manager itself don't need claims; tests for the hub do.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Admin.Api.Host.Hubs;

public sealed class AdminGroupManager
{
    // connectionId → groups this connection has joined
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();
    private readonly object _lock = new();

    /// <summary>Add a (connectionId, group) membership. Idempotent.</summary>
    public bool Join(string connectionId, string group)
    {
        if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException(nameof(connectionId));
        if (string.IsNullOrEmpty(group)) throw new ArgumentException(nameof(group));

        lock (_lock)
        {
            var set = _connectionGroups.GetOrAdd(connectionId, _ => new HashSet<string>(StringComparer.Ordinal));
            return set.Add(group);
        }
    }

    /// <summary>Remove a single (connectionId, group) membership. Idempotent.</summary>
    public bool Leave(string connectionId, string group)
    {
        lock (_lock)
        {
            if (!_connectionGroups.TryGetValue(connectionId, out var set)) return false;
            return set.Remove(group);
        }
    }

    /// <summary>Remove all memberships for a disconnecting connection.</summary>
    public IReadOnlyCollection<string> RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionGroups.TryRemove(connectionId, out var set)) return Array.Empty<string>();
            return set.ToArray();
        }
    }

    /// <summary>Returns a snapshot of the groups a connection has joined.</summary>
    public IReadOnlyCollection<string> GroupsFor(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionGroups.TryGetValue(connectionId, out var set)) return Array.Empty<string>();
            return set.ToArray();
        }
    }

    /// <summary>Count of distinct connections currently tracked.</summary>
    public int ConnectionCount => _connectionGroups.Count;

    /// <summary>
    /// Count of distinct group names with at least one member. Used by the
    /// admin SignalR observability metrics gauge.
    /// </summary>
    public int GroupCount
    {
        get
        {
            lock (_lock)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var set in _connectionGroups.Values)
                    foreach (var g in set)
                        seen.Add(g);
                return seen.Count;
            }
        }
    }

    /// <summary>True if the given connection holds the given group.</summary>
    public bool IsMemberOf(string connectionId, string group)
    {
        lock (_lock)
        {
            return _connectionGroups.TryGetValue(connectionId, out var set) && set.Contains(group);
        }
    }
}
