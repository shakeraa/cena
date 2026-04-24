// =============================================================================
// Minimal test fixture for integration-style tests that need an IServiceProvider
// without spinning up a full WebApplicationFactory.
// =============================================================================

using Cena.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Admin.Api.Tests;

public sealed class TestApiFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public TestApiFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:LlmBudget:GlobalTutorPerMinute"] = "1000",
                ["Cena:LlmBudget:TenantTutorPerMinute"] = "200",
                ["Cena:LlmBudget:GlobalDailyTokens"] = "10000000",
                ["Cena:LlmBudget:TenantDailyTokens"] = "500000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Mock Redis for AiTokenBudgetService
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        services.AddSingleton(redis);

        services.AddAiTokenBudget();

        _serviceProvider = services.BuildServiceProvider();
    }

    public IServiceProvider Services => _serviceProvider;

    public HttpClient CreateClient() => new();

    public void Dispose() => _serviceProvider.Dispose();
}
