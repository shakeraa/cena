// =============================================================================
// Cena Platform — Item Provenance + Deliverable<T> phantom-type (prr-008)
//
// Enforces the 2026-04-15 decision locked by CLAUDE.md non-negotiable #4 and
// ADR-0043 bagrut-reference-only-enforcement: Ministry-published Bagrut items
// are REFERENCE-ONLY. They can be ingested as structural inspiration for the
// BagrutRecreation pipeline (see BagrutRecreation.cs + ADR-0032), but they
// must NEVER reach a student. Student-facing items are AI-recreated and
// CAS-gated (ADR-0002), or are teacher-authored originals.
//
// This file provides the compile-time half of the three-layer defense:
//
//   1. Compile-time (this file): `Deliverable<T>` can only be constructed
//      from `AiRecreated` or `TeacherAuthoredOriginal` provenance; the
//      factory throws on `MinistryBagrut`. Any downstream code that insists
//      on typing its delivery payloads as `Deliverable<T>` gets the check
//      for free.
//
//   2. Runtime (IItemDeliveryGate.cs): a single chokepoint right before
//      serialization that SIEM-logs and throws on Ministry provenance,
//      catching paths that bypass the compile-time wrapper.
//
//   3. Architecture test (BagrutRecreationOnlyTest.cs): scans outbound
//      DTO surfaces for field names that would leak the Ministry reference
//      identifier to students.
//
// Cross-refs: ADR-0002 (SymPy oracle), ADR-0032 (CAS-gated ingestion),
// CLAUDE.md non-negotiable "Bagrut reference-only".
// =============================================================================

namespace Cena.Actors.Content;

/// <summary>
/// Origin classification of an item. Drives the delivery-gate decision.
/// </summary>
/// <remarks>
/// Adding a new enum value is a load-bearing change: <see cref="Deliverable{T}.From"/>
/// must be revisited, the <c>IItemDeliveryGate</c> implementation must be revisited,
/// and the architecture test must be updated. The ADR for this file explicitly
/// enumerates the allowed provenance kinds — a new entry needs a new ADR section.
/// </remarks>
public enum ProvenanceKind
{
    /// <summary>
    /// AI-generated recreation that passed the CAS gate AND expert review
    /// (see <see cref="BagrutRecreationAggregate.IsApprovedForProduction"/>).
    /// Deliverable to students.
    /// </summary>
    AiRecreated = 1,

    /// <summary>
    /// Original item authored by a platform teacher (not copied from any
    /// Ministry source). Deliverable to students.
    /// </summary>
    TeacherAuthoredOriginal = 2,

    /// <summary>
    /// Raw Ministry-published Bagrut item. REFERENCE-ONLY. Used by the
    /// recreation pipeline as structural inspiration; never surfaced to
    /// students. Passing this to <see cref="Deliverable{T}.From"/> throws;
    /// passing this through <c>IItemDeliveryGate.AssertDeliverable</c>
    /// throws and SIEM-logs.
    /// </summary>
    MinistryBagrut = 3,
}

/// <summary>
/// Origin record attached to an item. Captures the kind, when provenance
/// was recorded, and a human-readable source string (e.g. the
/// BagrutRecreation aggregate id for <see cref="ProvenanceKind.AiRecreated"/>,
/// the teacher id for <see cref="ProvenanceKind.TeacherAuthoredOriginal"/>,
/// the Ministry code for <see cref="ProvenanceKind.MinistryBagrut"/>).
/// </summary>
public readonly record struct Provenance(
    ProvenanceKind Kind,
    DateTimeOffset Recorded,
    string Source)
{
    /// <summary>
    /// True iff the kind is one of the deliverable classifications.
    /// Mirrored by the runtime gate in <c>IItemDeliveryGate</c>.
    /// </summary>
    public bool IsDeliverable =>
        Kind == ProvenanceKind.AiRecreated
        || Kind == ProvenanceKind.TeacherAuthoredOriginal;
}

/// <summary>
/// Phantom-type wrapper that, at construction time, refuses to carry a value
/// whose provenance is <see cref="ProvenanceKind.MinistryBagrut"/>. Downstream
/// code that types its delivery payloads as <c>Deliverable&lt;T&gt;</c> gets
/// the 2026-04-15 Bagrut-reference-only invariant enforced at the type system.
/// </summary>
/// <remarks>
/// This is defense-in-depth with <c>IItemDeliveryGate.AssertDeliverable</c>.
/// The gate is the single runtime chokepoint; this wrapper is the earlier
/// compile-time tripwire that surfaces the invariant in type signatures.
/// Threading full provenance through the existing item pipeline is Sprint-2
/// scope (see prr-008 task body, "scope cuts" section). Today this type is
/// used by the gate itself and by new code paths that voluntarily adopt it.
/// </remarks>
public readonly record struct Deliverable<T>(T Value, Provenance Provenance)
{
    /// <summary>
    /// Constructs a <see cref="Deliverable{T}"/>, enforcing the Bagrut-reference-only
    /// invariant. Throws <see cref="InvalidOperationException"/> if provenance is
    /// <see cref="ProvenanceKind.MinistryBagrut"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When <paramref name="provenance"/>.<see cref="Provenance.Kind"/> is
    /// <see cref="ProvenanceKind.MinistryBagrut"/>. The exception message cross-references
    /// the relevant ADR and the BagrutRecreation pipeline so the offending caller
    /// gets a pointer to the correct fix (run the item through <c>BagrutRecreationAggregate</c>).
    /// </exception>
    public static Deliverable<T> From(T value, Provenance provenance)
    {
        if (provenance.Kind == ProvenanceKind.MinistryBagrut)
        {
            throw new InvalidOperationException(
                "MinistryBagrut-provenanced content is reference-only per the 2026-04-15 "
                + "decision (CLAUDE.md non-negotiable 'Bagrut reference-only', "
                + "ADR-0032 CAS-gated ingestion, ADR-0043 bagrut-reference-only-enforcement). "
                + "Use BagrutRecreationAggregate to generate an AI recreation, have it "
                + "CAS-verified (ADR-0002) and expert-approved, then deliver the "
                + "AiRecreated item instead. Source=" + provenance.Source);
        }
        return new Deliverable<T>(value, provenance);
    }
}
