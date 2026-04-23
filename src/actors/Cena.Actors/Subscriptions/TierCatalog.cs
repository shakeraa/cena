// =============================================================================
// Cena Platform — TierCatalog (EPIC-PRR-I PRR-291, ADR-0057 §6)
//
// Compile-time canonical pricing. Editing ANY value in this file has legal,
// commercial, and contractual implications and MUST land via a PR reviewed
// by the pricing decision-holder. See BUSINESS-MODEL-001-pricing-10-persona-review.md
// for the rationale behind each anchor.
//
// Sibling-discount policy is expressed as a function rather than a constant
// record field because the discount depth depends on sibling ordinal (1-2
// get one rate, 3+ get a lower rate, per persona #5 household-cap review).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Canonical tier catalog. All retail prices VAT-inclusive (ADR-0057 §5).
/// Agorot (1/100 ILS) arithmetic throughout (ADR-0057 §4).
/// </summary>
public static class TierCatalog
{
    // Launch prices per BUSINESS-MODEL-001-pricing-10-persona-review.md
    private const long BasicMonthlyAgorot = 7_900L;       // ₪79.00
    private const long BasicAnnualAgorot = 79_000L;       // ₪790.00 (10 months for 12)
    private const long PlusMonthlyAgorot = 22_900L;       // ₪229.00 (decoy)
    private const long PlusAnnualAgorot = 229_000L;       // ₪2,290.00 (10-for-12)
    private const long PremiumMonthlyAgorot = 24_900L;    // ₪249.00 (target)
    private const long PremiumAnnualAgorot = 249_000L;    // ₪2,490.00 (10-for-12)
    private const long SchoolSkuPerStudentAgorot = 3_500L; // ₪35.00/student/mo B2B

    // Sibling discount structure (persona #5 household cap, ADR-0057):
    private const long SiblingFirstSecondAgorot = 14_900L;   // ₪149/mo siblings 1-2
    private const long SiblingThirdPlusAgorot = 9_900L;      // ₪99/mo sibling 3+

