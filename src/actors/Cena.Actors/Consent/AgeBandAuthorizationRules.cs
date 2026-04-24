// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// AgeBandAuthorizationRules — the command-handler invariant from
// ADR-0041 §"Age-band authorisation matrix".
//
// This helper is the single seam through which ConsentCommands decides
// whether an (actor, action, purpose) tuple is permitted for a subject
// of a given age band. It is deliberately expressed as a pure function
// over enums so that the authorization matrix is *testable in isolation*
// from aggregate plumbing.
//
// Matrix summary (grant / revoke / review):
//
//   Under13   | Parent|Admin grant&revoke; Student cannot grant nor revoke
//             | Teacher refused; System may revoke on compliance operations
//
//   Teen13to15| Parent|Admin grant&revoke for durable-data purposes
//             | Student cannot grant; Student MAY revoke non-durable purposes
//             | Teacher refused; System may revoke
//
//   Teen16to17| Student grant&revoke for all non-legal-reporting purposes
//             | Parent objection-only for legal-minor-grounds (not modelled here
//             | as a veto — policy layer applies); Admin compliance operations
//
//   Adult     | Student has full control; Parent refused (except fresh adult consent)
//             | Admin reserved for compliance operations; Teacher refused
//
// Durable data purposes (ADR-0041 §"Age-band authorisation matrix" — parent
// must grant for Teen13to15):
//
//   MisconceptionDetection, ParentDigest, TeacherShare, AiAssistance,
//   ExternalIntegration — these touch long-term mastery or cross-context
//   sharing. LeaderboardDisplay and CrossTenantBenchmarking are classified
//   as non-durable student-facing features.
//
// IMPORTANT: this helper is an authorization gate, not a policy engine.
// It refuses unambiguously-wrong combinations (e.g. 10-year-old self-granting
// ParentDigest) and leaves nuanced defaults to the command handler.
// =============================================================================

using System.Collections.Frozen;

namespace Cena.Actors.Consent;

/// <summary>
/// Pure authorization invariant for consent commands. Given an actor, their
/// role, a target subject's age band, and the purpose + action they wish to
/// perform, returns an <see cref="AuthorizationOutcome"/> that either
/// permits or denies the action with a machine-readable reason.
/// </summary>
public static class AgeBandAuthorizationRules
{
    /// <summary>
    /// Durable-data purposes as defined by ADR-0041 §"Age-band authorisation
    /// matrix". Parent consent is required for these purposes below the
    /// minor-dignity boundary (Under13, Teen13to15).
    /// </summary>
    public static readonly FrozenSet<ConsentPurpose> DurableDataPurposes =
        new[]
        {
            ConsentPurpose.MisconceptionDetection,
            ConsentPurpose.ParentDigest,
            ConsentPurpose.TeacherShare,
            ConsentPurpose.AiAssistance,
            ConsentPurpose.ExternalIntegration,
        }.ToFrozenSet();

    /// <summary>Returns <c>true</c> when <paramref name="purpose"/> is a durable-data purpose.</summary>
    public static bool IsDurableDataPurpose(ConsentPurpose purpose) =>
        DurableDataPurposes.Contains(purpose);

    /// <summary>
    /// Can this actor role grant the specified purpose for a subject of the
    /// given age band? This is the ADR-0041 matrix.
    /// </summary>
    public static AuthorizationOutcome CanActorGrant(
        ConsentPurpose purpose,
        ActorRole actorRole,
        AgeBand subjectBand)
    {
        return (subjectBand, actorRole) switch
        {
            // -- Under 13: COPPA VPC — parent or admin only
            (AgeBand.Under13, ActorRole.Parent) => AuthorizationOutcome.Allow(),
            (AgeBand.Under13, ActorRole.Admin) => AuthorizationOutcome.Allow(),
            (AgeBand.Under13, ActorRole.System) => AuthorizationOutcome.Deny(
                "System role may not grant consent for Under13; parent-or-admin only per ADR-0041."),
            (AgeBand.Under13, ActorRole.Student) => AuthorizationOutcome.Deny(
                "Under13 student cannot self-grant consent; COPPA VPC requires parent."),
            (AgeBand.Under13, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot grant consent for Under13; parent-or-admin only."),

            // -- Teen 13-15: parent grants durable data; student MAY grant only
            //    non-durable; teacher refused; admin/system compliance only
            (AgeBand.Teen13to15, ActorRole.Parent) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen13to15, ActorRole.Admin) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen13to15, ActorRole.Student) =>
                IsDurableDataPurpose(purpose)
                    ? AuthorizationOutcome.Deny(
                        $"Teen13to15 student cannot grant durable-data purpose '{purpose}'; parent consent required per ADR-0041.")
                    : AuthorizationOutcome.Allow(),
            (AgeBand.Teen13to15, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot grant consent for Teen13to15."),
            (AgeBand.Teen13to15, ActorRole.System) => AuthorizationOutcome.Deny(
                "System role may not grant consent for Teen13to15."),

            // -- Teen 16-17: student has grant authority; parent & teacher may not
            //    grant on their behalf; admin reserved for compliance
            (AgeBand.Teen16to17, ActorRole.Student) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen16to17, ActorRole.Admin) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen16to17, ActorRole.Parent) => AuthorizationOutcome.Deny(
                "Teen16to17 minor-dignity: parent cannot grant on student's behalf (ADR-0041 PPA 2025)."),
            (AgeBand.Teen16to17, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot grant consent for Teen16to17."),
            (AgeBand.Teen16to17, ActorRole.System) => AuthorizationOutcome.Deny(
                "System role may not grant consent for Teen16to17."),

            // -- Adult: student is the sole authority; parent/teacher refused;
            //    admin reserved for documented compliance operations
            (AgeBand.Adult, ActorRole.Student) => AuthorizationOutcome.Allow(),
            (AgeBand.Adult, ActorRole.Admin) => AuthorizationOutcome.Allow(),
            (AgeBand.Adult, ActorRole.Parent) => AuthorizationOutcome.Deny(
                "Adult subject: parent grants require a fresh adult-consent flow; ADR-0041."),
            (AgeBand.Adult, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot grant consent for Adult subject."),
            (AgeBand.Adult, ActorRole.System) => AuthorizationOutcome.Deny(
                "System role may not grant consent for Adult subject."),

            _ => AuthorizationOutcome.Deny($"Unhandled matrix cell: ({subjectBand},{actorRole})."),
        };
    }

