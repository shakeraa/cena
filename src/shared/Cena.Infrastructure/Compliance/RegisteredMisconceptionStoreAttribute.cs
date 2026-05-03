// =============================================================================
// Cena Platform — [RegisteredMisconceptionStore] source-level marker (prr-015)
//
// Architecture ratchet tests (NoUnregisteredMisconceptionStoreTest) scan
// every .cs file containing "misconception" + a persistence verb
// ("Insert", "Upsert", "Save", "Marten", "Redis", "cache") and require the
// file to either:
//
//   1. Carry this attribute at the class level, naming the registered
//      store so the reviewer can cross-check with IMisconceptionPiiStoreRegistry
//      registration, or
//
//   2. Be explicitly exempted via the in-test allowlist (entries paired
//      with an ADR or issue reference).
//
// This attribute has zero runtime behaviour — it exists only so the arch
// test can prove a human looked at the file and linked it to a registered
// store. The actual registry entry + purge callback are wired in DI.
//
// Usage:
//
//   [RegisteredMisconceptionStore(
//       Name = "learning-session-misconception-stream",
//       Reason = "ADR-0003 Decision 1: session-scoped misconception events")]
//   public class MisconceptionDetectionService { ... }
//
// See docs/adr/0003-misconception-session-scope.md.
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Marks a class that writes or reads misconception-adjacent PII from a
/// persistence seam. The <see cref="Name"/> MUST match a registered store
/// in <see cref="IMisconceptionPiiStoreRegistry"/>; the
/// <see cref="Reason"/> cites the ADR or compliance rationale.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class RegisteredMisconceptionStoreAttribute : Attribute
{
    /// <summary>
    /// Stable store identifier matching a runtime
    /// <see cref="IMisconceptionPiiStoreRegistry.Register"/> call.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// ADR or compliance reason for the registration. Required — naked
    /// annotations with no rationale are banned (same posture as
    /// <see cref="MlExcludedAttribute"/>).
    /// </summary>
    public string Reason { get; }

    public RegisteredMisconceptionStoreAttribute(string Name, string Reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        this.Name = Name;
        this.Reason = Reason;
    }
}
