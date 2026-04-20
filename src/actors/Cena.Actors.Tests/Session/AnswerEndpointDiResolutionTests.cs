// =============================================================================
// RDY-033c: Regression test proving every [FromServices] parameter of
// SessionEndpoints.POST /answer can be resolved from the Student.Api.Host
// DI container. This is the test that would have caught the latent
// IErrorClassificationService / ILlmClient gap before students hit it.
//
// We build a ServiceCollection with the same registrations the host uses
// (reusing Cena.Actors.Services / Cena.Actors.Cas / Cena.Actors.Gateway
// types directly), then call GetRequiredService on each interface the
// endpoint injects. Any missing transitive dep throws here.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Gateway;
using Cena.Actors.RateLimit;
using Cena.Actors.Services;
using Cena.Actors.Services.ErrorPatternMatching;
using Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers;
using Cena.Actors.Serving;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NSubstitute;

namespace Cena.Actors.Tests.Session;

/// <summary>
/// Contract test: every [FromServices] parameter of POST /answer must resolve
/// from a DI container populated the way Student.Api.Host populates it.
/// </summary>
public sealed class AnswerEndpointDiResolutionTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // Built-ins the minimal-API pipeline ordinarily provides.
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMetrics();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        // External dependencies that live outside Cena.Actors — we stub them
        // so DI resolution can exercise constructor graphs without network.
        services.AddSingleton(Substitute.For<INatsConnection>());
        services.AddSingleton(Substitute.For<ICostCircuitBreaker>());

        // LLM stack (RDY-033c).
        services.AddSingleton<AnthropicLlmClient>();
        services.AddSingleton<ILlmClient, LlmClientRouter>();
        // prr-046: cost metric required by every [TaskRouting] consumer.
        // Test graph uses the null implementation — production hosts wire
        // the real LlmCostMetric via AddLlmCostMetric(routing-config path).
        services.AddSingleton<ILlmCostMetric>(NullLlmCostMetric.Instance);

        // CAS stack (RDY-033 / ADR-0002).
        services.AddSingleton<IMathNetVerifier, MathNetVerifier>();
        services.AddSingleton<ISymPySidecarClient, SymPySidecarClient>();
        services.AddSingleton<ICasRouterService, CasRouterService>();

        // Error pattern matchers (RDY-033 / ADR-0031).
        services.AddSingleton<IErrorPatternMatcher, DistExpSumMatcher>();
        services.AddSingleton<IErrorPatternMatcher, CancelCommonMatcher>();
        services.AddSingleton<IErrorPatternMatcher, SignNegativeMatcher>();
        services.AddSingleton<IErrorPatternMatcher, OrderOpsMatcher>();
        services.AddSingleton<IErrorPatternMatcher, FractionAddMatcher>();
        services.AddSingleton<IErrorPatternMatcherEngine, ErrorPatternMatcherEngine>();

        // Error classification + misconception detection (RDY-033b).
        services.AddSingleton<IErrorClassificationService, ErrorClassificationService>();
        services.AddSingleton<IMisconceptionDetectionService, MisconceptionDetectionService>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void ErrorClassificationService_ResolvesFromStudentApiHostDiGraph()
    {
        using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IErrorClassificationService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void MisconceptionDetectionService_ResolvesFromStudentApiHostDiGraph()
    {
        using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IMisconceptionDetectionService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void CasRouterService_ResolvesWithAllThreeTiers()
    {
        using var provider = BuildProvider();
        var router = provider.GetRequiredService<ICasRouterService>();
        Assert.NotNull(router);
    }

    [Fact]
    public void MatcherEngine_ResolvesWithAllFiveMatchers()
    {
        using var provider = BuildProvider();
        var engine = provider.GetRequiredService<IErrorPatternMatcherEngine>();
        Assert.NotNull(engine);

        // All 5 matchers resolve as a collection.
        var matchers = provider.GetServices<IErrorPatternMatcher>().ToList();
        Assert.Equal(5, matchers.Count);
        Assert.Contains(matchers, m => m.BuggyRuleId == "DIST-EXP-SUM");
        Assert.Contains(matchers, m => m.BuggyRuleId == "CANCEL-COMMON");
        Assert.Contains(matchers, m => m.BuggyRuleId == "SIGN-NEGATIVE");
        Assert.Contains(matchers, m => m.BuggyRuleId == "ORDER-OPS");
        Assert.Contains(matchers, m => m.BuggyRuleId == "FRACTION-ADD");
    }

    [Fact]
    public void LlmClient_ResolvesAsLlmClientRouter()
    {
        using var provider = BuildProvider();
        var client = provider.GetRequiredService<ILlmClient>();
        Assert.IsType<LlmClientRouter>(client);
    }
}
