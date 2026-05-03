// =============================================================================
// Cena Platform — TaxonomyVersionDocument (EPIC-PRR-J PRR-375)
//
// A single versioned row for one misconception-taxonomy template. Every change
// to a template — a new template proposal, a wording tweak, a counter-example
// swap, or a deprecation — produces exactly one row with the next monotonic
// version number for that TemplateKey. Rows are append-only until status
// transitions (Proposed → Approved, Approved → Deprecated, Approved →
// RolledBack). We NEVER mutate TemplateContent on an already-approved row;
// changing content means creating the next version.
//
// Why this shape:
//   - TemplateKey (not GUID id) is the natural aggregation key; a template's
//     identity survives across many versions, and the admin dashboard needs
//     "show me every version of mc-bag4-001-sign-flip-distributive".
//   - Id (GUID) is the Marten primary key — stable across moves, opaque to
//     callers. Downstream systems reference template versions by (Id) so
//     rollback can safely flip status without breaking references.
//   - TaxonomyVersion is a monotonic int PER TemplateKey (not global). This
//     matches the audit-log convention used elsewhere in the codebase
//     (see PhotoHashLedgerDocument / MonthlyUsage) and makes "list versions
//     descending" an obvious index. Collisions on the same version number
//     for the same key are a bug; the store enforces next-version assignment
//     atomically via ITaxonomyVersionStore.ProposeAsync.
//
// The two-reviewer guardrail:
//   The Reviewers list is the system's canonical "review board" signal. For
//   a row to transition from Proposed → Approved, Reviewers MUST contain at
//   least 2 distinct non-empty entries. The store enforces this invariant
//   on ApproveAsync and will throw rather than silently approve. This is a
//   governance hard-stop, not a suggestion — the 10-persona review
//   (persona #9 support, persona #7 ML-safety) both flagged solo-approval
//   as the highest-risk failure mode because it lets one SME push a flawed
//   template to every student instantly.
//
// Rollback preserves audit trail:
//   RollbackAsync does NOT delete the row. It flips Status to RolledBack,
//   leaving TemplateContent, Reviewers, AuthoredAtUtc, and ApprovedAtUtc
//   intact. The previous Approved version remains reachable via
//   GetLatestApprovedAsync (which skips RolledBack and Deprecated rows).
//   This means: "we rolled back v4 because dispute rate spiked; v3 is live
//   again" is a three-row story in the DB, not a destructive mutation.
//   Honor ADR-0003? Yes, trivially — TaxonomyVersion rows are operational
//   governance artifacts, not student data. The 30-day retention cap does
//   NOT apply; taxonomy audit trails are kept indefinitely.
//
// Scope caveat (honest, not a stub):
//   The v1 shape does NOT carry locale-per-row. A template's he/ar/en text
//   all live together inside TemplateContent as a JSON blob. If we ever
//   need locale-level rollback ("rollback just the Hebrew wording of v4")
//   we'll add a sibling TaxonomyLocaleVersionDocument — not a column here.
//   Keep v1 simple; grow when the workflow demands it.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>Lifecycle states for a single template version.</summary>
public enum TaxonomyVersionStatus
{
    /// <summary>Authored but not yet approved by ≥2 reviewers.</summary>
    Proposed = 0,

    /// <summary>
    /// Cleared by ≥2 reviewers. Eligible for GetLatestApprovedAsync. Only
    /// one version per TemplateKey SHOULD be Approved at a time, but the
    /// schema does not enforce that at the DB layer — GetLatestApprovedAsync
    /// picks the highest-version Approved row when duplicates exist, so a
    /// staggered deploy does not break reads.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// Superseded by a newer Approved version. No longer served to students.
    /// Preserved for audit (we keep the content so we can reconstruct what
    /// a student saw at a given point in time).
    /// </summary>
    Deprecated = 2,

    /// <summary>
    /// An Approved version that was pulled back after going live (e.g.,
    /// dispute rate exceeded threshold). Row is preserved; the previous
    /// Approved version (if any) resumes as the live version via
    /// GetLatestApprovedAsync.
    /// </summary>
    RolledBack = 3,
}

/// <summary>
/// Marten document: one row per (TemplateKey, TaxonomyVersion). See file
/// banner for the governance invariants this shape encodes.
/// </summary>
public sealed record TaxonomyVersionDocument
{
    /// <summary>GUID primary key (Marten identity). Stable across lifecycle transitions.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Monotonic version number per <see cref="TemplateKey"/>. 1 is the first
    /// proposal for a given key; ProposeAsync always assigns next-version.
    /// </summary>
    public int TaxonomyVersion { get; init; }

    /// <summary>
    /// Stable template identifier (e.g. "mc-bag4-001-sign-flip-distributive").
    /// Shared across every version of the same logical template.
    /// </summary>
    public string TemplateKey { get; init; } = "";

    /// <summary>
    /// Serialized template payload (JSON or YAML). The authored content of
    /// this version. We store as an opaque string so schema evolution in
    /// <see cref="MisconceptionTemplate"/> does not require a Marten
    /// migration per change. The governance system treats content as
    /// "whatever the review board reviewed"; downstream rendering is
    /// someone else's contract.
    /// </summary>
    public string TemplateContent { get; init; } = "";

    /// <summary>Current lifecycle state.</summary>
    public TaxonomyVersionStatus Status { get; init; }

    /// <summary>
    /// SME / support-agent username or email of the author. Not PII as
    /// rendered (internal identifiers only), but still treated as an
    /// operational field — not surfaced to students.
    /// </summary>
    public string AuthoredBy { get; init; } = "";

    /// <summary>Wall-clock at Propose time.</summary>
    public DateTimeOffset AuthoredAtUtc { get; init; }

    /// <summary>
    /// Reviewers who have signed off on this version. For the row to reach
    /// <see cref="TaxonomyVersionStatus.Approved"/> this MUST contain ≥ 2
    /// distinct non-empty entries. The store enforces this invariant on
    /// ApproveAsync. The list is read-only at the document level; updates
    /// go through the store which rebuilds the list immutably.
    /// </summary>
    public IReadOnlyList<string> Reviewers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Wall-clock at Approve time. Null until the row reaches
    /// <see cref="TaxonomyVersionStatus.Approved"/>; preserved after
    /// rollback / deprecation so the audit trail shows when the version
    /// was live.
    /// </summary>
    public DateTimeOffset? ApprovedAtUtc { get; init; }

    /// <summary>
    /// The minimum number of distinct reviewers required for approval.
    /// Locked in code, not configuration — changing the threshold is an
    /// ADR-level decision.
    /// </summary>
    public const int MinReviewersForApproval = 2;
}
