// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// Aggregate root for the Consent bounded context. Stream key
// `consent-{subjectId}`; each stream is an independent subject's consent
// history (student OR parent — both are valid subject shapes).
//
// Replaces the scattered consent state previously held on StudentActor +
// GdprConsentManager with a single event-sourced primitive. The existing
// GdprConsentManager is retained as a read-side facade (see its updated
// source) that projects this aggregate's state into the legacy DTO shape.
//
// Design constraints (ADR-0038 + ADR-0041):
//   - PII fields on events are crypto-shredded via EncryptedFieldAccessor.
//   - Command handlers MUST enforce age-band authorization via
//     AgeBandAuthorizationRules BEFORE producing events.
//   - No command handler reaches into Marten directly — the aggregate is
//     pure state + event application; persistence is the caller's job.
// =============================================================================

using Cena.Actors.Consent.Events;

namespace Cena.Actors.Consent;

/// <summary>
/// Aggregate root for a single consent stream. Stream key:
/// <c>consent-{subjectId}</c>. State is <see cref="ConsentState"/>; event
/// application is delegated to the state type so the aggregate itself
/// remains a thin stream-key + dispatch shell.
/// </summary>
public sealed class ConsentAggregate
{
    /// <summary>Conventional stream-key prefix for this aggregate.</summary>
    public const string StreamKeyPrefix = "consent-";

    /// <summary>
    /// Build the stream key for a subject id. Callers must have already
    /// validated that <paramref name="subjectId"/> is non-empty.
    /// </summary>
    public static string StreamKey(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException(
                "Subject id must be non-empty for stream-key construction.",
                nameof(subjectId));
        }
        return StreamKeyPrefix + subjectId;
    }

    /// <summary>Backing state carried by this aggregate instance.</summary>
    public ConsentState State { get; } = new();

    /// <summary>
    /// Apply an inbound domain event. Unknown events are silently ignored to
    /// tolerate forward migration — matches the <c>LearningSessionAggregate</c>
    /// convention.
    /// </summary>
    public void Apply(object @event)
    {
        switch (@event)
        {
            case ConsentGranted_V1 granted:
                State.Apply(granted);
                break;
            case ConsentRevoked_V1 revoked:
                State.Apply(revoked);
                break;
            case ConsentPurposeAdded_V1 added:
                State.Apply(added);
                break;
            case ConsentReviewedByParent_V1 reviewed:
                State.Apply(reviewed);
                break;
            case StudentVisibilityVetoed_V1 vetoed:
                State.Apply(vetoed);
                break;
            case StudentVisibilityRestored_V1 restored:
                State.Apply(restored);
                break;
        }
    }

    /// <summary>
    /// Replay a sequence of events into a fresh aggregate. Used by the
    /// read-side facade to rebuild state from a Marten stream.
    /// </summary>
    public static ConsentAggregate ReplayFrom(IEnumerable<object> events)
    {
        var aggregate = new ConsentAggregate();
        foreach (var evt in events)
        {
            aggregate.Apply(evt);
        }
        return aggregate;
    }
}
