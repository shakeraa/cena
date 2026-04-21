// =============================================================================
// Cena Platform — ExamTarget value object (prr-218, ADR-0050 §1)
//
// Per ADR-0050 §1 "ExamTarget shape (normative)". A StudentPlan is a list of
// these records. There is no `grade`, `track`, or `deadline` at the student
// root — all lives on the target.
//
// Key constraints from ADR-0050:
//   - ADR-0050 §1: no free-text fields. ReasonTag enum replaces free-text.
//   - ADR-0050 §2: ExamCode is the Ministry numeric שאלון code, not the
//     display label. Labels are localised metadata keyed off ExamCode.
//   - ADR-0050 §3: Sittings are named tuples, never DateTimeOffset. The
//     canonical date is derived by the catalog (PRR-220).
//   - ADR-0050 §5: invariants are server-enforced by StudentPlanAggregate,
//     not this VO.
//   - ADR-0050 §6: ArchivedAt is terminal — no further mutations.
//   - ADR-0050 §8: TrackCode includes "2U" baseline + ModuleCode variant.
//
// Intentionally a plain record — all validation happens at the command
// handler seam, per the established ExamDateSet_V1 / WeeklyTimeBudgetSet_V1
// event contract that does not re-validate in the event body.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Who assigned the exam target. Drives permission rules (students can
/// archive anything they own; classroom-assigned targets require teacher
/// or self-archive with audit trail per ADR-0050 §Q3 lawful basis).
/// </summary>
public enum ExamTargetSource
{
    /// <summary>Student self-assigned (the default case).</summary>
    Student = 0,

    /// <summary>Classroom-assigned by a teacher within the student's
    /// enrollment (ADR-0001 bounded context).</summary>
    Classroom = 1,

    /// <summary>Tenant-assigned by an institute administrator (covers
    /// exam-board-mandated plans).</summary>
    Tenant = 2,

    /// <summary>Migrated from the legacy single-target StudentPlanConfig
    /// during the prr-219 upcast. Distinct from Student so analytics can
    /// track the migration cohort for 24 months.</summary>
    Migration = 3,
}

/// <summary>
/// Why the student added this target. Enum-only per ADR-0050 §1 — free-text
/// rejected by four-lens convergence (ethics + privacy + redteam + finops).
/// </summary>
public enum ReasonTag
{
    /// <summary>Retaking an exam (מועד ב or later).</summary>
    Retake = 0,

    /// <summary>A new subject the student is picking up for the first time.</summary>
    NewSubject = 1,

    /// <summary>Review-only — student already passed, wants brush-up.</summary>
    ReviewOnly = 2,

    /// <summary>Enrichment — student exceeding standard curriculum.</summary>
    Enrichment = 3,
}

/// <summary>
/// Season slot within the academic year. Summer / Winter per the Ministry
/// moed taxonomy.
/// </summary>
public enum SittingSeason
{
    /// <summary>Summer session (קיץ).</summary>
    Summer = 0,

    /// <summary>Winter session (חורף).</summary>
    Winter = 1,
}

/// <summary>
/// Moed within a season. A / B / C / Special (מיוחד).
/// </summary>
public enum SittingMoed
{
    /// <summary>Moed A (primary sitting).</summary>
    A = 0,

    /// <summary>Moed B (re-sit).</summary>
    B = 1,

    /// <summary>Moed C (extra re-sit where offered).</summary>
    C = 2,

    /// <summary>Special session (e.g. IDF reservists).</summary>
    Special = 3,
}

/// <summary>
/// Named tuple identifying a specific exam sitting. Per ADR-0050 §3,
/// raw <see cref="DateTimeOffset"/> deadlines are banned at the aggregate
/// boundary — this tuple dereferences to a canonical date via the catalog
/// (PRR-220) so the moed taxonomy stays first-class.
/// </summary>
/// <param name="AcademicYear">Hebrew academic-year string e.g.
/// "תשפ״ו" (or the Gregorian equivalent e.g. "2026-2027" for non-Ministry
/// catalogs such as SAT/PET).</param>
/// <param name="Season">Summer or Winter.</param>
/// <param name="Moed">Moed A/B/C/Special.</param>
public readonly record struct SittingCode(
    string AcademicYear,
    SittingSeason Season,
    SittingMoed Moed)
{
    /// <summary>Canonical wire form for structural comparison + logs.</summary>
    public override string ToString()
        => $"{AcademicYear}/{Season}/{Moed}";
}

