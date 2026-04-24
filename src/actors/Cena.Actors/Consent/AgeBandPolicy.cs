// =============================================================================
// Cena Platform — AgeBandPolicy (prr-052, EPIC-PRR-C)
//
// Central (and ONLY) policy seam for age-banded parent-dashboard visibility
// + student-veto decisions. Referenced from:
//
//   - GET  /api/v1/parent/minors/{studentAnonId}/dashboard-view
//            (ParentVisibilityDashboardEndpoint)
//   - GET  /api/v1/students/me/parent-visibility
//   - POST /api/v1/students/me/parent-visibility/revoke-purpose
//            (StudentVisibilityEndpoints)
//
// Architecture rule (enforced by NoAgeBandBranchingOutsidePolicyTest):
//   Any `switch` / `if` ladder keyed on <see cref="AgeBand"/> MUST live
//   inside this class, the existing AgeBand.cs classifier, or
//   AgeBandAuthorizationRules (grant/revoke matrix). Endpoint handlers,
//   Vue stores, and aggregate command handlers consume the policy's
//   OUTPUT — never the band directly — so the 13+/16+/18+ transitions
//   have exactly one owner.
//
// Note on file location:
//   The prr-052 task body named this file's path as
//   `src/shared/Cena.Infrastructure/Consent/AgeBandPolicy.cs`. That
//   location was rejected on senior-architect review: Cena.Infrastructure
//   has no ProjectReference to Cena.Actors and cannot see the AgeBand /
//   ConsentPurpose enums. Moving those enums down the stack would churn
//   every ConsentAggregate consumer. The architecturally correct seam is
//   here alongside the other Consent primitives; this is a sibling of
//   AgeBandAuthorizationRules (which encodes ADR-0041's grant/revoke
//   matrix) — together they are the Consent bounded context's complete
//   age-band policy surface.
//
// Legal rationale (all three regimes encoded here, not at call sites):
//
//   Under13  — COPPA (15 USC §6501) Verifiable Parental Consent:
//              child cannot self-govern their own data. Parent has FULL
//              visibility; student has NO veto. Safety flags ALWAYS
//              visible (FERPA §99.36 "health or safety emergency").
//
//   Teen13to15 — GDPR Art. 8 default + COPPA graduated:
//                child gains TRANSPARENCY ("your parent sees this") but
//                not AUTHORITY. No veto on the parent view; per-field
//                visibility flag on the dashboard response informs the
//                student. Mirrors ADR-0041 "Student sees what parent
//                sees" row for the 13–15 band.
//
//   Teen16to17 — Israeli PPL minor-dignity (PPA Feb 2025 opinion) +
//                GDPR maturity presumption:
//                student gains VETO RIGHTS for non-safety purposes.
//                Parent's view is filtered by revoked purposes. Safety
//                flags remain visible (duty-of-care cannot be revoked
//                by a minor — PPA opinion §4b).
//
//   Adult    — PPL/GDPR normal rules:
//              parent visibility requires a fresh grant (not this
//              policy's scope); if granted, student controls like
//              Teen16to17.
//
// Institute policy override (ADR-0041 §"Open questions"):
//   Institute policy can ONLY make a decision MORE restrictive than this
//   policy's default. It cannot expand parent visibility or remove
//   safety flags. An institute that disables veto via
//   <see cref="VisibilityPolicyInput.InstitutePolicyAllowsVeto"/> = false
//   narrows student rights; callers of this policy that ship institute-
//   policy overrides MUST log the override as an audit event.
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Safety-critical visibility categories that REMAIN parent-visible regardless
/// of age band or student veto. Separated from <see cref="ConsentPurpose"/>
/// on purpose — consent can be revoked, safety cannot. The distinction is
/// enforced at the policy layer; a caller cannot accidentally revoke a
/// safety flag because <see cref="AgeBandPolicy.CanStudentVetoPurpose"/>
/// only accepts a <see cref="ConsentPurpose"/> (not this enum).
/// </summary>
public enum SafetyVisibilityCategory
{
    /// <summary>
    /// Session-scoped at-risk signals (self-harm language, severe
    /// withdrawal). Per ADR-0003 session-scoped; visible to parent in
    /// aggregated/safe form (not the raw utterance). Legal duty-of-care.
    /// </summary>
    AtRiskSignal,

    /// <summary>
    /// Attendance patterns (logins per week, sudden cessation). Not
    /// PII-sensitive on its own; duty-of-care per IL Compulsory
    /// Education Law §4(a) and FERPA §99.36 emergency disclosure.
    /// </summary>
    AttendanceSignal,
}

/// <summary>
/// A purpose-or-category rendered on the parent dashboard. Carries the
/// age-band-derived visibility flags so the dashboard client (parent web
/// or student "your-parent-sees-this" view) can render the badge + veto
/// button in exactly the states the policy permits.
/// </summary>
public sealed record ParentVisibleField(
    string FieldKey,
    ParentVisibilityKind Kind,
    bool ParentCanSee,
    bool StudentSeesSameAsParent,
    bool StudentCanVeto,
    string LegalBasisRef);

