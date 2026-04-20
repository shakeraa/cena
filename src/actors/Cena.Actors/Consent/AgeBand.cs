// =============================================================================
// Cena Platform — ConsentAggregate (prr-155, EPIC-PRR-A)
//
// AgeBand: the four-band model from ADR-0041 §"Four age bands, not one".
// Replaces the single IsMinor<16 boolean at MeGdprEndpoints.cs:529 with a
// model that correctly distinguishes COPPA VPC (<13), GDPR-K default (13–15),
// PPA minor-dignity (16–17), and adult.
//
// Birthday transitions re-band the student on their next session; the
// StudentAgeBandChanged_V1 event (ADR-0041 §"Consent events") is emitted
// when a band flip happens mid-stream.
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Legal-regime age band used as the primary input to
/// <see cref="AgeBandAuthorizationRules"/>.
/// </summary>
public enum AgeBand
{
    /// <summary>Ages 0–12 — COPPA Verifiable Parental Consent required.</summary>
    Under13,

    /// <summary>Ages 13–15 — GDPR-K default, parent grants durable data purposes.</summary>
    Teen13to15,

    /// <summary>Ages 16–17 — PPA minor-dignity; student largely self-governs.</summary>
    Teen16to17,

    /// <summary>Ages 18+ — student is the sole consent authority.</summary>
    Adult
}

/// <summary>
/// Compute the <see cref="AgeBand"/> for a subject's age-in-years.
/// </summary>
public static class AgeBandComputation
{
    /// <summary>
    /// Map integer age (years) to the band. Negative ages are treated as
    /// <see cref="AgeBand.Under13"/> defensively — upstream code must not
    /// feed this a negative value but we refuse to throw from a pure
    /// classification function.
    /// </summary>
    public static AgeBand FromAge(int ageInYears)
    {
        if (ageInYears < 13) return AgeBand.Under13;
        if (ageInYears < 16) return AgeBand.Teen13to15;
        if (ageInYears < 18) return AgeBand.Teen16to17;
        return AgeBand.Adult;
    }

    /// <summary>
    /// Compute the band from a date-of-birth as of a reference instant.
    /// </summary>
    public static AgeBand FromDateOfBirth(DateOnly dateOfBirth, DateTimeOffset asOf)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;
        return FromAge(age);
    }
}
