// =============================================================================
// Cena Platform -- Mastery Seepage Service (TENANCY-P2a)
// Applies cross-track mastery transfer at enrollment time per ADR-0002.
// Model C: seepage with decay. One-time, auditable, never inflates.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Mastery;

public interface IMasterySeepageService
{
    SeepageResult ApplySeepage(
        StudentProfileSnapshot snapshot,
        string sourceEnrollmentId,
        string targetEnrollmentId,
        string sourceSubject,
        string targetSubject,
        IReadOnlyList<string> overlappingConceptIds);
}

public record SeepageResult(int ConceptsSeeded, List<MasterySeepageApplied_V1> Events);

/// <summary>
/// Applies cross-track mastery seepage when a student enrolls in a second track
/// that shares concepts with an existing track. Per ADR-0002 (Model C):
/// - Same-subject transfer factor: 0.60
/// - Cross-subject transfer factor: 0.20
/// - Max seeded PKnown: 0.50 (never skip prerequisite validation)
/// - One-time at enrollment, then tracks evolve independently
/// </summary>
public class MasterySeepageService : IMasterySeepageService
{
    public const double SameSubjectFactor = 0.60;
    public const double CrossSubjectFactor = 0.20;
    public const double MaxSeededPKnown = 0.50;

    public SeepageResult ApplySeepage(
        StudentProfileSnapshot snapshot,
        string sourceEnrollmentId,
        string targetEnrollmentId,
        string sourceSubject,
        string targetSubject,
        IReadOnlyList<string> overlappingConceptIds)
    {
        var factor = string.Equals(sourceSubject, targetSubject, StringComparison.OrdinalIgnoreCase)
            ? SameSubjectFactor
            : CrossSubjectFactor;

        var events = new List<MasterySeepageApplied_V1>();
        var now = DateTimeOffset.UtcNow;

        foreach (var conceptId in overlappingConceptIds)
        {
            var sourceKey = MasteryKeys.Key(sourceEnrollmentId, conceptId);
            if (!snapshot.ConceptMastery.TryGetValue(sourceKey, out var sourceMastery))
                continue;

            var targetKey = MasteryKeys.Key(targetEnrollmentId, conceptId);

            // Never overwrite existing target mastery data
            if (snapshot.ConceptMastery.ContainsKey(targetKey))
                continue;

            var seededPKnown = Math.Min(sourceMastery.PKnown * factor, MaxSeededPKnown);

            snapshot.ConceptMastery[targetKey] = new ConceptMasteryState
            {
                PKnown = seededPKnown,
                SourceEnrollmentId = sourceEnrollmentId,
                SeepageFactor = factor
            };

            // Seep half-life too if source has it
            if (snapshot.HalfLifeMap.TryGetValue(sourceKey, out var sourceHalfLife))
            {
                snapshot.HalfLifeMap[targetKey] = sourceHalfLife;
            }

            events.Add(new MasterySeepageApplied_V1(
                snapshot.StudentId, sourceEnrollmentId, targetEnrollmentId,
                conceptId, factor, sourceMastery.PKnown, seededPKnown, now));
        }

        return new SeepageResult(events.Count, events);
    }
}