/// <summary>
/// What the FieldKey represents — a consent-governed purpose or a
/// safety/duty-of-care category.
/// </summary>
public enum ParentVisibilityKind
{
    /// <summary>A consent-governed purpose (see <see cref="ConsentPurpose"/>).</summary>
    ConsentPurpose,

    /// <summary>A safety/duty-of-care flag (see <see cref="SafetyVisibilityCategory"/>).</summary>
    SafetyCategory,
}

/// <summary>
/// Minimal policy input — who asks (age band), what has been previously
/// vetoed, what institute are we scoped to, and whether institute policy
/// permits veto at all.
/// </summary>
public sealed record VisibilityPolicyInput(
    AgeBand SubjectBand,
    IReadOnlySet<ConsentPurpose> VetoedPurposes,
    string InstituteId,
    bool InstitutePolicyAllowsVeto = true);

/// <summary>
/// Policy output: the full dashboard field list plus aggregate flags for
/// client-side gating. Safety categories are always present and always
/// ParentCanSee=true.
/// </summary>
public sealed record VisibilityPolicyOutput(
    IReadOnlyList<ParentVisibleField> Fields,
    bool StudentCanSeeParentView,
    bool StudentHasAnyVetoRight);

/// <summary>
/// Central age-band policy — the ONLY place in the codebase that branches
/// on <see cref="AgeBand"/> for visibility / veto decisions. Endpoints
/// call <see cref="EvaluateDashboard"/> or <see cref="CanStudentVetoPurpose"/>;
/// they never inspect the band directly.
/// </summary>
public static class AgeBandPolicy
{
    /// <summary>
    /// Safety categories that every age-band includes in the parent
    /// dashboard. These cannot be vetoed. Exposed for tests and
    /// policy-introspection endpoints.
    /// </summary>
    public static readonly IReadOnlyList<SafetyVisibilityCategory> AlwaysVisibleSafetyCategories =
        new[]
        {
            SafetyVisibilityCategory.AtRiskSignal,
            SafetyVisibilityCategory.AttendanceSignal,
        };

    /// <summary>
    /// Consent purposes surfaced on the parent dashboard when allowed by
    /// the age band. Narrower than <see cref="ConsentPurpose"/>'s full
    /// enum — MarketingNudges / CrossTenantBenchmarking are not
    /// parent-facing concerns.
    /// </summary>
    public static readonly IReadOnlyList<ConsentPurpose> ParentDashboardPurposes =
        new[]
        {
            ConsentPurpose.ParentDigest,
            ConsentPurpose.MisconceptionDetection,
            ConsentPurpose.AiAssistance,
            ConsentPurpose.TeacherShare,
            ConsentPurpose.AnalyticsAggregation,
            ConsentPurpose.ExternalIntegration,
            ConsentPurpose.LeaderboardDisplay,
        };

    /// <summary>
    /// Evaluate the full parent-dashboard field list for a given subject.
    /// Returns safety categories + consent purposes with the age-band-
    /// appropriate flags on each.
    /// </summary>
    public static VisibilityPolicyOutput EvaluateDashboard(VisibilityPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var studentCanSee = StudentSeesParentView(input.SubjectBand);
        var bandVetoRight = StudentHasAnyVetoRight(input.SubjectBand);
        var studentCanVetoSomething = bandVetoRight && input.InstitutePolicyAllowsVeto;

        var fields = new List<ParentVisibleField>(
            AlwaysVisibleSafetyCategories.Count + ParentDashboardPurposes.Count);

        // -- Safety categories: always visible, never vetoable.
        foreach (var category in AlwaysVisibleSafetyCategories)
        {
            fields.Add(new ParentVisibleField(
                FieldKey: SafetyCategoryKey(category),
                Kind: ParentVisibilityKind.SafetyCategory,
                ParentCanSee: true,
                StudentSeesSameAsParent: studentCanSee,
                StudentCanVeto: false,
                LegalBasisRef: SafetyLegalBasis(category)));
        }

        // -- Consent purposes: band-gated + veto-aware.
        foreach (var purpose in ParentDashboardPurposes)
        {
            var vetoed = input.VetoedPurposes.Contains(purpose);
            var parentCanSee = ParentCanSeePurpose(input.SubjectBand, purpose, vetoed);
            var studentCanVetoThis = CanStudentVetoPurpose(input.SubjectBand, purpose)
                && input.InstitutePolicyAllowsVeto;

            fields.Add(new ParentVisibleField(
                FieldKey: purpose.ToString(),
                Kind: ParentVisibilityKind.ConsentPurpose,
                ParentCanSee: parentCanSee,
                StudentSeesSameAsParent: studentCanSee,
                StudentCanVeto: studentCanVetoThis,
                LegalBasisRef: LegalBasisFor(input.SubjectBand, purpose)));
        }

        return new VisibilityPolicyOutput(
            Fields: fields,
            StudentCanSeeParentView: studentCanSee,
            StudentHasAnyVetoRight: studentCanVetoSomething);
    }

