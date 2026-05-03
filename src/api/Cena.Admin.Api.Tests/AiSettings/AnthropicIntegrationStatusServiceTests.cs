// =============================================================================
// AnthropicIntegrationStatusService — unit + integration tests for the new
// surface that tells the admin SPA banner whether the Anthropic LLM tier
// is reachable. Covers:
//   - No key configured → ApiKeyConfigured=false, KeySource=None, Down
//   - Key only in IConfiguration → KeySource=Configuration, Healthy(Unknown)
//   - Key in AiSettingsDocument → KeySource=Marten
//   - Sliding window: Healthy / Degraded / Down classification
//   - Last failure category + truncated message
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Admin.Api.Tests.AiSettings;

public class AnthropicIntegrationStatusServiceTests
{
    private static IConfiguration Cfg(params (string k, string v)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e =>
                new KeyValuePair<string, string?>(e.k, e.v)))
            .Build();

    [Fact]
    public async Task NoKeyAnywhere_ReportsDown()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        var s = await svc.GetStatusAsync();

        Assert.False(s.ApiKeyConfigured);
        Assert.Equal(AnthropicApiKeySource.None, s.KeySource);
        Assert.Equal(AnthropicReachability.Down, s.Reachability);
    }

    [Fact]
    public async Task KeyInConfiguration_ReportsHealthyUnknownState()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        var s = await svc.GetStatusAsync();

        Assert.True(s.ApiKeyConfigured);
        Assert.Equal(AnthropicApiKeySource.Configuration, s.KeySource);
        Assert.Equal(AnthropicReachability.Unknown, s.Reachability);
    }

    [Fact]
    public async Task RecordingMixedCalls_Degraded()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        for (int i = 0; i < 7; i++) svc.RecordSuccess("concept_extraction");
        svc.RecordFailure("concept_extraction", AnthropicCallFailureKind.Transport, "timeout");

        var s = await svc.GetStatusAsync();

        Assert.Equal(AnthropicReachability.Degraded, s.Reachability);
        Assert.Equal(7, s.RecentSuccessCount);
        Assert.Equal(1, s.RecentFailureCount);
        Assert.Equal("Transport", s.LastFailureCategory);
        Assert.Equal("timeout", s.LastFailureMessage);
    }

    [Fact]
    public async Task RecordingOnlyFailures_Down()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        svc.RecordFailure("concept_extraction", AnthropicCallFailureKind.AuthFailure, "401 invalid_api_key");
        svc.RecordFailure("concept_extraction", AnthropicCallFailureKind.AuthFailure, "401 invalid_api_key");

        var s = await svc.GetStatusAsync();

        Assert.Equal(AnthropicReachability.Down, s.Reachability);
        Assert.Equal("AuthFailure", s.LastFailureCategory);
    }

    [Fact]
    public async Task RecordingOnlySuccesses_Healthy()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        svc.RecordSuccess("concept_extraction");
        svc.RecordSuccess("concept_extraction");

        var s = await svc.GetStatusAsync();

        Assert.Equal(AnthropicReachability.Healthy, s.Reachability);
    }

    [Fact]
    public async Task SlidingWindow_CapsAt50()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        // Push 60 successes; the window should retain only the most recent 50.
        for (int i = 0; i < 60; i++) svc.RecordSuccess("concept_extraction");

        var s = await svc.GetStatusAsync();

        Assert.Equal(50, s.RecentSuccessCount);
        Assert.Equal(0, s.RecentFailureCount);
    }

    [Fact]
    public async Task LongFailureMessage_TruncatedTo240Chars()
    {
        var svc = new AnthropicIntegrationStatusService(
            Cfg(("Anthropic:ApiKey", "sk-ant-test")),
            NullLogger<AnthropicIntegrationStatusService>.Instance,
            store: null);

        var huge = new string('x', 1000);
        svc.RecordFailure("concept_extraction", AnthropicCallFailureKind.Other, huge);

        var s = await svc.GetStatusAsync();

        Assert.NotNull(s.LastFailureMessage);
        Assert.True(s.LastFailureMessage!.Length <= 241,
            $"Expected ≤240 char (+ ellipsis); got {s.LastFailureMessage.Length}.");
        Assert.EndsWith("…", s.LastFailureMessage);
    }
}
