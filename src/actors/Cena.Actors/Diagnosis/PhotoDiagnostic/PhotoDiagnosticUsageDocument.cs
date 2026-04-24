// =============================================================================
// Cena Platform — PhotoDiagnosticUsageDocument (EPIC-PRR-J PRR-400)
//
// Marten document for monthly usage rows. Primary key is the
// "{StudentHash}|{YYYY-MM}" composite so per-(student, month) lookups are
// single-row. Not event-sourced: this is a running counter, not a domain
// fact stream; events would just be noise.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed record PhotoDiagnosticUsageDocument
{
    /// <summary>"{studentSubjectIdHash}|{yyyy-MM}" composite key.</summary>
    public string Id { get; init; } = "";

    public string StudentSubjectIdHash { get; init; } = "";

    /// <summary>Calendar-month key (yyyy-MM).</summary>
    public string MonthKey { get; init; } = "";

    public int Count { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public static string KeyOf(string studentSubjectIdHash, string monthKey) =>
        $"{studentSubjectIdHash}|{monthKey}";
}
