// =============================================================================
// Cena Platform — IStudentEntitlementResolver (EPIC-PRR-I PRR-310)
//
// Hot-path seam: resolve the current entitlement for a student. Consumed
// by LLM router, diagnostic intake, dashboard-visibility middleware.
// Implementation delegates to the read model (projected from subscription
// events).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Resolves the active <see cref="StudentEntitlementView"/> for a student.
/// Used as the single source of truth at every enforcement seam.
/// </summary>
public interface IStudentEntitlementResolver
{
    /// <summary>
    /// Return the entitlement view for <paramref name="studentSubjectIdEncrypted"/>,
    /// or a synthesized <see cref="SubscriptionTier.Unsubscribed"/> view if no
    /// subscription covers this student.
    /// </summary>
    Task<StudentEntitlementView> ResolveAsync(string studentSubjectIdEncrypted, CancellationToken ct);
}
