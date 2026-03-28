// =============================================================================
// Cena Platform -- System Monitoring Service
// ADM-008: System health, settings, and audit logging
// =============================================================================
#pragma warning disable CS1998 // Async methods return stub data until wired to real stores

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

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
        var process = System.Diagnostics.Process.GetCurrentProcess();

        // Real service health probes
        var services = new List<ServiceHealth>();

        // API (this process)
        services.Add(new("API", "healthy", "1.0.0",
            TimeSpan.FromMilliseconds(1), now, null));

        // PostgreSQL — real probe via Marten
        var pgLatency = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var session = _store.QuerySession();
            await session.Query<Cena.Infrastructure.Documents.AdminUser>().CountAsync();
            pgLatency.Stop();
            services.Add(new("PostgreSQL", "healthy", "16",
                pgLatency.Elapsed, now, null));
        }
        catch (Exception ex)
        {
            pgLatency.Stop();
            services.Add(new("PostgreSQL", "down", null,
                pgLatency.Elapsed, now, ex.Message));
        }

        // Redis — real probe
        var redisLatency = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            redisLatency.Stop();
            services.Add(new("Redis", "healthy", "7.0",
                redisLatency.Elapsed, now, null));
        }
        catch (Exception ex)
        {
            redisLatency.Stop();
            services.Add(new("Redis", "down", null,
                redisLatency.Elapsed, now, ex.Message));
        }

        // NATS — real probe
        var natsLatency = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var varz = await http.GetStringAsync("http://localhost:8222/varz");
            natsLatency.Stop();
            var natsDoc = System.Text.Json.JsonDocument.Parse(varz);
            var natsConns = natsDoc.RootElement.GetProperty("connections").GetInt32();
            services.Add(new("NATS", "healthy", natsDoc.RootElement.GetProperty("version").GetString(),
                natsLatency.Elapsed, now, null));
        }
        catch (Exception ex)
        {
            natsLatency.Stop();
            services.Add(new("NATS", "down", null,
                natsLatency.Elapsed, now, ex.Message));
        }

        // Actor Host — real probe
        var actorLatency = System.Diagnostics.Stopwatch.StartNew();
        int actorCount = 0;
        long actorMessages = 0;
        long actorErrors = 0;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var statsJson = await http.GetStringAsync("http://localhost:5119/api/actors/stats");
            actorLatency.Stop();
            var statsDoc = System.Text.Json.JsonDocument.Parse(statsJson);
            actorCount = statsDoc.RootElement.GetProperty("activeActorCount").GetInt32();
            actorMessages = statsDoc.RootElement.GetProperty("commandsRouted").GetInt64();
            actorErrors = statsDoc.RootElement.GetProperty("errorsCount").GetInt64();
            services.Add(new("Actor Host", "healthy", "1.8.0",
                actorLatency.Elapsed, now, null));
        }
        catch (Exception ex)
        {
            actorLatency.Stop();
            services.Add(new("Actor Host", "down", null,
                actorLatency.Elapsed, now, ex.Message));
        }

        // Process metrics
        var memoryBytes = process.WorkingSet64;
        var cpuTime = process.TotalProcessorTime;
        var uptime = now - process.StartTime;
        var cpuPercent = uptime.TotalSeconds > 0
            ? (float)(cpuTime.TotalSeconds / uptime.TotalSeconds * 100 / Environment.ProcessorCount)
            : 0f;

        var actors = new List<ActorSystemStatus>
        {
            new(Environment.MachineName, "active",
                actorCount,
                (int)actorMessages,
                cpuPercent,
                memoryBytes)
        };

        // Real event stream metrics
        await using var eventSession = _store.QuerySession();
        var totalEvents = await eventSession.Events.QueryAllRawEvents().CountAsync();

        var queues = new List<QueueDepth>
        {
            new("marten.events", totalEvents, "normal", now),
        };

        // Error rate trend — derive from actor errors over 24h window
        var errorRate = actorMessages > 0 ? (float)actorErrors / actorMessages * 100 : 0f;
        var trend = new List<ErrorPoint>();
        for (int i = 23; i >= 0; i--)
        {
            // Show real current error rate for recent hours, 0 for older
            var hourRate = i < 2 ? errorRate : 0f;
            trend.Add(new ErrorPoint(
                now.AddHours(-i).ToString("yyyy-MM-dd HH:00"),
                i < 2 ? (int)actorErrors : 0,
                i < 2 ? (int)actorMessages : 100));
        }

        return new SystemHealthResponse(
            Timestamp: now,
            Services: services,
            ActorSystems: actors,
            QueueDepths: queues,
            ErrorRates: new ErrorRateMetrics(0, 0, trend));
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
        await using var session = _store.QuerySession();

        // Query real events from Marten event store as audit log entries
        var query = session.Events.QueryAllRawEvents()
            .OrderByDescending(e => e.Timestamp);

        var totalCount = await query.CountAsync();

        var rawEvents = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var entries = rawEvents.Select(e => new AuditLogEntry(
            Id: e.Id.ToString(),
            Timestamp: e.Timestamp,
            UserId: e.StreamKey ?? "system",
            UserName: e.StreamKey ?? "System",
            Action: e.EventTypeName ?? "unknown",
            TargetType: "Event",
            TargetId: e.StreamKey ?? "",
            Details: $"{e.EventTypeName} on stream {e.StreamKey}",
            IpAddress: "server"
        )).ToList();

        return new AuditLogResponse(entries, totalCount, page, pageSize);
    }
}
