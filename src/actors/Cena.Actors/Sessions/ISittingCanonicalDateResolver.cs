// =============================================================================
// Cena Platform — ISittingCanonicalDateResolver (prr-226, ADR-0050 §3)
//
// The scheduler needs a canonical date for an ExamTarget.Sitting tuple to
// decide two things:
//   1. Is this target's sitting the NEAREST one not yet passed? (primary
//      ranking input to ActiveExamTargetPolicy.)
//   2. Is the student within the 14-day exam-week lock window? (ADR-0050 §10.)
//
// Per ADR-0050 §3, SittingCode is a named tuple (AcademicYear, Season, Moed).
// The Ministry exam catalog (prr-220, lives in Cena.Student.Api.Host) knows
// the canonical date. This interface is the small seam the Actor-layer
// scheduler depends on so the Sessions bounded context does not pick up the
// full catalog dependency graph — production wiring composes a resolver over
// the catalog; tests and pre-catalog hosts use an in-memory dictionary.
//
// TZ note (prr-157): the returned DateTimeOffset carries the Israel-local
// wall-clock moment the sitting begins (per Ministry publication). Callers
// that need a comparison against "today in Israel" should convert both sides
// via IsraelTimeZoneResolver.ConvertFromUtc to normalise before subtracting
// — the ActiveExamTargetPolicy does this for determinism across server TZs.
// =============================================================================

namespace Cena.Actors.Sessions;

using Cena.Actors.StudentPlan;

/// <summary>
/// Resolves a SittingCode tuple to its catalog canonical date. Implementations
/// MUST be deterministic — the same <see cref="SittingCode"/> always maps to
/// the same <see cref="DateTimeOffset"/>. Returns null if the catalog does
/// not know the sitting (e.g. future academic year not yet published); the
/// scheduler falls back to insertion-order ranking for unknown sittings.
/// </summary>
public interface ISittingCanonicalDateResolver
{
    /// <summary>
    /// Resolve the canonical date/time for a sitting. Null when unknown.
    /// </summary>
    DateTimeOffset? Resolve(SittingCode sitting);
}

/// <summary>
/// Empty resolver — every lookup returns null. Safe default for tests and
/// hosts that have not wired the catalog yet. When every target's sitting
/// resolves to null the policy ranks by insertion order + mastery deficit
/// only.
/// </summary>
public sealed class EmptySittingCanonicalDateResolver : ISittingCanonicalDateResolver
{
    /// <summary>Singleton instance.</summary>
    public static readonly EmptySittingCanonicalDateResolver Instance = new();

    private EmptySittingCanonicalDateResolver() { }

    /// <inheritdoc />
    public DateTimeOffset? Resolve(SittingCode sitting) => null;
}

/// <summary>
/// Test-only resolver backed by an explicit dictionary. Production code
/// should inject a catalog-backed implementation instead.
/// </summary>
public sealed class InMemorySittingCanonicalDateResolver : ISittingCanonicalDateResolver
{
    private readonly IReadOnlyDictionary<SittingCode, DateTimeOffset> _map;

    /// <summary>Construct from a finite map of sitting → canonical date.</summary>
    public InMemorySittingCanonicalDateResolver(
        IReadOnlyDictionary<SittingCode, DateTimeOffset> map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <inheritdoc />
    public DateTimeOffset? Resolve(SittingCode sitting)
        => _map.TryGetValue(sitting, out var value) ? value : null;
}
