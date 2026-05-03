// =============================================================================
// Cena Platform — Event Store Scaling Configuration (EVENT-SCALE-001)
// Per Dr. Rami Khalil (#45): snapshot every 50, monthly partitions, async daemon.
//
// Target: 73M events/year (1000 students × 200 events/day × 365).
// =============================================================================

namespace Cena.Infrastructure.EventStore;

/// <summary>
/// EVENT-SCALE-001: Configuration for event store scaling strategies.
/// Applied in MartenConfiguration.cs during StoreOptions setup.
/// </summary>
public static class EventStoreScalingConfig
{
    /// <summary>
    /// Snapshot frequency for inline projections. Every 50 events, Marten
    /// persists a snapshot so rebuilds start from the latest snapshot,
    /// not from event zero.
    /// </summary>
    public const int SnapshotEveryNEvents = 50;

    /// <summary>
    /// Projections that must be synchronous (inline) — these power
    /// real-time student-facing features.
    /// </summary>
    public static readonly string[] InlineProjections =
    {
        "StudentProfileSnapshot",
        "ActiveSessionSnapshot",
        "LearningSessionQueueProjection"
    };

    /// <summary>
    /// Projections that can be rebuilt asynchronously via Marten's daemon.
    /// These power admin/analytics features where eventual consistency is acceptable.
    /// </summary>
    public static readonly string[] AsyncProjections =
    {
        "MisconceptionTrendProjection",
        "IrtCalibrationProjection",
        "TeacherReportProjection",
        "ClassFeedItemProjection",
        "StudentLifetimeStatsProjection",
        "ThreadSummaryProjection",
        "SecurityAuditProjection",
        "QuestionListProjection"
    };

    /// <summary>
    /// Monthly partition retention: partitions older than this are candidates
    /// for archival to cold storage (separate tablespace or S3).
    /// </summary>
    public static readonly TimeSpan PartitionRetention = TimeSpan.FromDays(365);

    /// <summary>
    /// Number of monthly partitions to keep in the hot tablespace.
    /// Older partitions are detached (not dropped) and moved to cold storage.
    /// </summary>
    public const int HotPartitionMonths = 12;
}

/// <summary>
/// SQL migration for monthly range partitioning on mt_events.
/// Applied via DbUp migration runner.
/// </summary>
public static class EventPartitionMigration
{
    /// <summary>
    /// Generates SQL to convert mt_events to a range-partitioned table.
    /// This is a one-time migration. Subsequent months are auto-created
    /// by a scheduled job.
    /// </summary>
    public static string GeneratePartitionSql(DateOnly startMonth, int monthsAhead = 12)
    {
        var lines = new List<string>
        {
            "-- EVENT-SCALE-001: Monthly range partitioning for mt_events",
            "-- Run once via DbUp migration. Auto-create future partitions monthly.",
            "",
            "-- NOTE: This requires the table to be empty or converted using",
            "-- pg_partman or manual ATTACH PARTITION. See PostgreSQL docs.",
            ""
        };

        var current = startMonth;
        for (int i = 0; i < monthsAhead; i++)
        {
            var next = current.AddMonths(1);
            var partName = $"mt_events_{current:yyyyMM}";
            lines.Add($"CREATE TABLE IF NOT EXISTS {partName} PARTITION OF mt_events");
            lines.Add($"  FOR VALUES FROM ('{current:yyyy-MM-01}') TO ('{next:yyyy-MM-01}');");
            lines.Add("");
            current = next;
        }

        return string.Join("\n", lines);
    }
}
