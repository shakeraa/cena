// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// ActorRole: who is performing a consent action.
//
// Drawn from ADR-0041 §"Parent role authenticated via the same session-cookie
// pattern as students". The role is carried on every consent event and used
// by AgeBandAuthorizationRules to decide whether the action is permitted.
//
// System role is reserved for automated processes (retention worker,
// birthday age-band transitions) and is never mapped from a user session.
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Identity of the actor performing a consent operation. Every consent event
/// carries an ActorRole, and <see cref="AgeBandAuthorizationRules"/> uses it
/// to enforce the age-banded authorization matrix from ADR-0041.
/// </summary>
public enum ActorRole
{
    /// <summary>Parent or legal guardian of a student subject.</summary>
    Parent,

    /// <summary>The student themselves (the data subject).</summary>
    Student,

    /// <summary>A teacher acting in an instructional capacity.</summary>
    Teacher,

    /// <summary>An institute or platform administrator.</summary>
    Admin,

    /// <summary>
    /// Automated system process — retention worker, birthday age-band
    /// transitions, scheduled compliance flips. Never derived from a user
    /// session.
    /// </summary>
    System
}