    /// <summary>
    /// Can a student of the given band unilaterally veto the specified
    /// purpose? Safety categories are NOT accepted here — they cannot be
    /// vetoed; the API layer refuses revoke requests naming a safety key.
    /// </summary>
    public static bool CanStudentVetoPurpose(AgeBand band, ConsentPurpose purpose)
    {
        _ = purpose; // all dashboard purposes are equally veto-eligible today
        return band switch
        {
            AgeBand.Under13    => false, // COPPA: child cannot veto
            AgeBand.Teen13to15 => false, // GDPR-K default: transparency only, no veto
            AgeBand.Teen16to17 => true,  // PPA minor-dignity: veto non-safety purposes
            AgeBand.Adult      => true,  // self-governance
            _ => false,
        };
    }

    /// <summary>
    /// Does the student see what the parent sees? At 13+ the student has
    /// a right to transparency; below 13 the dashboard is parent-only.
    /// </summary>
    public static bool StudentSeesParentView(AgeBand band) => band switch
    {
        AgeBand.Under13    => false,
        AgeBand.Teen13to15 => true, // transparency right at 13
        AgeBand.Teen16to17 => true,
        AgeBand.Adult      => true,
        _ => false,
    };

    /// <summary>
    /// Does this band carry any veto right at all? Used by endpoints
    /// that need a coarse yes/no (e.g. 403 a veto attempt from Under13
    /// or Teen13to15 before even parsing the body).
    /// </summary>
    public static bool StudentHasAnyVetoRight(AgeBand band) =>
        band is AgeBand.Teen16to17 or AgeBand.Adult;

    /// <summary>
    /// Parent visibility per purpose, accounting for student veto. A
    /// vetoed purpose is only respected when the band permits veto;
    /// otherwise the veto flag is ignored (defensive — a Teen13to15
    /// veto event should not exist, but if one somehow lands in the
    /// stream we refuse to apply it here).
    /// </summary>
    public static bool ParentCanSeePurpose(AgeBand band, ConsentPurpose purpose, bool vetoedByStudent)
    {
        if (vetoedByStudent && CanStudentVetoPurpose(band, purpose))
        {
            return false;
        }
        return BandAllowsParentVisibility(band, purpose);
    }

    /// <summary>
    /// Coarse band-level parent visibility. Adult has no default parent
    /// visibility (requires fresh grant, out of this policy's scope);
    /// everything else defaults to visible (subject to veto above).
    /// </summary>
    public static bool BandAllowsParentVisibility(AgeBand band, ConsentPurpose purpose)
    {
        _ = purpose; // purpose-specific carve-outs may land later; none today.
        return band switch
        {
            AgeBand.Under13    => true,
            AgeBand.Teen13to15 => true,
            AgeBand.Teen16to17 => true,
            AgeBand.Adult      => false, // fresh-grant required; dashboard should not default-show
            _ => false,
        };
    }

    /// <summary>
    /// Parsed safety category from a revoke request key. Returns true
    /// when the key refers to a safety category — callers that get true
    /// MUST refuse the revoke with 403 (safety flags are not vetoable).
    /// </summary>
    public static bool IsSafetyCategoryKey(string key, out SafetyVisibilityCategory category)
    {
        if (Enum.TryParse<SafetyVisibilityCategory>(key, ignoreCase: false, out category)
            && Enum.IsDefined(typeof(SafetyVisibilityCategory), category))
        {
            return true;
        }
        category = default;
        return false;
    }

    // ── Helpers (private; no band branching outside this class) ─────────

    private static string SafetyCategoryKey(SafetyVisibilityCategory c) => c.ToString();

    private static string SafetyLegalBasis(SafetyVisibilityCategory c) => c switch
    {
        SafetyVisibilityCategory.AtRiskSignal =>
            "FERPA §99.36 (US) / PPL §23B(d) (IL) — health-or-safety emergency",
        SafetyVisibilityCategory.AttendanceSignal =>
            "IL Compulsory Education Law §4(a) / FERPA §99.36",
        _ => "duty-of-care",
    };

    private static string LegalBasisFor(AgeBand band, ConsentPurpose purpose)
    {
        _ = purpose;
        return band switch
        {
            AgeBand.Under13    => "COPPA (15 USC §6501) Verifiable Parental Consent",
            AgeBand.Teen13to15 => "GDPR Art. 8 / ADR-0041 transparency band",
            AgeBand.Teen16to17 => "IL PPA Feb-2025 minor-dignity opinion",
            AgeBand.Adult      => "GDPR/PPL adult self-determination",
            _ => "ADR-0041",
        };
    }
}
