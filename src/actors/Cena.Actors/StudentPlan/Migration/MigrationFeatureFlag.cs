// =============================================================================
// Cena Platform — Migration feature flag (prr-219)
//
// Per prr-219 DoD: "Feature flag default OFF — deploy safe."
// Per task spec: flag key `Cena:Migration:StudentPlanV2Enabled`.
// Per-tenant and per-student overrides so ops can enable staged rollout
// (internal → 1% → 10% → 100%).
//
// Implementation is DI-driven so tests can swap in a static "always on"
// variant.
// =============================================================================

namespace Cena.Actors.StudentPlan.Migration;

/// <summary>
/// Gate for the prr-219 migration. Checked at two layers:
///   1. The admin-triggered batch runner ("migrate institute X now").
///   2. The runtime first-login upcast path (future — out of scope for
///      prr-218/219 initial ship, but the gate is wired so that layer
///      lands behind the same flag).
/// </summary>
public interface IMigrationFeatureFlag
{
    /// <summary>True when the migration is enabled for a given
    /// institute. Used by the batch runner.</summary>
    bool IsEnabledForTenant(string tenantId);

    /// <summary>True when the migration is enabled for a given student
    /// (checked ahead of any runtime upcast path).</summary>
    bool IsEnabledForStudent(string tenantId, string studentAnonId);
}

/// <summary>
/// Config-backed flag reader. Reads from a simple snapshot map so hot
/// reloads re-read the dictionary without rebuilding the DI graph.
/// </summary>
public sealed class MigrationFeatureFlag : IMigrationFeatureFlag
{
    /// <summary>The canonical config key.</summary>
    public const string ConfigKey = "Cena:Migration:StudentPlanV2Enabled";

    private readonly Func<MigrationFeatureFlagSnapshot> _snapshotProvider;

    /// <summary>
    /// Wire with a snapshot provider — the delegate is called per check
    /// so changes to configuration take effect without a restart.
    /// </summary>
    public MigrationFeatureFlag(Func<MigrationFeatureFlagSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    /// <inheritdoc />
    public bool IsEnabledForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var snap = _snapshotProvider() ?? MigrationFeatureFlagSnapshot.Off;
        if (!snap.GlobalDefault && !snap.EnabledTenants.Contains(tenantId))
        {
            return false;
        }
        return !snap.BlockedTenants.Contains(tenantId);
    }

    /// <inheritdoc />
    public bool IsEnabledForStudent(string tenantId, string studentAnonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentAnonId);

        if (!IsEnabledForTenant(tenantId))
        {
            return false;
        }

        var snap = _snapshotProvider() ?? MigrationFeatureFlagSnapshot.Off;
        return !snap.BlockedStudents.Contains(studentAnonId);
    }
}

/// <summary>
/// Immutable snapshot of the flag. Built from appsettings or an admin
/// override store. Default is safe (all-off).
/// </summary>
/// <param name="GlobalDefault">When true, the flag is on for every tenant
/// except those in <paramref name="BlockedTenants"/>.</param>
/// <param name="EnabledTenants">When <paramref name="GlobalDefault"/> is
/// false, only these tenants are considered enabled.</param>
/// <param name="BlockedTenants">Tenants that are explicitly blocked —
/// overrides <paramref name="GlobalDefault"/>=true.</param>
/// <param name="BlockedStudents">Individual students blocked (e.g. known
/// broken legacy data; operator pauses migration for that row).</param>
public sealed record MigrationFeatureFlagSnapshot(
    bool GlobalDefault,
    IReadOnlySet<string> EnabledTenants,
    IReadOnlySet<string> BlockedTenants,
    IReadOnlySet<string> BlockedStudents)
{
    /// <summary>Default-off snapshot, used when no config is present.</summary>
    public static readonly MigrationFeatureFlagSnapshot Off = new(
        GlobalDefault: false,
        EnabledTenants: new HashSet<string>(),
        BlockedTenants: new HashSet<string>(),
        BlockedStudents: new HashSet<string>());
}
