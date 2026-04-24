// =============================================================================
// Cena Platform -- GDPR Processing Purpose Classification (SEC-006)
// Defines granular consent purposes with age-appropriate defaults per GDPR Article 8.
// Each purpose maps to a lawful basis under GDPR Article 6 and specifies defaults
// for adults and minors (under 16).
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// GDPR Article 6 lawful basis for data processing.
/// </summary>
public enum LawfulBasis
{
    /// <summary>Article 6(1)(b) - Processing necessary for contract performance.</summary>
    Contract,

    /// <summary>Article 6(1)(c) - Legal obligation compliance.</summary>
    LegalObligation,

    /// <summary>Article 6(1)(d) - Vital interests protection.</summary>
    VitalInterests,

    /// <summary>Article 6(1)(e) - Public task in public interest.</summary>
    PublicTask,

    /// <summary>Article 6(1)(f) - Legitimate interests pursued by controller.</summary>
    LegitimateInterest,

    /// <summary>Article 6(1)(a) - Consent of the data subject.</summary>
    Consent
}

/// <summary>
/// Specifies a human-readable description for a processing purpose.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PurposeDescriptionAttribute : Attribute
{
    /// <summary>Human-readable description of the processing purpose.</summary>
    public string Description { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PurposeDescriptionAttribute"/> class.
    /// </summary>
    /// <param name="description">The human-readable description.</param>
    public PurposeDescriptionAttribute(string description)
    {
        Description = description;
    }
}

/// <summary>
/// Specifies the default consent value for a processing purpose.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DefaultValueAttribute : Attribute
{
    /// <summary>Default consent value for adults.</summary>
    public bool Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultValueAttribute"/> class.
    /// </summary>
    /// <param name="value">The default consent value for adults.</param>
    public DefaultValueAttribute(bool value)
    {
        Value = value;
    }
}

/// <summary>
/// Specifies the GDPR Article 6 lawful basis for a processing purpose.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LawfulBasisAttribute : Attribute
{
    /// <summary>The GDPR lawful basis for this processing purpose.</summary>
    public LawfulBasis Basis { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LawfulBasisAttribute"/> class.
    /// </summary>
    /// <param name="basis">The GDPR lawful basis.</param>
    public LawfulBasisAttribute(LawfulBasis basis)
    {
        Basis = basis;
    }
}

/// <summary>
/// Specifies the default consent value for minors (under 16) per GDPR Article 8.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MinorDefaultAttribute : Attribute
{
    /// <summary>Default consent value for minors under 16.</summary>
    public bool Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MinorDefaultAttribute"/> class.
    /// </summary>
    /// <param name="value">The default consent value for minors.</param>
    public MinorDefaultAttribute(bool value)
    {
        Value = value;
    }
}

/// <summary>
/// Defines granular data processing purposes for GDPR consent management.
/// Each purpose specifies its lawful basis, description, and age-appropriate defaults.
/// </summary>
/// <remarks>
/// AccountAuth and SessionContinuity are always lawful (contract necessity) and cannot be disabled.
/// All other purposes require explicit consent and may have different defaults for minors.
/// </remarks>
public enum ProcessingPurpose
{
    /// <summary>
    /// Account authentication - always lawful and required for account operation.
    /// Cannot be disabled by the user.
    /// </summary>
    [PurposeDescription("Account authentication - required for account access and security")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Contract)]
    [MinorDefault(true)]
    AccountAuth,

    /// <summary>
    /// Session continuity - always lawful and required for session management.
    /// Cannot be disabled by the user.
    /// </summary>
    [PurposeDescription("Session management - required to maintain your learning session")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Contract)]
    [MinorDefault(true)]
    SessionContinuity,

    /// <summary>
    /// Personalized learning recommendations based on student performance and preferences.
    /// </summary>
    [PurposeDescription("Personalized learning recommendations tailored to your progress")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(true)]
    AdaptiveRecommendation,

    /// <summary>
    /// Comparing student progress with anonymized peer groups.
    /// </summary>
    [PurposeDescription("Compare your progress with anonymized peer groups")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    PeerComparison,

    /// <summary>
    /// Displaying student on leaderboards visible to other students.
    /// </summary>
    [PurposeDescription("Display your achievements on leaderboards visible to other students")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    LeaderboardDisplay,

    /// <summary>
    /// Social interactions including posts, comments, and community features.
    /// </summary>
    [PurposeDescription("Social features including posts, comments, and community interactions")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    SocialFeatures,

    /// <summary>
    /// Third-party AI tutoring services (Anthropic Claude) for personalized help.
    /// </summary>
    [PurposeDescription("AI tutoring assistance powered by third-party services")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    ThirdPartyAi,

    /// <summary>
    /// Focus tracking, behavior analysis, and learning analytics.
    /// </summary>
    [PurposeDescription("Learning analytics including focus tracking and behavior analysis")]
    [DefaultValue(true)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    BehavioralAnalytics,

    /// <summary>
    /// Cross-school and cross-tenant benchmarking for institutional comparisons.
    /// </summary>
    [PurposeDescription("Cross-school benchmarking for anonymized institutional comparisons")]
    [DefaultValue(false)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    CrossTenantBenchmarking,

    /// <summary>
    /// Promotional notifications and marketing nudges.
    /// Default OFF for minors per GDPR Article 8 best practices.
    /// </summary>
    [PurposeDescription("Promotional notifications and feature recommendations")]
    [DefaultValue(false)]
    [LawfulBasis(LawfulBasis.Consent)]
    [MinorDefault(false)]
    MarketingNudges
}

/// <summary>
/// Extension methods for <see cref="ProcessingPurpose"/> enum.
/// </summary>
public static class ProcessingPurposeExtensions
{
    /// <summary>
    /// Determines whether this processing purpose is always required and cannot be disabled.
    /// </summary>
    /// <param name="purpose">The processing purpose to check.</param>
    /// <returns>
    /// <c>true</c> if the purpose is always required (AccountAuth or SessionContinuity);
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool IsAlwaysRequired(this ProcessingPurpose purpose)
    {
        return purpose is ProcessingPurpose.AccountAuth or ProcessingPurpose.SessionContinuity;
    }

    /// <summary>
    /// Gets the default consent value for this processing purpose based on user age.
    /// </summary>
    /// <param name="purpose">The processing purpose.</param>
    /// <param name="isMinor">
    /// <c>true</c> if the user is under 16 years old (minor); otherwise, <c>false</c>.
    /// </param>
    /// <returns>
    /// The default consent value for the specified age group.
    /// </returns>
    public static bool GetDefaultConsent(this ProcessingPurpose purpose, bool isMinor)
    {
        var fieldInfo = purpose.GetType().GetField(purpose.ToString());
        if (fieldInfo is null)
        {
            return false;
        }

        if (isMinor)
        {
            var minorAttr = fieldInfo.GetCustomAttributes(typeof(MinorDefaultAttribute), false)
                .Cast<MinorDefaultAttribute>()
                .FirstOrDefault();
            if (minorAttr is not null)
            {
                return minorAttr.Value;
            }
        }

        var defaultAttr = fieldInfo.GetCustomAttributes(typeof(DefaultValueAttribute), false)
            .Cast<DefaultValueAttribute>()
            .FirstOrDefault();

        return defaultAttr?.Value ?? false;
    }

    /// <summary>
    /// Gets the human-readable description for this processing purpose.
    /// </summary>
    /// <param name="purpose">The processing purpose.</param>
    /// <returns>
    /// The description from the <see cref="PurposeDescriptionAttribute"/>,
    /// or the enum value name if no description is defined.
    /// </returns>
    public static string GetDescription(this ProcessingPurpose purpose)
    {
        var fieldInfo = purpose.GetType().GetField(purpose.ToString());
        if (fieldInfo is null)
        {
            return purpose.ToString();
        }

        var attr = fieldInfo.GetCustomAttributes(typeof(PurposeDescriptionAttribute), false)
            .Cast<PurposeDescriptionAttribute>()
            .FirstOrDefault();

        return attr?.Description ?? purpose.ToString();
    }
}
