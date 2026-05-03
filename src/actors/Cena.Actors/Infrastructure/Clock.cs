// =============================================================================
// FIND-qa-007: TimeProvider-based clock abstraction for deterministic testing
//
// Replaces direct DateTime.UtcNow usage with an injectable IClock service.
// Tests use FakeTimeProvider from Microsoft.Extensions.TimeProvider.Testing
// to achieve deterministic, non-flaky time-based assertions.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Abstraction for time operations to enable testable, deterministic time.
/// </summary>
public interface IClock
{
    /// <summary>Gets the current UTC time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Gets the current UTC date/time (for backward compatibility).</summary>
    DateTime UtcDateTime { get; }

    /// <summary>Gets the local date/time.</summary>
    DateTime LocalDateTime { get; }

    /// <summary>Returns the current UTC time as a string in the specified format.</summary>
    string FormatUtc(string format);
}

/// <summary>
/// Default implementation of IClock using .NET 8+ TimeProvider.
/// Registered as singleton in DI container.
/// </summary>
public sealed class Clock : IClock
{
    private readonly TimeProvider _timeProvider;

    public Clock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc />
    public DateTime UtcDateTime => _timeProvider.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public DateTime LocalDateTime => _timeProvider.GetLocalNow().LocalDateTime;

    /// <inheritdoc />
    public string FormatUtc(string format)
    {
        return UtcDateTime.ToString(format);
    }
}

/// <summary>
/// Extension methods for registering the clock in DI.
/// </summary>
public static class ClockRegistration
{
    /// <summary>
    /// Registers IClock as a singleton using the system time provider.
    /// Call this in production: services.AddClock();
    /// </summary>
    public static IServiceCollection AddClock(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, Clock>();
        return services;
    }
}
