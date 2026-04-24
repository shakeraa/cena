// =============================================================================
// Cena Platform — Admin SignalR metrics (RDY-060)
//
// OTel meter + counters for the admin hub. Registered singleton.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Admin.Api.Host.Hubs;

public sealed class AdminSignalRMetrics : IDisposable
{
    public const string MeterName = "Cena.Admin.Api.Host.Hubs";

    private readonly Meter _meter;
    private readonly UpDownCounter<long> _connections;
    private readonly Counter<long> _eventsSent;
    private readonly Counter<long> _groupRejects;
    private readonly Histogram<double> _groupJoinDuration;

    public AdminSignalRMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, "1.0.0");
        _connections = _meter.CreateUpDownCounter<long>(
            "cena.admin.signalr.connections",
            description: "Current number of connected admin clients (delta on open/close).");
        _eventsSent = _meter.CreateCounter<long>(
            "cena.admin.signalr.events_sent_total",
            description: "NATS events routed to admin SignalR groups, tagged by subject prefix + group.");
        _groupRejects = _meter.CreateCounter<long>(
            "cena.admin.signalr.group_rejects_total",
            description: "Join requests rejected by tenant-scope / role check, tagged by reason.");
        _groupJoinDuration = _meter.CreateHistogram<double>(
            "cena.admin.signalr.group_join_duration_ms",
            unit: "ms",
            description: "End-to-end duration of a Groups.AddToGroupAsync call.");
    }

    public void ConnectionOpened() => _connections.Add(1);
    public void ConnectionClosed() => _connections.Add(-1);

    public void EventSent(string subjectPrefix, string group)
    {
        var tags = new TagList
        {
            { "subject_prefix", subjectPrefix },
            { "group", group },
        };
        _eventsSent.Add(1, tags);
    }

    public void GroupRejected(string reason) =>
        _groupRejects.Add(1, new TagList { { "reason", reason } });

    public void GroupJoined(double durationMs) =>
        _groupJoinDuration.Record(durationMs);

    public void Dispose() => _meter.Dispose();
}
