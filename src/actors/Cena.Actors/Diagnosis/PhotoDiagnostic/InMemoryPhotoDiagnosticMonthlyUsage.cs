// =============================================================================
// Cena Platform — InMemoryPhotoDiagnosticMonthlyUsage (EPIC-PRR-J PRR-400)
//
// Process-local counter; fine for single-host dev/test deployments and for
// unit tests. Production DI binds MartenPhotoDiagnosticMonthlyUsage so
// counts survive host restarts and are consistent across actor-host and
// student-api replicas.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Process-local, thread-safe monthly usage counter.</summary>
public sealed class InMemoryPhotoDiagnosticMonthlyUsage : IPhotoDiagnosticMonthlyUsage
{
    private readonly ConcurrentDictionary<(string StudentHash, string Month), int> _counts = new();

    public Task<int> GetAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var key = (studentSubjectIdHash, MonthlyUsageKey.For(asOfUtc));
        _counts.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task<int> IncrementAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var key = (studentSubjectIdHash, MonthlyUsageKey.For(asOfUtc));
        var updated = _counts.AddOrUpdate(key, 1, (_, current) => current + 1);
        return Task.FromResult(updated);
    }
}
