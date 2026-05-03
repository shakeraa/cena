// =============================================================================
// Cena Platform — CloudDirectoryProviderRegistry tests (ADR-0058)
//
// Unit tests for the provider-dispatch seam. Uses NSubstitute to inject
// fake providers; no Marten, no AWS SDK, no filesystem.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class CloudDirectoryProviderRegistryTests
{
    [Fact]
    public void Resolve_returns_enabled_provider_by_id()
    {
        var local = FakeProvider("local", enabled: true);
        var s3 = FakeProvider("s3", enabled: true);

        var registry = new CloudDirectoryProviderRegistry(new[] { local, s3 });

        Assert.Same(local, registry.Resolve("local"));
        Assert.Same(s3, registry.Resolve("s3"));
    }

    [Fact]
    public void Resolve_is_case_insensitive()
    {
        var local = FakeProvider("local", enabled: true);

        var registry = new CloudDirectoryProviderRegistry(new[] { local });

        Assert.Same(local, registry.Resolve("LOCAL"));
        Assert.Same(local, registry.Resolve("Local"));
    }

    [Fact]
    public void Resolve_throws_on_unknown_provider()
    {
        var registry = new CloudDirectoryProviderRegistry(
            new[] { FakeProvider("local", enabled: true) });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("azure"));
        Assert.Contains("azure", ex.Message);
        Assert.Contains("local", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_throws_when_provider_disabled()
    {
        // S3 provider registered but IsEnabled=false (e.g. Ingestion:S3:Enabled=false
        // or AllowedBuckets empty).
        var s3 = FakeProvider("s3", enabled: false);
        var registry = new CloudDirectoryProviderRegistry(new[] { s3 });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("s3"));
        Assert.Contains("disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_throws_on_empty_provider_id()
    {
        var registry = new CloudDirectoryProviderRegistry(
            new[] { FakeProvider("local", enabled: true) });

        Assert.Throws<InvalidOperationException>(() => registry.Resolve(""));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve(null!));
    }

    [Fact]
    public void All_exposes_every_registered_provider_enabled_or_not()
    {
        var a = FakeProvider("local", enabled: true);
        var b = FakeProvider("s3", enabled: false);

        var registry = new CloudDirectoryProviderRegistry(new[] { a, b });

        Assert.Equal(2, registry.All.Count);
        Assert.Contains(a, registry.All);
        Assert.Contains(b, registry.All);
    }

    private static ICloudDirectoryProvider FakeProvider(string id, bool enabled)
    {
        var p = Substitute.For<ICloudDirectoryProvider>();
        p.ProviderId.Returns(id);
        p.IsEnabled.Returns(enabled);
        return p;
    }
}
