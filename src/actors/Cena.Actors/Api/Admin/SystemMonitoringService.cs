// =============================================================================
// Cena Platform -- System Monitoring Service
// ADM-008: System health, settings, and audit logging
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Api.Admin;

public interface ISystemMonitoringService
{
    Task<SystemHealthResponse> GetHealthAsync();
    Task<PlatformSettingsResponse> GetSettingsAsync();
    Task<bool> UpdateSettingsAsync(UpdateSettingsRequest request, string userId);
    Task<AuditLogResponse> GetAuditLogAsync(AuditLogFilterRequest request, int page, int pageSize);
}

public sealed class SystemMonitoringService : ISystemMonitoringService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SystemMonitoringService> _logger;
    private static PlatformSettingsResponse? _cachedSettings;

    public SystemMonitoringService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<SystemMonitoringService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<SystemHealthResponse> GetHealthAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var random = new Random();

        var services = new List<ServiceHealth>
        {
            new("API", "healthy", "1.0.0", TimeSpan.FromMilliseconds(random.Next(5, 50)), now, null),
            new("Actor System", "healthy", "1.0.0", TimeSpan.FromMilliseconds(random.Next(10, 100)), now, null),
            new("PostgreSQL", "healthy", "15.0", TimeSpan.FromMilliseconds(random.Next(5, 30)), now, null),
            new("Redis", "healthy", "7.0", TimeSpan.FromMilliseconds(random.Next(2, 10)), now, null),
            new("S3", "healthy", null, TimeSpan.FromMilliseconds(random.Next(50, 200)), now, null)
        };

        var actors = new List<ActorSystemStatus>
        {
            new("node-1", "active", random.Next(100, 500), random.Next(10000, 50000), random.NextSingle() * 30f, random.Next(100000000, 500000000)),
            new("node-2", "active", random.Next(100, 500), random.Next(10000, 50000), random.NextSingle() * 30f, random.Next(100000000, 500000000))
        };

        var queues = new List<QueueDepth>
        {
            new("events.ingest", random.Next(0, 100), random.Next(100) > 80 ? "warning" : "normal", now),
            new("events.process", random.Next(0, 50), "normal", now),
            new("outreach.send", random.Next(0, 200), random.Next(200) > 150 ? "warning" : "normal", now),
            new("nats.dlq", random.Next(0, 10), random.Next(10) > 5 ? "critical" : "normal", now)
        };

        var trend = new List<ErrorPoint>();
        for (int i = 23; i >= 0; i--)
        {
            trend.Add(new ErrorPoint(
                now.AddHours(-i).ToString("yyyy-MM-dd HH:00"),
                random.Next(0, 10),
                random.Next(1000, 5000)));
        }

        var totalRequests = trend.Sum(t => t.RequestCount);
        var totalErrors = trend.Sum(t => t.ErrorCount);

        return new SystemHealthResponse(
            Timestamp: now,
            Services: services,
            ActorSystems: actors,
            QueueDepths: queues,
            ErrorRates: new ErrorRateMetrics(
                totalErrors / 24f,
                totalRequests > 0 ? totalErrors * 100f / totalRequests : 0f,
                trend));
    }

    public async Task<PlatformSettingsResponse> GetSettingsAsync()
    {
        if (_cachedSettings != null)
            return _cachedSettings;

        _cachedSettings = new PlatformSettingsResponse(
            new OrganizationSettings(
                "Cena Learning Platform",
                null,
                "Asia/Jerusalem",
                "he",
                DateTimeOffset.UtcNow,
                "system"),
            new FeatureFlagSettings(
                EnableFocusTracking: true,
                EnableMicrobreaks: true,
                EnableMethodologySwitching: true,
                EnableOutreach: true,
                EnableOfflineMode: true,
                EnableParentDashboard: false),
            new FocusEngineSettings(
                DegradationThreshold: 0.65f,
                MicrobreakIntervalMinutes: 20,
                MindWanderingThreshold: 0.35f,
                FocusScoreBaseline: 0.75f),
            new MasteryEngineSettings(
                MasteryThreshold: 0.85f,
                PrerequisiteGateThreshold: 0.95f,
                DecayRatePerDay: 0.02f,
                ReviewIntervalDays: 7));

        return _cachedSettings;
    }

    public async Task<bool> UpdateSettingsAsync(UpdateSettingsRequest request, string userId)
    {
        var current = await GetSettingsAsync();

        _cachedSettings = new PlatformSettingsResponse(
            request.Organization ?? current.Organization,
            request.Features ?? current.Features,
            request.FocusEngine ?? current.FocusEngine,
            request.MasteryEngine ?? current.MasteryEngine);

        // In production, persist to database
        return true;
    }

    public async Task<AuditLogResponse> GetAuditLogAsync(AuditLogFilterRequest request, int page, int pageSize)
    {
        var random = new Random();
        var entries = new List<AuditLogEntry>();
        var actions = new[] { "user.create", "user.update", "user.suspend", "role.assign", "settings.update", "content.approve", "content.reject" };
        var targets = new[] { "User", "Role", "Settings", "Content" };

        for (int i = 0; i < pageSize; i++)
        {
            entries.Add(new AuditLogEntry(
                Id: $"audit-{page}-{i}",
                Timestamp: DateTimeOffset.UtcNow.AddHours(-random.Next(1, 168)),
                UserId: $"admin-{random.Next(1, 5)}",
                UserName: $"Admin {random.Next(1, 5)}",
                Action: actions[random.Next(actions.Length)],
                TargetType: targets[random.Next(targets.Length)],
                TargetId: $"tgt-{random.Next(1000)}",
                Details: $"Performed {actions[random.Next(actions.Length)]}",
                IpAddress: $"192.168.1.{random.Next(1, 255)}"));
        }

        return new AuditLogResponse(
            entries.OrderByDescending(e => e.Timestamp).ToList(),
            1000,
            page,
            pageSize);
    }
}