/// <summary>
/// Ministry primary-key exam code per ADR-0050 §2 + §7.
/// Catalog-owned stable identifier — never the localised display label.
/// Examples: "BAGRUT_MATH_5U" (Ministry subject 035, question paper 035581),
/// "PET" (NITE regulator, not Ministry), "SAT_MATH" (College Board).
/// </summary>
/// <remarks>
/// Wrapped in a strongly-typed record struct so primitive-obsession-hunter
/// static checks (rdy-009) don't flag raw strings, and so downstream
/// catalog lookups compile-time enforce the shape.
/// </remarks>
/// <param name="Value">The canonical catalog key. Non-empty,
/// case-sensitive, `[A-Z0-9_]+`. Validated at construction.</param>
public readonly record struct ExamCode
{
    /// <summary>Canonical string form.</summary>
    public string Value { get; }

    /// <summary>Construct an ExamCode. Throws on empty or whitespace.</summary>
    public ExamCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ExamCode value must be non-empty.", nameof(value));
        }
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Track within an exam. Per ADR-0050 §8, includes "2U" baseline and the
/// ModuleCode variant for Bagrut English Modules A–G.
/// </summary>
/// <remarks>
/// Encoded as a string so the catalog (PRR-220) owns the enumeration — the
/// aggregate does not need to validate which tracks a given ExamCode
/// supports; that validation belongs to the catalog.
/// </remarks>
/// <param name="Value">Track identifier: "2U" | "3U" | "4U" | "5U" |
/// "ModuleA".."ModuleG" | null (when the exam has no track concept).</param>
public readonly record struct TrackCode
{
    /// <summary>Canonical string form.</summary>
    public string Value { get; }

    /// <summary>Construct a TrackCode. Throws on empty or whitespace.</summary>
    public TrackCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("TrackCode value must be non-empty.", nameof(value));
        }
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Enrollment reference per ADR-0001 tenancy isolation. Null for
/// Source=Student (self-assigned, no enrollment coupling) or for migrated
/// pre-tenancy data. Non-null for Source=Classroom | Tenant.
/// </summary>
/// <param name="Value">Opaque enrollment identifier.</param>
public readonly record struct EnrollmentId
{
    /// <summary>Canonical string form.</summary>
    public string Value { get; }

    /// <summary>Construct. Throws on empty or whitespace.</summary>
    public EnrollmentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("EnrollmentId value must be non-empty.", nameof(value));
        }
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Strongly-typed id for an ExamTarget. Generated on the write path via
/// <see cref="New"/>; opaque to consumers.
/// </summary>
public readonly record struct ExamTargetId
{
    /// <summary>Canonical string form.</summary>
    public string Value { get; }

    /// <summary>Construct. Throws on empty or whitespace.</summary>
    public ExamTargetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ExamTargetId value must be non-empty.", nameof(value));
        }
        Value = value;
    }

    /// <summary>Freshly-generated identifier, "et-" prefix + Guid.N.</summary>
    public static ExamTargetId New() => new("et-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Strongly-typed id for a user (student / teacher / admin). Carries who
/// created the target in <see cref="ExamTarget.AssignedById"/>.
/// </summary>
public readonly record struct UserId
{
    /// <summary>Canonical string form.</summary>
    public string Value { get; }

    /// <summary>Construct. Throws on empty or whitespace.</summary>
    public UserId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("UserId value must be non-empty.", nameof(value));
        }
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Family of the exam code, used to gate the Bagrut-only
/// <see cref="ExamTarget.QuestionPaperCodes"/> invariant per
/// PRR-243 / ADR-0050 §1.
/// </summary>
/// <remarks>
/// The family is inferred from the <see cref="ExamCode"/> string prefix so
/// the aggregate does not need to consult the catalog at command-time
/// (the catalog validates the specific paper codes separately via
/// <c>IQuestionPaperCatalogValidator</c>). Classification rules:
/// <list type="bullet">
///   <item><description><c>BAGRUT_*</c> → <see cref="Bagrut"/> (requires ≥1 paper code).</description></item>
///   <item><description><c>SAT_*</c>, <c>PET</c>/<c>PET_*</c> → <see cref="Standardized"/> (forbids paper codes).</description></item>
///   <item><description>Anything else (IB, Tawjihi, enrichment codes) → <see cref="Other"/>
///     — neither required nor forbidden, paper codes are optional.</description></item>
/// </list>
/// </remarks>
public enum ExamCodeFamily
{
    /// <summary>Israeli Bagrut. Multi-שאלון mandatory per Ministry reality.</summary>
    Bagrut = 0,

    /// <summary>SAT, PET, and other standardised single-paper tests.</summary>
    Standardized = 1,

    /// <summary>Any other family — paper codes optional.</summary>
    Other = 2,
}

/// <summary>
/// Static helpers for inferring <see cref="ExamCodeFamily"/> from a raw
/// <see cref="ExamCode"/>. Kept in one place so the command handler,
/// architecture tests, and migration service all agree on the mapping.
/// </summary>
public static class ExamCodeFamilyClassifier
{
    /// <summary>
    /// Infer the family from the exam-code prefix. Case-sensitive
    /// (catalog codes are uppercase by convention).
    /// </summary>
    public static ExamCodeFamily Classify(ExamCode code)
    {
        var value = code.Value;
        if (value.StartsWith("BAGRUT_", StringComparison.Ordinal))
            return ExamCodeFamily.Bagrut;
        if (value.StartsWith("SAT_", StringComparison.Ordinal))
            return ExamCodeFamily.Standardized;
        // PET and PET_* both classify as Standardized (single-paper).
        if (value == "PET" || value.StartsWith("PET_", StringComparison.Ordinal))
            return ExamCodeFamily.Standardized;
        return ExamCodeFamily.Other;
    }

    /// <summary>True when the code is Bagrut-family (≥1 שאלון required).</summary>
    public static bool IsBagrut(ExamCode code) => Classify(code) == ExamCodeFamily.Bagrut;

    /// <summary>True when the code is Standardized-family (no שאלון).</summary>
    public static bool IsStandardized(ExamCode code) => Classify(code) == ExamCodeFamily.Standardized;
}

/// <summary>
/// The authoritative shape per ADR-0050 §1. Immutable record — every
/// mutation produces a new instance via <c>with</c>-expressions at the
/// aggregate root.
/// </summary>
/// <param name="Id">Stable target identifier.</param>
/// <param name="Source">Who assigned the target.</param>
/// <param name="AssignedById">Student id when Source=Student, teacher id
/// when Source=Classroom, admin id when Source=Tenant, system id when
/// Source=Migration.</param>
/// <param name="EnrollmentId">Enrollment reference per ADR-0001; null for
/// Source=Student | Migration.</param>
/// <param name="ExamCode">Catalog primary key (Ministry שאלון / PET /
/// SAT code).</param>
/// <param name="Track">Track within the exam (2U/3U/4U/5U/ModuleCode) or
/// null when the exam has no track concept.</param>
/// <param name="QuestionPaperCodes">Ministry שאלון codes the student is
/// preparing for, per PRR-243 / ADR-0050 §1. Non-empty for Bagrut family;
/// empty for Standardized (SAT/PET). Insertion-ordered; the command
/// handler de-duplicates before emitting events.</param>
/// <param name="Sitting">Primary sitting tuple — catalog resolves to
/// canonical date. Applies to all שאלונים unless overridden
/// per-paper.</param>
/// <param name="PerPaperSittingOverride">Optional sparse override map:
/// paper-code → sitting for שאלונים taken at a different sitting than the
/// primary (e.g. שאלון 1 in Grade-11 Summer, שאלונים 2+3 in Grade-12).
/// Null OR empty when no overrides. Keys must be a subset of
/// <paramref name="QuestionPaperCodes"/>; values must differ from the
/// primary <paramref name="Sitting"/> (minimal-map invariant).</param>
/// <param name="WeeklyHours">Hours per week committed to this target.
/// Range 1..40. Aggregate invariant: sum ≤ 40 across active targets.</param>
/// <param name="ReasonTag">Why the student added this target (enum-only).</param>
/// <param name="CreatedAt">When the target was first added.</param>
/// <param name="ArchivedAt">When archived (terminal). Null while
/// active.</param>
public sealed record ExamTarget(
    ExamTargetId Id,
    ExamTargetSource Source,
    UserId AssignedById,
    EnrollmentId? EnrollmentId,
    ExamCode ExamCode,
    TrackCode? Track,
    IReadOnlyList<string> QuestionPaperCodes,
    SittingCode Sitting,
    IReadOnlyDictionary<string, SittingCode>? PerPaperSittingOverride,
    int WeeklyHours,
    ReasonTag? ReasonTag,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt)
{
    /// <summary>True while the target is active (not archived).</summary>
    public bool IsActive => ArchivedAt is null;

    /// <summary>Minimum weekly-hour allocation per active target.</summary>
    public const int MinWeeklyHours = 1;

    /// <summary>Maximum weekly-hour allocation per active target and per
    /// aggregate total (ADR-0050 §5: sum ≤ 40).</summary>
    public const int MaxWeeklyHours = 40;

    /// <summary>Inferred family for invariant gating (Bagrut vs
    /// Standardized vs Other).</summary>
    public ExamCodeFamily Family => ExamCodeFamilyClassifier.Classify(ExamCode);
}
