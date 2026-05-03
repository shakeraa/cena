// =============================================================================
// Cena Platform -- SignalR Group Manager (SES-001.2)
// Thread-safe mapping of studentId to connectionId for targeted event delivery.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Api.Host.Hubs;

/// <summary>
/// Manages the mapping between student IDs and SignalR connection IDs.
/// Enforces max 1 active connection per student.
/// Used by NatsSignalRBridge to determine which students are currently connected.
/// </summary>
public sealed class SignalRGroupManager
{
    // studentId → connectionId (only the most recent connection)
    private readonly ConcurrentDictionary<string, string> _studentConnections = new();
    // connectionId → studentId (reverse lookup for cleanup)
    private readonly ConcurrentDictionary<string, string> _connectionStudents = new();

    /// <summary>
    /// Register a new connection for a student. Replaces any previous connection.
    /// </summary>
    public void AddConnection(string studentId, string connectionId)
    {
        // Remove old connection if exists
        if (_studentConnections.TryGetValue(studentId, out var oldConnectionId))
        {
            _connectionStudents.TryRemove(oldConnectionId, out _);
        }

        _studentConnections[studentId] = connectionId;
        _connectionStudents[connectionId] = studentId;
    }

    /// <summary>
    /// Remove a connection. Only removes if the connectionId matches the current one for the student.
    /// </summary>
    public void RemoveConnection(string studentId, string connectionId)
    {
        // Only remove if this is still the active connection for the student
        if (_studentConnections.TryGetValue(studentId, out var currentConnectionId)
            && currentConnectionId == connectionId)
        {
            _studentConnections.TryRemove(studentId, out _);
        }

        _connectionStudents.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Get the current connection ID for a student, or null if not connected.
    /// </summary>
    public string? GetConnectionId(string studentId)
    {
        return _studentConnections.TryGetValue(studentId, out var connectionId) ? connectionId : null;
    }

    /// <summary>
    /// Get the student ID for a given connection, or null.
    /// </summary>
    public string? GetStudentId(string connectionId)
    {
        return _connectionStudents.TryGetValue(connectionId, out var studentId) ? studentId : null;
    }

    /// <summary>
    /// Returns all currently connected student IDs.
    /// </summary>
    public IReadOnlyCollection<string> ConnectedStudentIds => _studentConnections.Keys.ToArray();

    /// <summary>
    /// Returns true if the given student has an active connection.
    /// </summary>
    public bool IsConnected(string studentId) => _studentConnections.ContainsKey(studentId);

    /// <summary>
    /// Total number of active connections.
    /// </summary>
    public int ConnectionCount => _studentConnections.Count;
}
