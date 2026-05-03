namespace Cena.Infrastructure.Correlation;

/// <summary>
/// Holds the current correlation ID in an AsyncLocal so it flows through
/// async continuations without being passed explicitly on every method call.
/// ERR-001.4
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();

    public static string? Current
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    /// <summary>
    /// Returns the current correlation ID, or generates and stores a new one if none is set.
    /// </summary>
    public static string GetOrCreate()
    {
        if (string.IsNullOrEmpty(_correlationId.Value))
            _correlationId.Value = Guid.NewGuid().ToString();

        return _correlationId.Value!;
    }
}
