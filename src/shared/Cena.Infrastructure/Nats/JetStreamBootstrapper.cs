// =============================================================================
// Cena Platform -- JetStream Bootstrapper (FIND-arch-022)
// Ensures 6 JetStream streams exist on startup for durable event categories.
// =============================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Cena.Infrastructure.Nats;

/// <summary>
/// Hosted service that creates JetStream streams on application startup.
/// FIND-arch-022: Replaces core NATS with JetStream for durable outbox publishing.
/// </summary>
public sealed class JetStreamBootstrapper : IHostedService
{
    private readonly INatsConnection _nats;
    private readonly ILogger<JetStreamBootstrapper> _logger;

    // Stream definitions matching NatsOutboxPublisher.GetDurableSubject categories
    private static readonly List<StreamConfig> StreamDefinitions = new()
    {
        new StreamConfig
        {
            Name = "CENA_LEARNER",
            Subjects = new List<string> { "cena.durable.learner.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        new StreamConfig
        {
            Name = "CENA_PEDAGOGY",
            Subjects = new List<string> { "cena.durable.pedagogy.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        new StreamConfig
        {
            Name = "CENA_ENGAGEMENT",
            Subjects = new List<string> { "cena.durable.engagement.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        new StreamConfig
        {
            Name = "CENA_OUTREACH",
            Subjects = new List<string> { "cena.durable.outreach.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        new StreamConfig
        {
            Name = "CENA_SYSTEM",
            Subjects = new List<string> { "cena.durable.system.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        new StreamConfig
        {
            Name = "CENA_CURRICULUM",
            Subjects = new List<string> { "cena.durable.curriculum.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        },
        // RDY-017: Dead-letter queue stream — 30-day retention for investigation + replay
        new StreamConfig
        {
            Name = "CENA_DLQ",
            Subjects = new List<string> { "cena.durable.dlq.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(30),
            Storage = StreamConfigStorage.File,
            NumReplicas = 1
        }
    };

    public JetStreamBootstrapper(INatsConnection nats, ILogger<JetStreamBootstrapper> logger)
    {
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JetStream bootstrapper starting — ensuring {Count} streams exist", StreamDefinitions.Count);

        try
        {
            var js = new NatsJSContext(_nats);

            foreach (var config in StreamDefinitions)
            {
                await EnsureStreamAsync(js, config, cancellationToken);
            }

            _logger.LogInformation("JetStream bootstrapper completed successfully");
        }
        catch (Exception ex)
        {
            // Log but don't crash — app can still function with core NATS
            _logger.LogError(ex, "JetStream bootstrapper failed — durable streams may not be available");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JetStream bootstrapper stopping");
        return Task.CompletedTask;
    }

    private async Task EnsureStreamAsync(INatsJSContext js, StreamConfig config, CancellationToken ct)
    {
        try
        {
            // Try to create the stream
            await js.CreateStreamAsync(config, ct);
            _logger.LogInformation("Created JetStream stream: {StreamName}", config.Name);
        }
        catch (NatsJSApiException ex) when (ex.Error?.ErrCode == 10058) // stream already exists
        {
            // Stream exists, update if needed
            _logger.LogDebug("JetStream stream {StreamName} already exists", config.Name);
            
            try
            {
                await js.UpdateStreamAsync(config, ct);
                _logger.LogDebug("Updated JetStream stream: {StreamName}", config.Name);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning(updateEx, "Failed to update JetStream stream {StreamName}", config.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create JetStream stream {StreamName}", config.Name);
            throw;
        }
    }
}