    /// <summary>
    /// Can this actor role revoke the specified purpose for a subject of the
    /// given age band? Revocation is strictly more permissive than grant —
    /// ADR-0041 grants the Teen13to15 student the right to revoke (though
    /// not grant), and the system role can revoke on compliance operations
    /// for any band.
    /// </summary>
    public static AuthorizationOutcome CanActorRevoke(
        ConsentPurpose purpose,
        ActorRole actorRole,
        AgeBand subjectBand)
    {
        // System and Admin may always revoke — compliance / retention flows.
        if (actorRole is ActorRole.System or ActorRole.Admin)
        {
            return AuthorizationOutcome.Allow();
        }

        return (subjectBand, actorRole) switch
        {
            // Under13: only parent/admin/system; student + teacher refused.
            (AgeBand.Under13, ActorRole.Parent) => AuthorizationOutcome.Allow(),
            (AgeBand.Under13, ActorRole.Student) => AuthorizationOutcome.Deny(
                "Under13 student cannot self-revoke; COPPA VPC reserves the right to the parent."),
            (AgeBand.Under13, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot revoke consent for Under13."),

            // Teen13to15: parent always; student MAY revoke (any purpose, even
            // durable — ADR-0041 §"Student can withdraw consent" = "For some
            // purposes (limited)", interpreted liberally at the aggregate
            // layer; stricter policy layers may further restrict).
            (AgeBand.Teen13to15, ActorRole.Parent) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen13to15, ActorRole.Student) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen13to15, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot revoke consent for Teen13to15."),

            // Teen16to17: student full self-revoke; parent legal-minor-grounds
            // objection (modelled as Allow at aggregate layer; policy layer
            // applies additional legal checks).
            (AgeBand.Teen16to17, ActorRole.Student) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen16to17, ActorRole.Parent) => AuthorizationOutcome.Allow(),
            (AgeBand.Teen16to17, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot revoke consent for Teen16to17."),

            // Adult: student sole authority (plus admin/system above).
            (AgeBand.Adult, ActorRole.Student) => AuthorizationOutcome.Allow(),
            (AgeBand.Adult, ActorRole.Parent) => AuthorizationOutcome.Deny(
                "Adult subject: parent cannot revoke; requires fresh adult-consent flow."),
            (AgeBand.Adult, ActorRole.Teacher) => AuthorizationOutcome.Deny(
                "Teacher cannot revoke consent for Adult subject."),

            _ => AuthorizationOutcome.Deny($"Unhandled revoke matrix cell: ({subjectBand},{actorRole})."),
        };
    }

    /// <summary>
    /// Can this actor perform a parent-review workflow? Always restricted to
    /// Parent role; other roles are refused. Review of Adult subjects is
    /// meaningless and refused.
    /// </summary>
    public static AuthorizationOutcome CanActorReviewAsParent(
        ActorRole actorRole,
        AgeBand subjectBand)
    {
        if (actorRole != ActorRole.Parent)
        {
            return AuthorizationOutcome.Deny(
                $"Parent-review is reserved for the Parent role; got {actorRole}.");
        }
        if (subjectBand == AgeBand.Adult)
        {
            return AuthorizationOutcome.Deny(
                "Parent-review is not meaningful for Adult subjects.");
        }
        return AuthorizationOutcome.Allow();
    }
}

/// <summary>
/// Outcome of an authorization decision. <c>Allowed</c> is <c>true</c> when
/// permitted; <c>DenialReason</c> carries a short machine-readable string
/// when denied (empty on allow).
/// </summary>
public readonly record struct AuthorizationOutcome(bool Allowed, string DenialReason)
{
    /// <summary>Build an allow outcome.</summary>
    public static AuthorizationOutcome Allow() => new(true, string.Empty);

    /// <summary>Build a deny outcome with a short reason string.</summary>
    public static AuthorizationOutcome Deny(string reason) => new(false, reason);
}
