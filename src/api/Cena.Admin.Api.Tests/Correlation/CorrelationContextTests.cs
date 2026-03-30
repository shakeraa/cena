using Cena.Infrastructure.Correlation;

namespace Cena.Admin.Api.Tests.Correlation;

/// <summary>
/// Unit tests for CorrelationContext AsyncLocal storage.
/// ERR-001.4
/// </summary>
public class CorrelationContextTests
{
    [Fact]
    public void GetOrCreate_ReturnsNewGuid_WhenNotSet()
    {
        // Clear any value from a previous test running on the same thread
        CorrelationContext.Current = null;

        var id = CorrelationContext.GetOrCreate();

        Assert.False(string.IsNullOrEmpty(id));
        Assert.True(Guid.TryParse(id, out _), $"Expected a GUID, got: {id}");
    }

    [Fact]
    public void GetOrCreate_ReturnsSameValue_WhenCalledTwice()
    {
        CorrelationContext.Current = null;

        var first = CorrelationContext.GetOrCreate();
        var second = CorrelationContext.GetOrCreate();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Current_CanBeSetAndRetrieved()
    {
        const string id = "test-correlation-id";
        CorrelationContext.Current = id;

        Assert.Equal(id, CorrelationContext.Current);

        // Cleanup
        CorrelationContext.Current = null;
    }

    [Fact]
    public async Task Current_IsIsolated_AcrossAsyncContexts()
    {
        CorrelationContext.Current = "parent-id";

        string? capturedInTask = null;
        await Task.Run(() =>
        {
            // AsyncLocal copies the value into child flows (read-only copy semantics)
            // Setting in child does NOT affect parent
            CorrelationContext.Current = "child-id";
            capturedInTask = CorrelationContext.Current;
        });

        Assert.Equal("child-id", capturedInTask);
        // Parent retains its own copy
        Assert.Equal("parent-id", CorrelationContext.Current);

        // Cleanup
        CorrelationContext.Current = null;
    }
}
