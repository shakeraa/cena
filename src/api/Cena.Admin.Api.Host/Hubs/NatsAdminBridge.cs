// =============================================================================
// Cena Platform — NATS → Admin SignalR bridge (RDY-060)
//
// BackgroundService subscribing to the union of admin-relevant NATS
// subject families. Each incoming message is parsed for its prefix and
// routed to one or more AdminGroupNames groups via IHubContext.
//
// Routing strategy (v1, deliberately simple):
//
//   cena.events.student.{studentId}.*
//     → admin:student-insights:{studentId}
//     → admin:system (SuperAdmin fan-out)
//
//   cena.events.focus.{studentId}.*
//     → admin:student-insights:{studentId}
//     → admin:system
//
//   cena.events.ingestion.*
//     → admin:ingestion
//     → admin:system
//
//   cena.system.*
//     → admin:system
//
// School-scoped routing (admin:school:{schoolId}) requires the source
// event to carry a schoolId in its payload. v1 does NOT unwrap payloads
// for that — we fan into admin:system which SUPER_ADMIN consumes.
// School-scoped filtering can be tightened once specific subjects
// publish schoolId consistently (tracked as a Phase 2 follow-up).
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using Cena.Infrastructure.Tracing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Admin.Api.Host.Hubs;

public sealed class NatsAdminBridge : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IHubContext<CenaAdminHub, ICenaAdminClient> _hub;
    private readonly AdminSignalRMetrics _metrics;
    private readonly ILogger<NatsAdminBridge> _logger;

    // Subjects we subscribe to. Wildcards at the deepest level only;
    // NATS doesn't allow `>` mixed with arbitrary filters here.
    private static readonly IReadOnlyList<string> Subjects = new[]
    {
        "cena.events.student.>",
        "cena.events.focus.>",
        "cena.events.ingestion.>",
        "cena.system.>",
    };

    public NatsAdminBridge(
        INatsConnection nats,
        IHubContext<CenaAdminHub, ICenaAdminClient> hub,
        AdminSignalRMetrics metrics,
        ILogger<NatsAdminBridge> logger)
    {
        _nats = nats;
        _hub = hub;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NatsAdminBridge starting — subscribing to {Subjects}",
            string.Join(", ", Subjects));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnce(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NatsAdminBridge subscription failed — reconnecting in 2s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("NatsAdminBridge stopped");
    }

    private async Task RunOnce(CancellationToken ct)
    {
        // Run one subscriber per wildcard subject in parallel so a burst on
        // one family doesn't starve others. Each subscription loops
        // independently until cancelled.
        var tasks = Subjects.Select(subject => SubscribeAndRoute(subject, ct));
        await Task.WhenAll(tasks);
    }

    private async Task SubscribeAndRoute(string subject, CancellationToken ct)
    {
        await foreach (var msg in _nats.SubscribeAsync<string>(subject, cancellationToken: ct))
        {
            try
            {
                using var traceActivity = NatsTracePropagation.ExtractTraceContext(
                    msg.Headers, $"NatsAdminBridge.Route {msg.Subject}");
                traceActivity?.SetTag("messaging.system", "nats");
                traceActivity?.SetTag("messaging.destination", msg.Subject);

                await Route(msg.Subject, msg.Data ?? "{}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to route NATS event from subject {Subject}", msg.Subject);
            }
        }
    }

    // Exposed as `internal` for unit-test reach over group selection.
    internal static IReadOnlyList<string> GroupsForSubject(string subject)
    {
        // cena.events.student.{studentId}.*  →  student-insights + system
        // cena.events.focus.{studentId}.*    →  student-insights + system
        // cena.events.ingestion.*            →  ingestion + system
        // cena.system.*                      →  system
        var parts = subject.Split('.');
        if (parts.Length >= 5 &&
            parts[0] == "cena" && parts[1] == "events" &&
            (parts[2] == "student" || parts[2] == "focus"))
        {
            var studentId = parts[3];
            return new[]
            {
                AdminGroupNames.StudentInsights(studentId),
                AdminGroupNames.System,
            };
        }
        if (parts.Length >= 4 &&
            parts[0] == "cena" && parts[1] == "events" && parts[2] == "ingestion")
        {
            return new[] { AdminGroupNames.Ingestion, AdminGroupNames.System };
        }
        if (parts.Length >= 2 && parts[0] == "cena" && parts[1] == "system")
        {
            return new[] { AdminGroupNames.System };
        }
        return Array.Empty<string>();
    }

    private async Task Route(string subject, string data, CancellationToken ct)
    {
        var groups = GroupsForSubject(subject);
        if (groups.Count == 0) return;

        var payload = ExtractPayload(data);
        var envelope = new AdminHubEnvelope(
            Subject: subject,
            Group: groups[0],
            PayloadJson: payload,
            ServerTimestamp: DateTimeOffset.UtcNow);

        foreach (var group in groups)
        {
            var groupEnvelope = envelope with { Group = group };
            await _hub.Clients.Group(group).ReceiveEvent(groupEnvelope);
            _metrics.EventSent(SubjectPrefix(subject), group);
        }
    }

    private static string SubjectPrefix(string subject)
    {
        // "cena.events.student.stu-1.answer_evaluated" → "cena.events.student"
        var parts = subject.Split('.');
        return parts.Length >= 3 ? string.Join(".", parts.Take(3)) : subject;
    }

    private static string ExtractPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("payload", out var payload))
                return payload.GetRawText();
            if (doc.RootElement.TryGetProperty("Payload", out var payloadPascal))
                return payloadPascal.GetRawText();
        }
        catch (JsonException)
        {
        }
        return json;
    }
}
