// =============================================================================
// Cena Platform — IStuckTypeClassifier (RDY-063 Phase 1)
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Primary classifier surface. Implementations must:
///   - Never throw on bad input; return <see cref="StuckDiagnosis.Unknown"/>
///     with the appropriate <see cref="StuckDiagnosisSource"/>.
///   - Never mutate <paramref name="context"/>.
///   - Never fetch cross-session data — only reason over what's passed.
///   - Never emit math claims, LaTeX, or any free-text scaffolding
///     content. Output is label-only.
/// </summary>
public interface IStuckTypeClassifier
{
    /// <summary>
    /// Diagnose the stuck-type for a given snapshot.
    /// </summary>
    Task<StuckDiagnosis> DiagnoseAsync(StuckContext context, CancellationToken ct = default);
}
