// =============================================================================
// Cena Platform -- Feature Flag Projection (FIND-arch-024)
// Persisted projection for feature flags with audit history.
// =============================================================================

using Cena.Actors.Events;
using Marten.Events;
using Marten.Events.Projections;

namespace Cena.Actors.Projections;

/// <summary>
/// Persisted document for a feature flag.
/// FIND-arch-024: Replaces in-memory storage with event-sourced persistence.
/// </summary>
public class FeatureFlagDocument
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public double RolloutPercent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public string? Reason { get; set; }
    public bool IsDeleted { get; set; }
    public List<FeatureFlagChangeHistory> History { get; set; } = new();
}

public class FeatureFlagChangeHistory
{
    public DateTimeOffset Timestamp { get; set; }
    public bool Enabled { get; set; }
    public double RolloutPercent { get; set; }
    public string ChangedBy { get; set; } = "";
    public string? Reason { get; set; }
}

/// <summary>
/// Inline projection that builds FeatureFlagDocument from events.
/// </summary>
public class FeatureFlagProjection : MultiStreamProjection<FeatureFlagDocument, string>
{
    public FeatureFlagProjection()
    {
        Identity<FeatureFlagSet_V1>(e => e.FlagName);
        Identity<FeatureFlagDeleted_V1>(e => e.FlagName);
        // Lifecycle configured at registration in MartenConfiguration
    }

    public void Apply(FeatureFlagSet_V1 evt, FeatureFlagDocument doc)
    {
        if (string.IsNullOrEmpty(doc.Id))
        {
            doc.Id = evt.FlagName;
            doc.Name = evt.FlagName;
        }

        doc.Enabled = evt.Enabled;
        doc.RolloutPercent = evt.RolloutPercent;
        doc.UpdatedAt = evt.Timestamp;
        doc.UpdatedBy = evt.SetByUserId;
        doc.Reason = evt.Reason;
        doc.IsDeleted = false;

        doc.History.Add(new FeatureFlagChangeHistory
        {
            Timestamp = evt.Timestamp,
            Enabled = evt.Enabled,
            RolloutPercent = evt.RolloutPercent,
            ChangedBy = evt.SetByUserId,
            Reason = evt.Reason
        });
    }

    public void Apply(FeatureFlagDeleted_V1 evt, FeatureFlagDocument doc)
    {
        doc.IsDeleted = true;
        doc.UpdatedAt = evt.Timestamp;
        doc.UpdatedBy = evt.DeletedByUserId;
    }
}