    /// <summary>Monthly price for a sibling add-on at ordinal <paramref name="ordinal"/>.</summary>
    /// <param name="ordinal">1-based ordinal (1 = first sibling added, i.e., second student in household).</param>
    public static Money SiblingMonthlyPrice(int ordinal)
    {
        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal), ordinal, "Sibling ordinal is 1-based; primary student is not a sibling.");
        }
        long agorot = ordinal <= 2 ? SiblingFirstSecondAgorot : SiblingThirdPlusAgorot;
        return Money.FromAgorot(agorot);
    }

    // B2B School SKU volume pricing brackets (PRR-341, BUSINESS-MODEL-001
    // persona #8 school coordinator + #10 CFO). Bracket boundaries and
    // prices come from the 2026-04-22 10-persona pricing review. Any
    // edit to these constants has the same legal/commercial weight as
    // editing the retail prices above and MUST land via a PR reviewed
    // by the pricing decision-holder (ADR-0057 §6).
    private const int VolumeSmallBracketMinStudents = 100;   // first volume bracket
    private const int VolumeMidBracketMinStudents = 500;     // second volume bracket
    private const int VolumeLargeBracketMinStudents = 1_500; // third volume bracket
    private const long VolumeSmallBracketPerStudentAgorot = 3_500L; // ₪35/student/mo (100-499)
    private const long VolumeMidBracketPerStudentAgorot = 2_900L;   // ₪29/student/mo (500-1499)
    private const long VolumeLargeBracketPerStudentAgorot = 2_400L; // ₪24/student/mo (1500+)

    /// <summary>
    /// Per-student monthly price for a B2B school contract with
    /// <paramref name="studentCount"/> seats. Brackets (inclusive lower bound):
    /// <list type="bullet">
    ///   <item>    0 –   99 students — ₪35/student/mo (entry-bracket default)</item>
    ///   <item>  100 –  499 students — ₪35/student/mo (small)</item>
    ///   <item>  500 – 1499 students — ₪29/student/mo (mid)</item>
    ///   <item> 1500+     students   — ₪24/student/mo (large)</item>
    /// </list>
    /// Contracts below 100 seats are uncommon in Israeli secondary schools
    /// but not banned; they pay the same per-student rate as the 100-499
    /// bracket so small pilots are not penalised. The <c>MonthlyPrice</c>
    /// field on <c>TierCatalog.Get(SchoolSku)</c> continues to hold the
    /// entry-bracket rate as the canonical single-seat anchor. This
    /// function is the bracketed variant for contract-level pricing.
    /// </summary>
    /// <param name="studentCount">Number of student seats on the contract. Must be ≥ 1.</param>
    public static Money SchoolSkuMonthlyPricePerStudent(int studentCount)
    {
        if (studentCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(studentCount), studentCount,
                "Student count must be at least 1.");
        }
        long perStudentAgorot = studentCount switch
        {
            >= VolumeLargeBracketMinStudents => VolumeLargeBracketPerStudentAgorot,
            >= VolumeMidBracketMinStudents => VolumeMidBracketPerStudentAgorot,
            _ => VolumeSmallBracketPerStudentAgorot,
        };
        return Money.FromAgorot(perStudentAgorot);
    }

    /// <summary>
    /// Total monthly contract value for a B2B school with
    /// <paramref name="studentCount"/> seats. Equals
    /// <c>SchoolSkuMonthlyPricePerStudent(studentCount) × studentCount</c>.
    /// Prefer this for contract-total arithmetic so the bracket lookup
    /// and multiplication happen atomically and rounding is consistent.
    /// </summary>
    public static Money SchoolSkuMonthlyContractTotal(int studentCount)
    {
        var perStudent = SchoolSkuMonthlyPricePerStudent(studentCount);
        return Money.FromAgorot(perStudent.Amount * studentCount);
    }

    /// <summary>
    /// Name of the volume bracket the given <paramref name="studentCount"/>
    /// falls into — useful for invoice line-items and sales pipelines where
    /// the bracket identity has commercial significance independent of the
    /// numeric price. Values: <c>small</c>, <c>mid</c>, <c>large</c>.
    /// </summary>
    public static string SchoolSkuVolumeBracket(int studentCount)
    {
        if (studentCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(studentCount), studentCount,
                "Student count must be at least 1.");
        }
        return studentCount switch
        {
            >= VolumeLargeBracketMinStudents => "large",
            >= VolumeMidBracketMinStudents => "mid",
            _ => "small",
        };
    }

    // ----- Caps -----
    private static readonly UsageCaps BasicCaps = new(
        SonnetEscalationsPerWeek: 20,
        PhotoDiagnosticsPerMonth: 0,                           // Basic: no diagnostic
        PhotoDiagnosticsHardCapPerMonth: 0,
        HintRequestsPerMonth: 500);

    private static readonly UsageCaps PlusCaps = new(
        SonnetEscalationsPerWeek: UsageCaps.Unlimited,
        PhotoDiagnosticsPerMonth: 20,                          // Plus differentiator
        PhotoDiagnosticsHardCapPerMonth: 20,                   // hard = soft at Plus
        HintRequestsPerMonth: 2_000);

    private static readonly UsageCaps PremiumCaps = new(
        SonnetEscalationsPerWeek: UsageCaps.Unlimited,
        PhotoDiagnosticsPerMonth: 100,                         // soft cap
        PhotoDiagnosticsHardCapPerMonth: 300,                  // hard cap
        HintRequestsPerMonth: 2_000);

    private static readonly UsageCaps SchoolSkuCaps = new(
        SonnetEscalationsPerWeek: UsageCaps.Unlimited,
        PhotoDiagnosticsPerMonth: 50,                          // classroom-managed
        PhotoDiagnosticsHardCapPerMonth: 100,
        HintRequestsPerMonth: 2_000);

    private static readonly UsageCaps UnsubscribedCaps = new(
        SonnetEscalationsPerWeek: 0,
        PhotoDiagnosticsPerMonth: 0,
        PhotoDiagnosticsHardCapPerMonth: 0,
        HintRequestsPerMonth: 0);

    // ----- Features -----
    private static readonly TierFeatureFlags BasicFeatures = new(
        ParentDashboard: false, TutorHandoffPdf: false, ArabicDashboard: false,
        PrioritySupport: false, ClassroomDashboard: false,
        TeacherAssignedPractice: false, Sso: false);

    private static readonly TierFeatureFlags PlusFeatures = new(
        ParentDashboard: false, TutorHandoffPdf: false, ArabicDashboard: false,
        PrioritySupport: false, ClassroomDashboard: false,
        TeacherAssignedPractice: false, Sso: false);

    private static readonly TierFeatureFlags PremiumFeatures = new(
        ParentDashboard: true, TutorHandoffPdf: true, ArabicDashboard: true,
        PrioritySupport: true, ClassroomDashboard: false,
        TeacherAssignedPractice: false, Sso: false);

    private static readonly TierFeatureFlags SchoolSkuFeatures = new(
        ParentDashboard: false,        // fenced per ADR-0057 §8
        TutorHandoffPdf: false,        // fenced
        ArabicDashboard: false,
        PrioritySupport: true,
        ClassroomDashboard: true,
        TeacherAssignedPractice: true,
        Sso: true);

    private static readonly TierFeatureFlags UnsubscribedFeatures = new(
        ParentDashboard: false, TutorHandoffPdf: false, ArabicDashboard: false,
        PrioritySupport: false, ClassroomDashboard: false,
        TeacherAssignedPractice: false, Sso: false);

    /// <summary>
    /// Canonical definition for a given tier. School SKU uses per-student
    /// monthly price stored in <see cref="TierDefinition.MonthlyPrice"/>; the
    /// annual field is equivalent to 12 * monthly (no 10-for-12 promotion).
    /// </summary>
    public static TierDefinition Get(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Basic => new TierDefinition(
            Tier: SubscriptionTier.Basic,
            MonthlyPrice: Money.FromAgorot(BasicMonthlyAgorot),
            AnnualPrice: Money.FromAgorot(BasicAnnualAgorot),
            Caps: BasicCaps,
            Features: BasicFeatures,
            IsRetail: true,
            IsDecoy: false),

        SubscriptionTier.Plus => new TierDefinition(
            Tier: SubscriptionTier.Plus,
            MonthlyPrice: Money.FromAgorot(PlusMonthlyAgorot),
            AnnualPrice: Money.FromAgorot(PlusAnnualAgorot),
            Caps: PlusCaps,
            Features: PlusFeatures,
            IsRetail: true,
            IsDecoy: true),                 // analytics-only; never UI-surfaced

        SubscriptionTier.Premium => new TierDefinition(
            Tier: SubscriptionTier.Premium,
            MonthlyPrice: Money.FromAgorot(PremiumMonthlyAgorot),
            AnnualPrice: Money.FromAgorot(PremiumAnnualAgorot),
            Caps: PremiumCaps,
            Features: PremiumFeatures,
            IsRetail: true,
            IsDecoy: false),

        SubscriptionTier.SchoolSku => new TierDefinition(
            Tier: SubscriptionTier.SchoolSku,
            MonthlyPrice: Money.FromAgorot(SchoolSkuPerStudentAgorot),
            AnnualPrice: Money.FromAgorot(SchoolSkuPerStudentAgorot * 12L),
            Caps: SchoolSkuCaps,
            Features: SchoolSkuFeatures,
            IsRetail: false,
            IsDecoy: false),

        SubscriptionTier.Unsubscribed => new TierDefinition(
            Tier: SubscriptionTier.Unsubscribed,
            MonthlyPrice: Money.ZeroIls,
            AnnualPrice: Money.ZeroIls,
            Caps: UnsubscribedCaps,
            Features: UnsubscribedFeatures,
            IsRetail: false,
            IsDecoy: false),

        _ => throw new ArgumentOutOfRangeException(
            nameof(tier), tier, "Unknown subscription tier."),
    };

    /// <summary>All retail tiers in display order (Basic, Plus, Premium).</summary>
    public static IReadOnlyList<TierDefinition> RetailTiers { get; } = new[]
    {
        Get(SubscriptionTier.Basic),
        Get(SubscriptionTier.Plus),
        Get(SubscriptionTier.Premium),
    };
}
